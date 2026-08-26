using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EncodingChecker;

/// <summary>What EC intends to do with one file.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum PlannedAction
{
    /// <summary>Convert it.</summary>
    Convert,

    /// <summary>Already in the target encoding; nothing to do.</summary>
    Unchanged,

    /// <summary>The encoding could not be identified, so it is left alone.</summary>
    Skip,

    /// <summary>Converting it cannot be shown to be safe.</summary>
    Refuse,
}

/// <summary>One file's entry in a conversion plan.</summary>
internal sealed record PlannedFile
{
    public required string Path { get; init; }

    public required long Size { get; init; }

    /// <summary>
    /// The file's bytes when the plan was made. What binds the plan to reality: if this
    /// no longer matches, the plan describes a file that no longer exists.
    /// </summary>
    public required string Sha256 { get; init; }

    public required PlannedAction Action { get; init; }

    public required string SourceEncoding { get; init; }

    public required int SourceCodePage { get; init; }

    public required bool SourceHasBom { get; init; }

    public required bool SourceWasSpecified { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required AmbiguityClass Ambiguity { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required AmbiguityReason AmbiguityReason { get; init; }

    /// <summary>Encodings that read this file differently, when there are any.</summary>
    public IReadOnlyList<string> CompetingEncodings { get; init; } = [];

    /// <summary>Why this action, in words, when the action is not a plain conversion.</summary>
    public string? Reason { get; init; }

    /// <summary>Whether converting this file could change its Unicode content.</summary>
    public bool MayChangeText => Ambiguity == AmbiguityClass.TextChanging;
}

/// <summary>
/// A conversion plan: what EC would do, recorded so it can be reviewed before anything is
/// changed and then executed exactly as reviewed.
/// </summary>
/// <remarks>
/// The point is not the preview but the binding. A preview that is followed by a fresh
/// detection pass is a demonstration, not a promise: the second pass can reach different
/// conclusions, and the user approved the first. Every file therefore carries the hash it
/// had when the plan was made, and applying the plan verifies each one. A file that
/// changed in between invalidates the plan rather than being converted on the strength of
/// a decision made about different bytes.
/// </remarks>
internal sealed record ConversionPlan
{
    public int PlanVersion { get; init; } = 1;

    public required string CreatedUtc { get; init; }

    public required string ECVersion { get; init; }

    public required string BaseDirectory { get; init; }

    public required string TargetEncoding { get; init; }

    public required bool TargetHasBom { get; init; }

    public required bool BackupEnabled { get; init; }

    /// <summary>The source encoding named by the caller, if any.</summary>
    public string? ExplicitSourceEncoding { get; init; }

    public required IReadOnlyList<PlannedFile> Files { get; init; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    internal static ConversionPlan FromEntries(
        IEnumerable<ConversionReportEntry> entries,
        string baseDirectory,
        string targetEncoding,
        bool targetHasBom,
        bool backupEnabled,
        string? explicitSource)
    {
        var files = new List<PlannedFile>();

        foreach (ConversionReportEntry entry in entries)
        {
            PlannedAction action = entry.Result switch
            {
                ConversionRowResult.Unchanged => PlannedAction.Unchanged,
                ConversionRowResult.Skipped => PlannedAction.Skip,
                ConversionRowResult.Error => PlannedAction.Refuse,
                _ => PlannedAction.Convert,
            };

            string hash;
            long size;

            try
            {
                hash = ConversionMetadataStore.ComputeSha256(entry.FilePath);
                size = new FileInfo(entry.FilePath).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file that cannot be read now cannot be planned for. Recorded as a
                // refusal rather than omitted, so the plan still accounts for it.
                hash = string.Empty;
                size = 0;
                action = PlannedAction.Refuse;
            }

            int codePage = 0;

            try
            {
                codePage = Encoding.GetEncoding(entry.SourceEncoding).CodePage;
            }
            catch (ArgumentException)
            {
                // Leave it at zero: an unrecognised label is itself part of the record.
            }

            files.Add(new PlannedFile
            {
                Path = entry.FilePath,
                Size = size,
                Sha256 = hash,
                Action = action,
                SourceEncoding = entry.SourceEncoding,
                SourceCodePage = codePage,
                SourceHasBom = entry.SourceHasBom,
                SourceWasSpecified = entry.SourceEncodingWasSpecified,
                Ambiguity = entry.Ambiguity,
                AmbiguityReason = entry.AmbiguityReason,
                CompetingEncodings = entry.CompetingEncodings,
                Reason = string.IsNullOrEmpty(entry.Diagnostic) ? null : entry.Diagnostic,
            });
        }

        return new ConversionPlan
        {
            CreatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ECVersion = typeof(ConversionPlan).Assembly.GetName().Version?.ToString()
                        ?? "unknown",
            BaseDirectory = baseDirectory,
            TargetEncoding = targetEncoding,
            TargetHasBom = targetHasBom,
            BackupEnabled = backupEnabled,
            ExplicitSourceEncoding = explicitSource,
            Files = files,
        };
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

    internal static ConversionPlan? Load(string path, out string? error)
    {
        try
        {
            ConversionPlan? plan = JsonSerializer.Deserialize<ConversionPlan>(
                File.ReadAllText(path));

            if (plan is null)
            {
                error = $"'{path}' is empty.";
                return null;
            }

            if (plan.PlanVersion != 1)
            {
                error = $"Plan version {plan.PlanVersion} is not supported.";
                return null;
            }

            error = null;
            return plan;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Confirms every file is still exactly what it was when the plan was made.
    /// </summary>
    /// <returns>
    /// The files that no longer match, empty when the plan is still valid.
    /// </returns>
    /// <remarks>
    /// Deliberately all-or-nothing. Converting the files that still match and skipping
    /// the rest would apply a plan the user reviewed as a whole to a directory that is no
    /// longer the one they reviewed, and the files most likely to have changed are the
    /// ones something else is actively writing.
    /// </remarks>
    internal IReadOnlyList<string> FindStaleFiles()
    {
        var stale = new List<string>();

        foreach (PlannedFile file in Files)
        {
            if (file.Action != PlannedAction.Convert)
                continue;

            try
            {
                if (!File.Exists(file.Path))
                {
                    stale.Add($"{file.Path} (no longer exists)");
                    continue;
                }

                if (ConversionMetadataStore.ComputeSha256(file.Path) != file.Sha256)
                    stale.Add($"{file.Path} (contents changed since the plan was made)");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stale.Add($"{file.Path} ({ex.Message})");
            }
        }

        return stale;
    }

    /// <summary>The summary a person reads before deciding.</summary>
    /// <remarks>
    /// Written so the counts add up on the page. A reader who cannot see that the
    /// sub-totals sum to the whole has to trust the numbers instead of checking them,
    /// which is the opposite of what a preflight is for.
    /// </remarks>
    internal string Summarize()
    {
        int Count(PlannedAction action) => Files.Count(f => f.Action == action);

        int convert = Count(PlannedAction.Convert);
        int equivalent = Files.Count(
            f => f.Action == PlannedAction.Convert
                 && f.Ambiguity == AmbiguityClass.TextEquivalent);
        int changing = Files.Count(f => f.MayChangeText);
        int otherRefusals = Count(PlannedAction.Refuse) - changing;

        var lines = new List<string>
        {
            $"Selected:                     {Files.Count}",
            string.Empty,
            $"Will convert:                 {convert}",
            $"  encoding determined:        {convert - equivalent}",
            $"  same text either way:       {equivalent}",
            $"Already in target encoding:   {Count(PlannedAction.Unchanged)}",
            $"Encoding not identified:      {Count(PlannedAction.Skip)}",
            $"Refused, ambiguous encoding:  {changing}",
            $"Refused, unreadable:          {otherRefusals}",
            string.Empty,
            $"Backups:                      {(BackupEnabled ? "enabled" : "DISABLED")}",
            $"Target:                       {TargetEncoding}"
                + (TargetHasBom ? " with BOM" : " without BOM"),
        };

        if (!string.IsNullOrEmpty(ExplicitSourceEncoding))
            lines.Add($"Source encoding:              {ExplicitSourceEncoding} (specified)");

        lines.Add(string.Empty);
        lines.Add("No files modified.");

        return string.Join(Environment.NewLine, lines);
    }
}
