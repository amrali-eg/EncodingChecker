using System.Text;

namespace EncodingChecker.Tests;

/// <summary>-Backup must copy the original byte-for-byte, and a failed conversion must never corrupt it.</summary>
public sealed class BackupIntegrityTests : IDisposable
{
    private readonly string _root;

    public BackupIntegrityTests()
    {
        _root = Directory.CreateTempSubdirectory("ec_backup_").FullName;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
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

        var entries = new List<ConversionReportEntry>();
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
    public void Backup_OverwritesAnyPreviousBackupFile()
    {
        string path = Path.Combine(_root, "convert-me.txt");
        File.WriteAllText(path, TestContent.Ascii, Encoding.ASCII);

        string backupPath = path + ".bak";
        File.WriteAllText(backupPath, "stale backup content that must be replaced");

        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            // Exclude the pre-seeded stale .bak file itself from the scan's input set.
            IncludePatterns = ["*.txt"],
            Action = ScanAction.Convert,
            TargetCharset = "utf-8",
            TargetWriteBom = true,
            Backup = true,
        };

        var entries = new List<ConversionReportEntry>();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

        Assert.Equal(ConversionRowResult.Converted, Assert.Single(entries).Result);
        Assert.Equal(Encoding.ASCII.GetBytes(TestContent.Ascii), File.ReadAllBytes(backupPath));
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

        var entries = new List<ConversionReportEntry>();
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

        var entries = new List<ConversionReportEntry>();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

        Assert.Equal(ConversionRowResult.Unchanged, Assert.Single(entries).Result);
        Assert.False(File.Exists(path + ".bak"));
    }
}
