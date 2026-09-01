using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace EncodingChecker;

/// <summary>What the person being asked chose.</summary>
internal enum ConfirmationChoice
{
    /// <summary>Carry out the plan as shown.</summary>
    Proceed,

    /// <summary>Change nothing.</summary>
    Cancel,

    /// <summary>Name the source encoding for the refused files, then ask again.</summary>
    ChooseSourceEncoding,
}

/// <summary>The user's choice, optional encoding, and files it applies to.</summary>
/// <param name="Files">
/// The files the chosen encoding applies to, or <see langword="null"/> for every refused
/// file. It never means every selected file because refused files may use different
/// encodings.
/// </param>
internal readonly record struct ConfirmationResponse(
    ConfirmationChoice Choice,
    string? SourceEncoding = null,
    IReadOnlyList<string>? Files = null)
{
    internal static readonly ConfirmationResponse Proceed = new(ConfirmationChoice.Proceed);

    internal static readonly ConfirmationResponse Cancel = new(ConfirmationChoice.Cancel);
}

/// <summary>How a run ended.</summary>
internal enum OrchestrationOutcome
{
    /// <summary>The conversion ran.</summary>
    Converted,

    /// <summary>A preview: what would happen, with nothing written.</summary>
    Previewed,

    /// <summary>The user declined. Nothing was written.</summary>
    Cancelled,

    /// <summary>The planned files changed, so nothing was written.</summary>
    PlanWentStale,

    /// <summary>The run could not be planned. Nothing was written.</summary>
    CouldNotPlan,
}

/// <summary>The result of a run and the plan shown to the user.</summary>
internal sealed record OrchestrationResult
{
    internal required OrchestrationOutcome Outcome { get; init; }

    /// <summary>The confirmed plan, or <see langword="null"/> if none was confirmed.</summary>
    internal ConversionPlan? Plan { get; init; }

    /// <summary>What to tell the user when nothing ran.</summary>
    internal string? Message { get; init; }

    /// <summary>The immutable journal captured from the exact completed run.</summary>
    internal ConversionJournal? Journal { get; init; }
}

/// <summary>
/// Decides, confirms, and carries out exactly the confirmed conversion plan.
/// </summary>
/// <remarks>
/// The orchestration is kept separate from the UI so the complete sequence can be tested
/// independently of any form or event handler.
/// <para>
/// Classification and conversion use the same entries. The UI only confirms the decided
/// plan; it does not perform another detection pass.
///</para>
/// </remarks>
internal sealed class ConversionOrchestrator
{
    private readonly Func<ConversionPlan, ConfirmationResponse> _confirm;

    /// <param name="confirm">
    /// Shows the decided plan and returns the user's choice. A UI caller is responsible
    /// for any required thread marshaling.
    /// </param>
    internal ConversionOrchestrator(Func<ConversionPlan, ConfirmationResponse> confirm) =>
        _confirm = confirm;

