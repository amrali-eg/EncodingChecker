using System;
using System.Collections.Generic;
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
    /// Full path of a file to exclude regardless of pattern matches - e.g. an
    /// in-progress -Report output, so it isn't rescanned as input on a later run.
    /// </summary>
    internal string? ExcludedFullPath { get; init; }

    internal ScanAction Action { get; init; }

    /// <summary>Accepted charset labels for validation.</summary>
    internal IReadOnlyCollection<string>? ValidCharsets { get; init; }

    /// <summary>Target charset for conversion, without "-bom".</summary>
    internal string? TargetCharset { get; init; }

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

    /// <summary>Charset label for a file whose encoding could not be established.</summary>
    internal const string UNKNOWN_CHARSET = "(Unknown)";

    #region Public API

    /// <summary>
    /// Scans the base directory and processes matching files with bounded parallelism.
    /// Skips excluded directories, reparse points, the tool's own backup/temp files,
    /// and likely binary files.
    /// </summary>
    /// <remarks>
    /// <paramref name="onEntry"/> is invoked concurrently from worker threads; callers
    /// must marshal to the UI thread themselves rather than touch UI controls directly.
    /// </remarks>
    internal static void ScanDirectory(
        ScanDirectoryOptions options,
        Action<ConversionReportEntry> onEntry,
        CancellationToken cancellationToken,
        Action<string>? onWarning = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onEntry);

        // Resolved once and reused per file; also makes an invalid target fail the
        // whole scan immediately, including under -WhatIf.
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
    /// Validates options up front, independent of caller-side validation. Returns the
    /// resolved target <see cref="Encoding"/> for <see cref="ScanAction.Convert"/>, or
    /// <see langword="null"/> otherwise.
    /// </summary>
    private static Encoding? ValidateOptions(ScanDirectoryOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseDirectory) ||
            !Directory.Exists(options.BaseDirectory))
        {
            throw new ArgumentException(
                $"Base directory '{options.BaseDirectory}' does not exist.",
                nameof(options));
        }

        if (DirectoryTraversal.IsReparsePointDirectory(options.BaseDirectory))
        {
            throw new ArgumentException(
                $"Base directory '{options.BaseDirectory}' is a symbolic link or " +
                "other reparse point.",
                nameof(options));
        }

        if (options.MaxParallelism < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxParallelism,
                "MaxParallelism must be at least 1.");
        }

        switch (options.Action)
        {
            case ScanAction.Convert:
                if (string.IsNullOrWhiteSpace(options.TargetCharset))
                {
                    throw new ArgumentException(
                        "TargetCharset is required for Convert.",
                        nameof(options));
                }

                return Encoding.GetEncoding(options.TargetCharset);

            case ScanAction.Validate:
                if (options.ValidCharsets is null ||
                    options.ValidCharsets.Count == 0)
                {
                    throw new ArgumentException(
                        "ValidCharsets is required for Validate.",
                        nameof(options));
                }

                break;
        }

        return null;
    }

    /// <summary>
    /// Converts previously detected entries with bounded parallelism.
    /// </summary>
    /// <param name="whatIf">Simulate conversion without writing, matching ScanDirectory's option.</param>
    /// <param name="backup">Back up each original before conversion, matching ScanDirectory's option.</param>
    /// <remarks>Same concurrent-<paramref name="onEntry"/> contract as <see cref="ScanDirectory"/>.</remarks>
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

        // Resolved once instead of per file, matching ScanDirectory.
        Encoding targetEncoding = Encoding.GetEncoding(targetCharset);

        RunParallel(
            entries,
            maxParallelism,
            getPath: entry => entry.FilePath,
            processItem: entry =>
            {
                // A row this tool already converted no longer matches its original scan
                // data, so the label recorded at install time wins. Decoding the new file
                // with the old encoding can silently produce mojibake that neither strict
                // decoding nor hash verification catches, since both would use the same
                // wrong encoding.
                string effectiveLabel =
                    entry.CurrentCharsetLabel
                    ?? FormatCharsetLabel(entry.SourceEncoding, entry.SourceHasBom);

                ParseCharsetLabel(
                    effectiveLabel,
                    out string sourceCharset,
                    out bool sourceHasBom);

                if (sourceCharset == UNKNOWN_CHARSET)
                {
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
                    entry.Result = ConversionRowResult.Error;
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

    #endregion


    #region Per-File Processing

    private static ConversionReportEntry? ProcessFileForScan(
        string path,
        ScanDirectoryOptions options,
        Encoding? targetEncoding,
        CancellationToken cancellationToken)
    {
        Encoding? detected = TextEncoding.DetectFromFile(path);

        bool hasBom =
            detected != null &&
            detected.GetPreamble().Length > 0;

        string sourceCharset =
            detected?.WebName ?? UNKNOWN_CHARSET;

        var entry = new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = sourceCharset,
            SourceHasBom = hasBom,
            TargetEncoding = sourceCharset,
            TargetHasBom = hasBom,
            Result = ConversionRowResult.Unchanged,
        };

        switch (options.Action)
        {
            case ScanAction.Detect:
                if (sourceCharset == UNKNOWN_CHARSET)
                    entry.Result = ConversionRowResult.Skipped;

                break;

            case ScanAction.Validate:
                string label =
                    FormatCharsetLabel(sourceCharset, hasBom);

                bool isValid =
                    sourceCharset != UNKNOWN_CHARSET &&
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

        // Compared against the file's current state, not entry.SourceEncoding, which
        // stays at its original-scan value for reporting even after a conversion.
        bool alreadyMatches =
            string.Equals(
                sourceCharset,
                targetCharset,
                StringComparison.OrdinalIgnoreCase) &&
            sourceHasBom == targetWriteBom;

        if (alreadyMatches)
        {
            entry.Result = ConversionRowResult.Unchanged;
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
        };

        // Without the token, Parallel.ForEach could only observe cancellation between
        // files, so a large in-flight conversion had to finish first. Convert's own
        // checkpoints make cancelling mid-file safe: nothing is installed until the
        // temp file is written, verified, and atomically replaced.
        ConversionResult result =
            EncodingConverter.Convert(
                path,
                path,
                sourceEncoding,
                targetEncoding,
                conversionOptions,
                progress: null,
                cancellationToken);

        // Convert reports cancellation as a result code rather than an exception.
        // Rethrow it so a user-initiated cancel unwinds the scan like every other
        // cancellation instead of being recorded as a per-file conversion failure.
        if (result.ErrorCode == ConversionErrorCode.Cancelled)
            throw new OperationCanceledException(cancellationToken);

        // Gated on whether the file was actually replaced, not on overall success:
        // MetadataRestoreFailed reports Success == false after the replacement has
        // already been installed, so the file on disk is the target encoding even
        // though the row is an error.
        if (result.ReplacementCommitted == true)
        {
            entry.CurrentCharsetLabel =
                FormatCharsetLabel(targetCharset, targetWriteBom);
        }
        else if (result.ReplacementCommitted is null)
        {
            // The replacement outcome is unknown, so neither the original scan data nor
            // the target describes the file reliably. "(Unknown)" makes a later
            // conversion skip the row instead of decoding it with a guess.
            entry.CurrentCharsetLabel = UNKNOWN_CHARSET;
        }

        entry.Result =
            result.Success
                ? ConversionRowResult.Converted
                : ConversionRowResult.Error;

        // Cleared on success so a retry that succeeds doesn't keep the previous failure.
        entry.Diagnostic =
            result.Success
                ? null
                : $"{result.ErrorCode}: {result.ErrorMessage}";
    }

    /// <summary>
    /// Writes "<paramref name="path"/>.bak" via temp-file-then-atomic-replace, so a
    /// crash mid-write can't leave a truncated backup (unlike a plain File.Copy).
    /// </summary>
    private static void CreateBackup(string path)
    {
        string? directory = Path.GetDirectoryName(path);

        if (string.IsNullOrEmpty(directory))
        {
            throw new IOException(
                $"Could not determine a directory for path '{path}'.");
        }

        // Sharing the conversion temp suffix means this file is excluded from future
        // scans by DirectoryTraversal's existing rule, rather than needing a second one.
        string tempPath = Path.Combine(
            directory,
            $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.bak.{EncodingConverter.TEMP_FILE_SUFFIX}");

        try
        {
            using (FileStream source = new(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       EncodingConverter.DEFAULT_BUFFER_SIZE,
                       FileOptions.SequentialScan))
            using (FileStream destination = new(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       EncodingConverter.DEFAULT_BUFFER_SIZE,
                       FileOptions.SequentialScan))
            {
                source.CopyTo(destination, EncodingConverter.DEFAULT_BUFFER_SIZE);
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
                // Cleanup failure does not affect the backup result.
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
