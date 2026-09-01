using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// The GUI's conversion sequence, end to end:
///
///   View → select rows → Convert → classify → confirm → carry out exactly that plan.
///
/// This exists because every component of that sequence was already correct and tested
/// while the sequence itself was unsafe. Classification ran only in Convert-mode scans;
/// the GUI scans in Detect mode; entries reached conversion carrying no classification at
/// all, and the refusal never fired. No component test could have caught it — the defect
/// was in the wiring between them, and the wiring lived in button handlers and
/// background-worker callbacks where nothing could run it.
///
/// So the wiring is <see cref="ConversionOrchestrator"/> and these drive it, against the
/// real conversion engine, on real files. Every case that must not modify anything reads
/// the source bytes before and after and compares them.
/// </summary>
public sealed class ConversionOrchestrationTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_orch_").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private string Write(string name, string text, string charset)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, Encoding.GetEncoding(charset).GetBytes(text));
        return path;
    }

    /// <summary>The rows the GUI's View button produces, which Convert then acts on.</summary>
    private List<ConversionReportEntry> View()
    {
        var scanned = new EntrySink();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                IncludeSubdirectories = true,
                IncludePatterns = ["*"],
                Action = ScanAction.Detect,
            },
            scanned.Add,
            CancellationToken.None);

        return [.. scanned];
    }

    /// <summary>Everything the GUI's Convert button does, with a scripted user.</summary>
    private OrchestrationResult Convert(
        IReadOnlyList<ConversionReportEntry> entries,
        Func<ConversionPlan, ConfirmationResponse> confirm,
        string target = "utf-8",
        bool backup = false,
        bool preview = false,
        Action<ConversionPlan>? betweenPlanAndWrite = null)
    {
        return new ConversionOrchestrator(plan =>
            {
                ConfirmationResponse response = confirm(plan);

                // A hook for the window between approving a plan and acting on it.
                betweenPlanAndWrite?.Invoke(plan);

                return response;
            })
            .Run(
                entries, _root, target, targetWriteBom: false,
                backup: backup, preview: preview,
                ScanEngine.DefaultMaxParallelism,
                _ => { },
                CancellationToken.None);
    }

    private static ConfirmationResponse Proceed(ConversionPlan _) =>
        ConfirmationResponse.Proceed;

    [Fact]
    public void ACompletedRunEmitsExactlyOneTerminalResultPerSelectedFile()
    {
        Write("one.txt", "plain ASCII", "ascii");
        Write("two.txt", "more plain ASCII", "ascii");

        List<ConversionReportEntry> entries = View();
        var terminal =
            new System.Collections.Concurrent.ConcurrentBag<ConversionReportEntry>();

        OrchestrationResult result = new ConversionOrchestrator(Proceed).Run(
            entries, _root, "utf-16", targetWriteBom: false,
            backup: false, preview: false,
            ScanEngine.DefaultMaxParallelism,
            terminal.Add,
            CancellationToken.None);

        Assert.Equal(OrchestrationOutcome.Converted, result.Outcome);
        Assert.Equal(entries.Count, terminal.Count);
        Assert.Equal(
            entries.Select(entry => entry.FilePath).OrderBy(path => path),
            terminal.Select(entry => entry.FilePath).Distinct().OrderBy(path => path));
    }

    [Fact]
    public void DetectionAndHashAreRefreshedTogetherBeforeThePlanIsShown()
    {
        const string text = "Hello 世界";
        string path = Path.Combine(_root, "moving.txt");
        File.WriteAllBytes(path, new UnicodeEncoding(false, false).GetBytes(text));

        List<ConversionReportEntry> entries = View();

        // Replace the bytes after View but before conversion planning. The plan must use
        // the new BOM-confirmed big-endian interpretation and its matching hash, not
        // View's old label. The BOM is intentional: this test proves snapshot binding,
        // while BOM-less byte-order ambiguity is covered by BomlessUtf16SafetyTests.
        var utf16Be = new UnicodeEncoding(true, true);
        File.WriteAllBytes(path, [.. utf16Be.GetPreamble(), .. utf16Be.GetBytes(text)]);
        string replacementHash = ConversionMetadataStore.ComputeSha256(path);

        ConversionPlan? shown = null;
        OrchestrationResult result = Convert(entries, plan =>
        {
            shown = plan;
            return ConfirmationResponse.Proceed;
        });

        Assert.NotNull(shown);
        PlannedFile file = Assert.Single(shown.Files);
        Assert.Equal(1201, file.SourceCodePage);
        Assert.True(file.SourceHasBom);
        Assert.Equal(replacementHash, file.Sha256);
        Assert.Equal(OrchestrationOutcome.Converted, result.Outcome);
        Assert.Equal(text, Encoding.UTF8.GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void CompletedRunCarriesAnImmutableJournalOfWhatActuallyRan()
    {
        Write("journal.txt", "plain ASCII", "ascii");
        List<ConversionReportEntry> entries = View();

        OrchestrationResult result = Convert(entries, Proceed, target: "utf-16");
        Assert.NotNull(result.Journal);
        ConversionJournal journal = result.Journal;

        // Later UI changes cannot rewrite the record returned by the orchestrator.
        entries[0].TargetEncoding = "windows-1252";

        Assert.Equal("utf-16", journal.TargetEncoding);
        Assert.Equal("us-ascii", Assert.Single(journal.Entries).SourceEncoding);
        Assert.Equal(ConversionStatus.Converted, Assert.Single(journal.Entries).Status);
    }

    // ----------------------------------------------------------- the three states

    [Fact]
    public void DetectedLegacyText_IsRefusedBeforeAnythingIsModified()
    {
        string path = Write("ambiguous.txt", "Le café était déjà prêt", "windows-1252");
        byte[] before = File.ReadAllBytes(path);

        var shown = new List<ConversionPlan>();

        OrchestrationResult result = Convert(View(), plan =>
        {
            shown.Add(plan);
            return ConfirmationResponse.Proceed;
        });

        PlannedFile refused = Assert.Single(Assert.Single(shown).Files);

        Assert.Equal(PlannedAction.Refuse, refused.Action);
        Assert.True(refused.NeedsSourceChoice);

        Assert.Equal(OrchestrationOutcome.Converted, result.Outcome);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void ExplicitUtf16Choice_ResolvesAnAmbiguousBomlessUtf16Refusal()
    {
        // The first plan must refuse because automatic detection cannot prove the byte
        // order. Choosing UTF-16BE then rebuilds that same file as an explicit source;
        // strict decoding and output verification still decide whether it can be written.
        string text = string.Concat(Enumerable.Repeat("\u4100\u0A00\u4200", 20));
        byte[] original = new UnicodeEncoding(
            bigEndian: true,
            byteOrderMark: false,
            throwOnInvalidBytes: true).GetBytes(text);
        string path = Path.Combine(_root, "ambiguous-utf16be.txt");
        File.WriteAllBytes(path, original);

        var plansShown = new List<ConversionPlan>();
        var answered = false;

        OrchestrationResult result = Convert(View(), plan =>
        {
            plansShown.Add(plan);

            if (answered)
                return ConfirmationResponse.Proceed;

            answered = true;
            return new ConfirmationResponse(
                ConfirmationChoice.ChooseSourceEncoding, "utf-16be", [path]);
        }, backup: true);

        PlannedFile first = Assert.Single(plansShown[0].Files);
        PlannedFile resolved = Assert.Single(plansShown[1].Files);

        Assert.Equal(PlannedAction.Refuse, first.Action);
        Assert.Equal(ConversionReasonCodes.AmbiguousBomlessUtf16, first.ReasonCode);
        Assert.True(first.NeedsSourceChoice);
        Assert.Equal(PlannedAction.Convert, resolved.Action);
        Assert.Equal("utf-16be", resolved.SourceEncoding);
        Assert.True(resolved.SourceWasSpecified);
        Assert.Equal(OrchestrationOutcome.Converted, result.Outcome);
        Assert.Equal(text, Encoding.UTF8.GetString(File.ReadAllBytes(path)));
        Assert.Equal(original, File.ReadAllBytes(path + ".bak"));
        Assert.True(File.Exists(ConversionMetadataStore.MetadataPathFor(path)));
    }

    [Fact]
    public void Ascii_IsConvertedAutomatically()
    {
        // ASCII has one safe Unicode interpretation and does not require a source choice.
        string path = Write("plain.txt", "plain ascii, no high bytes at all", "ascii");

        OrchestrationResult result = Convert(View(), Proceed);

        Assert.Equal(OrchestrationOutcome.Converted, result.Outcome);
        Assert.Equal(
            PlannedAction.Convert, Assert.Single(result.Plan!.Files).Action);
        Assert.Equal(
            "plain ascii, no high bytes at all",
            Encoding.UTF8.GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void AsciiEncoding_IsConverted()
    {
        const string text = "plain ASCII text";
        string path = Write("ascii.txt", text, "ascii");

        OrchestrationResult result = Convert(View(), Proceed);

        PlannedFile planned = Assert.Single(result.Plan!.Files);

        Assert.Equal(PlannedAction.Convert, planned.Action);
        Assert.Equal(SourceInterpretation.AutomaticUnicodeOrAscii, planned.SourceInterpretation);
        Assert.Equal(text, Encoding.UTF8.GetString(File.ReadAllBytes(path)));
    }

    // ------------------------------------------------------------ explicit source

    [Fact]
    public void ExplicitSourceSelection_IsUsed_AndSafetyStillApplies()
    {
        byte[] bytes =
            Encoding.GetEncoding("windows-1252").GetBytes("Le café était déjà prêt");
        string path = Path.Combine(_root, "chosen.txt");
        File.WriteAllBytes(path, bytes);

        var plansShown = new List<ConversionPlan>();
        var answered = false;

        OrchestrationResult result = Convert(View(), plan =>
        {
            plansShown.Add(plan);

            // Refused first; the user names an encoding; the plan comes back decided.
            if (answered)
                return ConfirmationResponse.Proceed;

            answered = true;
            return new ConfirmationResponse(
                ConfirmationChoice.ChooseSourceEncoding, "koi8-r");
        });

        Assert.Equal(2, plansShown.Count);
        Assert.Equal(PlannedAction.Refuse, Assert.Single(plansShown[0].Files).Action);

        PlannedFile resolved = Assert.Single(plansShown[1].Files);

        Assert.Equal(PlannedAction.Convert, resolved.Action);
        Assert.Equal("koi8-r", resolved.SourceEncoding);
        Assert.True(resolved.SourceWasSpecified);

        // Used, not merely permitted: the chosen codec is what read the bytes.
        Assert.Equal(OrchestrationOutcome.Converted, result.Outcome);
        Assert.Equal(
            Encoding.GetEncoding("koi8-r").GetString(bytes),
            Encoding.UTF8.GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void ExplicitSourceSelection_DoesNotSuspendStrictDecoding()
    {
        // EUC-JP bytes carrying a JIS X 0212 sequence code page 51932 cannot map.
        // Naming the encoding does not make the bytes representable.
        byte[] unrepresentable =
            [0x8F, 0xB0, 0xDF, 0xB9, 0xA5, 0xA1, 0xA4, 0xC0, 0xA4, 0xB3];
        string path = Path.Combine(_root, "undecodable.txt");
        File.WriteAllBytes(path, unrepresentable);

        var answered = false;

        Convert(View(), _ =>
        {
            if (answered)
                return ConfirmationResponse.Proceed;

            answered = true;
            return new ConfirmationResponse(
                ConfirmationChoice.ChooseSourceEncoding, "euc-jp");
        });

        Assert.Equal(unrepresentable, File.ReadAllBytes(path));
    }

    [Fact]
    public void ExplicitSourceSelection_AppliesOnlyToTheFilesItWasAskedAbout()
    {
        // A mixed batch must not have one codec imposed on all of it because the user
        // answered a question about two files.
        const string japanese = "こんにちは世界。日本語のテキストです。";
        string jp = Write("jp.txt", japanese, "shift_jis");
        string ambiguous = Write("ambiguous.txt", "Le café était prêt", "windows-1252");

        var answered = false;

        Convert(View(), _ =>
        {
            if (answered)
                return ConfirmationResponse.Proceed;

            answered = true;
            return new ConfirmationResponse(
                ConfirmationChoice.ChooseSourceEncoding, "windows-1252", [ambiguous]);
        });

        // The Shift_JIS file was never answered for and remains untouched.
        Assert.Equal(Encoding.GetEncoding("shift_jis").GetBytes(japanese), File.ReadAllBytes(jp));
        Assert.Equal(
            "Le café était prêt", Encoding.UTF8.GetString(File.ReadAllBytes(ambiguous)));
    }

    [Fact]
    public void ExplicitSourceSelection_AppliesOnlyToTheFilesTicked()
    {
        // A batch can hold refused files written in different encodings. One answer
        // settles only the files it was given about, so the response carries its scope
        // and the rest keep their refusal.
        string french = Write("french.txt", "Le café était déjà prêt", "windows-1252");
        string russian = Write("russian.txt", "Привет мир, это русский текст", "koi8-r");

        byte[] russianBefore = File.ReadAllBytes(russian);
        var answered = false;

        OrchestrationResult result = Convert(View(), plan =>
        {
            if (answered)
                return ConfirmationResponse.Proceed;

            answered = true;

            Assert.Equal(2, plan.Files.Count(f => f.Action == PlannedAction.Refuse));

            return new ConfirmationResponse(
                ConfirmationChoice.ChooseSourceEncoding,
                "windows-1252",
                [french]);
        });

        Assert.Equal(OrchestrationOutcome.Converted, result.Outcome);

        // The one that was answered for converted, read as the chosen encoding.
        Assert.Equal(
            "Le café était déjà prêt",
            Encoding.UTF8.GetString(File.ReadAllBytes(french)));

        // The one that was not is still refused, and still exactly as it was.
        Assert.Equal(russianBefore, File.ReadAllBytes(russian));
        Assert.Equal(
            PlannedAction.Refuse,
            Assert.Single(result.Plan!.Files, f => f.RelativePath == "russian.txt").Action);
    }

    [Fact]
    public void ChoosingAnEncodingForNoFilesChangesNothing()
    {
        // Unticking everything is a way of saying "none of these". It must not be read
        // as approval of a conversion with an empty scope.
        string path = Write("ambiguous.txt", "Le café était déjà prêt", "windows-1252");
        byte[] before = File.ReadAllBytes(path);

        OrchestrationResult result = Convert(View(), _ => new ConfirmationResponse(
            ConfirmationChoice.ChooseSourceEncoding, "windows-1252", []));

        Assert.Equal(OrchestrationOutcome.Cancelled, result.Outcome);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    // ------------------------------------------------------- nothing gets modified

    [Fact]
    public void CancellingTheConfirmation_ModifiesNothing()
    {
        string jp = Write("jp.txt", "こんにちは世界。テキスト", "shift_jis");
        string plain = Write("plain.txt", "plain ascii here", "ascii");

        byte[] jpBefore = File.ReadAllBytes(jp);
        byte[] plainBefore = File.ReadAllBytes(plain);

        OrchestrationResult result = Convert(
            View(), _ => ConfirmationResponse.Cancel, backup: true);

        Assert.Equal(OrchestrationOutcome.Cancelled, result.Outcome);
        Assert.Equal(jpBefore, File.ReadAllBytes(jp));
        Assert.Equal(plainBefore, File.ReadAllBytes(plain));

        // Not even a backup, which would be a modification of the directory.
        Assert.Empty(Directory.GetFiles(_root, "*.bak"));
    }

    [Fact]
    public void AFileChangedAfterTheConfirmation_StopsTheWholeRun()
    {
        // The user reads a dialog; that takes time. What they approved was the files as
        // they were. All-or-nothing, as with -Apply: a plan is reviewed as a whole.
        string stable = Write("stable.txt", "plain ascii text", "ascii");
        string moving = Write("moving.txt", "other ascii text", "ascii");

        byte[] stableBefore = File.ReadAllBytes(stable);

        OrchestrationResult result = Convert(
            View(),
            Proceed,
            betweenPlanAndWrite: plan =>
            {
                // Stated, because it is load-bearing and was once silently false: a file
                // missing from the plan is a file FindStaleFiles never looks at, so a
                // dropped entry turns this test's real subject into a pass.
                Assert.Equal(2, plan.Files.Count);

                File.WriteAllBytes(moving, Encoding.UTF8.GetBytes("changed underneath"));
            });

        Assert.Equal(OrchestrationOutcome.PlanWentStale, result.Outcome);
        Assert.Contains("changed after the conversion was planned", result.Message);

        // Neither file, not just the one that moved.
        Assert.Equal(stableBefore, File.ReadAllBytes(stable));
        Assert.Equal("changed underneath", File.ReadAllText(moving));
    }

    [Fact]
    public void AFailedBackup_LeavesTheSourceUntouched()
    {
        string path = Write("backupfail.txt", "こんにちは世界。テキスト", "shift_jis");
        byte[] before = File.ReadAllBytes(path);

        // A directory where the .bak must go: the copy cannot succeed.
        Directory.CreateDirectory(path + ".bak");

        Convert(View(), Proceed, backup: true);

        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void ContentTheTargetCannotHold_LeavesTheSourceUntouched()
    {
        // Post-conversion verification cannot pass, so nothing is installed.
        string path = Write("unencodable.txt", "世界 مرحبا", "utf-8");
        byte[] before = File.ReadAllBytes(path);

        Convert(View(), Proceed, target: "windows-1252");

        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void APreviewNeverAsksAndNeverWrites()
    {
        // A preview writes nothing, so it is its own answer. Asking would be asking
        // permission to do nothing.
        string path = Write("jp.txt", "こんにちは世界。テキスト", "shift_jis");
        byte[] before = File.ReadAllBytes(path);

        var asked = false;

        OrchestrationResult result = Convert(
            View(),
            _ =>
            {
                asked = true;
                return ConfirmationResponse.Proceed;
            },
            preview: true);

        Assert.Equal(OrchestrationOutcome.Previewed, result.Outcome);
        Assert.False(asked);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void AnUndecidedEntryStopsTheRunRatherThanBeingConverted()
    {
        // The shape of the original defect, driven through the real sequence. An entry
        // nobody classified must never read as one that is safe to convert.
        string path = Write("jp.txt", "こんにちは世界。テキスト", "shift_jis");
        byte[] before = File.ReadAllBytes(path);

        ConversionReportEntry entry = Assert.Single(View());

        Assert.Null(entry.Action);
        Assert.Null(entry.SourceInterpretation);

        // A conversion pass is what decides. Planning without one cannot proceed.
        Assert.Throws<InvalidOperationException>(() => ConversionPlan.FromEntries(
            [entry], _root, "utf-8", targetHasBom: false,
            backupEnabled: false, explicitSource: null));

        Assert.Equal(before, File.ReadAllBytes(path));
    }
}
