using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace EncodingChecker;

/// <summary>What the person being asked said.</summary>
internal enum ConfirmationChoice
{
    /// <summary>Carry out the plan as shown.</summary>
    Proceed,

    /// <summary>Change nothing.</summary>
    Cancel,

    /// <summary>Answer the refusal by naming the source encoding, then ask again.</summary>
    ChooseSourceEncoding,
}

/// <summary>The answer, the encoding chosen when there was one, and what it applies to.</summary>
/// <param name="Files">
/// The files the chosen encoding applies to, or <see langword="null"/> for every refused
/// file. Never every <em>selected</em> file: a batch can hold refused files in different
/// encodings, and one answer is only an answer for the files it was given about.
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

    /// <summary>Files changed between the plan and the confirmation. Nothing was written.</summary>
    PlanWentStale,

    /// <summary>The run could not be planned at all. Nothing was written.</summary>
    CouldNotPlan,
}

/// <summary>The result of a run, and the plan the user was actually shown.</summary>
internal sealed record OrchestrationResult
{
    internal required OrchestrationOutcome Outcome { get; init; }

    /// <summary>The plan as confirmed, or <see langword="null"/> if it never got that far.</summary>
    internal ConversionPlan? Plan { get; init; }

    /// <summary>What to tell the user when nothing ran.</summary>
    internal string? Message { get; init; }
}

/// <summary>
/// Decide, then ask, then carry out exactly what was agreed.
/// </summary>
/// <remarks>
/// This lives outside the form on purpose. The defect that prompted it - the GUI
/// converting files the CLI refuses - was not in any component. Every component was
/// correct and tested. It was in the sequence: a Detect-mode scan feeding entries into a
/// conversion, with no step in between that classified them. Sequences that live inside a
/// <c>Form</c>, spread across button handlers and background-worker callbacks, are
/// sequences nothing can run end to end, and this project has now been shown what that
/// costs.
/// <para>
/// So the sequence is a class, the confirmation is a delegate, and the conversion engine
/// underneath is the real one. A test can drive the whole thing and read the bytes on
/// disk afterwards.
/// </para>
/// </remarks>
internal sealed class ConversionOrchestrator
{
    private readonly Func<ConversionPlan, ConfirmationResponse> _confirm;

    /// <param name="confirm">
    /// Shows a decided plan and returns what the user said. Called on whatever thread the
    /// orchestrator runs on; a UI caller marshals inside it.
    /// </param>
    internal ConversionOrchestrator(Func<ConversionPlan, ConfirmationResponse> confirm) =>
        _confirm = confirm;

    /// <summary>
    /// Runs one conversion: a pass that decides, a confirmation, and a pass that acts.
    /// </summary>
    /// <param name="entries">
    /// The files to convert. Mutated in place, and the same objects are used by both
    /// passes - which is what stops the second pass from classifying anything again.
    /// </param>
    /// <param name="preview">
    /// When true, only the deciding pass runs. A preview writes nothing, so it is its own
    /// answer and there is nothing to confirm.
    /// </param>
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

        // Pass one. Decides, writes nothing, and leaves the decision on each entry.
        RunPass(
            entries, targetCharset, targetWriteBom,
            backup, whatIf: true, maxParallelism, onEntry, cancellationToken);

        if (preview)
            return new OrchestrationResult { Outcome = OrchestrationOutcome.Previewed };

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
                // An entry that got here undecided or unclassified is a bug in the
                // caller. Nothing has been written, and nothing will be.
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
                // The user answered the refusal. Only the files it was about change, so a
                // mixed batch does not have one codec imposed on all of it.
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
                    backup, whatIf: true, maxParallelism, onEntry, cancellationToken);

                continue;
            }

            // Confirmed. The same check -Apply makes, for the same reason: the plan was
            // approved for the files as they were, and a person reading a dialog takes
            // time. All-or-nothing, because a plan is reviewed as a whole.
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

            // Carried again at the moment of installation, so the window between the
            // check above and each individual write is narrowed too.
            BindToPlannedBytes(entries, plan);

            RunPass(
                entries, targetCharset, targetWriteBom,
                backup, whatIf: false, maxParallelism, onEntry, cancellationToken);

            return new OrchestrationResult
            {
                Outcome = OrchestrationOutcome.Converted,
                Plan = plan,
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
    /// Replaces detection with the user's answer, for the refused files only.
    /// </summary>
    /// <returns><see langword="false"/> if there was nothing to apply it to.</returns>
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
            if (entry.Action != PlannedAction.Refuse || !entry.MayChangeText())
                continue;

            if (scope is not null && !scope.Contains(entry.FilePath))
                continue;

            // The engine's existing override point: SourceEncoding keeps the scan's
            // answer for the report, and this is what the conversion reads.
            entry.CurrentCharsetLabel = charset;
            entry.SourceEncodingWasSpecified = true;
            entry.CompetingEncodings = [];
            entry.Diagnostic = null;

            // Cleared together so the policy decides again rather than reusing the
            // refusal, and re-records the classification for the new source.
            entry.Action = null;
            entry.Ambiguity = null;

            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Ties each entry to the bytes the plan was approved for.
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
                hashes.TryGetValue(entry.FilePath, out string? hash) ? hash : null;
        }
    }
}
