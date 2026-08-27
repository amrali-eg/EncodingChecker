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

    /// <summary>Converting could not be shown to be safe; not touched.</summary>
    Refused,

    /// <summary>Conversion was attempted and did not complete; not touched.</summary>
    Failed,

    /// <summary>
    /// Decided, and deliberately not carried out - a preview, or a run that stopped
    /// before reaching this file.
    /// </summary>
    NotAttempted,
}

/// <summary>
/// One file's line in the journal: what EC believed, what it decided, and what it wrote.
/// </summary>
internal sealed record JournalEntry
{
    public required string RelativePath { get; init; }

    /// <summary>The file's bytes before EC touched it.</summary>
    public required string Sha256Before { get; init; }

    /// <summary>
    /// The file's bytes afterwards, or <see langword="null"/> when nothing was written.
    /// </summary>
    /// <remarks>
    /// Present only for a conversion that completed. A null here and a
    /// <see cref="ConversionStatus"/> other than <see cref="ConversionStatus.Converted"/>
    /// together say that the file on disk is still the one <see cref="Sha256Before"/>
    /// describes.
    /// </remarks>
    public string? Sha256After { get; init; }

    // ---- what EC believed

    /// <summary>Whether the source encoding was detected or supplied.</summary>
    public required string DetectionMode { get; init; }

    /// <summary>What detection concluded, kept even when a person overrode it.</summary>
    public required string DetectedEncoding { get; init; }

    /// <summary>The encoding the conversion actually read the file as.</summary>
    public required string SourceEncoding { get; init; }

    public required int SourceCodePage { get; init; }

    public required bool SourceHasBom { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required AmbiguityClass Ambiguity { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required AmbiguityReason AmbiguityReason { get; init; }

    /// <summary>Encodings that also fit these bytes and read them differently.</summary>
    public IReadOnlyList<string> DetectionCandidates { get; init; } = [];

    // ---- what EC decided, and what happened

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required PlannedAction PlannedAction { get; init; }

    public required ConversionStatus Status { get; init; }

    /// <summary>Why, when the outcome was not a plain conversion.</summary>
    public string? Reason { get; init; }

    /// <summary>Where the original was kept, when it was.</summary>
    public string? BackupPath { get; init; }
}

/// <summary>
/// A record of one conversion run: every file EC looked at, what it concluded, what it
/// decided, and what it actually wrote.
/// </summary>
/// <remarks>
/// Distinct from the per-file <see cref="ConversionMetadata"/> sidecar, which exists so a
/// single conversion can be undone and is written only where there is a backup to undo it
/// from. This is the run, whole: the files that were refused and the files that were
/// skipped are in it too, because "why did EC not convert this?" is a question people ask
/// more often than "how do I put this one back?", and until now nothing answered it after
/// the console output scrolled away.
/// <para>
/// It records the decision that was carried out rather than the detector's raw output.
/// Those differ whenever somebody named the source encoding, and the difference is
/// exactly what an audit needs: what EC believed, what it decided, what was approved, and
/// what it wrote.
/// </para>
/// </remarks>
internal sealed record ConversionJournal
{
    internal const int CurrentJournalVersion = 1;

    public int JournalVersion { get; init; } = CurrentJournalVersion;

    /// <summary>The conversion behaviour this run was carried out under.</summary>
    public int SemanticsVersion { get; init; } = ConversionSemantics.Current;

    public ConversionSemantics Semantics { get; init; } = new();

    public required string ECVersion { get; init; }

    public required string StartedUtc { get; init; }

    public required string CompletedUtc { get; init; }

    /// <summary>Which interface ran it: the command line, or the application window.</summary>
    public required string Surface { get; init; }

    public required string BaseDirectory { get; init; }

    public required string TargetEncoding { get; init; }

    public required bool TargetHasBom { get; init; }

    public required bool BackupEnabled { get; init; }

    /// <summary>The source encoding named by a person, if any.</summary>
    public string? ExplicitSourceEncoding { get; init; }

    /// <summary>
    /// The plan this run carried out, when it carried out a written one.
    /// </summary>
    public string? AppliedPlan { get; init; }

    /// <summary>Whether this was a preview, which wrote nothing.</summary>
    /// <remarks>
    /// A preview reports its rows as "would be converted", and a journal that copied that
    /// through would claim files had been rewritten when the directory is untouched. The
    /// hashes would give it away - before and after would match - but a record should not
    /// need to be caught out to be read correctly.
    /// </remarks>
    public bool Preview { get; init; }

    public required IReadOnlyList<JournalEntry> Entries { get; init; }

    /// <summary>Counts by outcome, so the run can be read without tallying it.</summary>
    public IReadOnlyDictionary<string, int> Summary =>
        Entries
            .GroupBy(entry => entry.Status.ToString())
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count());

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Builds the journal from the entries a run finished with.
    /// </summary>
    /// <param name="startedUtc">When the run began.</param>
    /// <remarks>
    /// Reads each converted file once more to record what was actually written. That is
    /// the whole point of the last column: a journal that reports what EC intended is a
    /// journal that cannot be checked against the disk.
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
                // "Would be converted" is a decision, not an outcome.
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

            // What the conversion read, recorded at the time. Falling back to the
            // effective label would report a converted file's new encoding as the one it
            // was read as, which is the opposite of what happened.
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
                // Zero records that the label named no code page EC could resolve.
            }

            string backupPath = entry.FilePath + ".bak";

            lines.Add(new JournalEntry
            {
                RelativePath = Path.GetRelativePath(root, entry.FilePath),
                // The plan-bound paths already carry the approved hash, so a journal
                // over them costs no extra reading at all.
                Sha256Before = entry.JournalSourceSha256
                               ?? entry.ExpectedSourceSha256
                               ?? Hash(entry.FilePath),

                // Only a completed conversion changed anything, so only it has an
                // "after". Hashing the others would report a second reading of bytes
                // nothing touched, which reads as evidence and is not.
                Sha256After = status == ConversionStatus.Converted
                    ? Hash(entry.FilePath)
                    : null,

                DetectionMode = entry.SourceEncodingWasSpecified ? "Explicit" : "Detected",
                DetectedEncoding = entry.SourceEncoding,
                SourceEncoding = sourceCharset,
                SourceCodePage = codePage,
                SourceHasBom = sourceHasBom,
                Ambiguity = entry.Ambiguity ?? AmbiguityClass.TextChanging,
                AmbiguityReason = entry.AmbiguityReason,
                DetectionCandidates = entry.CompetingEncodings,
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
            ECVersion = typeof(ConversionJournal).Assembly.GetName().Version?.ToString()
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

    internal static ConversionJournal? Load(string path, out string? error)
    {
        try
        {
            ConversionJournal? journal =
                JsonSerializer.Deserialize<ConversionJournal>(File.ReadAllText(path));

            if (journal is null)
            {
                error = $"'{path}' is empty.";
                return null;
            }

            error = null;
            return journal;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = ex.Message;
            return null;
        }
    }
}
