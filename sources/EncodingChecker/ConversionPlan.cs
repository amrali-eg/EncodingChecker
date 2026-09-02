using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

/// <summary>
/// The guarantees provided by this build of EC during conversion.
/// </summary>
/// <remarks>
/// Recorded in every plan so an approved plan cannot later be applied under different
/// conversion or classification behaviour.
/// </remarks>
internal sealed record ConversionSemantics
{
    /// <summary>
    /// Changes only when the meaning of an existing plan's decisions changes.
    /// </summary>
    internal const int Current = 6;

    /// <summary>The guarantees of <see cref="Current"/> shown to the reader.</summary>
    internal const string Describes =
        "source-bound detection, strict codecs, verified output, atomic install, explicit source required for legacy text, proven BOM-less UTF-16 byte order, a reviewed refusal is binding";

    /// <summary>Malformed input is rejected rather than replaced.</summary>
    public bool StrictDecoding { get; init; } = true;

    /// <summary>Unrepresentable content is rejected rather than substituted.</summary>
    public bool StrictEncoding { get; init; } = true;

    /// <summary>Output is re-decoded and compared before installation.</summary>
    public bool OutputVerification { get; init; } = true;

    /// <summary>The source is never rewritten in place.</summary>
    public bool AtomicInstall { get; init; } = true;

    /// <summary>
    /// Automatically detected legacy text requires a user-selected source codec;
    /// Unicode and ASCII are converted automatically.
    /// </summary>
    public bool LegacyRequiresExplicitSource { get; init; } = true;
}

/// <summary>One file's entry in a conversion plan.</summary>
internal sealed record PlannedFile
{
    /// <summary>
    /// The path relative to the plan's <see cref="ConversionPlan.BaseDirectory"/>.
    /// </summary>
    /// <remarks>
    /// Relative paths keep the plan bound to its recorded directory rather than to the
    /// machine on which it was created.
    /// </remarks>
    public required string RelativePath { get; init; }

    public required long Size { get; init; }

    /// <summary>
    /// The file's SHA-256 when the plan was created. Applying the plan requires this
    /// value to remain unchanged.
    /// </summary>
    public required string Sha256 { get; init; }

    public required PlannedAction Action { get; init; }

    public required string SourceEncoding { get; init; }

    public required int SourceCodePage { get; init; }

    public required bool SourceHasBom { get; init; }

    public required bool SourceWasSpecified { get; init; }

    /// <summary>What automatic detection found before an explicit source was applied.</summary>
    public string? DetectedEncoding { get; init; }

    /// <summary>The detected encoding's canonical code page, when available.</summary>
    public int? DetectedCodePage { get; init; }

    /// <summary>Whether automatic detection found the encoding's BOM.</summary>
    public bool DetectedHasBom { get; init; }

    /// <summary>
    /// Whether the detected Unicode identity passed strict full-file validation.
    /// </summary>
    /// <remarks>
    /// This policy input must survive save/load so explicit-source conflict decisions
    /// remain reproducible when the plan is applied.
    /// </remarks>
    public bool HasReliableUnicodeDetection { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required SourceInterpretation SourceInterpretation { get; init; }

    /// <summary>Why this action was chosen, when it is not a plain conversion.</summary>
    public string? Reason { get; init; }

    /// <summary>Stable machine-readable form of <see cref="Reason"/>.</summary>
    public string? ReasonCode { get; init; }

    /// <summary>Whether automatic conversion needs the user to name the source codec.</summary>
    public bool NeedsSourceChoice =>
        Action == PlannedAction.Refuse &&
        ConversionPolicy.RequiresExplicitSourceChoice(SourceInterpretation, ReasonCode);
}

/// <summary>
/// The decision an approved plan recorded for one file, carried into the write pass.
/// </summary>
/// <remarks>
/// Applying a plan may become stricter if a file is no longer safe, but it must never
/// turn a reviewed non-writing action into a conversion.
/// </remarks>
internal sealed record ApprovedDecision(
    PlannedAction Action,
    SourceInterpretation SourceInterpretation,
    string? ReasonCode,
    string? Diagnostic);

/// <summary>
/// A conversion plan that can be reviewed before execution and then applied as approved.
/// </summary>
/// <remarks>
/// Every planned file carries its original hash, so applying the plan fails rather than
/// converting bytes that changed after review.
/// <para>
/// The plan also records the conversion settings and guarantees it was created under, so
/// applying it does not depend on unrelated current options.
///</para>
/// </remarks>
internal sealed record ConversionPlan
{
    /// <summary>The plan file schema version.</summary>
    internal const int CurrentPlanVersion = 5;

    public int PlanVersion { get; init; } = CurrentPlanVersion;

