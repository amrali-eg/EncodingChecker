using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using EncodingChecker.GuiSmoke;

namespace EncodingChecker.Tests;

/// <summary>
/// Pins the smoke gate's distinction between a product failure and a run that could
/// not observe the GUI, plus the evidence written for both outcomes.
/// </summary>
public sealed class GuiSmokeSafetyTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_gui_smoke_").FullName;

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

    [Theory]
    [InlineData(true, false, true, true, true, "Reachable")]
    [InlineData(false, true, true, true, false, "Absent")]
    [InlineData(false, false, true, true, true, "LookupFailed")]
    [InlineData(false, false, true, true, false, "EnvironmentUnavailable")]
    [InlineData(false, false, false, true, false, "Absent")]
    [InlineData(false, false, true, false, false, "Absent")]
    [InlineData(false, false, null, true, false, "Unknown")]
    [InlineData(false, false, true, null, false, "Unknown")]
    [InlineData(false, false, false, null, false, "Unknown")]
    [InlineData(false, false, null, false, false, "Unknown")]
    [InlineData(false, true, null, null, false, "Absent")]
    [InlineData(false, true, true, true, true, "LookupFailed")]
    public void ReachabilityRequiresPositiveEvidenceForAnEnvironmentRefusal(
        bool mainWindowFound,
        bool processWindowFound,
        bool? processAlive,
        bool? nativeWindowExists,
        bool lookupFailed,
        string expected)
    {
        GuiReachability actual = GuiReachabilityPolicy.Classify(
            mainWindowFound,
            processWindowFound,
            processAlive,
            nativeWindowExists,
            lookupFailed,
            onCurrentDesktop: false);

        Assert.Equal(expected, actual.ToString());
    }

    /// <summary>
    /// A failed lookup on a live native window could be a hung product. It must keep the
    /// product timeout instead of becoming an environmental refusal.
    /// </summary>
    [Fact]
    public void ALookupFailureIsNotReclassifiedAsAnUnavailableDesktop()
    {
        GuiReachability result = GuiReachabilityPolicy.Classify(
            mainWindowFound: false,
            processWindowFound: false,
            processAlive: true,
            nativeWindowExists: true,
            lookupFailed: true,
            onCurrentDesktop: false);

        Assert.Equal(GuiReachability.LookupFailed, result);
    }

    [Fact]
    public void IncompatibleBuildsAndLostDesktopsShareTheInconclusiveCategory()
    {
        Assert.IsAssignableFrom<SmokeInconclusiveException>(
            new IncompatibleBuildException("old build"));
        Assert.IsAssignableFrom<SmokeInconclusiveException>(
            new GuiEnvironmentException("desktop unavailable", UnavailableObservation()));
    }

    /// <summary>
    /// An inconclusive late phase must not erase earlier evidence or masquerade as a
    /// product failure. Both report formats are checked because CI publishes both.
    /// </summary>
    [Fact]
    public void InconclusiveReportsPreserveEarlierVerdictsAndUseExitCodeTwoSemantics()
    {
        SmokeReport report = Report(
            SmokeOutcome.Inconclusive,
            [
                Phase("A", SmokeOutcome.Passed, null),
                Phase("B", SmokeOutcome.Inconclusive, "desktop unavailable"),
            ]);

        Assert.Equal(2, report.Outcome.ExitCode());

        SmokeReportWriter.Write(_root, report);

        string jsonPath = Path.Combine(_root, SmokeReportWriter.JsonFileName);
        string markdownPath = Path.Combine(_root, SmokeReportWriter.MarkdownFileName);
        byte[] jsonBytes = File.ReadAllBytes(jsonPath);

        Assert.False(jsonBytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));

        using JsonDocument document = JsonDocument.Parse(jsonBytes);
        JsonElement root = document.RootElement;
        Assert.Equal("Inconclusive", root.GetProperty("Outcome").GetString());
        Assert.False(root.GetProperty("Passed").GetBoolean());

        JsonElement[] phases = [.. root.GetProperty("Phases").EnumerateArray()];
        Assert.Equal("Passed", phases[0].GetProperty("Outcome").GetString());
        Assert.Equal("Inconclusive", phases[1].GetProperty("Outcome").GetString());
        Assert.Equal("desktop unavailable", phases[1].GetProperty("Error").GetString());

        string markdown = File.ReadAllText(markdownPath, Encoding.UTF8);
        Assert.Contains("Result: **INCONCLUSIVE**", markdown, StringComparison.Ordinal);
        Assert.Contains("| A | PASS | 10 ms |", markdown, StringComparison.Ordinal);
        Assert.Contains("| B | INCONCLUSIVE | 10 ms |", markdown, StringComparison.Ordinal);
        Assert.Contains("desktop unavailable", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedAndPassedReportsRemainDistinctFromInconclusiveReports()
    {
        Assert.True(Report(SmokeOutcome.Passed, []).Passed);
        Assert.False(Report(SmokeOutcome.Failed, []).Passed);
        Assert.False(Report(SmokeOutcome.Inconclusive, []).Passed);
        Assert.Equal(0, SmokeOutcome.Passed.ExitCode());
        Assert.Equal(1, SmokeOutcome.Failed.ExitCode());
        Assert.Equal(2, SmokeOutcome.Inconclusive.ExitCode());
    }

    [Fact]
    public void AnInconclusivePreflightStillProducesBothEvidenceFiles()
    {
        SmokeReport report = Report(SmokeOutcome.Inconclusive, []) with
        {
            Error = "The review cannot be driven by this build.",
        };

        SmokeReportWriter.Write(_root, report);

        string json = File.ReadAllText(
            Path.Combine(_root, SmokeReportWriter.JsonFileName),
            Encoding.UTF8);
        string markdown = File.ReadAllText(
            Path.Combine(_root, SmokeReportWriter.MarkdownFileName),
            Encoding.UTF8);

        Assert.Contains("\"Outcome\": \"Inconclusive\"", json, StringComparison.Ordinal);
        Assert.Contains("Run-level problem", markdown, StringComparison.Ordinal);
        Assert.Contains(report.Error!, markdown, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public void AnEarnedFailureOutranksALaterInconclusivePhase()
    {
        SmokeOutcome outcome = new[]
        {
            SmokeOutcome.Passed,
            SmokeOutcome.Failed,
            SmokeOutcome.Inconclusive,
        }.Overall();

        Assert.Equal(SmokeOutcome.Failed, outcome);
    }

    private static WindowObservation UnavailableObservation() => new(
        GuiReachability.EnvironmentUnavailable, true, 42, true, false,
        null, null, null, null, null, Array.Empty<string>());

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void MissingAutomationWindowDoesNotProveAnUnavailableDesktop(bool? onCurrentDesktop)
    {
        Assert.Equal(GuiReachability.Unknown, GuiReachabilityPolicy.Classify(
            false, false, true, true, false, onCurrentDesktop));
    }

    [Theory]
    [InlineData("Conversion stopped: 16 converted, 0 unchanged, 0 skipped, 0 refused, 0 failed, 384 not attempted", 16, 384)]
    [InlineData("Conversion stopped: 116 converted, 0 unchanged, 0 skipped, 0 refused, 0 failed, 284 not attempted", 116, 284)]
    public void StoppedConversionCountsAreReadAsWholeNumbers(
        string status,
        int converted,
        int notAttempted)
    {
        Assert.True(ConversionStatusText.TryReadStoppedCounts(status, out StoppedConversionCounts actual));
        Assert.Equal(new StoppedConversionCounts(converted, notAttempted), actual);
    }

    [Fact]
    public void ASubstringCannotMakeDifferentStoppedCountsPass()
    {
        const string status = "Conversion stopped: 116 converted, 0 unchanged, 0 skipped, 0 refused, 0 failed, 284 not attempted";
        Assert.True(ConversionStatusText.TryReadStoppedCounts(status, out StoppedConversionCounts actual));
        Assert.NotEqual(new StoppedConversionCounts(16, 384), actual);
    }

    public static IEnumerable<object?[]> CancellationCases()
    {
        yield return ["Conversion stopped: 4 converted, 0 unchanged, 0 skipped, 0 refused, 0 failed, 396 not attempted", true, null, false, "Stopped", ""];
        yield return ["Conversion complete: 400 converted, 0 unchanged, 0 skipped, 0 refused, 0 failed", false, null, true, "Completed", "refused"];
        yield return ["Conversion complete: 400 converted, 0 unchanged, 0 skipped, 0 refused, 0 failed", true, "RPC failure", false, "Completed", "unknown outcome"];
        yield return ["Conversion complete: 400 converted, 0 unchanged, 0 skipped, 0 refused, 0 failed", false, null, false, "Completed", "finished before"];
        yield return ["Conversion failed.", false, null, false, "OtherFinalStatus", "neither stopped nor completed"];
        yield return [null, false, null, false, "TimedOut", "could not find a Cancel button"];
        yield return [null, false, null, true, "TimedOut", "Cancel was disabled"];
        yield return [null, true, "RPC timeout", false, "TimedOut", "RPC timeout"];
        yield return [null, true, null, false, "TimedOut", "press was attempted"];
    }

    [Fact]
    public void NoPhasesCannotEstablishSuccess()
    {
        Assert.Equal(SmokeOutcome.Inconclusive, Array.Empty<SmokeOutcome>().Overall());
    }

    [Theory]
    [MemberData(nameof(CancellationCases))]
    public void CancellationDecisionsKeepTheActualOutcomeAndPressEvidence(
        string? status,
        bool pressAttempted,
        string? uncertainMessage,
        bool refused,
        string expectedOutcome,
        string expectedMessage)
    {
        Exception? uncertain = uncertainMessage is null ? null : new COMException(uncertainMessage);
        Exception? refusal = refused ? new ElementNotEnabledException("Cancel was disabled") : null;

        CancellationDecision decision = CancellationPolicy.Decide(
            status,
            new CancellationAttempt(pressAttempted, uncertain),
            refusal);

        Assert.Equal(expectedOutcome, decision.Outcome.ToString());
        if (expectedMessage.Length == 0)
            Assert.Empty(decision.Message);
        else
            Assert.Contains(expectedMessage, decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SmokePhaseResult Phase(
        string id,
        SmokeOutcome outcome,
        string? error) =>
        new()
        {
            Id = id,
            Name = "Test phase " + id,
            Outcome = outcome,
            Error = error,
            DurationMilliseconds = 10,
            Before = new Dictionary<string, string>(),
            After = new Dictionary<string, string>(),
        };

    private static SmokeReport Report(
        SmokeOutcome outcome,
        IReadOnlyList<SmokePhaseResult> phases) =>
        new()
        {
            StartedUtc = "2026-09-10T00:00:00.0000000Z",
            CompletedUtc = "2026-09-10T00:00:01.0000000Z",
            EcVersion = "test",
            EcExecutable = "EncodingChecker.exe",
            EcSha256 = "00",
            OS = "Windows",
            DotNet = "10.0",
            Workspace = "workspace",
            DurationMilliseconds = 20,
            Outcome = outcome,
            Phases = phases,
        };
}
