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

    // Enumerate all files so excluded attributes remain visible in coverage counts.
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
    /// <remarks>Parallel traversal requires interlocked counters.</remarks>
    internal sealed class TraversalCounters
    {
        private int _filesExcludedByAttribute;
        private int _directoriesExcludedByAttribute;
        private int _directoriesExcludedByName;
        private int _directoriesUnreadable;
        private int _filesExcludedAsEcArtifact;

        /// <summary>Matching files skipped for being hidden, system, or reparse points.</summary>
        internal int FilesExcludedByAttribute => Volatile.Read(ref _filesExcludedByAttribute);

        /// <summary>Directories not entered because they are hidden, system, or reparse points.</summary>
        internal int DirectoriesExcludedByAttribute =>
            Volatile.Read(ref _directoriesExcludedByAttribute);

        /// <summary>
        /// Directories not entered because their name is a build or metadata convention.
        /// </summary>
        /// <remarks>
        /// Counted for the same reason the attribute exclusions are. Skipping these is
        /// deliberate and documented, but leaving them out of the coverage report let a
        /// clean result stand in for complete coverage - the one thing this report exists
        /// to prevent - and "build" and "target" are ordinary content directory names
        /// outside the conventions they were chosen for.
        /// </remarks>
        internal int DirectoriesExcludedByName =>
            Volatile.Read(ref _directoriesExcludedByName);

        /// <summary>Directories EC tried to list and could not.</summary>
        /// <remarks>
        /// Distinct from the two exclusion counters above, which record directories EC
        /// chose not to enter. This one records a failure, and it is the one that used to
        /// be invisible: an unreadable file becomes a row and drives exit code 3, while an
        /// unreadable directory produced a warning on stderr and nothing else - no row, no
        /// count, exit 0. A GUI scan reported nothing at all, because the window passes no
        /// warning callback. A run that examined none of the tree could report success.
        /// </remarks>
        internal int DirectoriesUnreadable =>
            Volatile.Read(ref _directoriesUnreadable);

        /// <summary>
        /// Matching files skipped for being EC's own backups, sidecars, or temporaries.
        /// </summary>
        internal int FilesExcludedAsEcArtifact => Volatile.Read(ref _filesExcludedAsEcArtifact);

        internal void CountFileExcludedByAttribute() =>
            Interlocked.Increment(ref _filesExcludedByAttribute);

        internal void CountFileExcludedAsEcArtifact() =>
            Interlocked.Increment(ref _filesExcludedAsEcArtifact);

        internal void CountDirectoryExcludedByAttribute() =>
            Interlocked.Increment(ref _directoriesExcludedByAttribute);

        internal void CountDirectoryExcludedByName() =>
            Interlocked.Increment(ref _directoriesExcludedByName);

        internal void CountDirectoryUnreadable() =>
            Interlocked.Increment(ref _directoriesUnreadable);
    }

    /// <summary>
    /// Files always excluded from scans, regardless of include patterns.
    /// </summary>
    internal static bool HasReservedArtifactSuffix(string path) =>
        path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(ConversionMetadataStore.Suffix, StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith("." + EncodingConverter.TempFileSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true for symlink, junction, or other reparse-point directories.
    /// Attribute-read failures are treated conservatively.
    /// </summary>
    internal static bool IsReparsePointDirectory(string dir)
        => IsReparsePointOrUnreadable(dir);

    private static bool IsReparsePointOrUnreadable(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Whether the planned path can still be trusted to reach its recorded root.</summary>
    internal static bool HasReparsePointInPath(string root, string path)
    {
        string normalizedRoot =
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

        string? current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        while (current is not null)
        {
            if (IsReparsePointOrUnreadable(current))
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

        // The path was not beneath the recorded root.
        return true;
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
                counters?.CountDirectoryUnreadable();

                onWarning?.Invoke(
                    $"Skipping directory (cannot list): {dir}{Environment.NewLine}    {ex.Message}");

                continue;
            }

            foreach (FileInfo info in files)
            {
                string file = info.FullName;
                string fileName = info.Name;

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

                // Count only excluded artifacts that the caller's patterns selected.
                if (HasReservedArtifactSuffix(fileName))
                {
                    counters?.CountFileExcludedAsEcArtifact();
                    continue;
                }

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
                counters?.CountDirectoryUnreadable();

                onWarning?.Invoke(
                    $"Skipping directory (cannot list): {dir}{Environment.NewLine}    {ex.Message}");

                continue;
            }

            foreach (DirectoryInfo subdirectory in subdirectories)
            {
                if (ExcludedDirectoryNames.Contains(subdirectory.Name))
                {
                    counters?.CountDirectoryExcludedByName();
                    continue;
                }

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
