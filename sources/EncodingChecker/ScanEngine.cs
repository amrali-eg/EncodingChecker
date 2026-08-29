using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EncodingChecker;

/// <summary>
/// Action performed for each scanned file.
/// </summary>
internal enum ScanAction
{
    /// <summary>
    /// Detect and report only.
    /// </summary>
    Detect,

    /// <summary>
    /// Detect and validate against <see cref="ScanDirectoryOptions.ValidCharsets"/>.
    /// </summary>
    Validate,

    /// <summary>
    /// Detect and convert when required.
    /// </summary>
    Convert,
}

/// <summary>
/// Options for <see cref="ScanEngine.ScanDirectory"/>.
/// </summary>
internal sealed class ScanDirectoryOptions
{
    internal required string BaseDirectory { get; init; }

    internal bool IncludeSubdirectories { get; init; } = true;

    /// <summary>Include masks; empty means "*".</summary>
    internal IReadOnlyList<string>? IncludePatterns { get; init; }

    /// <summary>Exclude masks applied after include masks.</summary>
    internal IReadOnlyList<string>? ExcludePatterns { get; init; }

    /// <summary>
    /// Full path of a file to exclude regardless of pattern matches.
    /// </summary>
    internal string? ExcludedFullPath { get; init; }

    internal ScanAction Action { get; init; }

    /// <summary>Accepted charset labels for validation.</summary>
    internal IReadOnlyCollection<string>? ValidCharsets { get; init; }

    /// <summary>Target charset for conversion, without "-bom".</summary>
    internal string? TargetCharset { get; init; }

    /// <summary>
    /// Source charset chosen by the caller, used instead of detection.
    /// </summary>
    /// <remarks>
    /// Choosing the source does not bypass conversion safeguards or verification.
    /// </remarks>
    internal string? SourceCharset { get; init; }

    /// <summary>
    /// Capture each source file's hash for the conversion journal.
    /// </summary>
    /// <remarks>
    /// Disabled by default to avoid an extra full read when no journal is needed.
    /// </remarks>
    internal bool CaptureSourceHashes { get; init; }

    internal bool TargetWriteBom { get; init; }

    /// <summary>Simulate conversion without writing.</summary>
    internal bool WhatIf { get; init; }

    /// <summary>Back up the original file before conversion.</summary>
    internal bool Backup { get; init; }

    internal int MaxParallelism { get; init; } = ScanEngine.DefaultMaxParallelism;
}

/// <summary>
/// Shared file-scanning and conversion pipeline for the GUI and CLI.
/// </summary>
internal static class ScanEngine
{
    internal static readonly int DefaultMaxParallelism =
        Math.Min(Environment.ProcessorCount, 4);

    /// <summary>Charset label used when the source encoding cannot be established.</summary>
    internal const string UnknownCharset = "(Unknown)";

    #region Public API

    /// <summary>
    /// Scans the base directory and processes matching files with bounded parallelism.
    /// </summary>
    /// <remarks>
    /// <paramref name="onEntry"/> is invoked concurrently from worker threads.
    /// </remarks>
    internal static void ScanDirectory(
        ScanDirectoryOptions options,
        Action<ConversionReportEntry> onEntry,
        CancellationToken cancellationToken,
        Action<string>? onWarning = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onEntry);

        // Resolve shared settings once and fail before scanning any file.
        Encoding? targetEncoding = ValidateOptions(options);

        List<Regex> includePatterns =
            DirectoryTraversal.CompilePatterns(options.IncludePatterns, defaultToMatchAll: true);

        List<Regex> excludePatterns =
            DirectoryTraversal.CompilePatterns(options.ExcludePatterns, defaultToMatchAll: false);

        IEnumerable<string> files = DirectoryTraversal.EnumerateFiles(
            options.BaseDirectory,
            options.IncludeSubdirectories,
            includePatterns,
            excludePatterns,
            options.ExcludedFullPath,
            onWarning);

