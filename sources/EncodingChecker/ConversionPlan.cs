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

/// <summary>
/// What a conversion carried out by this build of EC guarantees.
/// </summary>
/// <remarks>
/// Recorded in every plan so that <c>-Apply</c> is not merely repeating a list of files
/// but re-asserting the conversion those files were approved for. None of these are
/// user-settable today; they are written down because a plan approved under them must
/// not be carried out by a build that no longer provides them.
/// </remarks>
internal sealed record ConversionSemantics
{
    /// <summary>
    /// Bumped whenever conversion or classification behaviour changes in a way that
    /// makes an older plan's decisions no longer the ones this build would make.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from the assembly version. Tying plan validity to the
    /// version number would invalidate every plan on a release that changed nothing
    /// about conversion, which teaches people to work around the check rather than read
    /// it. This moves only when the meaning of a plan moves.
    /// </remarks>
    internal const int Current = 1;

    /// <summary>
    /// The guarantees of <see cref="Current"/>, in words, for a person reading a summary.
    /// </summary>
    /// <remarks>
    /// Comes from the build rather than from a loaded plan's booleans on purpose. A plan
    /// is an editable file; describing what it claims about itself would let an edited
    /// one state something untrue about the conversion that is actually going to happen.
    /// A plan only reaches a summary once its semantics version has been accepted, so
    /// describing this build is describing that plan.
    /// </remarks>
    internal const string Describes =
        "strict codecs, verified output, atomic install, ambiguity refusal";

    /// <summary>Malformed input is rejected rather than replaced.</summary>
    public bool StrictDecoding { get; init; } = true;

    /// <summary>Content the target cannot represent is rejected, not substituted.</summary>
    public bool StrictEncoding { get; init; } = true;

    /// <summary>The output is re-decoded and compared before it is installed.</summary>
    public bool OutputVerification { get; init; } = true;

    /// <summary>The source is never rewritten in place.</summary>
    public bool AtomicInstall { get; init; } = true;

    /// <summary>
    /// Files whose bytes do not identify their encoding, where the rival readings
    /// disagree about the text, are refused rather than converted on a guess.
    /// </summary>
    public bool AmbiguityRefusal { get; init; } = true;
}

/// <summary>One file's entry in a conversion plan.</summary>
internal sealed record PlannedFile
{
    /// <summary>
    /// The file's path relative to the plan's <see cref="ConversionPlan.BaseDirectory"/>.
    /// </summary>
    /// <remarks>
    /// The identity, in place of an absolute path. A plan carrying absolute paths that is
    /// copied alongside its tree still names the original tree, so applying it from the
    /// copy would convert the files somewhere else - and every hash would match, because
    /// those are the files the plan was made from. Resolving against the recorded root
    /// makes that impossible to do by accident, and makes the plan legible as a document
    /// about a directory rather than about one machine.
    /// </remarks>
    public required string RelativePath { get; init; }

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
/// <para>
/// The plan also describes the conversion itself - root, target, BOM policy, source
/// encoding and how it was arrived at, backup policy, and the guarantees the converting
/// build provides - so that applying it needs no ambient option state at all. The file is
/// the whole approval.
/// </para>
/// </remarks>
internal sealed record ConversionPlan
{
    /// <summary>The plan file's schema. Changed only when this shape changes.</summary>
    internal const int CurrentPlanVersion = 2;

    public int PlanVersion { get; init; } = CurrentPlanVersion;

    /// <summary>The conversion behaviour this plan was made under. Checked on apply.</summary>
    public int SemanticsVersion { get; init; } = ConversionSemantics.Current;

    public required string CreatedUtc { get; init; }

    public required string ECVersion { get; init; }

    /// <summary>
    /// What the conversion guaranteed when the plan was approved. Recorded for the
    /// reader; <see cref="SemanticsVersion"/> is what the tool enforces.
    /// </summary>
    public ConversionSemantics Semantics { get; init; } = new();

    /// <summary>The directory every <see cref="PlannedFile.RelativePath"/> is under.</summary>
    public required string BaseDirectory { get; init; }

    public required string TargetEncoding { get; init; }

    public required bool TargetHasBom { get; init; }

    public required bool BackupEnabled { get; init; }

    /// <summary>The source encoding named by the caller, if any.</summary>
    public string? ExplicitSourceEncoding { get; init; }

    /// <summary>
    /// Whether the source encoding was chosen by the caller or worked out from the bytes.
    /// </summary>
    public string DetectionMode =>
        string.IsNullOrEmpty(ExplicitSourceEncoding) ? "Detected" : "Explicit";

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
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        var files = new List<PlannedFile>();

        foreach (ConversionReportEntry entry in entries)
        {
            // Taken from the decision itself rather than re-derived from the row result,
            // which cannot tell a refusal apart from a conversion that failed. An
            // undecided entry is a caller that skipped the policy, and planning a
            // conversion nobody decided on is the failure this whole mechanism exists to
            // prevent - so it is raised, not defaulted.
            PlannedAction action = entry.Action
                ?? throw new InvalidOperationException(
                    $"'{entry.FilePath}' reached a conversion plan without a decision. "
                    + "Entries must go through a conversion pass before being planned.");

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

            // What the conversion will actually read the file as, which is not always
            // the scan's original answer: a user may have named the encoding since.
            ScanEngine.ParseCharsetLabel(
                entry.EffectiveSourceLabel,
                out string sourceCharset,
                out bool sourceHasBom);

            int codePage = 0;

            try
            {
                codePage = Encoding.GetEncoding(sourceCharset).CodePage;
            }
            catch (ArgumentException)
            {
                // Leave it at zero: an unrecognised label is itself part of the record.
            }

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

            // The schema can be identical while the conversion it describes is not. A
            // plan approved under different behaviour is not this build's plan, whatever
            // its file format says.
            if (plan.SemanticsVersion != ConversionSemantics.Current)
            {
                error = "This plan was made under different conversion behaviour "
                        + $"(semantics version {plan.SemanticsVersion}; this build uses "
                        + $"{ConversionSemantics.Current}, and the plan was written by "
                        + $"EC {plan.ECVersion}). What it approved is not what this "
                        + "build would do. Re-run -Plan and review the result.";
                return null;
            }

            if (plan.Files is null)
            {
                error = $"'{path}' does not list any files.";
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
    /// The absolute path of a planned file, resolved against the plan's own root.
    /// </summary>
    /// <returns>
    /// The full path, or <see langword="null"/> if the entry resolves outside that root -
    /// a plan is an ordinary file that anyone can edit, and one naming
    /// <c>..\..\Windows\System32</c> must not reach outside the directory it claims to
    /// be about.
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

            string? path = ResolvePath(file);

            if (path is null)
            {
                stale.Add($"{file.RelativePath} (resolves outside the plan's directory)");
                continue;
            }

            try
            {
                if (!File.Exists(path))
                {
                    stale.Add($"{path} (no longer exists)");
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

    /// <summary>The summary a person reads before deciding.</summary>
    /// <remarks>
    /// Written so the counts add up on the page. A reader who cannot see that the
    /// categories sum to the whole has to trust the numbers instead of checking them,
    /// which is the opposite of what a preflight is for. The two indented lines break
    /// down the one above them and are not part of that sum.
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
            $"Directory:                    {BaseDirectory}",
            $"Target:                       {TargetEncoding}"
                + (TargetHasBom ? " with BOM" : " without BOM"),
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