    /// <summary>The conversion behaviour this plan was created under.</summary>
    public int SemanticsVersion { get; init; } = ConversionSemantics.Current;

    public required string CreatedUtc { get; init; }

    public required string EcVersion { get; init; }

    /// <summary>The guarantees recorded for the reader.</summary>
    public ConversionSemantics Semantics { get; init; } = new();

    /// <summary>The directory containing all planned relative paths.</summary>
    public required string BaseDirectory { get; init; }

    public required string TargetEncoding { get; init; }

    public required bool TargetHasBom { get; init; }

    public required bool BackupEnabled { get; init; }

    /// <summary>The source encoding explicitly supplied by the caller, if any.</summary>
    public string? ExplicitSourceEncoding { get; init; }

    public required IReadOnlyList<PlannedFile> Files { get; init; }

    internal ConversionPlanSummary Summary => ConversionPlanSummary.From(Files);

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
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        var files = new List<PlannedFile>();

        foreach (ConversionReportEntry entry in entries)
        {
            // Planning requires a completed policy decision.
            PlannedAction action = entry.Action
                ?? throw new InvalidOperationException(
                    $"'{entry.FilePath}' reached a conversion plan without a decision. "
                    + "Entries must go through a conversion pass before being planned.");

            string hash;
            long size;

            try
            {
                // Keep detection and its hash bound to the same source snapshot.
                hash = entry.ExpectedSourceSha256
                       ?? ConversionMetadataStore.ComputeSha256(entry.FilePath);
                size = entry.ExpectedSourceSize
                       ?? new FileInfo(entry.FilePath).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Keep unreadable files visible in the plan rather than silently dropping them.
                hash = string.Empty;
                size = 0;
                action = PlannedAction.Refuse;
            }

            // Record the encoding the conversion will actually use.
            ScanEngine.ParseCharsetLabel(
                entry.EffectiveSourceLabel,
                out string sourceCharset,
                out bool sourceHasBom);

            int codePage = 0;

            if (TextEncoding.TryResolve(sourceCharset, out Encoding? encoding))
                codePage = encoding!.CodePage;

            int? detectedCodePage = null;

            if (TextEncoding.TryResolve(entry.DetectedEncodingLabel, out Encoding? detected))
                detectedCodePage = detected!.CodePage;

            files.Add(new PlannedFile
            {
                RelativePath = Path.GetRelativePath(root, entry.FilePath),
                Size = size,
                Sha256 = hash,
                Action = action,
                SourceEncoding = sourceCharset,
                SourceCodePage = codePage,
                SourceHasBom = sourceHasBom,
                SourceWasSpecified = entry.SourceEncodingWasSpecified,
                DetectedEncoding = entry.DetectedEncodingLabel,
                DetectedCodePage = detectedCodePage,
                DetectedHasBom = entry.DetectedEncodingHasBom,
                HasReliableUnicodeDetection = entry.HasReliableUnicodeDetection,
                SourceInterpretation = entry.SourceInterpretation
                    ?? throw new InvalidOperationException(
                        $"'{entry.FilePath}' reached a conversion plan without being "
                        + "decided."),
                Reason = string.IsNullOrEmpty(entry.Diagnostic) ? null : entry.Diagnostic,
                ReasonCode = entry.ReasonCode,
            });
        }

