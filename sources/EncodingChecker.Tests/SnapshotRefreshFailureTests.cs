using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// A file that cannot be read when the plan's source snapshot is taken becomes one explained
/// error row, and the files beside it are still snapshotted.
/// </summary>
/// <remarks>
/// The snapshot binds each entry to the exact bytes the plan was prepared from. A file that is
/// locked by another process, or gone by then, cannot be bound, so it is refused as an error
/// with no recorded hash rather than planned from a read that did not happen.
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
        catch (IOException)
        {
            // Best-effort cleanup.
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

    [Theory]
    [InlineData("locked")]
    [InlineData("missing")]
    public void AFileThatCannotBeReadIsAnErrorRowAndItsNeighbourIsStillSnapshotted(string problem)
    {
        string good = WriteSource("good.txt");
        string bad = WriteSource("bad.txt");
        byte[] badBytes = File.ReadAllBytes(bad);

        ConversionReportEntry goodEntry = Entry(good);
        ConversionReportEntry badEntry = Entry(bad);

        FileStream? held = problem == "locked"
            ? new FileStream(bad, FileMode.Open, FileAccess.Read, FileShare.None)
            : null;

        if (problem == "missing")
        {
            File.Delete(bad);
        }

        using (held)
        {
            ScanEngine.RefreshSourceSnapshots(
                [badEntry, goodEntry], maxParallelism: 1, CancellationToken.None);
        }

        Assert.Equal(ConversionRowResult.Error, badEntry.Result);
        Assert.Equal(PlannedAction.Refuse, badEntry.Action);
        Assert.Equal(SourceInterpretation.NotApplicable, badEntry.SourceInterpretation);
        Assert.Equal(ConversionReasonCodes.SourceSnapshotFailed, badEntry.ReasonCode);
        Assert.False(badEntry.ReplacementCommitted);
        Assert.StartsWith(
            "The source could not be read consistently for planning: ",
            badEntry.Diagnostic,
            StringComparison.Ordinal);

        // No hash was recorded, so nothing can later mistake this row for a verified source.
        Assert.Null(badEntry.ExpectedSourceSha256);

        // The failure is one row: the file beside it was bound to its own bytes.
        Assert.Equal(ConversionRowResult.Unchanged, goodEntry.Result);
        Assert.Equal(ConversionMetadataStore.ComputeSha256(good), goodEntry.ExpectedSourceSha256);

        // Snapshotting only reads; a locked file's bytes are untouched.
        if (problem == "locked")
        {
            Assert.Equal(badBytes, File.ReadAllBytes(bad));
        }
    }
}
