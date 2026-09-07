using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// A failed artifact write must preserve the previous complete file.
/// </summary>
/// <remarks>
/// An interrupted in-place write once left plans, journals, reports, and settings
/// incomplete. Losing a reviewed plan is especially unsafe because regenerating it creates
/// a different, unreviewed plan. These tests pin preservation, not the writer's mechanism.
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

    /// <summary>An unexpected write failure must still preserve the previous version.</summary>
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

    /// <summary>Writing a report through a stream must preserve its UTF-8 BOM for Excel.</summary>
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

    /// <summary>A leftover temporary artifact must use the suffix excluded from scans.</summary>
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
