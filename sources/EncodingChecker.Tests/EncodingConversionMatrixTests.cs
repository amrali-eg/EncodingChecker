using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// End-to-end conversion matrix via <see cref="ScanEngine.ScanDirectory"/>: every
/// source/target encoding and BOM combination, verifying exact byte-level round-trips.
/// </summary>
public sealed class EncodingConversionMatrixTests : IDisposable
{
    private readonly string _root;

    public EncodingConversionMatrixTests()
    {
        _root = Directory.CreateTempSubdirectory("ec_matrix_").FullName;
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

    public static IEnumerable<object[]> ValidConversionCases()
    {
        // (sourceEncoding, sourceHasBom, targetLabel, targetHasBom, content)
        // targetLabel must carry its own "-bom" suffix; targetHasBom alone doesn't imply it.
        yield return [Encoding.ASCII, false, "us-ascii", false, TestContent.Ascii];

        yield return [new UTF8Encoding(false), false, "utf-8-bom", true, TestContent.Ascii];
        yield return [new UTF8Encoding(true), true, "us-ascii", false, TestContent.Ascii];

        yield return [new UTF8Encoding(false), false, "utf-8-bom", true, TestContent.Multilingual];
        yield return [new UTF8Encoding(true), true, "utf-8", false, TestContent.Multilingual];

        yield return [new UnicodeEncoding(false, false), false, "utf-8", false, TestContent.Multilingual];
        yield return [new UnicodeEncoding(false, true), true, "utf-8-bom", true, TestContent.Multilingual];
        yield return [new UnicodeEncoding(true, false), false, "utf-8", false, TestContent.Multilingual];
        yield return [new UnicodeEncoding(true, true), true, "utf-8-bom", true, TestContent.Multilingual];

        yield return [new UTF32Encoding(false, false), false, "utf-8", false, TestContent.Multilingual];
        yield return [new UTF32Encoding(false, true), true, "utf-8-bom", true, TestContent.Multilingual];
        yield return [new UTF32Encoding(true, false), false, "utf-8", false, TestContent.Multilingual];
        yield return [new UTF32Encoding(true, true), true, "utf-8-bom", true, TestContent.Multilingual];

        yield return [new UTF8Encoding(false), false, "utf-32-bom", true, TestContent.Multilingual];
        yield return [new UTF8Encoding(false), false, "utf-32BE-bom", true, TestContent.Multilingual];
    }

    [Theory]
    [MemberData(nameof(ValidConversionCases))]
    public void Convert_ProducesCorrectBomAndRoundTripsContentExactly(
        Encoding sourceEncoding,
        bool sourceHasBom,
        string targetLabel,
        bool targetHasBom,
        string content)
    {
        string path = Path.Combine(_root, Guid.NewGuid() + ".txt");
        File.WriteAllText(path, content, sourceEncoding);

        ScanEngine.ParseCharsetLabel(targetLabel, out string targetBaseCharset, out bool parsedBom);
        Assert.Equal(targetHasBom, parsedBom);

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Convert,
            TargetCharset = targetBaseCharset,
            TargetWriteBom = targetHasBom,
        };

        var entries = new List<ConversionReportEntry>();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

        ConversionReportEntry entry = Assert.Single(entries);
        Assert.Equal(sourceHasBom, entry.SourceHasBom);
        Assert.NotEqual(ConversionRowResult.Error, entry.Result);

        byte[] resultBytes = File.ReadAllBytes(path);
        Encoding targetEncoding = Encoding.GetEncoding(targetBaseCharset);
        byte[] preamble = targetEncoding.GetPreamble();

        bool actualBom =
            preamble.Length > 0 &&
            resultBytes.Length >= preamble.Length &&
            resultBytes.AsSpan(0, preamble.Length).SequenceEqual(preamble);

        Assert.Equal(targetHasBom, actualBom);

        string decoded = targetEncoding.GetString(
            resultBytes,
            actualBom ? preamble.Length : 0,
            resultBytes.Length - (actualBom ? preamble.Length : 0));

        Assert.Equal(content, decoded);
    }

    [Fact]
    public void Convert_AlreadyMatchingTarget_LeavesFileUnchangedOnDisk()
    {
        string path = Path.Combine(_root, "already-utf8.txt");
        File.WriteAllText(path, TestContent.Multilingual, new UTF8Encoding(false));
        byte[] originalBytes = File.ReadAllBytes(path);
        DateTime originalWriteTime = File.GetLastWriteTimeUtc(path);

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Convert,
            TargetCharset = "utf-8",
            TargetWriteBom = false,
        };

        var entries = new List<ConversionReportEntry>();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

        ConversionReportEntry entry = Assert.Single(entries);
        Assert.Equal(ConversionRowResult.Unchanged, entry.Result);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(path));
    }
}
