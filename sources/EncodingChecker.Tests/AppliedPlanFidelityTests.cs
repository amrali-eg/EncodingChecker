using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// Applying a saved plan must never produce an outcome weaker than the one reviewed.
/// </summary>
/// <remarks>
/// A plan is re-decided rather than replayed, so a file that became unsafe after review
/// is still refused. That direction is deliberate. The opposite direction is the defect
/// this fixture exists for: <c>ConvertFiles</c> re-runs the whole policy, so any input
/// the plan schema did not carry was simply absent, and the policy answered a question
/// it had no evidence for.
/// <para>
/// The measured case: a UTF-8 file converted with <c>-From windows-1252</c> is refused
/// directly, because the explicit source contradicts a proven Unicode reading. The plan
/// recorded that refusal. Applying it converted the file anyway, wrote mojibake that
/// output verification structurally cannot catch (both sides decode through the same
/// wrong codec), and exited 0 with a journal claiming the action had been Convert.
/// </para>
/// <para>
/// Two independent defences are tested separately below, because either one alone would
/// make the end-to-end test pass and hide the loss of the other.
/// </para>
/// </remarks>
public sealed class AppliedPlanFidelityTests : IDisposable
{
    private const int ExpectedClean = 0;
    private const int ExpectedSafeRefusal = 5;

    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_planfidelity_").FullName;

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

    /// <summary>
    /// Multibyte UTF-8 that a full strict decode confirms, so detection is reliable and
    /// an explicit legacy source contradicts it.
    /// </summary>
    private string WriteReliableUtf8()
    {
        string path = Path.Combine(_root, "utf8.txt");
        File.WriteAllText(path, "café naïve — déjà vu", new UTF8Encoding(false));

        return path;
    }

    [Fact]
    public void PlanThatRefusesAnExplicitSourceConflict_StillRefusesWhenApplied()
    {
        string path = WriteReliableUtf8();
        byte[] original = File.ReadAllBytes(path);
        string planPath = Path.Combine(_root, "plan.json");

        // Plan mode returns 0 for a refusal by design - a refusal is an expected
        // preflight result, not a failed preflight. The plan's own contents are the
        // assertion that matters here.
        Assert.Equal(
            ExpectedClean,
            Run("-BasePath", _root, "-Target", "utf-8", "-From", "windows-1252",
                "-Plan", planPath, "-Quiet"));

        ConversionPlan? plan = ConversionPlan.Load(planPath, out string? loadError);

        Assert.Null(loadError);
        Assert.NotNull(plan);

        PlannedFile planned = Assert.Single(plan!.Files);

        Assert.Equal(PlannedAction.Refuse, planned.Action);
        Assert.Equal(
            ConversionReasonCodes.ExplicitSourceConflictsWithDetection,
            planned.ReasonCode);

        // The whole point: the same decision, reached again from the saved plan.
        Assert.Equal(ExpectedSafeRefusal, Run("-Apply", planPath));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void ThePlanCarriesTheDetectionReliabilityTheVetoDependsOn()
    {
        // Provenance fields alone were not enough. This one is a policy input, and
        // leaving it out is what let the veto silently stop firing at apply time.
        WriteReliableUtf8();
        string planPath = Path.Combine(_root, "plan.json");

        Run("-BasePath", _root, "-Target", "utf-8", "-From", "windows-1252",
            "-Plan", planPath, "-Quiet");

        ConversionPlan plan = ConversionPlan.Load(planPath, out _)!;

        Assert.True(Assert.Single(plan.Files).HasReliableUnicodeDetection);
    }

    [Fact]
    public void WithTheReliabilityFlagLost_TheReviewedRefusalStillBinds()
    {
        // Defence one alone: simulate a schema that does not carry the policy input,
        // which is exactly the shape every plan had before this was fixed. The ceiling
        // has to hold on its own, or the next missing input repeats the defect.
        string path = WriteReliableUtf8();
        byte[] original = File.ReadAllBytes(path);

        var entry = new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = "windows-1252",
            TargetEncoding = "utf-8",
            SourceEncodingWasSpecified = true,
            DetectedEncodingLabel = "utf-8",
            HasReliableUnicodeDetection = false, // lost in transit
            Action = PlannedAction.Refuse,
            SourceInterpretation = SourceInterpretation.ExplicitSource,
            ReasonCode = ConversionReasonCodes.ExplicitSourceConflictsWithDetection,
            Approved = new ApprovedDecision(
                PlannedAction.Refuse,
                SourceInterpretation.ExplicitSource,
                ConversionReasonCodes.ExplicitSourceConflictsWithDetection,
                "reviewed as refused"),
        };

        var sink = new EntrySink();

        ScanEngine.ConvertFiles(
            [entry], "utf-8", targetWriteBom: false, maxParallelism: 1,
            whatIf: false, backup: false, sink.Add, CancellationToken.None);

        Assert.Equal(PlannedAction.Refuse, entry.Action);
        Assert.Equal(ConversionRowResult.Refused, entry.Result);
        Assert.Equal(
            ConversionReasonCodes.ExplicitSourceConflictsWithDetection,
            entry.ReasonCode);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void WithNoCeiling_TheRecomputedVetoStillRefuses()
    {
        // Defence two alone: no approved decision at all, so only the recomputed policy
        // can stop this. Together with the test above, neither defence can be removed
        // without a test failing.
        string path = WriteReliableUtf8();
        byte[] original = File.ReadAllBytes(path);

        var entry = new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = "windows-1252",
            TargetEncoding = "utf-8",
            SourceEncodingWasSpecified = true,
            DetectedEncodingLabel = "utf-8",
            HasReliableUnicodeDetection = true,
            Approved = null,
        };

        var sink = new EntrySink();

        ScanEngine.ConvertFiles(
            [entry], "utf-8", targetWriteBom: false, maxParallelism: 1,
            whatIf: false, backup: false, sink.Add, CancellationToken.None);

        Assert.Equal(PlannedAction.Refuse, entry.Action);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void WithNeitherDefence_TheFileIsRewritten()
    {
        // The control. If this ever stops rewriting the file, the two tests above have
        // stopped proving anything and are passing for some unrelated reason.
        string path = WriteReliableUtf8();
        byte[] original = File.ReadAllBytes(path);

        var entry = new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = "windows-1252",
            TargetEncoding = "utf-8",
            SourceEncodingWasSpecified = true,
            DetectedEncodingLabel = "utf-8",
            HasReliableUnicodeDetection = false,
            Approved = null,
        };

        var sink = new EntrySink();

        ScanEngine.ConvertFiles(
            [entry], "utf-8", targetWriteBom: false, maxParallelism: 1,
            whatIf: false, backup: false, sink.Add, CancellationToken.None);

        Assert.Equal(ConversionRowResult.Converted, entry.Result);
        Assert.NotEqual(original, File.ReadAllBytes(path));

        // And this is the damage, derived from the codecs rather than pasted as a
        // literal: the original bytes read through the wrong codec. The result is
        // valid UTF-8 holding characters nobody wrote, which verification cannot
        // catch because both of its sides use that same wrong codec.
        Assert.Equal(
            Encoding.GetEncoding("windows-1252").GetString(original),
            File.ReadAllText(path, new UTF8Encoding(false)));
    }

