using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EncodingChecker;

/// <summary>What became of one file.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum ConversionStatus
{
    /// <summary>Rewritten in the target encoding.</summary>
    Converted,

    /// <summary>Already in the target encoding; not touched.</summary>
    Unchanged,

    /// <summary>Encoding not identified; not touched.</summary>
    Skipped,

    /// <summary>Conversion could not be shown to be safe; not touched.</summary>
    Refused,

    /// <summary>Conversion was attempted but did not complete; not touched.</summary>
    Failed,

    /// <summary>
    /// Decided but deliberately not carried out, such as in a preview or after an earlier
    /// failure stopped the run.
    /// </summary>
    NotAttempted,
}

/// <summary>
///
/// One file's journal entry: what EC believed, decided, and actually wrote.
/// </summary>
internal sealed record JournalEntry
{
    public required string RelativePath { get; init; }

    /// <summary>The file's bytes before EC touched it.</summary>
    public required string Sha256Before { get; init; }

    /// <summary>
    /// The file's bytes afterward, or <see langword="null"/> when nothing was written.
    /// </summary>
    /// <remarks>
    /// Only completed conversions have an after-hash; all other statuses leave the
    /// original bytes identified by <see cref="Sha256Before"/>.
    /// </remarks>
    public string? Sha256After { get; init; }

    // ---- what EC believed

    /// <summary>Whether the source encoding was detected or supplied.</summary>
    public required string DetectionMode { get; init; }

    /// <summary>
    /// What detection concluded, or <see langword="null"/> when detection did not run.
    /// </summary>
    /// <remarks>
    /// Null under <c>-From</c>, which supplies the source encoding instead of running
    /// detection.
    /// </remarks>
    public string? DetectedEncoding { get; init; }

    /// <summary>The encoding the conversion actually read the file as.</summary>
    public required string SourceEncoding { get; init; }

    public required int SourceCodePage { get; init; }

    public required bool SourceHasBom { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required SourceInterpretation SourceInterpretation { get; init; }

    // ---- what EC decided, and what happened

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required PlannedAction PlannedAction { get; init; }

    public required ConversionStatus Status { get; init; }

    /// <summary>Why the outcome was not a plain conversion.</summary>
    public string? Reason { get; init; }

    /// <summary>Where the original was kept, when it was.</summary>
    public string? BackupPath { get; init; }
}

/// <summary>
/// A record of one conversion run: what EC concluded, decided, and actually wrote.
/// </summary>
/// <remarks>
/// Unlike the per-file <see cref="ConversionMetadata"/> sidecar, this records the whole
/// run, including refused and skipped files, so the conversion history remains auditable.
/// <para>
/// It preserves both detection and the effective decision. Those differ when an explicit
/// source encoding was supplied, and that distinction matters for an audit trail.
/// </para>
/// </remarks>
internal sealed record ConversionJournal
{
    internal const int CurrentJournalVersion = 2;

    public int JournalVersion { get; init; } = CurrentJournalVersion;

    /// <summary>The conversion behaviour this run used.</summary>
    public int SemanticsVersion { get; init; } = ConversionSemantics.Current;

    public ConversionSemantics Semantics { get; init; } = new();

    public required string EcVersion { get; init; }

    public required string StartedUtc { get; init; }

    public required string CompletedUtc { get; init; }

    /// <summary>Which interface ran the conversion.</summary>
    public required string Surface { get; init; }

    public required string BaseDirectory { get; init; }

    public required string TargetEncoding { get; init; }

    public required bool TargetHasBom { get; init; }

    public required bool BackupEnabled { get; init; }

    /// <summary>The source encoding explicitly supplied by the user, if any.</summary>
    public string? ExplicitSourceEncoding { get; init; }

    /// <summary>The plan applied by the run, when one was used.</summary>
    public string? AppliedPlan { get; init; }

    /// <summary>Whether this was a preview and therefore wrote nothing.</summary>
    public bool Preview { get; init; }

    public required IReadOnlyList<JournalEntry> Entries { get; init; }

