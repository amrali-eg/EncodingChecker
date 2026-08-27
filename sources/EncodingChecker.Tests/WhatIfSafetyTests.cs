using System.Collections.Concurrent;
using System.Text;

namespace EncodingChecker.Tests;

/// <summary>-WhatIf must report the outcome without ever touching the file or taking a backup.</summary>
public sealed class WhatIfSafetyTests : IDisposable
{
    private readonly string _root;

    public WhatIfSafetyTests()
    {
        _root = Directory.CreateTempSubdirectory("ec_whatif_").FullName;
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

    [Fact]
    public void WhatIf_FileNeedsConversion_ReportsConvertedWithoutWritingOrBackingUp()
    {
        string path = Path.Combine(_root, "needs-conversion.txt");
        File.WriteAllText(path, TestContent.Multilingual, new UTF8Encoding(false));
        byte[] originalBytes = File.ReadAllBytes(path);
        DateTime originalWriteTime = File.GetLastWriteTimeUtc(path);

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Convert,
            TargetCharset = "utf-8",
            TargetWriteBom = true,
            WhatIf = true,
            Backup = true, // WhatIf must take precedence and skip this entirely.
        };

        var entries = new EntrySink();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

        ConversionReportEntry entry = Assert.Single(entries);
        Assert.Equal(ConversionRowResult.Converted, entry.Result); // "would be converted"

        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void WhatIf_FileAlreadyMatchesTarget_ReportsUnchanged()
    {
        string path = Path.Combine(_root, "already-matches.txt");
        File.WriteAllText(path, TestContent.Multilingual, new UTF8Encoding(false));
        byte[] originalBytes = File.ReadAllBytes(path);

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Convert,
            TargetCharset = "utf-8",
            TargetWriteBom = false,
            WhatIf = true,
        };

        var entries = new EntrySink();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

        ConversionReportEntry entry = Assert.Single(entries);
        Assert.Equal(ConversionRowResult.Unchanged, entry.Result);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void WhatIf_MultipleFilesNeedingDifferentTreatment_LeavesAllBytesUntouched()
    {
        string needsConversion = Path.Combine(_root, "a.txt");
        File.WriteAllText(needsConversion, TestContent.Ascii, Encoding.ASCII);

        string alreadyMatches = Path.Combine(_root, "b.txt");
        File.WriteAllText(alreadyMatches, TestContent.Multilingual, new UTF8Encoding(true));

        byte[] originalA = File.ReadAllBytes(needsConversion);
        byte[] originalB = File.ReadAllBytes(alreadyMatches);

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Convert,
            TargetCharset = "utf-8",
            TargetWriteBom = true,
            WhatIf = true,
        };

        // Two files means onEntry can genuinely be invoked concurrently.
        var collected = new ConcurrentBag<ConversionReportEntry>();
        ScanEngine.ScanDirectory(options, collected.Add, CancellationToken.None);
        List<ConversionReportEntry> entries = [.. collected];

        Assert.Equal(2, entries.Count);
        Assert.Equal(originalA, File.ReadAllBytes(needsConversion));
        Assert.Equal(originalB, File.ReadAllBytes(alreadyMatches));

        ConversionReportEntry entryA = entries.Single(e => e.FilePath == needsConversion);
        ConversionReportEntry entryB = entries.Single(e => e.FilePath == alreadyMatches);
        Assert.Equal(ConversionRowResult.Converted, entryA.Result);
        Assert.Equal(ConversionRowResult.Unchanged, entryB.Result);
    }
}
