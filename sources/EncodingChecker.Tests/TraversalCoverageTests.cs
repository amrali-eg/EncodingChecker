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
        string root,
        DirectoryTraversal.TraversalCounters counters,
        IReadOnlyList<string>? include = null,
        IReadOnlyList<string>? exclude = null)
    {
        List<Regex> includePatterns =
            DirectoryTraversal.CompilePatterns(include, defaultToMatchAll: true);
        List<Regex> excludePatterns =
            DirectoryTraversal.CompilePatterns(exclude, defaultToMatchAll: false);

        return DirectoryTraversal.EnumerateFiles(
            root,
            includeSubdirectories: true,
            includePatterns,
            excludePatterns,
            excludedFullPaths: null,
            onWarning: null,
            counters: counters).ToList();
    }

    private static (int ExitCode, string Error) RunCli(params string[] args)
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();

        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            return (Program.RunConsoleMode(args), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
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
            Assert.Equal(1, counters.FilesExcludedByAttribute);
            Assert.Equal(0, counters.DirectoriesExcludedByAttribute);
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
            Assert.Equal(1, counters.FilesExcludedByAttribute);
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
            Assert.Equal(0, counters.FilesExcludedByAttribute);
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
            Assert.Equal(2, counters.FilesExcludedByAttribute);
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
            Assert.Equal(0, counters.FilesExcludedByAttribute);
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

    [Fact]
    public void HiddenFileOutsideIncludePattern_IsNotCounted()
    {
        string root = NewFolder();

        try
        {
            string hidden = Path.Combine(root, "hidden.jpg");
            File.WriteAllText(hidden, "hidden");
            File.SetAttributes(hidden, FileAttributes.Hidden);
            File.WriteAllText(Path.Combine(root, "kept.txt"), "kept");

            var counters = new DirectoryTraversal.TraversalCounters();
            List<string> found = Enumerate(root, counters, include: ["*.txt"]);

            Assert.Single(found);
            Assert.Equal(0, counters.FilesExcludedByAttribute);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HiddenFileExcludedByPattern_IsNotCounted()
    {
        string root = NewFolder();

        try
        {
            string hidden = Path.Combine(root, "ignored.txt");
            File.WriteAllText(hidden, "hidden");
            File.SetAttributes(hidden, FileAttributes.Hidden);
            File.WriteAllText(Path.Combine(root, "kept.txt"), "kept");

            var counters = new DirectoryTraversal.TraversalCounters();
            List<string> found = Enumerate(root, counters, exclude: ["ignored.txt"]);

            Assert.Single(found);
            Assert.Equal(0, counters.FilesExcludedByAttribute);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HiddenEcArtifact_IsNotCounted()
    {
        string root = NewFolder();

        try
        {
            string artifact = Path.Combine(root, "a.txt.bak");
            File.WriteAllText(artifact, "backup");
            File.SetAttributes(artifact, FileAttributes.Hidden);

            var counters = new DirectoryTraversal.TraversalCounters();

            Assert.Empty(Enumerate(root, counters));
            Assert.Equal(0, counters.FilesExcludedByAttribute);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HiddenDirectory_IsReportedWithoutEnteringIt()
    {
        string root = NewFolder();

        try
        {
            string hiddenDirectory = Path.Combine(root, "hidden-folder");
            Directory.CreateDirectory(hiddenDirectory);
            File.WriteAllText(Path.Combine(hiddenDirectory, "not-counted.txt"), "hidden");
            File.SetAttributes(hiddenDirectory, FileAttributes.Hidden);
            File.WriteAllText(Path.Combine(root, "kept.txt"), "kept");

            var counters = new DirectoryTraversal.TraversalCounters();
            List<string> found = Enumerate(root, counters);

            Assert.Single(found);
            Assert.Equal(0, counters.FilesExcludedByAttribute);
            Assert.Equal(1, counters.DirectoriesExcludedByAttribute);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GuiCoverageText_ReportsBothKindsOfIncompleteCoverage()
    {
        string root = NewFolder();

        try
        {
            string hiddenFile = Path.Combine(root, "hidden.txt");
            File.WriteAllText(hiddenFile, "hidden");
            File.SetAttributes(hiddenFile, FileAttributes.Hidden);

            string hiddenDirectory = Path.Combine(root, "hidden-folder");
            Directory.CreateDirectory(hiddenDirectory);
            File.SetAttributes(hiddenDirectory, FileAttributes.Hidden);

            var counters = new DirectoryTraversal.TraversalCounters();
            _ = Enumerate(root, counters);

            string text = MainForm.FormatCoverage(counters);

            Assert.Contains("1 matching file(s) not examined", text);
            Assert.Contains("1 folder(s) not entered", text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CliReportsBothKindsOfIncompleteCoverageOnStandardError()
    {
        string root = NewFolder();

        try
        {
            File.WriteAllText(Path.Combine(root, "kept.txt"), "kept");

            string hiddenFile = Path.Combine(root, "hidden.txt");
            File.WriteAllText(hiddenFile, "hidden");
            File.SetAttributes(hiddenFile, FileAttributes.Hidden);

            string hiddenDirectory = Path.Combine(root, "hidden-folder");
            Directory.CreateDirectory(hiddenDirectory);
            File.SetAttributes(hiddenDirectory, FileAttributes.Hidden);

            (int exitCode, string error) = RunCli(
                "-BasePath", root, "-Include", "*.txt", "-DetectOnly", "-Quiet");

            Assert.Equal(0, exitCode);
            Assert.Contains("1 matching file(s) not examined", error);
            Assert.Contains("1 folder(s) not entered", error);
            Assert.Contains("contents were not counted", error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
