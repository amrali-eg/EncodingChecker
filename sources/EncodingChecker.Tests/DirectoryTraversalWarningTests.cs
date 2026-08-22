using System.Text.RegularExpressions;

namespace EncodingChecker.Tests;

/// <summary>
/// A directory-listing failure (access denied, vanished mid-scan) must be reported through
/// the onWarning callback instead of being silently swallowed, without aborting the scan.
/// </summary>
public sealed class DirectoryTraversalWarningTests
{
    [Fact]
    public void UnlistableBaseDirectory_ReportsWarning_ReturnsEmptyRatherThanThrowing()
    {
        string ghostRoot = Path.Combine(
            Path.GetTempPath(),
            "ec-tests-ghost-" + Guid.NewGuid().ToString("N"));

        List<Regex> matchAll = DirectoryTraversal.CompilePatterns(["*"], defaultToMatchAll: true);

        var warnings = new List<string>();

        // Never created, so listing it fails with DirectoryNotFoundException.
        var found = DirectoryTraversal.EnumerateFiles(
            ghostRoot,
            includeSubdirectories: true,
            matchAll,
            [],
            excludedFullPath: null,
            onWarning: warnings.Add).ToList();

        Assert.Empty(found);

        string warning = Assert.Single(warnings);
        Assert.Contains("cannot list", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ghostRoot, warning);
    }

    [Fact]
    public void AccessibleBaseDirectory_ReportsNoWarnings()
    {
        string root = Directory.CreateTempSubdirectory("ec_traversal_warning_").FullName;

        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "x");

            List<Regex> matchAll = DirectoryTraversal.CompilePatterns(["*"], defaultToMatchAll: true);

            var warnings = new List<string>();

            var found = DirectoryTraversal.EnumerateFiles(
                root,
                includeSubdirectories: true,
                matchAll,
                [],
                excludedFullPath: null,
                onWarning: warnings.Add).ToList();

            Assert.Single(found);
            Assert.Empty(warnings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
