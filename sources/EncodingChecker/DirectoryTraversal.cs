using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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
        // Never traverse symlinks, junctions, or other reparse points.
        AttributesToSkip = FileAttributes.ReparsePoint,

        // Keep access failures visible so callers can report skipped directories.
        IgnoreInaccessible = false,
    };

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
    /// Enumerates matching files while skipping excluded directories and reparse points.
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
                // Enumerate inside the try so directory access failures can be reported.
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
                    $"Skipping directory (cannot list): {dir}{Environment.NewLine}    {ex.Message}");

                continue;
            }

            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);

                if (IsAlwaysExcludedFile(fileName))
                    continue;

                // Compare full paths because the scan root may itself be relative.
                if (excludedFullPath is not null &&
                    string.Equals(
                        Path.GetFullPath(file),
                        excludedFullPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Bare masks match filenames; masks containing a separator match relative paths.
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
                    $"Skipping directory (cannot list): {dir}{Environment.NewLine}    {ex.Message}");

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
