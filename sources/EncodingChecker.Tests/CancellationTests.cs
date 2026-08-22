using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// Cancellation must unwind cleanly at every stage: nothing half-written, no temp files
/// left behind, and no file modified. Genuine OS-level Ctrl+C delivery can't be unit
/// tested, so these drive the same CancellationToken the CLI and GUI hand in.
/// </summary>
public sealed class CancellationTests : IDisposable
{
    private readonly string _root;

    public CancellationTests()
    {
        _root = Directory.CreateTempSubdirectory("ec_cancel_").FullName;
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

    private static string[] TempArtifacts(string root) =>
        Directory.GetFiles(root, $"*.{EncodingConverter.TEMP_FILE_SUFFIX}");

    [Fact]
    public void Convert_PreCancelledToken_ReportsCancelled_WithoutTouchingTheFile()
    {
        // StreamConvert checks the token at the top of its read loop, so a pre-cancelled
        // token is observed deterministically before any byte is converted.
        string path = Path.Combine(_root, "convert-me.txt");
        File.WriteAllText(path, TestContent.Multilingual, new UTF8Encoding(false));
        byte[] originalBytes = File.ReadAllBytes(path);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        ConversionResult result = EncodingConverter.Convert(
            path,
            path,
            Encoding.UTF8,
            new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
            new ConversionOptions { WriteBom = true },
            progress: null,
            cts.Token);

        Assert.False(result.Success);
        Assert.Equal(ConversionErrorCode.Cancelled, result.ErrorCode);
        Assert.False(result.ReplacementCommitted);

        // The original must be byte-identical and no temp file may survive.
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.Empty(TempArtifacts(_root));
    }

    [Fact]
    public void ScanDirectory_PreCancelledToken_ThrowsAndConvertsNothing()
    {
        string path = Path.Combine(_root, "convert-me.txt");
        File.WriteAllText(path, TestContent.Multilingual, new UTF8Encoding(false));
        byte[] originalBytes = File.ReadAllBytes(path);

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Convert,
            TargetCharset = "utf-16",
            TargetWriteBom = true,
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            ScanEngine.ScanDirectory(options, _ => { }, cts.Token));

        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.Empty(TempArtifacts(_root));
    }

    [Fact]
    public void ScanDirectory_CancelledFromCallback_StopsAndLeavesRemainingFilesUntouched()
    {
        // MaxParallelism 1 makes this deterministic: the first delivered entry cancels
        // the run, so at least one of the remaining files must never be converted.
        var originals = new Dictionary<string, byte[]>();

        for (int i = 0; i < 12; i++)
        {
            string path = Path.Combine(_root, $"file{i:D2}.txt");
            File.WriteAllText(path, TestContent.Multilingual, new UTF8Encoding(false));
            originals[path] = File.ReadAllBytes(path);
        }

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Convert,
            TargetCharset = "utf-16",
            TargetWriteBom = true,
            MaxParallelism = 1,
        };

        using var cts = new CancellationTokenSource();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            ScanEngine.ScanDirectory(options, _ => cts.Cancel(), cts.Token));

        int untouched = originals.Count(
            pair => File.ReadAllBytes(pair.Key).AsSpan().SequenceEqual(pair.Value));

        // The run stopped early rather than converting everything anyway.
        Assert.True(
            untouched > 0,
            "Cancellation did not stop the scan; every file was still converted.");

        // A cancelled run must never leave conversion temp files behind.
        Assert.Empty(TempArtifacts(_root));
    }

    [Fact]
    public void ConvertFiles_WhatIf_CancelledFromCallback_StopsRatherThanScanningEverything()
    {
        // Preview (MainForm's chkPreviewChanges -> ConvertFiles' whatIf parameter) does no
        // writing, hashing, or backup, so it finishes too fast for a real GUI cancellation
        // click to ever observe mid-run - confirmed empirically via live UI automation
        // against an 80+ MB / 400-file workload. MaxParallelism 1 makes this deterministic
        // instead: the first delivered entry cancels the run, proving the same
        // ScanEngine.ConvertFiles -> RunParallel -> Parallel.ForEach cancellation path
        // Cancel-during-Backup already exercises also protects whatIf: true.
        var originals = new Dictionary<string, byte[]>();

        for (int i = 0; i < 12; i++)
        {
            string path = Path.Combine(_root, $"file{i:D2}.txt");
            File.WriteAllText(path, TestContent.Ascii, Encoding.ASCII);
            originals[path] = File.ReadAllBytes(path);
        }

        var entries = originals.Keys
            .Select(path => new ConversionReportEntry
            {
                FilePath = path,
                SourceEncoding = "us-ascii",
                TargetEncoding = "utf-8-bom",
            })
            .ToList();

        using var cts = new CancellationTokenSource();
        int deliveredCount = 0;

        Assert.ThrowsAny<OperationCanceledException>(() =>
            ScanEngine.ConvertFiles(
                entries,
                "utf-8",
                targetWriteBom: true,
                maxParallelism: 1,
                onEntry: _ =>
                {
                    Interlocked.Increment(ref deliveredCount);
                    cts.Cancel();
                },
                cts.Token,
                whatIf: true));

        // The run stopped early rather than delivering every entry anyway.
        Assert.True(
            deliveredCount < entries.Count,
            "Cancellation did not stop the run; every entry was still delivered.");

        // Preview never writes regardless of cancellation, but confirm it anyway.
        Assert.All(originals, pair => Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key)));
    }

    [Fact]
    public void ConvertFiles_PreCancelledToken_ThrowsAndConvertsNothing()
    {
        string path = Path.Combine(_root, "convert-me.txt");
        File.WriteAllText(path, TestContent.Multilingual, new UTF8Encoding(false));
        byte[] originalBytes = File.ReadAllBytes(path);

        var entries = new List<ConversionReportEntry>
        {
            new()
            {
                FilePath = path,
                SourceEncoding = "utf-8",
                TargetEncoding = "utf-8",
            },
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            ScanEngine.ConvertFiles(
                entries,
                "utf-16",
                targetWriteBom: true,
                ScanEngine.DefaultMaxParallelism,
                onEntry: _ => { },
                cts.Token));

        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.Empty(TempArtifacts(_root));
    }
}