    /// <summary>Counts by outcome, so the run can be read without tallying entries.</summary>
    public IReadOnlyDictionary<string, int> Summary =>
        Entries
            .GroupBy(entry => entry.Status.ToString())
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count());

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    /// <summary>Builds a journal from the results of one run.</summary>
    /// <param name="surface">Which interface ran the conversion.</param>
    /// <param name="startedUtc">When the run began.</param>
    /// <param name="entries">The entries to include in the journal.</param>
    /// <param name="baseDirectory">The base directory for the conversion.</param>
    /// <param name="targetCharset">The target charset for the conversion.</param>
    /// <param name="targetHasBom">Indicates whether the target file has a BOM.</param>
    /// <param name="backupEnabled">Indicates whether backups are enabled.</param>
    /// <param name="explicitSource">The explicit source encoding, if any.</param>
    /// <param name="appliedPlan">The plan applied by the run, when one was used.</param>
    /// <param name="preview">Indicates whether the run was a preview.</param>
    /// <remarks>
    /// Completed conversions are hashed again so the journal records the bytes actually
    /// written, not merely what the conversion intended to write.
    /// </remarks>
    internal static ConversionJournal FromRun(
        IEnumerable<ConversionReportEntry> entries,
        string baseDirectory,
        string targetCharset,
        bool targetHasBom,
        bool backupEnabled,
        string? explicitSource,
        string surface,
        DateTime startedUtc,
        string? appliedPlan = null,
        bool preview = false)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        var lines = new List<JournalEntry>();

        foreach (ConversionReportEntry entry in entries)
        {
            ConversionStatus status = entry.Result switch
            {
                // A preview records the decision, not a conversion that happened.
                ConversionRowResult.Converted when preview
                    => ConversionStatus.NotAttempted,
                ConversionRowResult.Converted => ConversionStatus.Converted,
                ConversionRowResult.Unchanged => ConversionStatus.Unchanged,
                ConversionRowResult.Skipped => ConversionStatus.Skipped,
                ConversionRowResult.Error when entry.Action == PlannedAction.Refuse
                    => ConversionStatus.Refused,
                ConversionRowResult.Error => ConversionStatus.Failed,
                _ => ConversionStatus.NotAttempted,
            };

            // Record the encoding actually used to read the source file.
            ScanEngine.ParseCharsetLabel(
                entry.ResolvedSourceLabel ?? entry.EffectiveSourceLabel,
                out string sourceCharset,
                out bool sourceHasBom);

            int codePage = 0;

            try
            {
                codePage = Encoding.GetEncoding(sourceCharset).CodePage;
            }
            catch (ArgumentException)
            {
                // Zero means EC could not resolve a code page for the recorded label.
            }

            string backupPath = entry.FilePath + ".bak";

            lines.Add(new JournalEntry
            {
                RelativePath = Path.GetRelativePath(root, entry.FilePath),
                // Reuse the plan's hash when available; avoid rereading the source.
                Sha256Before = entry.JournalSourceSha256
                               ?? entry.ExpectedSourceSha256
                               ?? Hash(entry.FilePath),

                // Only a completed conversion needs an after-hash.
                Sha256After = status == ConversionStatus.Converted
                    ? Hash(entry.FilePath)
                    : null,

                DetectionMode = entry.SourceEncodingWasSpecified ? "Explicit" : "Detected",
                DetectedEncoding = entry.DetectedEncodingLabel,
                SourceEncoding = sourceCharset,
                SourceCodePage = codePage,
                SourceHasBom = sourceHasBom,
                SourceInterpretation = entry.SourceInterpretation
                    ?? SourceInterpretation.NotApplicable,
                PlannedAction = entry.Action ?? PlannedAction.Skip,
                Status = status,
                Reason = string.IsNullOrEmpty(entry.Diagnostic) ? null : entry.Diagnostic,
                BackupPath =
                    status == ConversionStatus.Converted
                    && backupEnabled
                    && File.Exists(backupPath)
                        ? Path.GetRelativePath(root, backupPath)
                        : null,
            });
        }

        return new ConversionJournal
        {
            EcVersion = typeof(ConversionJournal).Assembly.GetName().Version?.ToString()
                        ?? "unknown",
            StartedUtc = startedUtc.ToString("O", CultureInfo.InvariantCulture),
            CompletedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Surface = surface,
            BaseDirectory = root,
            TargetEncoding = targetCharset,
            TargetHasBom = targetHasBom,
            BackupEnabled = backupEnabled,
            ExplicitSourceEncoding = explicitSource,
            AppliedPlan = appliedPlan,
            Preview = preview,
            Entries = [.. lines.OrderBy(l => l.RelativePath, StringComparer.OrdinalIgnoreCase)],
        };
    }

    /// <summary>A file's SHA-256, or empty when it cannot be read.</summary>
    private static string Hash(string path)
    {
        try
        {
            return File.Exists(path) ? ConversionMetadataStore.ComputeSha256(path) : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    internal string? Save(string path)
    {
        try
        {
            File.WriteAllText(
                path, JsonSerializer.Serialize(this, Options), new UTF8Encoding(false));
            return null;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return ex.Message;
        }
    }

}
