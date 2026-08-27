using System.Text;

namespace EncodingChecker.Tests;

/// <summary>Converting an already-matching file, or re-converting after a real one, must be a no-op.</summary>
public sealed class ConversionIdempotencyTests : IDisposable
{
    private readonly string _root;

    public ConversionIdempotencyTests()
    {
        _root = Directory.CreateTempSubdirectory("ec_idempotent_").FullName;
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

    private static List<ConversionReportEntry> ConvertOnce(
        string root, string targetLabel)
    {
        ScanEngine.ParseCharsetLabel(targetLabel, out string baseCharset, out bool writeBom);

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = root,
            Action = ScanAction.Convert,
            TargetCharset = baseCharset,
            TargetWriteBom = writeBom,
        };

        var entries = new EntrySink();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);
        return entries.ToList();
    }

    [Theory]
    [InlineData("utf-8-bom")]
    [InlineData("utf-16-bom")]
    [InlineData("utf-32BE-bom")]
    public void ConvertingAlreadyMatchingFile_ReportsUnchangedAndTouchesNoBytes(string targetLabel)
    {
        ScanEngine.ParseCharsetLabel(targetLabel, out string baseCharset, out bool writeBom);
        Encoding sourceEncoding = Encoding.GetEncoding(baseCharset);
        if (writeBom)
        {
            sourceEncoding = baseCharset switch
            {
                "utf-8" => new UTF8Encoding(true),
                "utf-16" => new UnicodeEncoding(false, true),
                "utf-32BE" => new UTF32Encoding(true, true),
                _ => sourceEncoding,
            };
        }

        string path = Path.Combine(_root, "already-matching.txt");
        File.WriteAllText(path, TestContent.Multilingual, sourceEncoding);
        byte[] originalBytes = File.ReadAllBytes(path);

        List<ConversionReportEntry> entries = ConvertOnce(_root, targetLabel);

        ConversionReportEntry entry = Assert.Single(entries);
        Assert.Equal(ConversionRowResult.Unchanged, entry.Result);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void ConvertingTwice_SecondRunIsANoOpAfterTheFirstRealConversion()
    {
        string path = Path.Combine(_root, "convert-twice.txt");
        File.WriteAllText(path, TestContent.Multilingual, new UTF8Encoding(false));

        // First conversion must actually change the file.
        List<ConversionReportEntry> firstRun = ConvertOnce(_root, "utf-16-bom");
        ConversionReportEntry firstEntry = Assert.Single(firstRun);
        Assert.Equal(ConversionRowResult.Converted, firstEntry.Result);

        byte[] afterFirstConversion = File.ReadAllBytes(path);
        var utf16LeBom = new UnicodeEncoding(false, true);
        Assert.Equal(
            TestContent.Multilingual,
            utf16LeBom.GetString(afterFirstConversion.AsSpan(utf16LeBom.GetPreamble().Length)));

        // Second conversion must be a no-op; the file already matches now.
        List<ConversionReportEntry> secondRun = ConvertOnce(_root, "utf-16-bom");
        ConversionReportEntry secondEntry = Assert.Single(secondRun);
        Assert.Equal(ConversionRowResult.Unchanged, secondEntry.Result);

        Assert.Equal(afterFirstConversion, File.ReadAllBytes(path));
    }

    [Fact]
    public void ConvertingThreeTimesInARow_StaysStableAfterTheFirstRealConversion()
    {
        // Multilingual, not ASCII: bare ASCII redetects as "us-ascii", not "utf-8".
        string path = Path.Combine(_root, "convert-thrice.txt");
        File.WriteAllText(path, TestContent.Multilingual, new UTF8Encoding(true));

        List<ConversionReportEntry> firstRun = ConvertOnce(_root, "utf-8");
        Assert.Equal(ConversionRowResult.Converted, Assert.Single(firstRun).Result);
        byte[] settledBytes = File.ReadAllBytes(path);

        for (int i = 0; i < 2; i++)
        {
            List<ConversionReportEntry> repeatRun = ConvertOnce(_root, "utf-8");
            Assert.Equal(ConversionRowResult.Unchanged, Assert.Single(repeatRun).Result);
            Assert.Equal(settledBytes, File.ReadAllBytes(path));
        }
    }
}
