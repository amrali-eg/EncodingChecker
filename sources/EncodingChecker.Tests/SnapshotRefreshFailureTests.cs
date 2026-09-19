using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// A file that cannot be read when the plan's source snapshot is taken becomes one explained
/// error row, and the files beside it are still snapshotted.
/// </summary>
/// <remarks>
/// The snapshot binds each entry to the exact bytes the plan was prepared from. A file that is
/// locked by another process, gone by then, or chosen with a source encoding that is not
/// available cannot be bound, so it is refused as an error rather than planned from a read that
/// did not happen.
/// </remarks>
public sealed class SnapshotRefreshFailureTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_snapshot_").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory must not fail the test run.
        }
    }

    private string WriteSource(string name)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, "hello world", new UTF8Encoding(true));

        return path;
    }

    private static ConversionReportEntry Entry(string path) => new()
    {
        FilePath = path,
        SourceEncoding = "utf-8",
        TargetEncoding = "utf-8",
    };

    // The failing entry is listed first and the run is sequential, so its neighbour is reached
    // only if the failure stayed one row.
    private static void Refresh(ConversionReportEntry bad, ConversionReportEntry good)
    {
        ScanEngine.RefreshSourceSnapshots(
            [bad, good], maxParallelism: 1, CancellationToken.None);
    }

    private static void AssertRefusedAsUnreadable(ConversionReportEntry bad)
    {
        Assert.Equal(ConversionRowResult.Error, bad.Result);
        Assert.Equal(PlannedAction.Refuse, bad.Action);
        Assert.Equal(SourceInterpretation.NotApplicable, bad.SourceInterpretation);
        Assert.Equal(ConversionReasonCodes.SourceSnapshotFailed, bad.ReasonCode);
        Assert.False(bad.ReplacementCommitted);
        Assert.StartsWith(
            "The source could not be read consistently for planning: ",
            bad.Diagnostic,
            StringComparison.Ordinal);

        // This attempt captured nothing, so it recorded no hash.
        Assert.Null(bad.ExpectedSourceSha256);
    }

    private static void AssertSnapshotted(ConversionReportEntry good, string path)
    {
        Assert.Equal(ConversionRowResult.Unchanged, good.Result);
        Assert.Equal(ConversionMetadataStore.ComputeSha256(path), good.ExpectedSourceSha256);
    }

    [Fact]
    public void AFileHeldByAnotherProcessIsAnErrorRowAndItsNeighbourIsStillSnapshotted()
    {
        string goodPath = WriteSource("good.txt");
        string badPath = WriteSource("bad.txt");
        ConversionReportEntry good = Entry(goodPath);
        ConversionReportEntry bad = Entry(badPath);

        using (new FileStream(badPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Refresh(bad, good);
        }

        AssertRefusedAsUnreadable(bad);
        AssertSnapshotted(good, goodPath);
    }

    [Fact]
    public void AFileThatVanishedIsAnErrorRowAndItsNeighbourIsStillSnapshotted()
    {
        string goodPath = WriteSource("good.txt");
        string badPath = WriteSource("bad.txt");
        ConversionReportEntry good = Entry(goodPath);
        ConversionReportEntry bad = Entry(badPath);

        File.Delete(badPath);
        Refresh(bad, good);

        AssertRefusedAsUnreadable(bad);
        AssertSnapshotted(good, goodPath);
    }

    [Fact]
    public void AnExplicitSourceEncodingThatIsNotAvailableIsAnErrorRowNamingIt()
    {
        string goodPath = WriteSource("good.txt");
        ConversionReportEntry good = Entry(goodPath);
        ConversionReportEntry bad = Entry(WriteSource("bad.txt"));

        // A source the user chose is never silently replaced by what detection would have said.
        bad.SourceEncodingWasSpecified = true;
        bad.CurrentCharsetLabel = "not-a-real-charset";

        Refresh(bad, good);

        AssertRefusedAsUnreadable(bad);
        Assert.Contains("'not-a-real-charset' is not available", bad.Diagnostic);
        AssertSnapshotted(good, goodPath);
    }
}