    /// <summary>
    /// Runs one conversion: decide, confirm, then carry out the confirmed plan.
    /// </summary>
    /// <param name="entries">
    /// The files to process. The same entries are used for both passes so the second pass
    /// does not classify them again.
    /// </param>
    /// <param name="backup">Whether to create backup files.</param>
    /// <param name="preview">
    /// When true, only the deciding pass runs and nothing is written.
    /// </param>
    /// <param name="baseDirectory">The directory containing the files to process.</param>
    /// <param name="targetCharset">The character set to convert to.</param>
    /// <param name="targetWriteBom">Whether to write a BOM when converting to the target charset.</param>
    /// <param name="maxParallelism">The maximum number of parallel conversion operations.</param>
    /// <param name="onEntry">The action to perform for each entry.</param>
    /// <param name="cancellationToken"></param>
    internal OrchestrationResult Run(
        IReadOnlyList<ConversionReportEntry> entries,
        string baseDirectory,
        string targetCharset,
        bool targetWriteBom,
        bool backup,
        bool preview,
        int maxParallelism,
        Action<ConversionReportEntry> onEntry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        DateTime startedUtc = DateTime.UtcNow;

        // View is informational. Re-detect the selected automatic-source files while
        // hashing the same locked bytes, so the plan never combines stale detection with
        // a newer source hash. Explicit choices are hashed but not detected.
        ScanEngine.RefreshSourceSnapshots(
            entries,
            maxParallelism,
            cancellationToken);

        // Decide without writing; the resulting actions stay on the entries.
        RunPass(
            entries, targetCharset, targetWriteBom,
            backup, whatIf: true, maxParallelism, _ => { }, cancellationToken);

        if (preview)
        {
            foreach (ConversionReportEntry entry in entries)
                onEntry(entry);

            return new OrchestrationResult { Outcome = OrchestrationOutcome.Previewed };
        }

        cancellationToken.ThrowIfCancellationRequested();

        while (true)
        {
            ConversionPlan plan;

            try
            {
                plan = ConversionPlan.FromEntries(
                    entries, baseDirectory, targetCharset, targetWriteBom, backup,
                    explicitSource: entries.All(e => e.SourceEncodingWasSpecified)
                                    && entries.Count > 0
                        ? entries[0].EffectiveSourceLabel
                        : null);
            }
            catch (InvalidOperationException ex)
            {
                // An undecided entry means the caller failed to provide a complete plan.
                return new OrchestrationResult
                {
                    Outcome = OrchestrationOutcome.CouldNotPlan,
                    Message = ex.Message,
                };
            }

            ConfirmationResponse response = _confirm(plan);

            if (response.Choice == ConfirmationChoice.Cancel)
            {
                return new OrchestrationResult
                {
                    Outcome = OrchestrationOutcome.Cancelled,
                    Plan = plan,
                };
            }

            if (response.Choice == ConfirmationChoice.ChooseSourceEncoding)
            {
                // Scope the user's answer to the refused files it was intended for.
                if (!ApplyChosenSource(
                        response.SourceEncoding, response.Files, entries))
                {
                    return new OrchestrationResult
                    {
                        Outcome = OrchestrationOutcome.Cancelled,
                        Plan = plan,
                    };
                }

                RunPass(
                    entries, targetCharset, targetWriteBom,
                    backup, whatIf: true, maxParallelism, _ => { }, cancellationToken);

                continue;
            }

            // Recheck the approved plan because files may have changed while it was being reviewed.
            IReadOnlyList<string> stale = plan.FindStaleFiles();

            if (stale.Count > 0)
            {
                return new OrchestrationResult
                {
                    Outcome = OrchestrationOutcome.PlanWentStale,
                    Plan = plan,
                    Message =
                        $"{stale.Count} file(s) changed after the conversion was planned, "
                        + "so nothing was converted. Run View again to start from the "
                        + "files as they are now."
                        + Environment.NewLine + Environment.NewLine
                        + string.Join(Environment.NewLine, stale.Take(10)),
                };
            }

            // Carry the approved hashes into the write pass to narrow the time-of-check gap.
            BindToPlannedBytes(entries, plan);

            RunPass(
                entries, targetCharset, targetWriteBom,
                backup, whatIf: false, maxParallelism, onEntry, cancellationToken);

            string? explicitSource = entries.All(e => e.SourceEncodingWasSpecified)
                                     && entries.Count > 0
                ? entries[0].ResolvedSourceLabel ?? entries[0].EffectiveSourceLabel
                : null;

            return new OrchestrationResult
            {
                Outcome = OrchestrationOutcome.Converted,
                Plan = plan,
                Journal = ConversionJournal.FromRun(
                    entries,
                    baseDirectory,
                    targetCharset,
                    targetWriteBom,
                    backup,
                    explicitSource,
                    surface: "Gui",
                    startedUtc),
            };
        }
    }

    private static void RunPass(
        IReadOnlyList<ConversionReportEntry> entries,
        string targetCharset,
        bool targetWriteBom,
        bool backup,
        bool whatIf,
        int maxParallelism,
        Action<ConversionReportEntry> onEntry,
        CancellationToken cancellationToken) =>
        ScanEngine.ConvertFiles(
            entries,
            targetCharset,
            targetWriteBom,
            maxParallelism,
            whatIf: whatIf,
            backup: backup,
            onEntry,
            cancellationToken);

    /// <summary>
    /// Replaces detection with the user's source encoding for the refused files in scope.
    /// </summary>
    /// <returns><see langword="false"/> when no refused file matched the scope.</returns>
    private static bool ApplyChosenSource(
        string? charset,
        IReadOnlyList<string>? files,
        IReadOnlyList<ConversionReportEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(charset))
            return false;

        HashSet<string>? scope = files is null
            ? null
            : new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);

        var changed = false;

        foreach (ConversionReportEntry entry in entries)
        {
            if (entry.Action != PlannedAction.Refuse ||
                !ConversionPolicy.RequiresExplicitSourceChoice(
                    entry.SourceInterpretation, entry.ReasonCode))
                continue;

            if (scope is not null && !scope.Contains(entry.FilePath))
                continue;

            // Use the existing source override so conversion reads the chosen encoding.
            entry.CurrentCharsetLabel = charset;
            entry.SourceEncodingWasSpecified = true;
            entry.Diagnostic = null;

            // The conversion pass decides again using the explicit source choice.
            entry.Action = null;
            entry.SourceInterpretation = null;

            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Ties each planned conversion to the bytes that were approved.
    /// </summary>
    private static void BindToPlannedBytes(
        IReadOnlyList<ConversionReportEntry> entries, ConversionPlan plan)
    {
        Dictionary<string, string> hashes = plan.Files
            .Where(f => f.Action == PlannedAction.Convert)
            .ToDictionary(
                f => plan.ResolvePath(f) ?? f.RelativePath,
                f => f.Sha256,
                StringComparer.OrdinalIgnoreCase);

        foreach (ConversionReportEntry entry in entries)
        {
            entry.ExpectedSourceSha256 =
                hashes.GetValueOrDefault(entry.FilePath);
        }
    }
}
