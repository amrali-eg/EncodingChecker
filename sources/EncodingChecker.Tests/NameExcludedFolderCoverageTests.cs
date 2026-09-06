using System.Text;
using System.Threading;

namespace EncodingChecker.Tests;

/// <summary>
/// Folders skipped by name must appear in the coverage report.
/// </summary>
/// <remarks>
/// Eleven directory names — <c>.git .svn .hg .vs .idea bin obj node_modules packages dist
/// build target</c> — are skipped deliberately, and that is documented. Attribute-excluded
/// folders were counted; these were not, so a scan of a tree whose only content sat under
/// <c>build/</c> reported one file, zero exclusions and no warning. In a tool that reports
/// coverage precisely so a clean result cannot stand in for complete coverage, this was the
/// one hole the coverage report could not see.
/// <para>
/// What is scanned is unchanged: these folders are still skipped, and no pattern reaches
/// into one. Only the accounting is fixed.
/// </para>
/// </remarks>
public sealed class NameExcludedFolderCoverageTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec-namedirs-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private void Populate(params string[] folders)
    {
        foreach (string folder in folders)
        {
            Directory.CreateDirectory(Path.Combine(_root, folder));
            File.WriteAllText(
                Path.Combine(_root, folder, "a.txt"), "content", new UTF8Encoding(false));
        }
    }

    private (List<ConversionReportEntry> Entries, DirectoryTraversal.TraversalCounters Counters)
        Scan()
    {
        var counters = new DirectoryTraversal.TraversalCounters();
        var entries = new EntrySink();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                Action = ScanAction.Detect,
                Counters = counters,
                MaxParallelism = 1,
            },
            entries.Add,
            CancellationToken.None);

        return (entries.ToList(), counters);
    }

    [Fact]
    public void EverySkippedFolderIsCounted()
    {
        Populate("build", "target", "dist", "packages", "src");

        var (entries, counters) = Scan();

        // Unchanged: only src is walked.
        Assert.Equal("src", Path.GetFileName(Path.GetDirectoryName(Assert.Single(entries).FilePath)));

        // Fixed: the other four are accounted for rather than invisible.
        Assert.Equal(4, counters.DirectoriesExcludedByName);
        Assert.Equal(0, counters.DirectoriesExcludedByAttribute);
    }

    [Theory]
    [InlineData(".git")]
    [InlineData(".svn")]
    [InlineData(".hg")]
    [InlineData(".vs")]
    [InlineData(".idea")]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("node_modules")]
    [InlineData("packages")]
    [InlineData("dist")]
    [InlineData("build")]
    [InlineData("target")]
    public void EachDocumentedNameIsCounted(string folder)
    {
        // The list in docs/CLI.md and the built-in help names all twelve; none may be
        // skipped without saying so.
        Populate(folder);

        Assert.Equal(1, Scan().Counters.DirectoriesExcludedByName);
    }

    [Fact]
    public void ANameExclusionIsReportedSeparatelyFromAnAttributeOne()
    {
        // Two different reasons a folder was not entered, and the report says which.
        Populate("build", "hidden");
        File.SetAttributes(
            Path.Combine(_root, "hidden"),
            File.GetAttributes(Path.Combine(_root, "hidden")) | FileAttributes.Hidden);

        var (_, counters) = Scan();

        Assert.Equal(1, counters.DirectoriesExcludedByName);
        Assert.Equal(1, counters.DirectoriesExcludedByAttribute);

        string coverage = MainForm.FormatCoverage(counters);

        Assert.Contains("1 folder(s) not entered", coverage);
        Assert.Contains("1 build/metadata folder(s) not entered", coverage);
    }

    [Fact]
    public void ACleanTreeStillReportsNothing()
    {
        // The counters must stay silent when there is genuinely nothing to disclose,
        // or the report becomes noise people learn to skip.
        Populate("src");

        var (_, counters) = Scan();

        Assert.Equal(0, counters.DirectoriesExcludedByName);
        Assert.Equal(string.Empty, MainForm.FormatCoverage(counters));
    }
}
