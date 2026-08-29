using System.Text;
using System.Text.Json;

namespace EncodingChecker.Tests;

/// <summary>
/// The contract for <c>-Plan</c> and <c>-Apply</c>.
///
/// A preview whose only guarantee is "we looked at these files once" is worth very
/// little: between the preview and the conversion the directory can change, and a
/// second detection pass over changed bytes can reach different conclusions than the
/// one the user read and approved. So the plan records the SHA-256 of every file it
/// schedules, and applying it verifies each hash before anything is written.
///
/// The invariants pinned here are: planning writes nothing; applying converts exactly
/// what was previewed without detecting again; and a plan that no longer describes the
/// files on disk is refused whole rather than applied in part.
/// </summary>
public sealed class ConversionPlanTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_plan_").FullName;

    private string PlanPath => Path.Combine(_root, "plan.json");

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

    private static int Run(params string[] args)
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;

        try
        {
            Console.SetOut(new StringWriter());
            Console.SetError(new StringWriter());

            return Program.RunConsoleMode(args);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private string Write(string name, string text, string charset)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, Encoding.GetEncoding(charset).GetBytes(text));
        return path;
    }

    private static Dictionary<string, byte[]> Snapshot(string directory) =>
        Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(p => p, File.ReadAllBytes);

    private int Plan(params string[] extra) =>
        Run(["-BasePath", _root, "-Target", "utf-8", "-Plan", PlanPath, "-Quiet", .. extra]);

    private ConversionPlan LoadPlan()
    {
        ConversionPlan? plan = ConversionPlan.Load(PlanPath, out string? error);

        Assert.Null(error);
        Assert.NotNull(plan);

        return plan;
    }

    private PlannedFile PlannedFor(string name) =>
        Assert.Single(
            LoadPlan().Files,
            f => string.Equals(f.RelativePath, name, StringComparison.Ordinal));

    /// <summary>Edits the plan on disk, the way someone with a text editor would.</summary>
    private void Rewrite(Action<Dictionary<string, JsonElement>> edit)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(PlanPath));

        Dictionary<string, JsonElement> fields = document.RootElement
            .EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());

        edit(fields);
        File.WriteAllText(PlanPath, JsonSerializer.Serialize(fields));
    }

    [Fact]
    public void PlanningWritesNothing()
    {
        // The one thing a dry run must never do.
        Write("jp.txt", "こんにちは世界。日本語のテキストです。", "shift_jis");
        Write("ru.txt", "Привет мир, это русский текст", "koi8-r");
        Write("plain.txt", "just ascii here", "ascii");

        Dictionary<string, byte[]> before = Snapshot(_root);

        Assert.Equal(0, Plan("-From", "shift_jis"));

        // The plan file lands in the same directory, so compare the originals rather
        // than the directory listing.
        foreach ((string path, byte[] content) in before)
            Assert.Equal(content, File.ReadAllBytes(path));

        Assert.True(File.Exists(PlanPath));
    }

    [Fact]
    public void ApplyingConvertsExactlyWhatWasPreviewed()
    {
        const string text = "こんにちは世界。日本語のテキストです。";
        string path = Write("jp.txt", text, "shift_jis");

        Assert.Equal(0, Plan("-From", "shift_jis"));
        Assert.Equal(PlannedAction.Convert, PlannedFor("jp.txt").Action);

        Assert.Equal(0, Run("-Apply", PlanPath));
        Assert.Equal(text, Encoding.UTF8.GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void AFileChangedAfterPlanningInvalidatesTheWholePlan()
    {
        // All-or-nothing on purpose. Converting the files that still match would apply a
        // plan the user reviewed as a whole to a directory that is no longer the one they
        // reviewed - and the files most likely to have changed are the ones something
        // else is actively writing.
        string stable = Write("stable.txt", "こんにちは世界。テキスト", "shift_jis");
        string moved = Write("moved.txt", "さようなら世界。テキスト", "shift_jis");

        Assert.Equal(0, Plan("-From", "shift_jis"));

        byte[] stableBefore = File.ReadAllBytes(stable);
        File.WriteAllBytes(moved, Encoding.UTF8.GetBytes("replaced after the plan"));

        Assert.Equal(3, Run("-Apply", PlanPath));

        // Neither file was touched, not only the one that changed.
        Assert.Equal(stableBefore, File.ReadAllBytes(stable));
        Assert.Equal("replaced after the plan", File.ReadAllText(moved));
    }

    [Fact]
    public void AFileDeletedAfterPlanningInvalidatesThePlan()
    {
        string kept = Write("kept.txt", "こんにちは世界。テキスト", "shift_jis");
        string removed = Write("removed.txt", "さようなら世界。テキスト", "shift_jis");

        Assert.Equal(0, Plan("-From", "shift_jis"));

        byte[] keptBefore = File.ReadAllBytes(kept);
        File.Delete(removed);

        Assert.Equal(3, Run("-Apply", PlanPath));
        Assert.Equal(keptBefore, File.ReadAllBytes(kept));
    }

    [Fact]
    public void ApplyingUsesThePlansEncodingRatherThanDetectingAgain()
    {
        // The point of the whole feature. These bytes detect as one thing and were
        // planned as another, so the resulting text says which pass decided.
        byte[] bytes = Encoding.GetEncoding("windows-1252").GetBytes("café");
        string path = Path.Combine(_root, "reinterpreted.txt");
        File.WriteAllBytes(path, bytes);

        Assert.Equal(0, Plan("-From", "koi8-r"));
        Assert.Equal("koi8-r", PlannedFor("reinterpreted.txt").SourceEncoding);

        Assert.Equal(0, Run("-Apply", PlanPath));

        Assert.Equal(
            Encoding.GetEncoding("koi8-r").GetString(bytes),
            Encoding.UTF8.GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void ARefusedFileIsRecordedAsRefusedAndNeverConverted()
    {
        byte[] original =
            Encoding.GetEncoding("windows-1252").GetBytes("Le café était déjà prêt");
        string path = Path.Combine(_root, "ambiguous.txt");
        File.WriteAllBytes(path, original);

        Assert.Equal(0, Plan());

        PlannedFile planned = PlannedFor("ambiguous.txt");

        Assert.Equal(PlannedAction.Refuse, planned.Action);
        Assert.True(planned.NeedsSourceChoice);
        Assert.Contains("Automatic conversion of legacy text is disabled", planned.Reason);

        Assert.Equal(0, Run("-Apply", PlanPath));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void TheBackupChoiceIsTakenFromThePlanNotFromTheApplyingRun()
    {
        // What the user reviewed included whether originals would be kept. Applying must
        // not quietly convert without backups because the second command line omitted a
        // flag the first one carried.
        string path = Write("jp.txt", "こんにちは世界。テキスト", "shift_jis");

        Assert.Equal(0, Plan("-Backup", "-From", "shift_jis"));
        Assert.True(LoadPlan().BackupEnabled);

        Assert.Equal(0, Run("-Apply", PlanPath));

        Assert.True(File.Exists(path + ".bak"));
        var metadata = JsonSerializer.Deserialize<ConversionMetadata>(
            File.ReadAllText(ConversionMetadataStore.MetadataPathFor(path)))!;
        Assert.Equal(
            ConversionMetadataStore.ComputeSha256(path + ".bak"),
            metadata.OriginalSha256);
    }

    [Fact]
    public void EveryScheduledFileCarriesTheHashItHadWhenPlanned()
    {
        string path = Write("jp.txt", "こんにちは世界。テキスト", "shift_jis");

        Assert.Equal(0, Plan("-From", "shift_jis"));

        Assert.Equal(
            ConversionMetadataStore.ComputeSha256(path),
            PlannedFor("jp.txt").Sha256);
    }

    [Fact]
    public void AnUnreadablePlanIsReportedRatherThanIgnored()
    {
        File.WriteAllText(PlanPath, "{ not json at all");

        Assert.Equal(1, Run("-Apply", PlanPath));
        Assert.Null(ConversionPlan.Load(PlanPath, out string? error));
        Assert.NotNull(error);
    }

    [Fact]
    public void APlanFromAFutureVersionIsRefusedRatherThanGuessedAt()
    {
        Write("jp.txt", "こんにちは世界。テキスト", "shift_jis");
        Assert.Equal(0, Plan());

        Rewrite(fields => fields["PlanVersion"] = JsonSerializer.SerializeToElement(99));

        Assert.Equal(1, Run("-Apply", PlanPath));
    }

    [Fact]
    public void PlanAndApplyCannotRunTogether()
    {
        // Two commands with a human decision in between is the entire mechanism.
        Write("jp.txt", "こんにちは世界。テキスト", "shift_jis");

        Assert.Equal(1, Run(
            "-BasePath", _root, "-Target", "utf-8",
            "-Plan", PlanPath, "-Apply", PlanPath));
    }

    [Theory]
    [InlineData("-Backup")]
    [InlineData("-Target", "utf-16")]
    [InlineData("-From", "koi8-r")]
    [InlineData("-BasePath", ".")]
    public void ApplyRejectsFlagsThePlanAlreadyFixes(params string[] flag)
    {
        // -Backup is the one that matters: silently ignoring it would let a user write
        // what reads as an instruction to keep the originals and get a run that does not.
        Write("jp.txt", "こんにちは世界。テキスト", "shift_jis");
        Assert.Equal(0, Plan());

        Assert.Equal(1, Run(["-Apply", PlanPath, .. flag]));
    }

    [Fact]
    public void ApplyRejectsAPlanThatDoesNotExist()
    {
        Assert.Equal(1, Run("-Apply", Path.Combine(_root, "absent.json")));
    }

    [Theory]
    [InlineData("-DetectOnly")]
    [InlineData("-Validate", "utf-8")]
    public void PlanIsRejectedInModesThatConvertNothing(params string[] mode)
    {
        Assert.Equal(1, Run(
            ["-BasePath", _root, "-Target", "utf-8", "-Plan", PlanPath, .. mode]));
    }

    [Fact]
    public void ThePlanDescribesTheConversionAndNotOnlyTheFiles()
    {
        // "-Apply plan.json" must need no ambient option state to mean something exact,
        // so everything that shapes the conversion has to be in the file.
        Write("jp.txt", "こんにちは世界。テキスト", "shift_jis");

        Assert.Equal(0, Plan("-Backup"));

        ConversionPlan plan = LoadPlan();

        Assert.Equal(ConversionPlan.CurrentPlanVersion, plan.PlanVersion);
        Assert.Equal(ConversionSemantics.Current, plan.SemanticsVersion);
        Assert.Equal("utf-8", plan.TargetEncoding);
        Assert.False(plan.TargetHasBom);
        Assert.True(plan.BackupEnabled);
        Assert.Null(plan.ExplicitSourceEncoding);
        Assert.Equal(Path.GetFullPath(_root), plan.BaseDirectory);
        Assert.NotEmpty(plan.EcVersion);

        Assert.True(plan.Semantics.StrictDecoding);
        Assert.True(plan.Semantics.StrictEncoding);
        Assert.True(plan.Semantics.OutputVerification);
        Assert.True(plan.Semantics.AtomicInstall);
        Assert.True(plan.Semantics.LegacyRequiresExplicitSource);
    }

    [Fact]
    public void ExplicitSourceSelectionIsRecordedAsSuch()
    {
        Write("jp.txt", "こんにちは世界。テキスト", "shift_jis");

        Assert.Equal(0, Plan("-From", "shift_jis"));

        ConversionPlan plan = LoadPlan();

        Assert.Equal("shift_jis", plan.ExplicitSourceEncoding);
        Assert.True(Assert.Single(plan.Files).SourceWasSpecified);
        Assert.Contains("detection bypassed", plan.Summarize());
    }

    [Fact]
    public void APlanMadeUnderDifferentConversionBehaviourIsRefused()
    {
        // The schema can be identical while the conversion it describes is not. What the
        // user approved was a conversion, not a file listing.
        string path = Write("jp.txt", "こんにちは世界。テキスト", "shift_jis");
        byte[] original = File.ReadAllBytes(path);

        Assert.Equal(0, Plan());
        Rewrite(fields =>
            fields["SemanticsVersion"] =
                JsonSerializer.SerializeToElement(ConversionSemantics.Current + 1));

        Assert.Equal(1, Run("-Apply", PlanPath));
        Assert.Equal(original, File.ReadAllBytes(path));

        Assert.Null(ConversionPlan.Load(PlanPath, out string? error));
        Assert.Contains("different conversion behaviour", error);
    }

    [Fact]
    public void APlanAppliedFromACopyStillConvertsTheTreeItWasApprovedFor()
    {
        // A plan carrying absolute paths that is copied alongside its tree still names
        // the original tree, and every hash matches, because those are the files it was
        // made from. Resolving against the recorded root makes the intent explicit
        // instead of incidental: the plan is about one directory, and says which.
        const string text = "こんにちは世界。テキスト";
        string original = Write("jp.txt", text, "shift_jis");

        Assert.Equal(0, Plan("-From", "shift_jis"));

        string copy = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(copy);

        try
        {
            string copiedPlan = Path.Combine(copy, "plan.json");
            File.Copy(original, Path.Combine(copy, "jp.txt"));
            File.Copy(PlanPath, copiedPlan);

            Assert.Equal(0, Run("-Apply", copiedPlan));

            // The tree the plan named was converted; the copy was not touched.
            Assert.Equal(text, Encoding.UTF8.GetString(File.ReadAllBytes(original)));
            Assert.Equal(
                Encoding.GetEncoding("shift_jis").GetBytes(text),
                File.ReadAllBytes(Path.Combine(copy, "jp.txt")));
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    [Fact]
    public void APlanWhoseDirectoryIsGoneIsRefusedRatherThanResolvedElsewhere()
    {
        Write("jp.txt", "こんにちは世界。テキスト", "shift_jis");
        Assert.Equal(0, Plan());

        Rewrite(fields => fields["BaseDirectory"] = JsonSerializer.SerializeToElement(
            Path.Combine(_root, "no-such-directory")));

        Assert.Equal(3, Run("-Apply", PlanPath));
    }

    [Fact]
    public void AnEntryReachingOutsideThePlansDirectoryIsRefused()
    {
        // A plan is an ordinary file that anyone can edit or receive from someone else.
        // Its paths must stay inside the directory it claims to be about.
        string outside = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        byte[] content = Encoding.GetEncoding("shift_jis").GetBytes("こんにちは世界。テキスト");
        File.WriteAllBytes(outside, content);

        try
        {
            Write("jp.txt", "こんにちは世界。テキスト", "shift_jis");
            Assert.Equal(0, Plan("-From", "shift_jis"));

            Rewrite(fields =>
            {
                List<JsonElement> files = [.. fields["Files"].EnumerateArray()];

                Dictionary<string, JsonElement> entry = files[0]
                    .EnumerateObject()
                    .ToDictionary(p => p.Name, p => p.Value.Clone());

                entry["RelativePath"] = JsonSerializer.SerializeToElement(
                    Path.GetRelativePath(_root, outside));
                entry["Sha256"] = JsonSerializer.SerializeToElement(
                    ConversionMetadataStore.ComputeSha256(outside));

                fields["Files"] = JsonSerializer.SerializeToElement(new[] { entry });
            });

            Assert.Equal(3, Run("-Apply", PlanPath));

            // Untouched, and still Shift_JIS rather than the UTF-8 it would have become.
            Assert.Equal(content, File.ReadAllBytes(outside));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public void TheDisplayedCategoriesSumToTheSelectedPopulation()
    {
        // A category total that does not add up is how a mechanism that is actually safe
        // loses the confidence it earned.
        Write("jp.txt", "こんにちは世界。日本語のテキストです。", "shift_jis");
        Write("ambiguous.txt", "Le café était déjà prêt", "windows-1252");
        Write("plain.txt", "just ascii here", "ascii");

        Assert.Equal(0, Plan());

        ConversionPlan plan = LoadPlan();
        string summary = plan.Summarize();

        int Line(string label)
        {
            string line = Assert.Single(
                summary.Split(Environment.NewLine),
                l => l.StartsWith(label, StringComparison.Ordinal));

            return int.Parse(line[label.Length..].Trim());
        }

        // The indented pair breaks down "Will convert" and is not part of the sum.
        Assert.Equal(
            Line("Selected:"),
            Line("Will convert:")
            + Line("Already in target encoding:")
            + Line("Encoding not identified:")
            + Line("Needs legacy source choice:")
            + Line("Refused, unreadable:"));

        Assert.Equal(plan.Files.Count, Line("Selected:"));
    }

    [Fact]
    public void TheSummaryAccountsForEverySelectedFile()
    {
        // A reader who cannot check that the parts sum to the whole has to trust the
        // numbers instead, which is the opposite of what a preflight is for.
        Write("jp.txt", "こんにちは世界。日本語のテキストです。", "shift_jis");
        Write("ambiguous.txt", "Le café était déjà prêt", "windows-1252");
        Write("plain.txt", "just ascii here", "ascii");

        Assert.Equal(0, Plan());

        ConversionPlan plan = LoadPlan();

        Assert.Equal(3, plan.Files.Count);
        Assert.Equal(
            plan.Files.Count,
            plan.Files.Count(f => f.Action == PlannedAction.Convert)
            + plan.Files.Count(f => f.Action == PlannedAction.Unchanged)
            + plan.Files.Count(f => f.Action == PlannedAction.Skip)
            + plan.Files.Count(f => f.Action == PlannedAction.Refuse));

        Assert.Contains("No files modified.", plan.Summarize());
    }
}
