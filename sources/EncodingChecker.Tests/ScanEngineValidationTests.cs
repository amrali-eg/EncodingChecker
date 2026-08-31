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

        var entries = new EntrySink();

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
    public void ScanDirectory_EmptyFile_IsUnknownAndSkipped_NotAnError()
    {
        File.WriteAllBytes(Path.Combine(_root, "empty.txt"), []);

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Detect,
        };

        var entries = new EntrySink();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

        ConversionReportEntry entry = Assert.Single(entries);
        Assert.Equal("(Unknown)", entry.SourceEncoding);
        // Undetectable encoding is Skipped, not Unchanged - "Unchanged" would misleadingly
        // imply the file was compared against a target and already matched it.
        Assert.Equal(ConversionRowResult.Skipped, entry.Result);
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

        var entries = new EntrySink();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

        ConversionReportEntry entry = Assert.Single(entries);
        Assert.Equal("(Unknown)", entry.SourceEncoding);
        Assert.Equal(ConversionRowResult.Skipped, entry.Result);
    }

    [Fact]
    public void Validate_RejectsARecognizedUtf8FileWithATruncatedTail()
    {
        byte[] prefix = Encoding.UTF8.GetBytes(string.Concat(
            Enumerable.Repeat("Hello 世界 ", 128)));
        byte[] bytes = [.. prefix, 0xE2, 0x82];
        File.WriteAllBytes(Path.Combine(_root, "invalid.txt"), bytes);
        var entries = new EntrySink();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                Action = ScanAction.Validate,
                ValidCharsets = ["utf-8"],
            },
            entries.Add,
            CancellationToken.None);

        ConversionReportEntry entry = Assert.Single(entries);
        Assert.Equal(ConversionRowResult.Invalid, entry.Result);
        Assert.Equal(ConversionReasonCodes.StrictValidationFailed, entry.ReasonCode);
    }

    [Fact]
    public void Validate_AcceptsWellFormedUtf8()
    {
        File.WriteAllBytes(Path.Combine(_root, "valid.txt"), "Hello 世界"u8.ToArray());
        var entries = new EntrySink();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                Action = ScanAction.Validate,
                ValidCharsets = ["utf-8"],
            },
            entries.Add,
            CancellationToken.None);

        Assert.Equal(ConversionRowResult.Unchanged, Assert.Single(entries).Result);
    }

    [Fact]
    public void ConvertFiles_BinaryFile_IsSkipped_NotConvertedAndBytesUnchanged()
    {
        var random = new Random(5678);
        byte[] randomBytes = new byte[4096];
        random.NextBytes(randomBytes);
        string path = Path.Combine(_root, "binary.dat");
        File.WriteAllBytes(path, randomBytes);

        // Convert mode over a file whose encoding couldn't be detected: must be Skipped,
        // not silently Unchanged (which would misleadingly imply it already matched
        // "utf-8"), and must never be written to.
        var scanOptions = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Convert,
            TargetCharset = "utf-8",
        };

        var scanned = new EntrySink();
        ScanEngine.ScanDirectory(scanOptions, scanned.Add, CancellationToken.None);

        ConversionReportEntry scanEntry = Assert.Single(scanned);
        Assert.Equal(ConversionRowResult.Skipped, scanEntry.Result);
        Assert.Equal(randomBytes, File.ReadAllBytes(path));

        // ConvertFiles (the GUI's "convert previously-scanned entries" path) must reach
        // the same conclusion independently, not just inherit it from the prior scan.
        var converted = new ConcurrentBag<ConversionReportEntry>();
        ScanEngine.ConvertFiles(
            [scanEntry],
            "utf-8",
            targetWriteBom: false,
            ScanEngine.DefaultMaxParallelism,
            whatIf: false,
            backup: false,
            converted.Add,
            CancellationToken.None);

        ConversionReportEntry convertEntry = Assert.Single(converted);
        Assert.Equal(ConversionRowResult.Skipped, convertEntry.Result);
        Assert.Equal(randomBytes, File.ReadAllBytes(path));
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

        var entries = new EntrySink();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

        ConversionReportEntry entry = Assert.Single(entries);
        Assert.Equal("utf-16", entry.SourceEncoding);
        Assert.True(entry.SourceHasBom);
    }

    [Fact]
    public void ScanDirectory_ExplicitBomCapableCodec_DoesNotInventABom()
    {
        // Encoding.GetPreamble() says UTF-16 can carry a BOM; it must not cause EC to
        // report one when this particular source does not start with it.
        File.WriteAllText(
            Path.Combine(_root, "utf16-nobom.txt"),
            TestContent.Multilingual,
            new UnicodeEncoding(bigEndian: false, byteOrderMark: false));

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Detect,
            SourceCharset = "utf-16",
        };

        var entries = new EntrySink();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

        ConversionReportEntry entry = Assert.Single(entries);
        Assert.Equal("utf-16", entry.SourceEncoding);
        Assert.False(entry.SourceHasBom);
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
        Assert.Equal(PlannedAction.Refuse, lockedEntry.Action);
        Assert.Equal(SourceInterpretation.NotApplicable, lockedEntry.SourceInterpretation);
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
                whatIf: false,
                backup: false,
                onEntry: _ => { },
                CancellationToken.None));
    }

    [Fact]
    public void ConvertFiles_EntryWithUnresolvableSourceEncoding_RefusesInsteadOfThrowing()
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

        var completed = new EntrySink();
        ScanEngine.ConvertFiles(
            entries,
            "utf-8",
            targetWriteBom: false,
            ScanEngine.DefaultMaxParallelism,
            whatIf: false,
            backup: false,
            onEntry: completed.Add,
            CancellationToken.None);

        ConversionReportEntry result = Assert.Single(completed);
        Assert.Equal(ConversionRowResult.Refused, result.Result);
    }
}
