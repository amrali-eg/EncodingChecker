using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace EncodingChecker.GuiSmoke;

internal sealed class SmokePhaseContext(string directory)
{
    private readonly List<string> _cleanupErrors = [];
    private readonly Dictionary<string, long> _metrics = new(StringComparer.Ordinal);
    internal string Directory { get; } = directory;
    internal IReadOnlyDictionary<string, string> Before { get; private set; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    internal IReadOnlyList<string> CleanupErrors => _cleanupErrors;
    internal IReadOnlyDictionary<string, long> Metrics => _metrics;

    internal Dictionary<string, string> CaptureBefore()
    {
        Dictionary<string, string> snapshot = Snapshot(Directory);
        Before = Freeze(snapshot);
        return snapshot;
    }

    internal void RecordCleanupError(Exception error) => _cleanupErrors.Add(error.ToString());

    internal void RecordMetric(string name, long value) => _metrics.Add(name, value);

    internal EcGuiDriver OpenGui(string app) => new(app, RecordCleanupError);

    internal static Dictionary<string, string> Snapshot(string directory) =>
        System.IO.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                path => Path.GetRelativePath(directory, path),
                Hash,
                StringComparer.OrdinalIgnoreCase);

    internal static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    internal static IReadOnlyDictionary<string, string> Freeze(
        IReadOnlyDictionary<string, string> values) =>
        new ReadOnlyDictionary<string, string>(values.ToDictionary(
            pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase));
}

internal sealed record SmokePhase(string Id, string Name, Action<SmokePhaseContext> Body);

/// <summary>Keeps the phase error, cleanup errors and final file evidence together.</summary>
internal static class SmokePhaseExecution
{
    internal static SmokePhaseResult Run(string directory, SmokePhase phase)
    {
        var context = new SmokePhaseContext(directory);
        Stopwatch timer = Stopwatch.StartNew();
        SmokeOutcome outcome = SmokeOutcome.Passed;
        bool blocksLaterPhases = false;
        string? error = null;
        WindowObservation? observation = null;
        IReadOnlyDictionary<string, string> after = SmokePhaseContext.Freeze(
            new Dictionary<string, string>());

        try
        {
            Directory.CreateDirectory(directory);
            phase.Body(context);
        }
        catch (SmokeInconclusiveException ex)
        {
            outcome = SmokeOutcome.Inconclusive;
            blocksLaterPhases = true;
            error = ex.ToString();
            observation = ex is GuiEnvironmentException environment ? environment.Observation : null;
        }
        catch (Exception ex)
        {
            outcome = SmokeOutcome.Failed;
            error = ex.ToString();
            observation = ex is GuiWaitException wait ? wait.Observation : null;
        }

        try
        {
            after = SmokePhaseContext.Freeze(SmokePhaseContext.Snapshot(directory));
        }
        catch (Exception ex)
        {
            error = (error is null ? string.Empty : error + Environment.NewLine)
                + "The final file snapshot failed: " + ex;
            outcome = SmokeOutcome.Failed;
            blocksLaterPhases = true;
        }

        // A leaked process could interfere with the next phase, even if this one passed.
        if (context.CleanupErrors.Count > 0)
        {
            outcome = SmokeOutcome.Failed;
            blocksLaterPhases = true;
        }

        return new SmokePhaseResult
        {
            Id = phase.Id,
            Name = phase.Name,
            Outcome = outcome,
            Error = error,
            CleanupErrors = Array.AsReadOnly(context.CleanupErrors.ToArray()),
            Metrics = new ReadOnlyDictionary<string, long>(
                context.Metrics.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)),
            BlocksLaterPhases = blocksLaterPhases,
            Observation = observation,
            DurationMilliseconds = timer.ElapsedMilliseconds,
            Before = SmokePhaseContext.Freeze(context.Before),
            After = after,
        };
    }
}
