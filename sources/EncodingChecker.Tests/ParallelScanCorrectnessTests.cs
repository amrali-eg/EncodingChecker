using System.Collections.Concurrent;
using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// MaxParallelism=1 vs. >1 must produce the exact same result set - same files, same
/// encoding/BOM/outcome per file. Ordering is deliberately not asserted; only delivery is.
/// </summary>
public sealed class ParallelScanCorrectnessTests : IDisposable
{
    private readonly string _root;

    public ParallelScanCorrectnessTests()
    {
        _root = Directory.CreateTempSubdirectory("ec_parallel_").FullName;
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

    private int CreateMixedFileSet()
    {
        (Encoding encoding, string content)[] variants =
        [
            (Encoding.ASCII, TestContent.Ascii),
            (new UTF8Encoding(false), TestContent.Multilingual),
            (new UTF8Encoding(true), TestContent.Multilingual),
            (new UnicodeEncoding(false, false), TestContent.Multilingual),
            (new UnicodeEncoding(true, true), TestContent.Multilingual),
            (new UTF32Encoding(false, true), TestContent.Multilingual),
        ];

        int fileCount = 0;
        var random = new Random(42);

        for (int i = 0; i < 150; i++)
        {
            (Encoding encoding, string content) = variants[i % variants.Length];
            File.WriteAllText(Path.Combine(_root, $"text{i}.txt"), content, encoding);
            fileCount++;
        }

        for (int i = 0; i < 5; i++)
        {
            byte[] randomBytes = new byte[2048];
            random.NextBytes(randomBytes);
            File.WriteAllBytes(Path.Combine(_root, $"binary{i}.dat"), randomBytes);
            fileCount++;
        }

        File.WriteAllBytes(Path.Combine(_root, "empty.txt"), []);
        fileCount++;

        return fileCount;
    }

    private static Dictionary<string, ConversionReportEntry> Scan(string root, int maxParallelism)
    {
        var options = new ScanDirectoryOptions
        {
            BaseDirectory = root,
            Action = ScanAction.Detect,
            MaxParallelism = maxParallelism,
        };

        // onEntry fires concurrently across worker threads; List<T> would race here.
        var collected = new ConcurrentBag<ConversionReportEntry>();
        ScanEngine.ScanDirectory(options, collected.Add, CancellationToken.None);

        return collected.ToDictionary(e => e.FilePath, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void SequentialAndParallelScans_ProduceIdenticalResultSets()
    {
        int expectedFileCount = CreateMixedFileSet();

        Dictionary<string, ConversionReportEntry> sequential = Scan(_root, maxParallelism: 1);
        Dictionary<string, ConversionReportEntry> parallel = Scan(
            _root, maxParallelism: Math.Max(4, Environment.ProcessorCount));

        Assert.Equal(expectedFileCount, sequential.Count);
        Assert.Equal(sequential.Count, parallel.Count);
        Assert.Equal(
            sequential.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase),
            parallel.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));

        foreach ((string path, ConversionReportEntry sequentialEntry) in sequential)
        {
            ConversionReportEntry parallelEntry = parallel[path];

            Assert.Equal(sequentialEntry.SourceEncoding, parallelEntry.SourceEncoding);
            Assert.Equal(sequentialEntry.SourceHasBom, parallelEntry.SourceHasBom);
            Assert.Equal(sequentialEntry.Result, parallelEntry.Result);
        }
    }

    [Fact]
    public void ParallelConvert_AllFilesConvertedCorrectlyRegardlessOfDegreeOfParallelism()
    {
        int expectedFileCount = CreateMixedFileSet();

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Convert,
            TargetCharset = "utf-8",
            TargetWriteBom = true,
            MaxParallelism = Math.Max(4, Environment.ProcessorCount),
        };

        var collected = new ConcurrentBag<ConversionReportEntry>();
        ScanEngine.ScanDirectory(options, collected.Add, CancellationToken.None);
        List<ConversionReportEntry> entries = [.. collected];

        Assert.Equal(expectedFileCount, entries.Count);
        Assert.DoesNotContain(entries, e => e.Result == ConversionRowResult.Error);

        // Every non-binary, non-empty file must now decode as UTF-8 with a BOM.
        var target = new UTF8Encoding(true);

        foreach (ConversionReportEntry entry in entries)
        {
            if (entry.SourceEncoding == "(Unknown)")
                continue;

            byte[] bytes = File.ReadAllBytes(entry.FilePath);
            Assert.True(bytes.AsSpan(0, Math.Min(3, bytes.Length)).SequenceEqual(target.GetPreamble()));
        }
    }
}
