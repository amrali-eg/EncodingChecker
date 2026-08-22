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

        // Default (true) silently drops access-denied entries instead of throwing,
        // which would defeat the onWarning reporting below for the most common
        // real-world enumeration failure (an ACL-denied subdirectory).
        IgnoreInaccessible = false,
    };

    /// <summary>
    /// Always excluded, even with a broad -Include like "*" - otherwise -Backup would
    /// rescan its own ".bak" output on a later run, cascading (.bak.bak, ...) and
    /// racing concurrent backup writes.
    /// </summary>
    private static bool IsAlwaysExcludedFile(string fileName) =>
        fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith("." + EncodingConverter.TEMP_FILE_SUFFIX, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true for symlink, junction, or other reparse-point directories.
    /// Attribute-read failures are treated conservatively (unsafe to walk).
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
    /// Enumerates matching files while skipping excluded directories and reparse points.
    /// Inaccessible directories are skipped.
    /// </summary>
    internal static IEnumerable<string> EnumerateFiles(
        string baseDirectory,
        bool includeSubdirectories,
        List<Regex> includePatterns,
        List<Regex> excludePatterns,
        string? excludedFullPath = null,
        Action<string>? onWarning = null)
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
                onWarning?.Invoke(
                    string.Format(
                        "Skipping directory (cannot list): {0}{1}    {2}",
                        dir,
                        Environment.NewLine,
                        ex.Message));

                continue;
            }

            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);

                if (IsAlwaysExcludedFile(fileName))
                    continue;

                if (excludedFullPath is not null &&
                    string.Equals(file, excludedFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // No separator -> match bare filename (backward-compatible); with a
                // separator -> match path relative to baseDirectory (see CompilePatterns).
                string relativePath =
                    Path.GetRelativePath(baseDirectory, file).Replace('\\', '/');

                if (MatchesAny(relativePath, includePatterns) &&
                    !MatchesAny(relativePath, excludePatterns))
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
                onWarning?.Invoke(
                    string.Format(
                        "Skipping directory (cannot list): {0}{1}    {2}",
                        dir,
                        Environment.NewLine,
                        ex.Message));

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
    /// Converts wildcard masks to case-insensitive regexes. A mask with no "/" or "\"
    /// matches the bare filename; one with a separator matches the path relative to the
    /// scan root instead (see <see cref="EnumerateFiles"/>). "\" is normalized to "/".
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
                // A separator-free mask must match the filename at any depth, not just
                // at the scan root, so it needs an optional directory prefix.
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
