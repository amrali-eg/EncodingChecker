using System.Collections.Concurrent;
using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// Covers the lower-level conversion pass used after a caller has already selected and
/// classified files. The GUI orchestration is covered separately by
/// <see cref="ConversionOrchestrationTests"/>; these tests pin the two safety switches
/// passed into <see cref="ScanEngine.ConvertFiles"/> itself:
/// <list type="bullet">
/// <item><description>Preview reports what would happen without writing a file or backup.</description></item>
/// <item><description>Backup preserves the original before a real conversion can replace it.</description></item>
/// </list>
/// </summary>
public sealed class ConvertFilesPreviewAndBackupTests : IDisposable
{
    private readonly string _root;

    public ConvertFilesPreviewAndBackupTests()
    {
        _root = Directory.CreateTempSubdirectory("ec_convertfiles_options_").FullName;
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

    private static ConversionReportEntry MakeAsciiEntry(string path) => new()
    {
        FilePath = path,
        SourceEncoding = "us-ascii",
        SourceHasBom = false,
        TargetEncoding = "utf-8",
    };

    [Fact]
    public void Backup_Unchecked_CreatesNoBakFile()
    {
        string path = Path.Combine(_root, "f.txt");
        File.WriteAllText(path, TestContent.Ascii, Encoding.ASCII);

        var completed = new EntrySink();
        ScanEngine.ConvertFiles(
            [MakeAsciiEntry(path)],
            "utf-8",
            targetWriteBom: true,
            ScanEngine.DefaultMaxParallelism,
            whatIf: false,
            backup: false,
            completed.Add,
            CancellationToken.None);

        Assert.Equal(ConversionRowResult.Converted, Assert.Single(completed).Result);
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void Backup_Checked_CreatesBakContainingTheOriginal()
    {
        string path = Path.Combine(_root, "f.txt");
        File.WriteAllText(path, TestContent.Ascii, Encoding.ASCII);
        byte[] originalBytes = File.ReadAllBytes(path);

        var completed = new EntrySink();
        ScanEngine.ConvertFiles(
            [MakeAsciiEntry(path)],
            "utf-8",
            targetWriteBom: true,
            ScanEngine.DefaultMaxParallelism,
            whatIf: false,
            backup: true,
            completed.Add,
            CancellationToken.None);

        Assert.Equal(ConversionRowResult.Converted, Assert.Single(completed).Result);
        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal(originalBytes, File.ReadAllBytes(path + ".bak"));
        Assert.NotEqual(originalBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void RepeatedLeadingBom_IsRefusedBeforeBackupOrMetadataIsCreated()
    {
        string path = Path.Combine(_root, "double-bom.txt");
        byte[] bom = Encoding.UTF8.GetPreamble();
        byte[] text = Encoding.UTF8.GetBytes("plain text");
        File.WriteAllBytes(path, [.. bom, .. bom, .. text]);
        byte[] original = File.ReadAllBytes(path);

        var entry = new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = "utf-8",
            SourceHasBom = true,
            TargetEncoding = "utf-8",
        };

        var completed = new EntrySink();
        ScanEngine.ConvertFiles(
            [entry], "utf-8", targetWriteBom: false,
            ScanEngine.DefaultMaxParallelism,
            whatIf: false, backup: true,
            completed.Add, CancellationToken.None);

        ConversionReportEntry result = Assert.Single(completed);
        Assert.Equal(ConversionRowResult.Refused, result.Result);
        Assert.Equal(
            ConversionReasonCodes.MultipleLeadingByteOrderMarks,
            result.ReasonCode);
        Assert.Contains("more than one byte-order mark", result.Diagnostic);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".bak"));
        Assert.False(File.Exists(ConversionMetadataStore.MetadataPathFor(path)));
    }

    [Fact]
    public void Preview_LeavesBytesUnchanged_CreatesNoBackupEvenIfRequested_ReportsWouldConvert()
    {
        string path = Path.Combine(_root, "f.txt");
        File.WriteAllText(path, TestContent.Ascii, Encoding.ASCII);
        byte[] originalBytes = File.ReadAllBytes(path);
        DateTime originalWriteTime = File.GetLastWriteTimeUtc(path);

        var completed = new EntrySink();
        ScanEngine.ConvertFiles(
            [MakeAsciiEntry(path)],
            "utf-8",
            targetWriteBom: true,
            ScanEngine.DefaultMaxParallelism,
            whatIf: true,
            // Preview takes precedence: it must not create a backup or write output.
            backup: true,
            completed.Add,
            CancellationToken.None);

        // In a preview, Converted means "would convert". The source bytes, timestamp,
        // and backup state prove that this result describes a plan rather than a write.
        Assert.Equal(ConversionRowResult.Converted, Assert.Single(completed).Result);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void Preview_AlreadyMatchingFile_ReportsUnchanged()
    {
        string path = Path.Combine(_root, "already.txt");
        File.WriteAllText(path, TestContent.Multilingual, new UTF8Encoding(true));
        byte[] originalBytes = File.ReadAllBytes(path);

        var entry = new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = "utf-8",
            SourceHasBom = true,
            TargetEncoding = "utf-8",
        };

        var completed = new EntrySink();
        ScanEngine.ConvertFiles(
            [entry],
            "utf-8",
            targetWriteBom: true,
            ScanEngine.DefaultMaxParallelism,
            whatIf: true,
            backup: false,
            completed.Add,
            CancellationToken.None);

        Assert.Equal(ConversionRowResult.Unchanged, Assert.Single(completed).Result);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void PreviewAndBackupTogether_NeverModifiesSource_NeverCreatesBackup_ReportsCorrectResults()
    {
        // Scenario: preview and backup are both requested for a mixed selection.
        // Protection: preview wins for every row, including rows that would convert.
        string needsConversion = Path.Combine(_root, "a.txt");
        File.WriteAllText(needsConversion, TestContent.Ascii, Encoding.ASCII);

        string alreadyMatches = Path.Combine(_root, "b.txt");
        File.WriteAllText(alreadyMatches, TestContent.Multilingual, new UTF8Encoding(true));

        byte[] originalA = File.ReadAllBytes(needsConversion);
        byte[] originalB = File.ReadAllBytes(alreadyMatches);

        ConversionReportEntry[] entries =
        [
            MakeAsciiEntry(needsConversion),
            new ConversionReportEntry
            {
                FilePath = alreadyMatches,
                SourceEncoding = "utf-8",
                SourceHasBom = true,
                TargetEncoding = "utf-8",
            },
        ];

        var completed = new ConcurrentBag<ConversionReportEntry>();
        ScanEngine.ConvertFiles(
            entries,
            "utf-8",
            targetWriteBom: true,
            ScanEngine.DefaultMaxParallelism,
            whatIf: true,
            backup: true,
            completed.Add,
            CancellationToken.None);

        Assert.Equal(2, completed.Count);
        Assert.Equal(originalA, File.ReadAllBytes(needsConversion));
        Assert.Equal(originalB, File.ReadAllBytes(alreadyMatches));
        Assert.False(File.Exists(needsConversion + ".bak"));
        Assert.False(File.Exists(alreadyMatches + ".bak"));

        Dictionary<string, ConversionReportEntry> byPath =
            completed.ToDictionary(e => e.FilePath);

        Assert.Equal(ConversionRowResult.Converted, byPath[needsConversion].Result); // would convert
        Assert.Equal(ConversionRowResult.Unchanged, byPath[alreadyMatches].Result);
    }

    [Fact]
    public void ExplicitFalseFalse_BehavesExactlyAsBeforeThisFeature()
    {
        // Scenario: neither optional safety switch is requested.
        // Expected behavior: ConvertFiles performs its normal conversion without creating
        // a backup. This keeps the lower-level API explicit for callers that opt out.
        string path = Path.Combine(_root, "f.txt");
        File.WriteAllText(path, TestContent.Ascii, Encoding.ASCII);
        byte[] originalBytes = File.ReadAllBytes(path);

        var completed = new EntrySink();
        ScanEngine.ConvertFiles(
            [MakeAsciiEntry(path)],
            "utf-8",
            targetWriteBom: true,
            ScanEngine.DefaultMaxParallelism,
            whatIf: false,
            backup: false,
            completed.Add,
            CancellationToken.None);

        Assert.Equal(ConversionRowResult.Converted, Assert.Single(completed).Result);
        Assert.NotEqual(originalBytes, File.ReadAllBytes(path)); // actually converted, not previewed
        Assert.False(File.Exists(path + ".bak"));
    }
}
