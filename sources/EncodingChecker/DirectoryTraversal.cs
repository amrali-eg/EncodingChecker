using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace EncodingChecker;

/// <summary>
/// Directory walking and file-pattern matching shared by the scan engine.
/// </summary>
internal static class DirectoryTraversal
{
    // Exclude common source-control, build, and dependency directories.
    private static readonly HashSet<string> ExcludedDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            ".svn",
            ".hg",
            ".vs",
            ".idea",
            "bin",
            "obj",
            "node_modules",
            "packages",
            "dist",
            "build",
            "target"
        };

    private static readonly EnumerationOptions DirectoryWalkOptions = new()
    {
        // Enumerate excluded directories so callers can report that they were not entered.
        AttributesToSkip = FileAttributes.None,

        // Keep access failures visible so callers can report skipped directories.
        IgnoreInaccessible = false,
    };

    // Files are enumerated without an attribute filter so the excluded ones can be
    // counted rather than vanishing. AttributesToSkip drops them inside the OS
    // enumeration, which left a scan unable to distinguish "this folder is clean"
    // from "this folder holds files I never looked at". The same attributes are
    // still excluded below; they are now counted first.
    private static readonly EnumerationOptions FileWalkOptions = new()
    {
        AttributesToSkip = FileAttributes.None,
        IgnoreInaccessible = false,
    };

    private const FileAttributes ExcludedFileAttributes =
        FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint;

    /// <summary>
    /// Counts files a scan never examined, so a report can say so.
    /// </summary>
    /// <remarks>
    /// Incremented while <see cref="Parallel.ForEach"/> pulls from the traversal, so
    /// the increments are interlocked rather than assumed to be serialized.
    /// </remarks>
    internal sealed class TraversalCounters
    {
        private int _filesExcludedByAttribute;
        private int _directoriesExcludedByAttribute;

        /// <summary>Matching files skipped for being hidden, system, or reparse points.</summary>
        internal int FilesExcludedByAttribute => Volatile.Read(ref _filesExcludedByAttribute);

        /// <summary>Directories not entered because they are hidden, system, or reparse points.</summary>
        internal int DirectoriesExcludedByAttribute =>
            Volatile.Read(ref _directoriesExcludedByAttribute);

        internal void CountFileExcludedByAttribute() =>
            Interlocked.Increment(ref _filesExcludedByAttribute);

        internal void CountDirectoryExcludedByAttribute() =>
            Interlocked.Increment(ref _directoriesExcludedByAttribute);
    }

    /// <summary>
    /// Files always excluded from scans, regardless of include patterns.
    /// </summary>
    private static bool IsAlwaysExcludedFile(string fileName) =>
        fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(ConversionMetadataStore.Suffix, StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith("." + EncodingConverter.TempFileSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true for symlink, junction, or other reparse-point directories.
    /// Attribute-read failures are treated conservatively.
    /// </summary>
    internal static bool IsReparsePointDirectory(string dir)
    {
        try
        {
            return (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// Whether any directory from the file's own folder up to and including
    /// <paramref name="root"/> is a reparse point.
    /// </summary>
    /// <remarks>
    /// A scan never enters a reparse-point directory, so no planned path can legitimately
    /// contain one. If one appears later, the tree the plan described has been replaced by
    /// a different tree, and the recorded hashes cannot say so: two identical copies hash
    /// identically. Attribute-read failures are treated conservatively, matching
    /// <see cref="IsReparsePointDirectory"/>.
    /// </remarks>
    internal static bool HasReparsePointInPath(string root, string path)
    {
        string normalizedRoot =
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

        string? current = Path.GetDirectoryName(Path.GetFullPath(path));

        while (current is not null)
        {
            if (IsReparsePointDirectory(current))
                return true;

            // Stop at the root; what lies above it is not part of the plan's scope.
            if (string.Equals(
                    Path.TrimEndingDirectorySeparator(current),
                    normalizedRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            current = Path.GetDirectoryName(current);
        }

        return false;
    }

    /// <summary>
    /// Enumerates matching files while skipping excluded directories and reparse points.
    /// </summary>
    internal static IEnumerable<string> EnumerateFiles(
        string baseDirectory,
        bool includeSubdirectories,
        List<Regex> includePatterns,
        List<Regex> excludePatterns,
        IReadOnlyCollection<string>? excludedFullPaths = null,
        Action<string>? onWarning = null,
        TraversalCounters? counters = null)
    {
        var pending = new Stack<string>();
        pending.Push(baseDirectory);

        while (pending.Count > 0)
        {
            string dir = pending.Pop();

            List<FileInfo> files;

            try
            {
                // Enumerate inside the try so directory access failures can be reported.
                // FileInfo carries the attributes the enumeration already returned, so
                // the exclusion test below costs no extra call per file.
                files =
                [
                    .. new DirectoryInfo(dir).EnumerateFiles(
                        "*",
                        FileWalkOptions)
                ];
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                onWarning?.Invoke(
                    $"Skipping directory (cannot list): {dir}{Environment.NewLine}    {ex.Message}");

                continue;
            }

            foreach (FileInfo info in files)
            {
                string file = info.FullName;
                string fileName = info.Name;

                if (IsAlwaysExcludedFile(fileName))
                    continue;

                // Compare full paths because the scan root may itself be relative.
                if (excludedFullPaths is not null &&
                    excludedFullPaths.Contains(
                        Path.GetFullPath(file),
                        StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Bare masks match filenames; masks containing a separator match relative paths.
                string relativePath =
                    Path.GetRelativePath(baseDirectory, file).Replace('\\', '/');

                if (!MatchesAny(relativePath, includePatterns) ||
                    MatchesAny(relativePath, excludePatterns))
                    continue;

                // Count only matching files. Files outside the requested scope and EC's
                // own artifacts must not inflate the incomplete-coverage warning.
                if ((info.Attributes & ExcludedFileAttributes) != 0)
                {
                    counters?.CountFileExcludedByAttribute();
                    continue;
                }

                yield return file;
            }

            if (!includeSubdirectories)
                continue;

            List<DirectoryInfo> subdirectories;

            try
            {
                subdirectories =
                [
                    .. new DirectoryInfo(dir).EnumerateDirectories(
                        "*",
                        DirectoryWalkOptions)
                ];
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                onWarning?.Invoke(
                    $"Skipping directory (cannot list): {dir}{Environment.NewLine}    {ex.Message}");

                continue;
            }

            foreach (DirectoryInfo subdirectory in subdirectories)
            {
                if (ExcludedDirectoryNames.Contains(
                    subdirectory.Name))
                    continue;

                // Do not traverse excluded directories merely to count their contents.
                // Reporting the directory itself is honest about the unknown scope.
                if ((subdirectory.Attributes & ExcludedFileAttributes) != 0)
                {
                    counters?.CountDirectoryExcludedByAttribute();
                    continue;
                }

                pending.Push(subdirectory.FullName);
            }
        }
    }

    private static bool MatchesAny(
        string fileName,
        List<Regex> patterns)
    {
        foreach (Regex pattern in patterns)
        {
            if (pattern.IsMatch(fileName))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Converts wildcard masks to case-insensitive regexes.
    /// </summary>
    internal static List<Regex> CompilePatterns(
        IReadOnlyList<string>? patterns,
        bool defaultToMatchAll)
    {
        var effectivePatterns = (patterns ?? [])
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        if (effectivePatterns.Count == 0)
        {
            if (!defaultToMatchAll)
                return [];

            effectivePatterns.Add("*");
        }

        return
        [
            .. effectivePatterns.Select(mask =>
            {
                // Filename-only masks must work at any directory depth.
                bool hasSeparator = mask.Contains('/') || mask.Contains('\\');

                string body =
                    Regex.Escape(mask.Replace('\\', '/'))
                        .Replace(@"\*", ".*")
                        .Replace(@"\?", ".");

                string anchored =
                    hasSeparator
                        ? "^" + body + "$"
                        : "^(?:.*/)?" + body + "$";

                return new Regex(
                    anchored,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
            })
        ];
    }
}
