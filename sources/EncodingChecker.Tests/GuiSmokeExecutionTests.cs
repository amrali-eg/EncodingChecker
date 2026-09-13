using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using EncodingChecker.GuiSmoke;

namespace EncodingChecker.Tests;

/// <summary>Exercises the same phase execution and recording path as the real GUI suite.</summary>
public sealed class GuiSmokeExecutionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ec_smoke_execution_").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private SmokeReport Run(params SmokePhase[] phases) =>
        new SmokeSuite(typeof(SmokeSuite).Assembly.Location, _root).RunCore(_ => { }, phases);

    [Fact]
    public void ReachableTimeoutKeepsItsCauseAndAllowsAnIndependentNextPhase()
    {
        var cause = new InvalidOperationException("provider did not answer");
        WindowObservation observation = Observation(GuiReachability.Reachable);
        SmokeReport report = Run(
            new("A", "failure", phase =>
            {
                File.WriteAllText(Path.Combine(phase.Directory, "original.txt"), "before");
                phase.CaptureBefore();
                File.WriteAllText(Path.Combine(phase.Directory, "original.txt"), "after");
                throw new GuiWaitException("expected result missing", observation, cause);
            }),
            new("B", "independent", _ => { }));

        Assert.Equal(SmokeOutcome.Failed, report.Outcome);
        Assert.Equal(1, report.Outcome.ExitCode());
        Assert.Equal(2, report.Phases.Count);
        SmokePhaseResult failed = report.Phases[0];
        Assert.Contains("expected result missing", failed.Error);
        Assert.Contains(cause.Message, failed.Error);
        Assert.Equal(observation, failed.Observation);
        Assert.NotEqual(failed.Before["original.txt"], failed.After["original.txt"]);
        Assert.Equal(SmokeOutcome.Passed, report.Phases[1].Outcome);
    }

    [Fact]
    public void AFailedDiagnosticCannotReplaceTheTimeoutOrBecomeAnEnvironmentalExcuse()
    {
        WindowObservation observation = WindowObservation.Capture(
            () => throw new InvalidOperationException("diagnosis also failed"));
        SmokeReport report = Run(new SmokePhase("A", "unknown window", _ =>
            throw new GuiWaitException("original timeout", observation,
                new InvalidOperationException("original provider error"))));

        Assert.Equal(SmokeOutcome.Failed, report.Outcome);
        Assert.Equal(GuiReachability.Unknown, report.Phases[0].Observation!.Reachability);
        Assert.Null(observation.ProcessAlive);
        Assert.Null(observation.NativeWindowExists);
        Assert.Contains("diagnosis also failed", Assert.Single(observation.Errors));
        Assert.Contains("original timeout", report.Phases[0].Error);
        Assert.Contains("original provider error", report.Phases[0].Error);
        Assert.Throws<ArgumentException>(() => new GuiEnvironmentException("not established", observation));
    }

    [Fact]
    public void InconclusivePhaseKeepsEarlierResultsAndSnapshotsAndStopsTheLoop()
    {
        SmokeReport report = Run(
            new("A", "passed", _ => { }),
            new("B", "desktop lost", phase =>
            {
                File.WriteAllText(Path.Combine(phase.Directory, "written.txt"), "evidence");
                throw new GuiEnvironmentException("desktop unavailable",
                    Observation(GuiReachability.EnvironmentUnavailable),
                    new TimeoutException("original wait"));
            }),
            new("C", "must not run", _ => throw new Exception("later phase ran")));

        Assert.Equal(SmokeOutcome.Inconclusive, report.Outcome);
        Assert.Equal(2, report.Outcome.ExitCode());
        Assert.Equal(new[] { "A", "B" }, report.Phases.Select(phase => phase.Id));
        Assert.Single(report.Phases[1].After);
        Assert.Contains("original wait", report.Phases[1].Error);
        Assert.False(Directory.Exists(Path.Combine(_root, "C")));

        SmokeReportWriter.Write(_root, report);
        using JsonDocument json = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(_root, SmokeReportWriter.JsonFileName)));
        JsonElement phaseB = json.RootElement.GetProperty("Phases")[1];
        Assert.Equal("EnvironmentUnavailable",
            phaseB.GetProperty("Observation").GetProperty("Reachability").GetString());
        Assert.Contains("INCONCLUSIVE", File.ReadAllText(
            Path.Combine(_root, SmokeReportWriter.MarkdownFileName)));
    }

    [Fact]
    public void ActualPreflightExceptionIsRecordedAndPreventsAllPhases()
    {
        SmokeReport report = new SmokeSuite(typeof(SmokeSuite).Assembly.Location, _root)
            .RunCore(_ => throw new IncompatibleBuildException("unsupported build"),
                [new("A", "must not run", _ => throw new Exception("ran"))]);

        Assert.Equal(SmokeOutcome.Inconclusive, report.Outcome);
        Assert.Empty(report.Phases);
        Assert.Contains("unsupported build", report.Preflight!.Error);
        SmokeReportWriter.Write(_root, report);
        Assert.True(File.Exists(Path.Combine(_root, SmokeReportWriter.JsonFileName)));
        Assert.True(File.Exists(Path.Combine(_root, SmokeReportWriter.MarkdownFileName)));
    }

    [Fact]
    public void CleanupFailureDuringUnwindPreservesBothErrorsAndStopsLaterPhases()
    {
        SmokeReport report = Run(
            new("A", "phase and cleanup fail", phase =>
            {
                using var cleanup = new OnDispose(() => GuiCleanup.Run(
                    () => throw new Win32Exception("close failed"),
                    () => throw new InvalidOperationException("release failed"),
                    phase.RecordCleanupError));
                throw new InvalidOperationException("real phase failure");
            }),
            new("B", "must not run", _ => throw new Exception("ran")));

        SmokePhaseResult result = Assert.Single(report.Phases);
        Assert.Equal(SmokeOutcome.Failed, report.Outcome);
        Assert.Contains("real phase failure", result.Error);
        Assert.Equal(2, result.CleanupErrors.Count);
        Assert.Contains("close failed", result.CleanupErrors[0]);
        Assert.Contains("release failed", result.CleanupErrors[1]);
        Assert.False(Directory.Exists(Path.Combine(_root, "B")));
        Assert.Contains("close failed", SmokeReportWriter.RenderMarkdown(report));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CleanupFailureCannotLeaveAPassOrHideAnInconclusiveCause(bool loseDesktop)
    {
        SmokeReport report = Run(new SmokePhase("A", "cleanup", phase =>
        {
            using var cleanup = new OnDispose(() => GuiCleanup.Run(
                () => throw new InvalidOperationException("cleanup race"),
                () => { }, phase.RecordCleanupError));
            if (loseDesktop)
                throw new GuiEnvironmentException("desktop lost",
                    Observation(GuiReachability.EnvironmentUnavailable));
        }));

        Assert.Equal(SmokeOutcome.Failed, report.Outcome);
        Assert.Single(report.Phases[0].CleanupErrors);
        if (loseDesktop)
            Assert.Contains("desktop lost", report.Phases[0].Error);
    }

    [Fact]
    public void SnapshotFailureTurnsAnInconclusivePhaseIntoAFailureAndStopsTheRun()
    {
        SmokeReport report = Run(
            new("A", "lost desktop and snapshot", phase =>
            {
                Directory.Delete(phase.Directory, recursive: true);
                throw new GuiEnvironmentException("desktop lost",
                    Observation(GuiReachability.EnvironmentUnavailable));
            }),
            new("B", "must not run", _ => throw new Exception("ran")));

        SmokePhaseResult result = Assert.Single(report.Phases);
        Assert.Equal(SmokeOutcome.Failed, report.Outcome);
        Assert.Equal(SmokeOutcome.Failed, result.Outcome);
        Assert.True(result.BlocksLaterPhases);
        Assert.Contains("desktop lost", result.Error);
        Assert.Contains("final file snapshot failed", result.Error);
        Assert.False(Directory.Exists(Path.Combine(_root, "B")));
    }

    [Fact]
    public void PhaseMetricsArePreservedInBothEvidenceFormats()
    {
        SmokeReport report = Run(new SmokePhase("I", "cancellation", phase =>
        {
            phase.RecordMetric("SelectedFiles", 400);
            phase.RecordMetric("ConvertedBeforeCancellation", 16);
            phase.RecordMetric("NotAttempted", 384);
        }));

        SmokeReportWriter.Write(_root, report);
        using JsonDocument json = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(_root, SmokeReportWriter.JsonFileName)));
        JsonElement metrics = json.RootElement.GetProperty("Phases")[0].GetProperty("Metrics");
        Assert.Equal(16, metrics.GetProperty("ConvertedBeforeCancellation").GetInt64());
        Assert.Equal(384, metrics.GetProperty("NotAttempted").GetInt64());
        Assert.Contains("ConvertedBeforeCancellation: `16`", File.ReadAllText(
            Path.Combine(_root, SmokeReportWriter.MarkdownFileName)));
    }

    [Fact]
    public void LaterInconclusiveResultDoesNotEraseAnEarlierFailure()
    {
        SmokeReport report = Run(
            new("A", "failed", _ => throw new Exception("earlier failure")),
            new("B", "inconclusive", _ => throw new IncompatibleBuildException("later")),
            new("C", "must not run", _ => throw new Exception("ran")));
        Assert.Equal(SmokeOutcome.Failed, report.Outcome);
        Assert.Equal(2, report.Phases.Count);
        Assert.Contains("earlier failure", report.Phases[0].Error);
    }

    [Fact]
    public void RemovingTheExecutableDuringAPhaseCannotEraseCompletedResults()
    {
        string app = Path.Combine(_root, "app.dll");
        File.Copy(typeof(SmokeSuite).Assembly.Location, app);
        SmokeReport report = new SmokeSuite(app, Path.Combine(_root, "workspace"))
            .RunCore(_ => { }, [new("A", "remove artifact", _ => File.Delete(app))]);

        Assert.Equal(SmokeOutcome.Passed, report.Outcome);
        Assert.NotNull(report.EcSha256);
        Assert.Single(report.Phases);
        SmokeReportWriter.Write(_root, report);
        Assert.True(File.Exists(Path.Combine(_root, SmokeReportWriter.JsonFileName)));
    }

    [Fact]
    public void UnreadableBuildProducesAFailedReportWithoutInventingProvenance()
    {
        SmokeReport report = new SmokeSuite(Path.Combine(_root, "missing.exe"), _root)
            .RunCore(_ => throw new Exception("must not start"), []);
        Assert.Equal(SmokeOutcome.Failed, report.Outcome);
        Assert.NotEmpty(report.EvidenceErrors);
        Assert.Null(report.EcSha256);
        Assert.Empty(report.Phases);
        SmokeReportWriter.Write(_root, report);
        Assert.Contains("Executable SHA-256", File.ReadAllText(
            Path.Combine(_root, SmokeReportWriter.MarkdownFileName)));
    }

    [Fact]
    public async Task AStartupRefusalWritesAnInconclusivePreflightReport()
    {
        string output = Path.Combine(_root, "startup-refusal");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(typeof(SmokeSuite).Assembly.Location);
        start.ArgumentList.Add("--app");
        start.ArgumentList.Add(Path.Combine(_root, "missing.exe"));
        start.ArgumentList.Add("--output");
        start.ArgumentList.Add(output);

        using Process process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await process.WaitForExitAsync(timeout.Token);

        Assert.Equal(2, process.ExitCode);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(output, SmokeReportWriter.JsonFileName)));
        Assert.Equal("Inconclusive", report.RootElement.GetProperty("Outcome").GetString());
        Assert.Equal("Inconclusive", report.RootElement.GetProperty("Preflight")
            .GetProperty("Outcome").GetString());
        Assert.True(File.Exists(Path.Combine(output, SmokeReportWriter.MarkdownFileName)));
    }

    [Fact]
    public void ProgressOutputFailureKeepsThePhaseAlreadyCompleted()
    {
        SmokeReport report = new SmokeSuite(typeof(SmokeSuite).Assembly.Location, _root)
            .RunCore(_ => { }, [new("A", "completed", _ => { }), new("B", "must not run", _ => { })],
                result =>
                {
                    if (result.Id == "A")
                        throw new IOException("progress pipe closed");
                });

        Assert.Equal(SmokeOutcome.Failed, report.Outcome);
        Assert.Equal(SmokeOutcome.Passed, Assert.Single(report.Phases).Outcome);
        Assert.Contains("progress pipe closed", report.Error);
        Assert.False(Directory.Exists(Path.Combine(_root, "B")));
        SmokeReportWriter.Write(_root, report);
        Assert.Contains("progress pipe closed", File.ReadAllText(
            Path.Combine(_root, SmokeReportWriter.MarkdownFileName)));
    }

    [Fact]
    public void AReportWriteFailureDoesNotPreventTheOtherFormatBeingWritten()
    {
        SmokeReport report = Run(new SmokePhase("A", "pass", _ => { }));
        Directory.CreateDirectory(Path.Combine(_root, SmokeReportWriter.JsonFileName));
        Assert.Throws<AggregateException>(() => SmokeReportWriter.Write(_root, report));
        Assert.True(File.Exists(Path.Combine(_root, SmokeReportWriter.MarkdownFileName)));
    }

    [Fact]
    public void ReacquiringAReplacementReviewCannotSatisfyTheOriginalIdentity()
    {
        int[] runtimeId = [1, 2, 3];
        var expected = new GuiWindowIdentity(42, 12, runtimeId);
        runtimeId[2] = 99;
        Assert.True(expected.Matches(42, 12, [1, 2, 3]));
        Assert.False(expected.Matches(42, 12, [1, 2, 4]));
        Assert.False(expected.Matches(42, 13, [1, 2, 3]));
        Assert.False(expected.Matches(43, 12, [1, 2, 3]));
    }

    [Fact]
    public void AReplacementReviewMayReuseTheOldHandleButNotItsIdentity()
    {
        var previous = new GuiWindowIdentity(42, 12, [1, 2, 3]);
        Assert.True(GuiWindowIdentity.IsReplacement(previous, 42, 12, [1, 2, 4]));
        Assert.False(GuiWindowIdentity.IsReplacement(previous, 42, 12, [1, 2, 3]));
    }

    [Theory]
    [InlineData(1u, 12u, 12, true)]
    [InlineData(1u, 13u, 12, false)]
    [InlineData(0u, 12u, 12, false)]
    public void ReusedNativeHandleMustStillBelongToTheLaunchedProcess(
        uint thread, uint owner, int expected, bool accepted) =>
        Assert.Equal(accepted, GuiWindowIdentity.BelongsToProcess(thread, owner, expected));

    [Theory]
    [InlineData(0, "Passed", 0, "passed")]
    [InlineData(1, "Failed", 1, "failed")]
    [InlineData(2, "Inconclusive", 1, "inconclusive")]
    [InlineData(0, "Inconclusive", 1, "do not establish")]
    [InlineData(2, "Passed", 1, "do not establish")]
    [InlineData(0, null, 1, "no usable report")]
    [InlineData(2, null, 1, "could not start")]
    public async Task TheActualCiGateExplainsTheOutcomeAndOnlyAcceptsAPass(
        int suiteExitCode, string? outcome, int expectedExitCode, string expectedMessage)
    {
        if (outcome is not null)
            File.WriteAllText(Path.Combine(_root, SmokeReportWriter.JsonFileName),
                JsonSerializer.Serialize(new { Outcome = outcome, Phases = new[] { new { Id = "A" } } }));
        string summary = Path.Combine(_root, "summary.md");
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(AppContext.BaseDirectory, "Write-GateSummary.ps1"),
            "-OutputDirectory", _root, "-SuiteExitCode", suiteExitCode.ToString(), "-SummaryPath", summary })
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == expectedExitCode, (await output) + (await error));
        Assert.Contains(expectedMessage, File.ReadAllText(summary), StringComparison.OrdinalIgnoreCase);
    }

    private static WindowObservation Observation(GuiReachability state) => new(
        state, true, 42, true, state == GuiReachability.Reachable,
        false, true, true, false, "observed status", Array.Empty<string>());

    private sealed class OnDispose(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