        return new ConversionPlan
        {
            CreatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            EcVersion = typeof(ConversionPlan).Assembly.GetName().Version?.ToString()
                        ?? "unknown",
            BaseDirectory = root,
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

            if (plan.PlanVersion != CurrentPlanVersion)
            {
                error = $"This plan uses schema version {plan.PlanVersion}; this build "
                        + $"writes and reads version {CurrentPlanVersion}. Re-run -Plan "
                        + "to produce one it can carry out.";
                return null;
            }

            // A compatible file shape does not imply compatible conversion behaviour.
            if (plan.SemanticsVersion != ConversionSemantics.Current)
            {
                error = "This plan was made under different conversion behaviour "
                        + $"(semantics version {plan.SemanticsVersion}; this build uses "
                        + $"{ConversionSemantics.Current}, and the plan was written by "
                        + $"EC {plan.EcVersion}). What it approved is not what this "
                        + "build would do. Re-run -Plan and review the result.";
                return null;
            }

            if (string.IsNullOrWhiteSpace(plan.BaseDirectory) ||
                string.IsNullOrWhiteSpace(plan.TargetEncoding) ||
                plan.Files is null)
            {
                error = "The plan is missing required conversion information.";
                return null;
            }

            foreach (PlannedFile? file in plan.Files)
            {
                if (file is null ||
                    string.IsNullOrWhiteSpace(file.RelativePath) ||
                    string.IsNullOrWhiteSpace(file.Sha256) ||
                    string.IsNullOrWhiteSpace(file.SourceEncoding))
                {
                    error = "The plan contains an incomplete file entry.";
                    return null;
                }
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
    /// Resolves a planned file against the plan's recorded directory.
    /// </summary>
    /// <returns>
    /// The full path, or <see langword="null"/> when the path escapes the plan's directory.
    /// </returns>
    internal string? ResolvePath(PlannedFile file)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(BaseDirectory));
        string full;

        try
        {
            full = Path.GetFullPath(Path.Combine(root, file.RelativePath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }

        return full.StartsWith(
            root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase)
            ? full
            : null;
    }

    /// <summary>
    /// Finds planned files whose bytes no longer match the approved plan.
    /// </summary>
    /// <returns>The files that changed, or an empty list when all remain valid.</returns>
    /// <remarks>
    /// Any stale planned file invalidates the run rather than partially applying a plan
    /// that was reviewed as a whole.
    /// </remarks>
    internal IReadOnlyList<string> FindStaleFiles()
    {
        var stale = new List<string>();

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!TextEncoding.TryResolve(TargetEncoding, out _))
            stale.Add($"Target encoding '{TargetEncoding}' is not available.");

        foreach (PlannedFile file in Files)
        {
            string? path = ResolvePath(file);

            if (path is null)
            {
                stale.Add($"{file.RelativePath} (resolves outside the plan's directory)");
                continue;
            }

            if (!paths.Add(path))
            {
                stale.Add($"{path} (appears more than once in the plan)");
                continue;
            }

            Encoding? sourceEncoding = null;

            if (file.Action == PlannedAction.Convert &&
                !TextEncoding.TryResolve(file.SourceEncoding, out sourceEncoding))
            {
                stale.Add($"{path} (source encoding '{file.SourceEncoding}' is not available)");
                continue;
            }

            if (file.Action == PlannedAction.Convert &&
                sourceEncoding!.CodePage != file.SourceCodePage)
            {
                stale.Add($"{path} (source codec identity does not match the plan)");
                continue;
            }

            if (file.DetectedEncoding is not null)
            {
                if (!TextEncoding.TryResolve(file.DetectedEncoding, out Encoding? detected) ||
                    !file.DetectedCodePage.HasValue ||
                    detected!.CodePage != file.DetectedCodePage.Value)
                {
                    stale.Add($"{path} (detected codec identity does not match the plan)");
                    continue;
                }
            }
            else if (file.DetectedCodePage.HasValue || file.DetectedHasBom)
            {
                stale.Add($"{path} (detected codec provenance is incomplete)");
                continue;
            }

            try
            {
                // Existence first: a missing file makes every path component
                // uninspectable, and the link check would then report it as one.
                if (!File.Exists(path))
                {
                    stale.Add($"{path} (no longer exists)");
                    continue;
                }

                // A path is only as stable as the components above it, and the hash
                // cannot see the difference: an identical copy behind a link hashes
                // identically.
                if (DirectoryTraversal.HasReparsePointInPath(BaseDirectory, path))
                {
                    stale.Add(
                        $"{path} (the file or a directory in its path is now a symbolic " +
                        "link, another reparse point, or could not be inspected)");
                    continue;
                }

                if (ConversionMetadataStore.ComputeSha256(path) != file.Sha256)
                    stale.Add($"{path} (contents changed since the plan was made)");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stale.Add($"{path} ({ex.Message})");
            }
        }

        return stale;
    }

    /// <summary>The summary shown before the user decides.</summary>
    internal string Summarize()
    {
        ConversionPlanSummary summary = Summary;

        var lines = new List<string>
        {
            $"Selected:                     {summary.Selected}",
            string.Empty,
            $"Will convert:                 {summary.ReadyToConvert}",
            $"Already in target encoding:   {summary.AlreadyTarget}",
            $"Encoding not identified:      {summary.NotIdentified}",
            $"Needs source choice:          {summary.NeedsSourceChoice}",
            $"Refused, unreadable:          {summary.OtherRefusals}",
            string.Empty,
            $"Directory:                    {BaseDirectory}",
            $"Target:                       {ScanEngine.DescribeTarget(TargetEncoding, TargetHasBom)}",
            "Source encoding:              "
                + (string.IsNullOrEmpty(ExplicitSourceEncoding)
                    ? "detected per file"
                    : $"{ExplicitSourceEncoding} (specified; detection bypassed)"),
            $"Backups:                      {(BackupEnabled ? "enabled" : "DISABLED")}",
            $"Guarantees:                   {ConversionSemantics.Describes}",
            string.Empty,
            "No files modified.",
        };

        return string.Join(Environment.NewLine, lines);
    }
}
