using System.Text.RegularExpressions;

namespace EncodingChecker.Tests;

/// <summary>
/// Files excluded by attribute must be counted, not silently dropped.
/// </summary>
/// <remarks>
/// A hidden file used to leave no trace at all: no row, no count, no note. A folder
/// containing one validated clean and exited 0, so a CI check reported success over a
/// file it never opened. The exclusion itself is deliberate and unchanged; what the
/// count restores is the difference between "this folder is clean" and "this folder
/// holds files I did not look at".
/// </remarks>
public sealed class TraversalCoverageTests
{
    private static string NewFolder()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "ec-coverage-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);
        return root;
    }

    private static List<string> Enumerate(
        string root, DirectoryTraversal.TraversalCounters counters)
    {
        List<Regex> matchAll =
            DirectoryTraversal.CompilePatterns(["*"], defaultToMatchAll: true);

        return DirectoryTraversal.EnumerateFiles(
            root,
            includeSubdirectories: true,
            matchAll,
            [],
            excludedFullPaths: null,
            onWarning: null,
            counters: counters).ToList();
    }

    [Fact]
    public void HiddenFile_IsExcludedFromResults_ButCounted()
    {
        string root = NewFolder();

        try
        {
            string visible = Path.Combine(root, "visible.txt");
            string hidden = Path.Combine(root, "hidden.txt");

            File.WriteAllText(visible, "visible");
            File.WriteAllText(hidden, "hidden");
            File.SetAttributes(hidden, FileAttributes.Hidden);

            var counters = new DirectoryTraversal.TraversalCounters();
            List<string> found = Enumerate(root, counters);

            // Behaviour is unchanged: the hidden file is still not scanned.
            Assert.Equal([visible], found);

            // What changed: the caller can now say so.
            Assert.Equal(1, counters.ExcludedByAttribute);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SystemFile_IsCounted()
    {
        string root = NewFolder();

        try
        {
            string systemFile = Path.Combine(root, "system.txt");
            File.WriteAllText(systemFile, "system");
            File.SetAttributes(systemFile, FileAttributes.System);

            var counters = new DirectoryTraversal.TraversalCounters();

            Assert.Empty(Enumerate(root, counters));
            Assert.Equal(1, counters.ExcludedByAttribute);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FolderWithNothingExcluded_CountsZero()
    {
        // A count that is never zero would be as useless as no count at all.
        string root = NewFolder();

        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "a");
            File.WriteAllText(Path.Combine(root, "b.txt"), "b");

            var counters = new DirectoryTraversal.TraversalCounters();

            Assert.Equal(2, Enumerate(root, counters).Count);
            Assert.Equal(0, counters.ExcludedByAttribute);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HiddenFilesInSubdirectories_AreCountedToo()
    {
        string root = NewFolder();

        try
        {
            string sub = Path.Combine(root, "sub");
            Directory.CreateDirectory(sub);

            foreach (string name in new[] { "one.txt", "two.txt" })
            {
                string path = Path.Combine(sub, name);
                File.WriteAllText(path, name);
                File.SetAttributes(path, FileAttributes.Hidden);
            }

            File.WriteAllText(Path.Combine(root, "kept.txt"), "kept");

            var counters = new DirectoryTraversal.TraversalCounters();

            Assert.Single(Enumerate(root, counters));
            Assert.Equal(2, counters.ExcludedByAttribute);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExcludedArtifacts_AreNotCountedAsUnexamined()
    {
        // EC's own .bak and .ecmeta.json files are excluded by name, not by attribute.
        // Counting them would inflate the number and train the reader to ignore it.
        string root = NewFolder();

        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "a");
            File.WriteAllText(Path.Combine(root, "a.txt.bak"), "backup");
            File.WriteAllText(
                Path.Combine(root, "a.txt" + ConversionMetadataStore.Suffix), "{}");

            var counters = new DirectoryTraversal.TraversalCounters();

            Assert.Single(Enumerate(root, counters));
            Assert.Equal(0, counters.ExcludedByAttribute);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CountersAreOptional()
    {
        // The scan must behave identically when the caller does not ask for coverage.
        string root = NewFolder();

        try
        {
            string hidden = Path.Combine(root, "hidden.txt");
            File.WriteAllText(hidden, "hidden");
            File.SetAttributes(hidden, FileAttributes.Hidden);
            File.WriteAllText(Path.Combine(root, "kept.txt"), "kept");

            List<Regex> matchAll =
                DirectoryTraversal.CompilePatterns(["*"], defaultToMatchAll: true);

            List<string> found = DirectoryTraversal.EnumerateFiles(
                root,
                includeSubdirectories: true,
                matchAll,
                []).ToList();

            Assert.Single(found);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
