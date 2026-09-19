using System.Text;
using System.Windows.Forms;

namespace EncodingChecker.Tests;

/// <summary>
/// The confirmation dialog is the only part of the safety model a GUI user ever reads, and
/// it describes the outcomes at exactly the moment the user is asked to approve them. Layout
/// code that throws on an unusual mix of outcomes would fail there.
///
/// These build it against real plans rather than asserting on pixels: every outcome mix,
/// on an STA thread, checking that it constructs and that what it says matches the plan
/// it was given.
/// </summary>
public sealed class ConversionConfirmationFormTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_dialog_").FullName;

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

    private void Write(string name, string text, string charset) =>
        File.WriteAllBytes(
            Path.Combine(_root, name), Encoding.GetEncoding(charset).GetBytes(text));

    private void WriteBytes(string name, byte[] bytes) =>
        File.WriteAllBytes(Path.Combine(_root, name), bytes);

    /// <summary>A plan over whatever is currently in the directory.</summary>
    private ConversionPlan Plan(bool backup = true, string target = "utf-8")
    {
        var entries = new EntrySink();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                IncludeSubdirectories = true,
                IncludePatterns = ["*"],
                Action = ScanAction.Convert,
                TargetCharset = target,
                TargetWriteBom = false,
                WhatIf = true,
            },
            entries.Add,
            CancellationToken.None);

        return ConversionPlan.FromEntries(
            entries, _root, target, targetHasBom: false,
            backupEnabled: backup, explicitSource: null);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;

            foreach (Control nested in Descendants(child))
                yield return nested;
        }
    }

    private static string AllText(Control root) =>
        string.Join("\n", Descendants(root).Select(c => c.Text));

    private static IEnumerable<Control> Named(Control root, string name) =>
        Descendants(root).Where(c => c.Name == name);

    // The count the dialog shows beside a category label, or null when the category is hidden.
    private static string? CountShownFor(Control root, string label)
    {
        Label? cell = Descendants(root).OfType<Label>()
            .FirstOrDefault(l => l.Text == label && l.Parent is TableLayoutPanel);

        if (cell?.Parent is not TableLayoutPanel table)
            return null;

        return table.GetControlFromPosition(0, table.GetPositionFromControl(cell).Row)?.Text;
    }

    [Fact]
    public void TheSummaryShowsEveryOutcomeWithItsCount()
    {
        // A different number of files in each of the five categories the dialog reports, so a
        // count shown against the wrong label cannot go unnoticed: 1, 2, 3, 4 and 5.
        //
        // Each fixture reaches its category for a reason: a UTF-8 BOM the target omits is
        // rewritten; Shift_JIS text needs the user to name its source; UTF-8 without a BOM
        // already matches; random bytes identify as no encoding, and the fixed seeds keep them
        // that way; two leading BOMs are refused for a reason no source choice can fix. If a
        // detector change moves a fixture, the plan-summary assertions below fail first.
        WriteBytes("convertible1.txt", [0xEF, 0xBB, 0xBF, .. "hello world"u8]);

        for (int i = 1; i <= 2; i++)
            Write($"jp{i}.txt", "こんにちは世界。日本語のテキストです。", "shift_jis");

        for (int i = 1; i <= 3; i++)
            WriteBytes($"already{i}.txt", Encoding.UTF8.GetBytes("already utf-8 世界"));

        for (int i = 1; i <= 4; i++)
        {
            byte[] unidentified = new byte[4096];
            new Random(418 + i).NextBytes(unidentified);
            WriteBytes($"unknown{i}.bin", unidentified);
        }

        for (int i = 1; i <= 5; i++)
            WriteBytes($"double-bom{i}.txt", [0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF, .. "hello world"u8]);

        ConversionPlan plan = Plan();
        ConversionPlanSummary summary = plan.Summary;

        // The fixtures reach the outcomes they are named for, or the checks below prove nothing.
        Assert.Equal(15, summary.Selected);
        Assert.Equal(1, summary.ReadyToConvert);
        Assert.Equal(2, summary.NeedsSourceChoice);
        Assert.Equal(3, summary.AlreadyTarget);
        Assert.Equal(4, summary.NotIdentified);
        Assert.Equal(5, summary.OtherRefusals);

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);

            Assert.Equal("1", CountShownFor(form, "Ready to convert"));
            Assert.Equal("2", CountShownFor(form, "Needs a source encoding"));
            Assert.Equal("3", CountShownFor(form, "Already in the target encoding"));
            Assert.Equal("4", CountShownFor(form, "Encoding not identified"));
            Assert.Equal("5", CountShownFor(form, "Cannot be processed safely"));

            // Only the files that need a source are offered a choice.
            var refused = Assert.IsType<ListView>(
                Assert.Single(Named(form, "lstRefusedFiles")));
            Assert.Equal(
                ["jp1.txt", "jp2.txt"],
                refused.Items.Cast<ListViewItem>().Select(i => i.Text).Order());
            Assert.Single(Named(form, "lstSourceEncoding"));
            Assert.Single(Named(form, "btnConfirmSourceEncoding"));
        });
    }

    [Fact]
    public void ItBuildsWithoutASourceChoicePanelWhenNothingNeedsOne()
    {
        // The common case, and the one where the refusal panel must not appear at all.
        WriteBytes("convertible.txt", [0xEF, 0xBB, 0xBF, .. "hello world"u8]);

        ConversionPlan plan = Plan();

        PlannedFile planned = Assert.Single(plan.Files);
        Assert.Equal(PlannedAction.Convert, planned.Action);
        Assert.False(planned.NeedsSourceChoice);

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);

            Assert.Equal("1", CountShownFor(form, "Ready to convert"));

            // Categories with no files are not listed at all.
            Assert.Null(CountShownFor(form, "Needs a source encoding"));
            Assert.Null(CountShownFor(form, "Already in the target encoding"));
            Assert.Null(CountShownFor(form, "Encoding not identified"));
            Assert.Null(CountShownFor(form, "Cannot be processed safely"));

            Assert.Empty(Named(form, "lstRefusedFiles"));
            Assert.Empty(Named(form, "lstSourceEncoding"));
            Assert.Empty(Named(form, "btnConfirmSourceEncoding"));
        });
    }

    /// <summary>
    /// A plan whose root does not contain its own files. The GUI reaches this whenever the
    /// directory box changes after a scan - by typing, by picking a recent entry, or by
    /// dropping a folder on it - because the results list is only cleared when a scan
    /// starts.
    /// </summary>
    private ConversionPlan PlanRootedElsewhere(string target = "utf-8")
    {
        var entries = new EntrySink();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                IncludeSubdirectories = true,
                IncludePatterns = ["*"],
                Action = ScanAction.Convert,
                TargetCharset = target,
                TargetWriteBom = false,
                WhatIf = true,
            },
            entries.Add,
            CancellationToken.None);

        string elsewhere = Directory.CreateTempSubdirectory("ec_elsewhere_").FullName;

        return ConversionPlan.FromEntries(
            entries, elsewhere, target, targetHasBom: false,
            backupEnabled: true, explicitSource: null);
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        Descendants(root).OfType<T>().Single(c => c.Name == name);

    [Fact]
    public void ASourceChoiceThatCannotBeAppliedIsRefusedRatherThanDropped()
    {
        // Each refused row carries the path this review resolved for it. When the plan's
        // root does not contain the file that path is null, and the ticked set used to be
        // built by filtering those rows out - so the button did nothing, and the run
        // reported "Conversion cancelled. No files were modified." The user had ticked a
        // file and chosen an encoding; the cancellation was neither theirs nor explained.
        Write("legacy.txt", "Le café était déjà prêt", "windows-1252");
        ConversionPlan plan = PlanRootedElsewhere();

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);
            form.CreateControl();

            var refused = Find<ListView>(form, "lstRefusedFiles");
            var chooser = Find<ComboBox>(form, "lstSourceEncoding");
            var confirm = Find<Button>(form, "btnConfirmSourceEncoding");

            Assert.Single(refused.Items);

            // The row is listed, and the path behind it is exactly what is missing.
            Assert.Null(refused.Items[0].Tag);

            refused.Items[0].Checked = true;
            chooser.SelectedItem = "windows-1252";

            Assert.True(
                confirm.Enabled,
                "The confirm button was disabled, so the refusal below could not be reached.");

            string? problem = form.DescribeUnusableScope();

            Assert.False(
                problem is null,
                "The review would have applied a choice it cannot scope to any file, "
                + "which resolves to a cancellation the user never asked for.");

            Assert.Contains("no longer inside this review", problem!, StringComparison.Ordinal);
            Assert.Contains("Run View again", problem, StringComparison.Ordinal);

            // Refusing means staying put: nothing is chosen and no result is reported.
            Assert.Equal(DialogResult.None, form.DialogResult);
            Assert.Empty(form.ChosenFiles);
        });
    }

    [Fact]
    public void InteractiveControlsExposeStableAutomationIds()
    {
        Write("legacy.txt", "Le café était déjà prêt", "windows-1252");
        ConversionPlan plan = Plan();

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);
            string[] names = [.. Descendants(form).Select(control => control.Name)];

            Assert.Equal("ConversionConfirmationForm", form.Name);
            Assert.Contains("lstRefusedFiles", names);
            Assert.Contains("lstSourceEncoding", names);
            Assert.Contains("btnConfirmSourceEncoding", names);
            Assert.Contains("btnCancelConversionReview", names);
            Assert.Contains("btnProceedConversion", names);
        });
    }

    /// <summary>
    /// One planned file carrying the given BOM-less advisory reason code.
    /// </summary>
    private ConversionPlan PlanWithAdvisory(string reasonCode, string reason)
    {
        return new ConversionPlan
        {
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            EcVersion = "test",
            BaseDirectory = _root,
            TargetEncoding = "utf-8",
            TargetHasBom = false,
            BackupEnabled = true,
            ExplicitSourceEncoding = "utf-16",
            Files =
            [
                new PlannedFile
                {
                    RelativePath = "bomless.txt",
                    Size = 20,
                    Sha256 = new string('0', 64),
                    Action = PlannedAction.Convert,
                    SourceEncoding = "utf-16",
                    SourceCodePage = 1200,
                    SourceHasBom = false,
                    SourceWasSpecified = true,
                    DetectedEncoding = "utf-16",
                    DetectedCodePage = 1200,
                    SourceInterpretation = SourceInterpretation.ExplicitSource,
                    ReasonCode = reasonCode,
                    Reason = reason,
                },
            ],
        };
    }

    [Fact]
    public void ItShowsAnExplicitSourceThatMatchesAnUnprovableEstimate()
    {
        // v3.10.1 established that agreeing with an estimate EC has already called
        // unprovable is the more dangerous of the two cases - the caller repeating a
        // wrong guess. The review dialog matched only the contradicting reason code, so
        // it warned about the safer choice and stayed silent for the riskier one. That
        // is the same inversion v3.10.1 fixed, one surface over.
        ConversionPlan plan = PlanWithAdvisory(
            ConversionReasonCodes.ExplicitSourceOnUnprovableBomlessUnicode,
            "Your selection matches EC's estimate, but that estimate is not evidence.");

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);
            string text = AllText(form);

            Assert.Contains("cannot prove", text);
            Assert.Contains("bomless.txt", text);
            Assert.Contains("not evidence", text);
            Assert.Contains("Convert 1 file(s)", text);
            Assert.DoesNotContain("Needs a source encoding", text);
        });
    }

    [Fact]
    public void TheAdvisoryDoesNotClaimTheChoiceDiffersWhenItAgrees()
    {
        // The old wording said the estimate "differs from your source choice", which is
        // untrue for the agreeing case and would tell the reader the opposite of the
        // risk. Each file's own reason carries the distinction instead.
        ConversionPlan plan = PlanWithAdvisory(
            ConversionReasonCodes.ExplicitSourceOnUnprovableBomlessUnicode,
            "Your selection matches EC's estimate.");

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);

            Assert.DoesNotContain("differs from your source choice", AllText(form));
        });
    }

    [Fact]
    public void AnOrdinaryConversionShowsNoAdvisoryAtAll()
    {
        // The control. Both tests above would pass against a dialog that showed the
        // advisory unconditionally.
        ConversionPlan plan = PlanWithAdvisory(reasonCode: null!, reason: null!);

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);

            Assert.DoesNotContain("taken on trust", AllText(form));
        });
    }

    [Fact]
    public void ItShowsBomlessUnicodeDisagreementWithoutCallingItARefusal()
    {
        var plan = new ConversionPlan
        {
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            EcVersion = "test",
            BaseDirectory = _root,
            TargetEncoding = "utf-8",
            TargetHasBom = false,
            BackupEnabled = true,
            ExplicitSourceEncoding = "windows-1252",
            Files =
            [
                new PlannedFile
                {
                    RelativePath = "bomless.txt",
                    Size = 20,
                    Sha256 = new string('0', 64),
                    Action = PlannedAction.Convert,
                    SourceEncoding = "windows-1252",
                    SourceCodePage = 1252,
                    SourceHasBom = false,
                    SourceWasSpecified = true,
                    SourceInterpretation = SourceInterpretation.ExplicitSource,
                    ReasonCode = ConversionReasonCodes
                        .ExplicitSourceDiffersFromBomlessUnicodeEstimate,
                    Reason =
                        "EC estimated BOM-less utf-16BE, but you selected windows-1252.",
                },
            ],
        };

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);
            string text = AllText(form);

            // Wording is now shared by both advisory cases, so the assertion pins the
            // per-file reason - which is what distinguishes them - rather than the
            // heading that no longer mentions disagreement.
            Assert.Contains("cannot prove the byte order", text);
            Assert.Contains("bomless.txt", text);
            Assert.Contains("but you selected windows-1252", text);
            Assert.Contains("Convert 1 file(s)", text);
            Assert.DoesNotContain("Needs a source encoding", text);
        });
    }

    [Fact]
    public void ItBuildsWhenEverythingIsRefused()
    {
        // Nothing to convert. The button has to say so rather than offering an action
        // that would do nothing.
        Write("a.txt", "Le café était déjà prêt", "windows-1252");
        Write("b.txt", "Привет мир, это русский", "koi8-r");

        ConversionPlan plan = Plan();

        Assert.All(plan.Files, f => Assert.Equal(PlannedAction.Refuse, f.Action));

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);

            string text = AllText(form);

            Assert.Contains("require their source encoding to be identified or confirmed", text);
            Assert.Contains("Nothing ready to convert", text);
            Assert.DoesNotContain("Convert 1 file", text);
        });
    }

    [Fact]
    public void ItNamesTheDetectedLegacyEncodingAndOffersAnExplicitChoice()
    {
        // A detected legacy label alone is not permission to rewrite the file. The
        // review must name it and offer an explicit source choice.
        Write("ambiguous.txt", "Le café était déjà prêt", "windows-1252");

        ConversionPlan plan = Plan();
        PlannedFile refused = Assert.Single(plan.Files);

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);

            List<string> cells =
            [
                .. Descendants(form)
                    .OfType<ListView>()
                    .SelectMany(v => v.Items.Cast<ListViewItem>())
                    .SelectMany(i => i.SubItems.Cast<ListViewItem.ListViewSubItem>())
                    .Select(sub => sub.Text)
            ];

            Assert.Contains("ambiguous.txt", cells);
            Assert.Contains(refused.SourceEncoding, cells);

            // And the way out is offered, populated from the charsets EC supports.
            ComboBox chooser = Assert.Single(Descendants(form).OfType<ComboBox>());

            Assert.True(chooser.Items.Count > 1);
            Assert.Contains("windows-1252", chooser.Items.Cast<string>());
            Assert.True(chooser.Width >= 235, "the full source-encoding prompt must be visible");
        });
    }

    [Fact]
    public void ItOffersAnExplicitChoiceForAmbiguousBomlessUtf16()
    {
        // This is not legacy text: EC detects UTF-16LE, but the exact bytes also strictly
        // decode as UTF-16BE. Automatic conversion must refuse, while the review must give
        // the user the same explicit UTF-16/UTF-16BE choice that -From provides in CLI.
        string text = string.Concat(Enumerable.Repeat("\u4100\u0A00\u4200", 20));
        byte[] bytes = new UnicodeEncoding(
            bigEndian: true,
            byteOrderMark: false,
            throwOnInvalidBytes: true).GetBytes(text);
        WriteBytes("ambiguous-utf16be.txt", bytes);

        ConversionPlan plan = Plan();
        PlannedFile refused = Assert.Single(plan.Files);

        Assert.Equal(ConversionReasonCodes.AmbiguousBomlessUtf16, refused.ReasonCode);
        Assert.True(refused.NeedsSourceChoice);

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);
            ComboBox chooser = Assert.Single(Descendants(form).OfType<ComboBox>());
            ListView list = Assert.Single(Descendants(form).OfType<ListView>());

            Assert.Contains("utf-16", chooser.Items.Cast<string>());
            Assert.Contains("utf-16BE", chooser.Items.Cast<string>());
            Assert.Contains(
                "ambiguous-utf16be.txt",
                list.Items.Cast<ListViewItem>().Select(item => item.Text));
            Assert.Contains("cannot safely process", AllText(form));
        });
    }

    [Fact]
    public void SourcePickerOffersOnlyRuntimeSupportedCanonicalEncodings()
    {
        Write("ambiguous.txt", "Le café était déjà prêt", "windows-1252");

        ConversionPlan plan = Plan();

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);
            ComboBox chooser = Assert.Single(Descendants(form).OfType<ComboBox>());

            string[] offered =
            [
                .. chooser.Items.Cast<string>().Skip(1)
            ];
            string[] expected =
            [
                .. TextEncoding.SupportedEncodings.Select(encoding => encoding.WebName)
            ];

            Assert.Equal(expected, offered);
            Assert.Equal(offered.Length, offered.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(offered, name => Assert.NotNull(Encoding.GetEncoding(name)));

            int[] offeredCodePages =
            [
                .. offered.Select(name => Encoding.GetEncoding(name).CodePage)
            ];

            Assert.Equal(offeredCodePages.Length, offeredCodePages.Distinct().Count());
            Assert.Contains(949, offeredCodePages);
        });
    }

    [Fact]
    public void ItExposesTheReviewControlsToKeyboardAndAssistiveTechnology()
    {
        Write("legacy.txt", "Le caf\u00e9 \u20ac", "windows-1252");

        ConversionPlan plan = Plan();

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);

            ListView list = Assert.Single(Descendants(form).OfType<ListView>());
            ComboBox source = Assert.Single(Descendants(form).OfType<ComboBox>());
            Button confirmSource = Assert.Single(
                Descendants(form).OfType<Button>(),
                button => button.Text.StartsWith("Confirm for"));

            Assert.Equal("Files requiring source encoding", list.AccessibleName);
            Assert.False(string.IsNullOrWhiteSpace(list.AccessibleDescription));
            Assert.Equal("Source encoding for selected files", source.AccessibleName);
            Assert.False(string.IsNullOrWhiteSpace(source.AccessibleDescription));
            Assert.Equal("Confirm selected source encoding", confirmSource.AccessibleName);
            Assert.False(string.IsNullOrWhiteSpace(confirmSource.AccessibleDescription));

            // Enter and Escape retain their conventional, safe meanings.
            Assert.IsType<Button>(form.AcceptButton);
            Assert.IsType<Button>(form.CancelButton);
            Assert.Equal(DialogResult.Cancel, ((Button)form.CancelButton!).DialogResult);
        });
    }

    [Fact]
    public void ItMakesTheScopeOfAnEncodingChoiceUnmistakable()
    {
        // The button says how many files the choice would apply to, and the count moves
        // with the ticks. A user must never have to infer how far their answer reaches.
        Write("french.txt", "Le café était déjà prêt", "windows-1252");
        Write("russian.txt", "Привет мир, это русский текст", "koi8-r");

        ConversionPlan plan = Plan();

        Assert.Equal(2, plan.Files.Count(f => f.Action == PlannedAction.Refuse));

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);

            // ListView caches check state until it has a window handle, and only raises
            // ItemChecked once it does. Nothing here pumps messages; the handle is enough.
            form.CreateControl();
            _ = form.Handle;

            ListView list = Assert.Single(Descendants(form).OfType<ListView>());

            list.CreateControl();
            _ = list.Handle;

            // Everything the dialog asked about starts ticked, and the button says so.
            Assert.Equal(2, list.CheckedItems.Count);
            Assert.Contains("Confirm for 2 file(s)", AllText(form));

            list.Items[0].Checked = false;

            Assert.Contains("Confirm for 1 file(s)", AllText(form));

            // And with none ticked there is nothing to apply, so it cannot be pressed.
            list.Items[1].Checked = false;

            Button apply = Assert.Single(
                Descendants(form).OfType<Button>(),
                b => b.Text.StartsWith("Confirm for"));

            Assert.False(apply.Enabled);
            Assert.True(apply.Width >= 230, "the source-confirmation button must stay easy to read and click");
        });
    }

    [Fact]
    public void ItOffersEveryLegacyFileNamedInTheReview()
    {
        // A large batch used to show the true total in the explanation but silently
        // truncated the selectable list at 200 files.
        PlannedFile[] files =
        [
            .. Enumerable.Range(1, 310)
                .Select(i => new PlannedFile
                {
                    RelativePath = $"legacy-{i:D3}.txt",
                    Size = 1,
                    Sha256 = new string('0', 64),
                    Action = PlannedAction.Refuse,
                    SourceEncoding = "windows-1252",
                    SourceCodePage = 1252,
                    SourceHasBom = false,
                    SourceWasSpecified = false,
                    SourceInterpretation = SourceInterpretation.LegacyNeedsSourceChoice,
                })
        ];

        var plan = new ConversionPlan
        {
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            EcVersion = "test",
            BaseDirectory = _root,
            TargetEncoding = "utf-8",
            TargetHasBom = false,
            BackupEnabled = true,
            Files = files,
        };

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);
            form.CreateControl();
            _ = form.Handle;

            ListView list = Assert.Single(Descendants(form).OfType<ListView>());
            list.CreateControl();
            _ = list.Handle;

            Assert.Equal(310, list.Items.Count);
            Assert.Equal(310, list.CheckedItems.Count);
            Assert.Contains(
                "310 file(s) require their source encoding to be identified or confirmed",
                AllText(form));
            Assert.Contains("Confirm for 310 file(s)", AllText(form));
        });
    }

    [Fact]
    public void ItSaysWhetherOriginalsWillBeKept()
    {
        // Whether a conversion is undoable is part of what is being approved.
        Write("jp.txt", "こんにちは世界。テキスト", "shift_jis");

        ConversionPlan withBackups = Plan(backup: true);
        ConversionPlan without = Plan(backup: false);

        UiTest.OnStaThread(() =>
        {
            using var kept = new ConversionConfirmationForm(withBackups);
            using var lost = new ConversionConfirmationForm(without);

            Assert.Contains(".bak", AllText(kept));
            Assert.Contains("OFF", AllText(lost));
        });
    }

    [Fact]
    public void ItReportsThePlanItWasGivenRatherThanRecountingTheDirectory()
    {
        // The dialog must display the supplied plan. Rescanning or recounting here could
        // make the confirmation describe a different decision from the one that executes.
        Write("jp.txt", "こんにちは世界。日本語のテキストです。", "shift_jis");
        Write("ambiguous.txt", "Le café était déjà prêt", "windows-1252");

        ConversionPlan plan = Plan();

        int convert = plan.Files.Count(f => f.Action == PlannedAction.Convert);

        // Change the directory after planning. The dialog must still show the existing
        // plan, not the current directory contents.
        File.Delete(Path.Combine(_root, "jp.txt"));
        Write("late-arrival.txt", "added after the plan", "ascii");

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);

            string text = AllText(form);

            Assert.Contains($"{plan.Files.Count} selected file(s). Target:", text);
            Assert.DoesNotContain("late-arrival", text);
        });
    }
}
