using System.Text;
using System.Text.RegularExpressions;

namespace EncodingChecker.Tests;

/// <summary>-Backup must copy the original byte-for-byte, and a failed conversion must never corrupt it.</summary>
public sealed class BackupIntegrityTests : IDisposable
{
    private readonly string _root;

    public BackupIntegrityTests()
    {
        _root = Directory.CreateTempSubdirectory("ec_backup_").FullName;
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
    public void Backup_SuccessfulConversion_BackupMatchesOriginalAndTargetIsCorrect()
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
            Backup = true,
        };

        var entries = new EntrySink();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

        ConversionReportEntry entry = Assert.Single(entries);
        Assert.Equal(ConversionRowResult.Converted, entry.Result);

        string backupPath = path + ".bak";
        Assert.True(File.Exists(backupPath));
        Assert.Equal(originalBytes, File.ReadAllBytes(backupPath));

        byte[] convertedBytes = File.ReadAllBytes(path);
        Assert.NotEqual(originalBytes, convertedBytes);

        var target = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        string decoded = target.GetString(convertedBytes[target.GetPreamble().Length..]);
        Assert.Equal(TestContent.Multilingual, decoded);
    }

    [Fact]
    public void Backup_SuccessfulConversion_LeavesNoTempArtifactBehind()
    {
        // Backup is now temp-file-then-atomic-replace, not a plain File.Copy -
        // confirm the "*.bak.<TEMP_FILE_SUFFIX>" sibling never survives.
        string path = Path.Combine(_root, "convert-me.txt");
        File.WriteAllText(path, TestContent.Multilingual, new UTF8Encoding(false));

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Convert,
            TargetCharset = "utf-16",
            TargetWriteBom = true,
            Backup = true,
        };

        var entries = new EntrySink();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

        Assert.Equal(ConversionRowResult.Converted, Assert.Single(entries).Result);
        Assert.True(File.Exists(path + ".bak"));
        // Temp filename shape: "<name>.<guid>.bak.<TEMP_FILE_SUFFIX>".
        Assert.Empty(Directory.GetFiles(_root, $"*.bak.{EncodingConverter.TempFileSuffix}"));
    }

    [Fact]
    public void Backup_OverwritesAnyPreviousBackupFile()
    {
        string path = Path.Combine(_root, "convert-me.txt");
        File.WriteAllText(path, TestContent.Ascii, Encoding.ASCII);

        string backupPath = path + ".bak";
        File.WriteAllText(backupPath, "stale backup content that must be replaced");

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            // No IncludePatterns restriction: ScanEngine excludes *.bak on its own now
            // (see Backup_WildcardInclude_NeverScansItsOwnBakOrTempFiles).
            Action = ScanAction.Convert,
            TargetCharset = "utf-8",
            TargetWriteBom = true,
            Backup = true,
        };

        var entries = new EntrySink();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

        Assert.Equal(ConversionRowResult.Converted, Assert.Single(entries).Result);
        Assert.Equal(Encoding.ASCII.GetBytes(TestContent.Ascii), File.ReadAllBytes(backupPath));
    }

    [Fact]
    public void Backup_WildcardInclude_NeverScansItsOwnBakOrTempFiles()
    {
        // Regression test: -Include "*" with -Backup must not re-scan its own .bak
        // output - that used to cascade (.bak.bak, ...) and race backup writes.
        string path = Path.Combine(_root, "convert-me.txt");
        File.WriteAllText(path, TestContent.Multilingual, new UnicodeEncoding(false, true));

        // A leftover temp-conversion artifact, as could survive a crash mid-conversion.
        File.WriteAllText(
            Path.Combine(_root, $"other.txt.{Guid.NewGuid():N}.{EncodingConverter.TempFileSuffix}"),
            "leftover temp file content");

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            IncludePatterns = ["*"],
            Action = ScanAction.Convert,
            TargetCharset = "utf-8",
            TargetWriteBom = false,
            Backup = true,
        };

        var firstRun = new EntrySink();
        ScanEngine.ScanDirectory(options, firstRun.Add, CancellationToken.None);
        Assert.Equal(ConversionRowResult.Converted, Assert.Single(firstRun).Result);

        string backupPath = path + ".bak";
        Assert.True(File.Exists(backupPath));
        Assert.False(File.Exists(backupPath + ".bak"));

        // Re-running over the same directory (now containing convert-me.txt.bak) must
        // still see only the one real source file, not the backup it just wrote.
        var secondRun = new EntrySink();
        ScanEngine.ScanDirectory(options, secondRun.Add, CancellationToken.None);

        ConversionReportEntry onlyEntry = Assert.Single(secondRun);
        Assert.Equal(path, onlyEntry.FilePath);
        Assert.False(File.Exists(backupPath + ".bak"));
    }

    [Theory]
    [InlineData("convert-me.txt.abc123456789abcdef.bak.unicodechecker.tmp", false)] // new backup temp shape
    [InlineData("convert-me.txt.ABC123456789ABCDEF.BAK.UNICODECHECKER.TMP", false)] // case-insensitive
    [InlineData("convert-me.txt.bak", false)]                                       // installed backup
    [InlineData("convert-me.txt.abc123456789abcdef.unicodechecker.tmp", false)]     // normal converter temp
    [InlineData("convert-me.txt.tmp", true)]                                        // ordinary .tmp, not ours
    [InlineData("convert-me.txt.abc.unicodechecker.tmpx", true)]                    // near-miss suffix
    [InlineData("convert-me.txt", true)]
    public void BackupTempFileShape_IsExcludedFromEnumeration_LikeOtherToolGeneratedFiles(
        string fileName, bool expectedCandidate)
    {
        File.WriteAllText(Path.Combine(_root, fileName), "x");

        List<Regex> matchAll = DirectoryTraversal.CompilePatterns(["*"], defaultToMatchAll: true);

        var found = DirectoryTraversal.EnumerateFiles(
            _root,
            includeSubdirectories: false,
            matchAll,
            [],
            excludedFullPath: null,
            onWarning: null).ToList();

        bool isCandidate = found.Any(f => Path.GetFileName(f) == fileName);
        Assert.Equal(expectedCandidate, isCandidate);
    }

    [Fact]
    public void Backup_ConversionFailsVerification_OriginalUntouchedAndBackupStillIntact()
    {
        // windows-1252 silently substitutes '?' for CJK; hash verification rejects it.
        string path = Path.Combine(_root, "unmappable.txt");
        File.WriteAllText(path, "世界", new UTF8Encoding(false));
        byte[] originalBytes = File.ReadAllBytes(path);

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Convert,
            TargetCharset = "windows-1252",
            TargetWriteBom = false,
            Backup = true,
        };

        var entries = new EntrySink();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

        ConversionReportEntry entry = Assert.Single(entries);
        Assert.Equal(ConversionRowResult.Error, entry.Result);

        Assert.Equal(originalBytes, File.ReadAllBytes(path));

        string backupPath = path + ".bak";
        Assert.True(File.Exists(backupPath));
        Assert.Equal(originalBytes, File.ReadAllBytes(backupPath));
    }

    [Fact]
    public void Backup_FileAlreadyMatchesTarget_NoBackupCreated()
    {
        // Multilingual, not ASCII: bare ASCII redetects as "us-ascii", not "utf-8".
        string path = Path.Combine(_root, "already-matches.txt");
        File.WriteAllText(path, TestContent.Multilingual, new UTF8Encoding(false));

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Convert,
            TargetCharset = "utf-8",
            TargetWriteBom = false,
            Backup = true,
        };

        var entries = new EntrySink();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

        Assert.Equal(ConversionRowResult.Unchanged, Assert.Single(entries).Result);
        Assert.False(File.Exists(path + ".bak"));
    }
}
