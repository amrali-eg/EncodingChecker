using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// Files EC skips as its own must be counted when the caller's patterns selected them.
/// </summary>
/// <remarks>
/// <c>.bak</c>, <c>.ecmeta.json</c> and <c>.unicodechecker.tmp</c> were dropped before
/// the coverage counters ran, so they appeared in no row, no count and no warning. The
/// sharp end was <c>-Include "*.bak"</c>: the caller named those files explicitly and
/// got an empty report and exit 0 - a clean answer to a question EC had not looked at.
/// <para>
/// The test is the exclusion's placement, not its existence. Skipping EC's own backups
/// and sidecars is right; doing it silently is not. The counters were added in v3.9.2
/// precisely so a clean result could not stand in for complete coverage, and this
/// group sat outside them.
/// </para>
/// </remarks>
public sealed class ArtifactExclusionCoverageTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_artifactcoverage_").FullName;

    public ArtifactExclusionCoverageTests()
    {
        Write("a.txt");
        Write("b.bak");
        Write("c.txt" + ConversionMetadataStore.Suffix);
        Write("d.txt." + EncodingConverter.TempFileSuffix);
    }

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

    private void Write(string name) =>
        File.WriteAllText(
            Path.Combine(_root, name), "plain ascii", new UTF8Encoding(false));

    private (List<string> Files, DirectoryTraversal.TraversalCounters Counters) Enumerate(
        params string[] includePatterns)
    {
        var counters = new DirectoryTraversal.TraversalCounters();

        List<string> files =
        [
            .. DirectoryTraversal.EnumerateFiles(
                _root,
                includeSubdirectories: true,
                DirectoryTraversal.CompilePatterns(includePatterns, defaultToMatchAll: true),
                DirectoryTraversal.CompilePatterns(null, defaultToMatchAll: false),
                excludedFullPaths: null,
                onWarning: null,
                counters)
        ];

        return (files, counters);
    }

    [Fact]
    public void AskingForBackupsReportsThemSkippedRatherThanReturningNothing()
    {
        (List<string> files, DirectoryTraversal.TraversalCounters counters) =
            Enumerate("*.bak");

        // Still excluded - that part is deliberate and unchanged.
        Assert.Empty(files);

        // But no longer silently. An empty report with nothing else said is
        // indistinguishable from "your files are fine".
        Assert.Equal(1, counters.FilesExcludedAsEcArtifact);
    }

    [Fact]
    public void AllThreeArtifactKindsAreCounted()
    {
        // Named through the shared constants, so renaming a suffix cannot quietly
        // drop one of them out of the count.
        (List<string> files, DirectoryTraversal.TraversalCounters counters) = Enumerate();

        Assert.Equal(["a.txt"], files.Select(Path.GetFileName));
        Assert.Equal(3, counters.FilesExcludedAsEcArtifact);
    }

    [Fact]
    public void FilesTheCallerDidNotSelectAreNotCounted()
    {
        // The control, and the reason the check sits after the pattern match rather
        // than before it: a count that fires for files outside the requested scope is
        // noise, and noise in a coverage warning is how the warning stops being read.
        (List<string> files, DirectoryTraversal.TraversalCounters counters) =
            Enumerate("*.txt");

        Assert.Equal(["a.txt"], files.Select(Path.GetFileName));
        Assert.Equal(0, counters.FilesExcludedAsEcArtifact);
    }

    [Fact]
    public void OrdinaryFilesAreStillReturnedAndNeverCounted()
    {
        // A counter that incremented for everything would satisfy the first two tests
        // while making the number meaningless.
        (List<string> files, DirectoryTraversal.TraversalCounters counters) =
            Enumerate("a.txt");

        Assert.Equal(["a.txt"], files.Select(Path.GetFileName));
        Assert.Equal(0, counters.FilesExcludedAsEcArtifact);
        Assert.Equal(0, counters.FilesExcludedByAttribute);
    }

    [Fact]
    public void TheGuiCoverageLineMentionsThem()
    {
        // The GUI reports coverage in the status bar rather than on stderr, so the
        // count has to reach that surface too or the window shows a clean scan.
        var counters = new DirectoryTraversal.TraversalCounters();
        counters.CountFileExcludedAsEcArtifact();

        string coverage = MainForm.FormatCoverage(counters);

        Assert.Contains("1", coverage, StringComparison.Ordinal);
        Assert.Contains("not examined", coverage, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingSkippedProducesNoCoverageText()
    {
        Assert.Equal(
            string.Empty,
            MainForm.FormatCoverage(new DirectoryTraversal.TraversalCounters()));
    }
}
