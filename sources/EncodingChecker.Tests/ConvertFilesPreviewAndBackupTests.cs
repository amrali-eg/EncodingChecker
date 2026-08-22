using System.Collections.Concurrent;
using System.Text;

namespace EncodingChecker.Tests;

/// <summary>
/// ScanEngine.ConvertFiles's whatIf/backup parameters are what wire MainForm's
/// chkPreviewChanges/chkCreateBackup checkboxes into the conversion pipeline
/// (see MainForm.OnConvert: WhatIf = chkPreviewChanges.Checked, Backup =
/// chkCreateBackup.Checked). These mirror WhatIfSafetyTests/BackupIntegrityTests,
/// which exercise the same two ApplyConversion parameters through ScanDirectory
/// (the CLI/View-then-Convert path), for ConvertFiles - the GUI's own entry point
/// for converting an already-scanned selection.
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

        var completed = new List<ConversionReportEntry>();
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

        var completed = new List<ConversionReportEntry>();
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
    public void Preview_LeavesBytesUnchanged_CreatesNoBackupEvenIfRequested_ReportsWouldConvert()
    {
        string path = Path.Combine(_root, "f.txt");
        File.WriteAllText(path, TestContent.Ascii, Encoding.ASCII);
        byte[] originalBytes = File.ReadAllBytes(path);
        DateTime originalWriteTime = File.GetLastWriteTimeUtc(path);

        var completed = new List<ConversionReportEntry>();
        ScanEngine.ConvertFiles(
            [MakeAsciiEntry(path)],
            "utf-8",
            targetWriteBom: true,
            ScanEngine.DefaultMaxParallelism,
            whatIf: true,
            backup: true, // Both checkboxes checked - Preview must still win (see ApplyConversion).
            completed.Add,
            CancellationToken.None);

        // "Converted" is this codebase's existing convention for "would be converted"
        // under a dry run (see ConversionRowResult.Converted's own doc comment) - the
        // same value WhatIfSafetyTests asserts for the ScanDirectory path.
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

        var completed = new List<ConversionReportEntry>();
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
        // Mirrors the exact combination MainForm can produce: both checkboxes checked,
        // over a mixed selection like OnConvert builds from lstResults.CheckedItems.
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
        // Regression guard for every pre-existing caller (MainForm's non-preview/non-
        // backup path, and every existing test): whatIf: false, backup: false is a real
        // conversion with no backup, exactly as ConvertFiles behaved before it gained
        // these two parameters.
        string path = Path.Combine(_root, "f.txt");
        File.WriteAllText(path, TestContent.Ascii, Encoding.ASCII);
        byte[] originalBytes = File.ReadAllBytes(path);

        var completed = new List<ConversionReportEntry>();
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