    [Fact]
    public void TheCeilingOnlyBindsDownward_APlannedConvertMayStillBeRefused()
    {
        // Re-deciding must still be able to refuse a file the plan approved; that is
        // why applying re-decides at all. The ceiling must not turn into a floor.
        string path = Path.Combine(_root, "ambiguous.txt");
        File.WriteAllBytes(
            path, new UnicodeEncoding(false, false).GetBytes("Hello World, plain text."));

        var entry = new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = "utf-16",
            TargetEncoding = "utf-8",
            DetectedEncodingLabel = "utf-16",
            Approved = new ApprovedDecision(
                PlannedAction.Convert,
                SourceInterpretation.AutomaticUnicodeOrAscii,
                null,
                null),
        };

        byte[] original = File.ReadAllBytes(path);
        var sink = new EntrySink();

        ScanEngine.ConvertFiles(
            [entry], "utf-8", targetWriteBom: false, maxParallelism: 1,
            whatIf: false, backup: false, sink.Add, CancellationToken.None);

        Assert.Equal(PlannedAction.Refuse, entry.Action);
        Assert.Equal(ConversionReasonCodes.AmbiguousBomlessUtf16, entry.ReasonCode);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void APlanWrittenByAnEarlierSchemaIsRefusedRatherThanReinterpreted()
    {
        // Plans written before this fix record decisions this build cannot reproduce,
        // so they must not be applied at all.
        WriteReliableUtf8();
        string planPath = Path.Combine(_root, "plan.json");

        Run("-BasePath", _root, "-Target", "utf-8", "-Plan", planPath, "-Quiet");

        string downgraded = File.ReadAllText(planPath)
            .Replace(
                $"\"PlanVersion\": {ConversionPlan.CurrentPlanVersion}",
                "\"PlanVersion\": 4",
                StringComparison.Ordinal);

        Assert.Contains("\"PlanVersion\": 4", downgraded, StringComparison.Ordinal);
        File.WriteAllText(planPath, downgraded);

        Assert.Null(ConversionPlan.Load(planPath, out string? error));
        Assert.Contains("schema version 4", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOrdinaryPlanStillApplies()
    {
        // The ceiling must not block work the review approved.
        string path = Path.Combine(_root, "plain.txt");
        File.WriteAllText(path, "hello world", new UTF8Encoding(true));
        string planPath = Path.Combine(_root, "plan.json");

        Assert.Equal(
            ExpectedClean,
            Run("-BasePath", _root, "-Target", "utf-8", "-Plan", planPath, "-Quiet"));
        Assert.Equal(ExpectedClean, Run("-Apply", planPath));

        Assert.Equal("hello world", File.ReadAllText(path));
        Assert.NotEqual(Encoding.UTF8.GetPreamble(), File.ReadAllBytes(path)[..3]);
    }
}
