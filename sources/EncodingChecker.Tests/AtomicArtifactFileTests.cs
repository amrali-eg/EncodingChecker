using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// EC's own artifacts must survive a failed write the way a converted file does.
/// </summary>
/// <remarks>
/// The plan, journal, report and settings each truncated their destination and then wrote
/// into it, so an interruption left a half-written artifact where a readable one had been.
/// A truncated plan is the worst of the four: it destroys the reviewed plan a user was
/// about to apply, and re-running <c>-Plan</c> produces one that has not been reviewed.
///
/// These pin the property that matters - the previous version is still there - rather
/// than the mechanism, so the writer can change without the tests having to.
/// </remarks>
public sealed class AtomicArtifactFileTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_atomic_").FullName;

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

    private string Existing(string name, string content)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private string[] TempArtifacts() =>
        Directory.GetFiles(_root, "*." + EncodingConverter.TempFileSuffix);

    [Fact]
    public void AFailedWriteLeavesThePreviousArtifactIntact()
    {
        string path = Existing("plan.json", "{\"reviewed\":true}");
        byte[] before = File.ReadAllBytes(path);

        string? error = AtomicArtifactFile.Write(
            path, _ => throw new InvalidOperationException("serialiser gave up"));

        Assert.NotNull(error);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(TempArtifacts());
    }

    /// <summary>
    /// A failure the writer does not model must still not cost the previous version.
    /// </summary>
    [Fact]
    public void AnUnexpectedFailureAlsoLeavesThePreviousArtifactIntact()
    {
        string path = Existing("journal.json", "{\"runs\":1}");
        byte[] before = File.ReadAllBytes(path);

        Assert.Throws<FormatException>(
            () => AtomicArtifactFile.Write(path, _ => throw new FormatException()));

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(TempArtifacts());
    }

    [Fact]
    public void ASuccessfulWriteReplacesTheArtifactAndLeavesNoTemporaryFile()
    {
        string path = Existing("report.csv", "old,content\n");

        string? error = AtomicArtifactFile.WriteText(
            path, "new,content\n", new UTF8Encoding(false));

        Assert.Null(error);
        Assert.Equal("new,content\n", File.ReadAllText(path));
        Assert.Empty(TempArtifacts());
    }

    [Fact]
    public void ADestinationThatDoesNotExistYetIsCreated()
    {
        string path = Path.Combine(_root, "fresh.json");

        string? error = AtomicArtifactFile.WriteText(
            path, "{}", new UTF8Encoding(false));

        Assert.Null(error);
        Assert.Equal("{}", File.ReadAllText(path));
        Assert.Empty(TempArtifacts());
    }

    /// <summary>
    /// The report is UTF-8 with a BOM so it opens correctly in Excel. Writing into a
    /// stream rather than onto a path must not drop the preamble.
    /// </summary>
    [Fact]
    public void TheEncodingsPreambleIsStillWritten()
    {
        string path = Path.Combine(_root, "bom.csv");

        Assert.Null(AtomicArtifactFile.WriteText(
            path, "File,Encoding\n", ConversionReport.CsvFileEncoding));

        Assert.Equal(
            ConversionReport.CsvFileEncoding.GetPreamble(),
            File.ReadAllBytes(path).Take(3));
    }

    /// <summary>
    /// The temporary file carries the suffix a scan already excludes, so a leftover from a
    /// crashed run cannot become a scan candidate in the user's output directory.
    /// </summary>
    [Fact]
    public void TheTemporaryFileUsesTheSuffixScansAlreadyExclude()
    {
        string path = Path.Combine(_root, "observed.json");
        string? observed = null;

        AtomicArtifactFile.Write(path, _ =>
            observed = TempArtifacts().SingleOrDefault());

        Assert.NotNull(observed);
        Assert.True(DirectoryTraversal.HasReservedArtifactSuffix(observed));
        Assert.Empty(TempArtifacts());
    }
}
