using System.Collections.Concurrent;
using System.Text;

namespace EncodingChecker.Tests;

/// <summary>Invalid-input validation and fault isolation for <see cref="ScanEngine"/>.</summary>
public sealed class ScanEngineValidationTests : IDisposable
{
    private readonly string _root;

    public ScanEngineValidationTests()
    {
        _root = Directory.CreateTempSubdirectory("ec_validate_").FullName;
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
    public void ScanDirectory_NonExistentBaseDirectory_Throws()
    {
        var options = new ScanDirectoryOptions
        {
            BaseDirectory = Path.Combine(_root, "does-not-exist"),
            Action = ScanAction.Detect,
        };

        Assert.Throws<ArgumentException>(
            () => ScanEngine.ScanDirectory(options, _ => { }, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ScanDirectory_InvalidMaxParallelism_Throws(int maxParallelism)
    {
        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Detect,
            MaxParallelism = maxParallelism,
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ScanEngine.ScanDirectory(options, _ => { }, CancellationToken.None));
    }

    [Fact]
    public void ScanDirectory_ConvertWithoutTargetCharset_Throws()
    {
        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Convert,
            TargetCharset = null,
        };

        Assert.Throws<ArgumentException>(
            () => ScanEngine.ScanDirectory(options, _ => { }, CancellationToken.None));
    }

    [Fact]
    public void ScanDirectory_ConvertWithUnrecognizedTargetCharset_Throws()
    {
        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Convert,
            TargetCharset = "not-a-real-encoding",
        };

        Assert.ThrowsAny<ArgumentException>(
            () => ScanEngine.ScanDirectory(options, _ => { }, CancellationToken.None));
    }

    [Fact]
    public void ScanDirectory_WhatIfWithUnrecognizedTargetCharset_StillThrowsUpFront()
    {
        // Regression test: WhatIf must not bypass target-encoding validation.
        File.WriteAllText(Path.Combine(_root, "a.txt"), TestContent.Ascii, Encoding.ASCII);

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Convert,
            TargetCharset = "not-a-real-encoding",
            WhatIf = true,
        };

        var entries = new List<ConversionReportEntry>();

        Assert.ThrowsAny<ArgumentException>(
            () => ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None));

        // The scan must fail before processing any file, not report a false "would convert".
        Assert.Empty(entries);
    }

    [Fact]
    public void ScanDirectory_ValidateWithNullValidCharsets_Throws()
    {
        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Validate,
            ValidCharsets = null,
        };

        Assert.Throws<ArgumentException>(
            () => ScanEngine.ScanDirectory(options, _ => { }, CancellationToken.None));
    }

    [Fact]
    public void ScanDirectory_ValidateWithEmptyValidCharsets_Throws()
    {
        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Validate,
            ValidCharsets = [],
        };

        Assert.Throws<ArgumentException>(
            () => ScanEngine.ScanDirectory(options, _ => { }, CancellationToken.None));
    }

    [Fact]
    public void ScanDirectory_EmptyFile_IsUnknownAndUnchanged_NotAnError()
    {
        File.WriteAllBytes(Path.Combine(_root, "empty.txt"), []);

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Detect,
        };

        var entries = new List<ConversionReportEntry>();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

        ConversionReportEntry entry = Assert.Single(entries);
        Assert.Equal("(Unknown)", entry.SourceEncoding);
        Assert.Equal(ConversionRowResult.Unchanged, entry.Result);
    }

    [Fact]
    public void ScanDirectory_BinaryFile_IsUnknown_NotMisdetectedAsText()
    {
        var random = new Random(1234);
        byte[] randomBytes = new byte[4096];
        random.NextBytes(randomBytes);
        File.WriteAllBytes(Path.Combine(_root, "binary.dat"), randomBytes);

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Detect,
        };

        var entries = new List<ConversionReportEntry>();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

        ConversionReportEntry entry = Assert.Single(entries);
        Assert.Equal("(Unknown)", entry.SourceEncoding);
    }

    [Fact]
    public void ScanDirectory_MultilingualUtf16File_IsNotMisdetectedAsBinary()
    {
        // UTF-16's inherent NUL bytes must not trip TextEncoding's entropy-based binary check.
        File.WriteAllText(
            Path.Combine(_root, "utf16.txt"),
            TestContent.Multilingual,
            new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Detect,
        };

        var entries = new List<ConversionReportEntry>();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

        ConversionReportEntry entry = Assert.Single(entries);
        Assert.Equal("utf-16", entry.SourceEncoding);
        Assert.True(entry.SourceHasBom);
    }

    [Fact]
    public void ScanDirectory_OneLockedFile_ReportsErrorWithoutAbortingOtherFiles()
    {
        File.WriteAllText(Path.Combine(_root, "good1.txt"), TestContent.Ascii, Encoding.ASCII);
        File.WriteAllText(Path.Combine(_root, "good2.txt"), TestContent.Multilingual, new UTF8Encoding(false));

        string lockedPath = Path.Combine(_root, "locked.txt");
        File.WriteAllText(lockedPath, TestContent.Ascii, Encoding.ASCII);

        using FileStream lockHandle = new(
            lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Detect,
        };

        // Three files means onEntry can race across worker threads; List<T> would not be safe.
        var collected = new ConcurrentBag<ConversionReportEntry>();
        ScanEngine.ScanDirectory(options, collected.Add, CancellationToken.None);
        List<ConversionReportEntry> entries = [.. collected];

        Assert.Equal(3, entries.Count);

        ConversionReportEntry lockedEntry = entries.Single(
            e => string.Equals(e.FilePath, lockedPath, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ConversionRowResult.Error, lockedEntry.Result);
        Assert.NotNull(lockedEntry.Diagnostic);

        Assert.Contains(entries, e => e.FilePath.EndsWith("good1.txt") && e.Result != ConversionRowResult.Error);
        Assert.Contains(entries, e => e.FilePath.EndsWith("good2.txt") && e.Result != ConversionRowResult.Error);
    }

    [Fact]
    public void ConvertFiles_UnrecognizedTargetCharset_ThrowsImmediately()
    {
        var entries = new List<ConversionReportEntry>
        {
            new()
            {
                FilePath = Path.Combine(_root, "a.txt"),
                SourceEncoding = "us-ascii",
                TargetEncoding = "us-ascii",
            },
        };

        Assert.ThrowsAny<ArgumentException>(() =>
            ScanEngine.ConvertFiles(
                entries,
                "not-a-real-encoding",
                targetWriteBom: false,
                ScanEngine.DefaultMaxParallelism,
                onEntry: _ => { },
                CancellationToken.None));
    }

    [Fact]
    public void ConvertFiles_EntryWithUnresolvableSourceEncoding_ReportsErrorInsteadOfThrowing()
    {
        string path = Path.Combine(_root, "stale.txt");
        File.WriteAllText(path, TestContent.Ascii, Encoding.ASCII);

        var entries = new List<ConversionReportEntry>
        {
            new()
            {
                FilePath = path,
                SourceEncoding = "not-a-real-encoding", // simulates a stale/corrupted entry
                TargetEncoding = "not-a-real-encoding",
            },
        };

        var completed = new List<ConversionReportEntry>();
        ScanEngine.ConvertFiles(
            entries,
            "utf-8",
            targetWriteBom: false,
            ScanEngine.DefaultMaxParallelism,
            onEntry: completed.Add,
            CancellationToken.None);

        ConversionReportEntry result = Assert.Single(completed);
        Assert.Equal(ConversionRowResult.Error, result.Result);
    }
}
