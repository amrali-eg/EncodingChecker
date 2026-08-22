using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EncodingChecker
{
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
        /// Full path of a file to exclude regardless of patterns, e.g. an in-progress
        /// -Report output that would otherwise get rescanned as ordinary input on a
        /// later run with a broad -Include.
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
            CancellationToken cancellationToken)
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
                options.ExcludedFullPath);

            RunParallel(
                files,
                options.MaxParallelism,
                getPath: path => path,
                processItem: path => ProcessFileForScan(path, options, targetEncoding),
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
        /// <remarks>Same concurrent-<paramref name="onEntry"/> contract as <see cref="ScanDirectory"/>.</remarks>
        internal static void ConvertFiles(
            IEnumerable<ConversionReportEntry> entries,
            string targetCharset,
            bool targetWriteBom,
            int maxParallelism,
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
                    if (entry.SourceEncoding == "(Unknown)")
                        return entry;

                    Encoding sourceEncoding;

                    try
                    {
                        sourceEncoding = Encoding.GetEncoding(entry.SourceEncoding);
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
                        targetCharset,
                        targetEncoding,
                        targetWriteBom,
                        whatIf: false,
                        backup: false);

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
            Encoding? targetEncoding)
        {
            Encoding? detected = TextEncoding.DetectFromFile(path);

            bool hasBom =
                detected != null &&
                detected.GetPreamble().Length > 0;

            string sourceCharset =
                detected?.WebName ?? "(Unknown)";

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
                    break;

                case ScanAction.Validate:
                    string label =
                        FormatCharsetLabel(sourceCharset, hasBom);

                    bool isValid =
                        sourceCharset != "(Unknown)" &&
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
                            options.TargetCharset!,
                            targetEncoding!,
                            options.TargetWriteBom,
                            options.WhatIf,
                            options.Backup);
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
            string targetCharset,
            Encoding targetEncoding,
            bool targetWriteBom,
            bool whatIf,
            bool backup)
        {
            entry.TargetEncoding = targetCharset;
            entry.TargetHasBom = targetWriteBom;

            bool alreadyMatches =
                string.Equals(
                    entry.SourceEncoding,
                    targetCharset,
                    StringComparison.OrdinalIgnoreCase) &&
                entry.SourceHasBom == targetWriteBom;

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
                    File.Copy(
                        path,
                        path + ".bak",
                        overwrite: true);
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

            ConversionResult result =
                EncodingConverter.Convert(
                    path,
                    path,
                    sourceEncoding,
                    targetEncoding,
                    conversionOptions);

            entry.Result =
                result.Success
                    ? ConversionRowResult.Converted
                    : ConversionRowResult.Error;

            if (!result.Success)
                entry.Diagnostic =
                    $"{result.ErrorCode}: {result.ErrorMessage}";
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
}