        RunParallel(
            files,
            options.MaxParallelism,
            getPath: path => path,
            processItem: path =>
                ProcessFileForScan(path, options, targetEncoding, cancellationToken),
            onEntry: onEntry,
            cancellationToken);
    }

    /// <summary>
    /// Validates options independently of caller-side validation.
    /// </summary>
    private static Encoding? ValidateOptions(ScanDirectoryOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseDirectory) ||
            !Directory.Exists(options.BaseDirectory))
        {
            throw new ArgumentException(
                $@"Base directory '{options.BaseDirectory}' does not exist.",
                nameof(options));
        }

        if (DirectoryTraversal.IsReparsePointDirectory(options.BaseDirectory))
        {
            throw new ArgumentException(
                $@"Base directory '{options.BaseDirectory}' is a symbolic link or " +
                $@"other reparse point.",
                nameof(options));
        }

        if (options.MaxParallelism < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxParallelism,
                @"MaxParallelism must be at least 1.");
        }

        switch (options.Action)
        {
            case ScanAction.Convert:
                if (string.IsNullOrWhiteSpace(options.TargetCharset))
                {
                    throw new ArgumentException(
                        @"TargetCharset is required for Convert.",
                        nameof(options));
                }

                return Encoding.GetEncoding(options.TargetCharset);

            case ScanAction.Validate:
                if (options.ValidCharsets is null ||
                    options.ValidCharsets.Count == 0)
                {
                    throw new ArgumentException(
                        @"ValidCharsets is required for Validate.",
                        nameof(options));
                }

                break;
        }

        return null;
    }

    /// <summary>
    /// Converts previously detected entries with bounded parallelism.
    /// </summary>
    /// <param name="maxParallelism">The maximum number of concurrent operations.</param>
    /// <param name="whatIf">Simulate conversion without writing.</param>
    /// <param name="backup">Back up each original before conversion.</param>
    /// <param name="entries">The list of files to convert.</param>
    /// <param name="targetCharset">The character set to convert to.</param>
    /// <param name="targetWriteBom">Whether to write a BOM when converting to the target charset.</param>
    /// <param name="onEntry">The callback to invoke for each converted entry.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <remarks>
    /// <paramref name="onEntry"/> has the same concurrent-callback contract as
    /// <see cref="ScanDirectory"/>.
    /// </remarks>
    internal static void ConvertFiles(
        IEnumerable<ConversionReportEntry> entries,
        string targetCharset,
        bool targetWriteBom,
        int maxParallelism,
        bool whatIf,
        bool backup,
        Action<ConversionReportEntry> onEntry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(onEntry);

        // Resolve the target once, matching ScanDirectory.
        Encoding targetEncoding = Encoding.GetEncoding(targetCharset);

        RunParallel(
            entries,
            maxParallelism,
            getPath: entry => entry.FilePath,
            processItem: entry =>
            {
                // Use the label that describes the file as it exists now.
                ParseCharsetLabel(
                    entry.EffectiveSourceLabel,
                    out string sourceCharset,
                    out bool sourceHasBom);

                // Unknown sources are skipped before reaching Encoding.GetEncoding.
                if (sourceCharset == UnknownCharset)
                {
                    entry.Action = PlannedAction.Skip;
                    entry.SourceInterpretation = SourceInterpretation.NotApplicable;
                    entry.Result = ConversionRowResult.Skipped;
                    return entry;
                }

                Encoding sourceEncoding;

                try
                {
                    sourceEncoding = Encoding.GetEncoding(sourceCharset);
                }
                catch (ArgumentException)
                {
                    entry.Action = PlannedAction.Refuse;
                    entry.SourceInterpretation = SourceInterpretation.NotApplicable;
                    entry.Result = ConversionRowResult.Error;
                    entry.Diagnostic = $"The source encoding '{sourceCharset}' is not available.";
                    return entry;
                }

                ApplyConversion(
                    entry,
                    entry.FilePath,
                    sourceEncoding,
                    sourceCharset,
                    sourceHasBom,
                    targetCharset,
                    targetEncoding,
                    targetWriteBom,
                    whatIf,
                    backup,
                    cancellationToken);

                return entry;
            },
            onEntry: onEntry,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Splits a charset label into its base name and BOM flag.
    /// </summary>
    internal static void ParseCharsetLabel(
        string label,
        out string baseCharset,
        out bool hasBom)
    {
        hasBom = label.EndsWith("-bom", StringComparison.OrdinalIgnoreCase);
        baseCharset = hasBom ? label[..^4] : label;
    }

    internal static string FormatCharsetLabel(
        string baseCharset,
        bool hasBom) =>
        hasBom ? baseCharset + "-bom" : baseCharset;

    /// <summary>Whether EC offers this Unicode encoding with an optional BOM.</summary>
    internal static bool IsBomCapable(string charset) =>
        charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase)
        || charset.Equals("utf-16", StringComparison.OrdinalIgnoreCase)
        || charset.Equals("utf-16BE", StringComparison.OrdinalIgnoreCase)
        || charset.Equals("utf-32", StringComparison.OrdinalIgnoreCase)
        || charset.Equals("utf-32BE", StringComparison.OrdinalIgnoreCase);

    /// <summary>Formats a target for people without inventing BOM semantics for ASCII.</summary>
    internal static string DescribeTarget(string charset, bool hasBom) =>
        IsBomCapable(charset)
            ? charset + (hasBom ? " with a BOM" : " without a BOM")
            : charset;

    #endregion


    #region Per-File Processing

    private static ConversionReportEntry ProcessFileForScan(
        string path,
        ScanDirectoryOptions options,
        Encoding? targetEncoding,
        CancellationToken cancellationToken)
    {
        bool sourceWasSpecified = !string.IsNullOrWhiteSpace(options.SourceCharset);

        Encoding? detected;

        if (sourceWasSpecified)
        {
            try
            {
                detected = Encoding.GetEncoding(options.SourceCharset!);
            }
            catch (ArgumentException)
            {
                detected = null;
            }
        }
        else
        {
            // Record the application's request for automatic detection. Keeping this
            // here makes TextEncoding a pure byte-to-encoding utility, while tests can
            // still prove that View, conversion, and -Apply never re-detect a file.
            DetectionCounters.RecordDetection();
            detected = TextEncoding.DetectFromFile(path);
        }

        // A codec having a preamble does not mean this particular file has one.
        // Preserve the bytes' actual BOM state in plans and reports.
        bool hasBom = detected != null && HasPreamble(path, detected);

        string sourceCharset =
            detected?.WebName ?? UnknownCharset;

        var entry = new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = sourceCharset,
            SourceHasBom = hasBom,
            TargetEncoding = sourceCharset,
            TargetHasBom = hasBom,
            Result = ConversionRowResult.Unchanged,
            // Preserve whether the source was detected or explicitly supplied.
            SourceEncodingWasSpecified = sourceWasSpecified,
            CaptureSourceHash = options.CaptureSourceHashes
        };

        // Record detection only when detection actually ran.
        if (!sourceWasSpecified && detected is not null)
            entry.DetectedEncodingLabel = detected.WebName;

        switch (options.Action)
        {
            case ScanAction.Detect:
                if (sourceCharset == UnknownCharset)
                    entry.Result = ConversionRowResult.Skipped;

                break;

            case ScanAction.Validate:
                string label =
                    FormatCharsetLabel(sourceCharset, hasBom);

                bool isValid =
                    sourceCharset != UnknownCharset &&
                    options.ValidCharsets is not null &&
                    options.ValidCharsets.Contains(
                        label,
                        StringComparer.OrdinalIgnoreCase);

                entry.Result =
                    isValid
                        ? ConversionRowResult.Unchanged
                        : ConversionRowResult.Invalid;

                break;

            case ScanAction.Convert:
                if (detected != null)
                {
                    // Guaranteed present for Convert by ValidateOptions.
                    ApplyConversion(
                        entry,
                        path,
                        detected,
                        sourceCharset,
                        hasBom,
                        options.TargetCharset!,
                        targetEncoding!,
                        options.TargetWriteBom,
                        options.WhatIf,
                        options.Backup,
                        cancellationToken);
                }
                else
                {
                    entry.Result = ConversionRowResult.Skipped;
                }

                break;
        }

        return entry;
    }

    /// <summary>
    /// Determines whether this file begins with the given codec's actual preamble.
    /// </summary>
    /// <remarks>
    /// <see cref="Encoding.GetPreamble"/> describes a codec's optional marker, not the
    /// bytes in any particular file. A BOM-less UTF file must remain BOM-less in a plan.
    /// </remarks>
    private static bool HasPreamble(string path, Encoding encoding)
    {
        byte[] preamble = encoding.GetPreamble();

        if (preamble.Length == 0)
            return false;

        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            preamble.Length,
            FileOptions.SequentialScan);

        if (stream.Length < preamble.Length)
            return false;

        Span<byte> prefix = stackalloc byte[preamble.Length];
        stream.ReadExactly(prefix);
        return prefix.SequenceEqual(preamble);
    }

    /// <summary>
    /// Converts when the source does not already match the target.
    /// </summary>
    private static void ApplyConversion(
        ConversionReportEntry entry,
        string path,
        Encoding sourceEncoding,
        string sourceCharset,
        bool sourceHasBom,
        string targetCharset,
        Encoding targetEncoding,
        bool targetWriteBom,
        bool whatIf,
        bool backup,
        CancellationToken cancellationToken)
    {
        entry.TargetEncoding = targetCharset;
        entry.TargetHasBom = targetWriteBom;
        entry.ResolvedSourceLabel = FormatCharsetLabel(sourceCharset, sourceHasBom);

        // Capture the original hash before anything can overwrite the file.
        if (entry is { CaptureSourceHash: true, JournalSourceSha256: null })
        {
            try
            {
                entry.JournalSourceSha256 =
                    ConversionMetadataStore.ComputeSha256(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Leave the hash empty; the journal will reflect that it was unavailable.
            }
        }

        // A previously reviewed entry still has its source and target attached. Recheck
        // the small deterministic rule for each pass, but never detect the bytes again.
        if (entry.Action is null)
            DetectionCounters.RecordClassification();

        PlannedAction action = ConversionPolicy.Decide(
            sourceCharset,
            sourceHasBom,
            targetCharset,
            targetWriteBom,
            entry.SourceEncodingWasSpecified,
            TextEncoding.IsUnicodeOrAscii(sourceEncoding),
            out SourceInterpretation sourceInterpretation,
            out string? policyReason);

        entry.Action = action;
        entry.SourceInterpretation = sourceInterpretation;

        if (action != PlannedAction.Convert)
        {
            entry.Result = ConversionPolicy.ToRowResult(action);
            entry.Diagnostic = policyReason;
            return;
        }

        if (whatIf)
        {
            entry.Result = ConversionRowResult.Converted; // "would be converted"
            return;
        }

        if (backup)
        {
            try
            {
                CreateBackup(path);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                entry.Result = ConversionRowResult.Error;
                entry.Diagnostic = $"Backup failed: {ex.Message}";
                return;
            }
        }

        var conversionOptions = new ConversionOptions
        {
            WriteBom = targetWriteBom,

            // Without a backup there is nothing to restore from.
            RecordConversion = backup
                ? record => WriteConversionMetadata(path, record, entry)
                : null,

            // Present only when an approved plan pinned the original bytes.
            ExpectedSourceSha256 = entry.ExpectedSourceSha256,
        };

        // Conversion checks cancellation between safe installation points.
        ConversionResult result =
            EncodingConverter.Convert(
                path,
                path,
                sourceEncoding,
                targetEncoding,
                conversionOptions,
                progress: null,
                cancellationToken);

        // Propagate cancellation rather than recording it as a file error.
        if (result.ErrorCode == ConversionErrorCode.Cancelled)
            throw new OperationCanceledException(cancellationToken);

        // Track what encoding the file contains after the replacement attempt.
        if (result.ReplacementCommitted == true)
        {
            entry.CurrentCharsetLabel =
                FormatCharsetLabel(targetCharset, targetWriteBom);
        }
        else if (result.ReplacementCommitted is null)
        {
            // Unknown replacement state must not be decoded using either old or new metadata.
            entry.CurrentCharsetLabel = UnknownCharset;
        }

        entry.Result =
            result.Success
                ? ConversionRowResult.Converted
                : ConversionRowResult.Error;

        entry.Diagnostic =
            result.Success
                ? null
                : $"{result.ErrorCode}: {result.ErrorMessage}";
    }

    /// <summary>
    /// Writes "<paramref name="path"/>.bak" through a temporary file before replacement.
    /// </summary>
    /// <summary>
    /// Writes the sidecar describing how to undo this conversion, next to the backup.
    /// </summary>
    /// <remarks>
    /// Called after output verification and before installation so metadata failure leaves
    /// the original file intact.
    /// </remarks>
    private static string? WriteConversionMetadata(
        string path, ConversionRecord record, ConversionReportEntry entry)
    {
        string backupPath = path + ".bak";

        string backupHash;

        try
        {
            backupHash = ConversionMetadataStore.ComputeSha256(backupPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"the backup at '{backupPath}' could not be read: {ex.Message}";
        }

        // Reject stale backups; only the current source is a valid restore point.
        if (!string.IsNullOrEmpty(record.SourceSha256) &&
            !backupHash.Equals(record.SourceSha256, StringComparison.OrdinalIgnoreCase))
        {
            return $"the backup at '{backupPath}' does not match the file being "
                   + "converted, so it is not a valid restore point.";
        }

        return ConversionMetadataStore.Write(path, new ConversionMetadata
        {
            ConversionId = Guid.NewGuid().ToString("D"),
            ConversionTimestampUtc =
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            EcVersion = typeof(ScanEngine).Assembly.GetName().Version?.ToString()
                        ?? "unknown",
            OriginalPath = path,
            OriginalSize = record.SourceBytes,
            OriginalSha256 = record.SourceSha256,
            BackupPath = backupPath,
            BackupSha256 = backupHash,
            // The recovery key is the codec that actually read the source.
            SourceEncodingId = record.SourceCodePage,
            SourceEncodingName = record.SourceEncoding,
            SourceEncodingMode = entry.SourceEncodingWasSpecified
                ? SourceEncodingMode.Explicit
                : SourceEncodingMode.Detected,
            SourceHasBom = record.SourceHasBom,

            // Provenance is null when detection did not run.
            DetectedEncodingId = ResolveCodePage(entry.DetectedEncodingLabel),
            DetectedEncodingName = entry.DetectedEncodingLabel,

            TargetEncodingId = record.TargetCodePage,
            TargetEncodingName = record.TargetEncoding,
            TargetHasBom = record.TargetHasBom,
            SourceTextSha256 = record.SourceTextSha256,
            OutputTextSha256 = record.OutputTextSha256,
            UnicodeScalars = record.UnicodeScalars,
        });
    }

    /// <summary>The code page for a charset label, or null when it cannot be resolved.</summary>
    private static int? ResolveCodePage(string? label)
    {
        if (string.IsNullOrEmpty(label))
            return null;

        try
        {
            return Encoding.GetEncoding(label).CodePage;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static void CreateBackup(string path)
    {
        string? directory = Path.GetDirectoryName(path);

        if (string.IsNullOrEmpty(directory))
        {
            throw new IOException(
                $"Could not determine a directory for path '{path}'.");
        }

        // Reuse the existing temp-file naming rule so the backup temp stays out of scans.
        string tempPath = Path.Combine(
            directory,
            $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.bak.{EncodingConverter.TempFileSuffix}");

        try
        {
            using (FileStream source = new(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       EncodingConverter.DefaultBufferSize,
                       FileOptions.SequentialScan))
            using (FileStream destination = new(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       EncodingConverter.DefaultBufferSize,
                       FileOptions.SequentialScan))
            {
                source.CopyTo(destination, EncodingConverter.DefaultBufferSize);
                destination.Flush(flushToDisk: true);
            }

            EncodingConverter.AtomicReplaceForBackup(tempPath, path + ".bak");
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                // Cleanup failure does not invalidate the completed backup.
            }
        }
    }

    #endregion


    #region Bounded Parallel Execution

    /// <summary>
    /// Processes items with bounded parallelism and isolates per-file errors.
    /// Cancellation propagates normally.
    /// </summary>
    private static void RunParallel<T>(
        IEnumerable<T> items,
        int maxParallelism,
        Func<T, string> getPath,
        Func<T, ConversionReportEntry?> processItem,
        Action<ConversionReportEntry> onEntry,
        CancellationToken cancellationToken)
    {
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism =
                Math.Max(1, maxParallelism),

            CancellationToken =
                cancellationToken,
        };

        Parallel.ForEach(
            items,
            parallelOptions,
            item =>
            {
                ConversionReportEntry? entry;

                try
                {
                    entry = processItem(item);
                }
                catch (Exception ex) when (
                    ex is IOException or
                    UnauthorizedAccessException or
                    ArgumentException or
                    NotSupportedException)
                {
                    entry = new ConversionReportEntry
                    {
                        FilePath = getPath(item),
                        SourceEncoding = "(Error)",
                        TargetEncoding = "(Error)",
                        Result = ConversionRowResult.Error,
                        Diagnostic = ex.Message,
                    };
                }

                if (entry is not null)
                    onEntry(entry);
            });
    }

    #endregion
}
