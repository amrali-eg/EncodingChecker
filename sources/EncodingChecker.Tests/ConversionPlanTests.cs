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
            f => string.Equals(Path.GetFileName(f.Path), name, StringComparison.Ordinal));

    [Fact]
    public void PlanningWritesNothing()
    {
        // The one thing a dry run must never do.
        Write("jp.txt", "こんにちは世界。日本語のテキストです。", "shift_jis");
        Write("ru.txt", "Привет мир, это русский текст", "koi8-r");
        Write("plain.txt", "just ascii here", "ascii");

        Dictionary<string, byte[]> before = Snapshot(_root);

        Assert.Equal(0, Plan());

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

        Assert.Equal(0, Plan());
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

        Assert.Equal(0, Plan());

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

        Assert.Equal(0, Plan());

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
        Assert.True(planned.MayChangeText);
        Assert.NotEmpty(planned.CompetingEncodings);
        Assert.Contains("could not be determined uniquely", planned.Reason);

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

        Assert.Equal(0, Plan("-Backup"));
        Assert.True(LoadPlan().BackupEnabled);

        Assert.Equal(0, Run("-Apply", PlanPath));

        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal(
            RestoreAvailability.Available,
            ConversionMetadataStore.Inspect(path).Availability);
    }

    [Fact]
    public void EveryScheduledFileCarriesTheHashItHadWhenPlanned()
    {
        string path = Write("jp.txt", "こんにちは世界。テキスト", "shift_jis");

        Assert.Equal(0, Plan());

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

        using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(PlanPath)))
        {
            Dictionary<string, JsonElement> fields = document.RootElement
                .EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.Clone());

            fields["PlanVersion"] = JsonSerializer.SerializeToElement(99);
            File.WriteAllText(PlanPath, JsonSerializer.Serialize(fields));
        }

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
