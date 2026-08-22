using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace EncodingChecker;

/// <summary>
/// Directory-tree walking and file-pattern matching shared by the scan engine.
/// </summary>
internal static class DirectoryTraversal
{
    // Never descend into these directories.
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
        // Do not traverse symlinks or junctions.
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <summary>
    /// Always excluded, regardless of -Include/-Exclude: the tool's own backup and
    /// temporary-file artifacts. Without this, a broad include pattern (e.g. "*")
    /// combined with -Backup re-scans previous runs' ".bak" files as ordinary input,
    /// which cascades (.bak, .bak.bak, ...) and can race against another file's
    /// concurrent backup write to the same path.
    /// </summary>
    private static bool IsAlwaysExcludedFile(string fileName) =>
        fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith("." + EncodingConverter.TEMP_FILE_SUFFIX, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Enumerates matching files while skipping excluded directories and reparse points.
    /// Inaccessible directories are skipped.
    /// </summary>
    internal static IEnumerable<string> EnumerateFiles(
        string baseDirectory,
        bool includeSubdirectories,
        List<Regex> includePatterns,
        List<Regex> excludePatterns)
    {
        var pending = new Stack<string>();
        pending.Push(baseDirectory);

        while (pending.Count > 0)
        {
            string dir = pending.Pop();

            List<string> files;

            try
            {
                // Force enumeration here so access errors stay inside the try block.
                files =
                [
                    .. Directory.EnumerateFiles(
                        dir,
                        "*",
                        DirectoryWalkOptions)
                ];
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);

                if (IsAlwaysExcludedFile(fileName))
                    continue;

                if (MatchesAny(fileName, includePatterns) &&
                    !MatchesAny(fileName, excludePatterns))
                {
                    yield return file;
                }
            }

            if (!includeSubdirectories)
                continue;

            List<string> subdirectories;

            try
            {
                subdirectories =
                [
                    .. Directory.EnumerateDirectories(
                        dir,
                        "*",
                        DirectoryWalkOptions)
                ];
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string subdirectory in subdirectories)
            {
                if (!ExcludedDirectoryNames.Contains(
                    Path.GetFileName(subdirectory)))
                {
                    pending.Push(subdirectory);
                }
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
                new Regex(
                    "^" +
                    Regex.Escape(mask)
                        .Replace(@"\*", ".*")
                        .Replace(@"\?", ".") +
                    "$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        ];
    }
}
