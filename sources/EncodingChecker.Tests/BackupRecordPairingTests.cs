using System.Text;
using System.Text.Json;

namespace EncodingChecker.Tests;

/// <summary>
/// A recovery record must never describe a backup that is no longer there.
/// </summary>
/// <remarks>
/// <c>&lt;file&gt;.bak</c> is a fixed name, so a second conversion replaces the first
/// one's restore point. The record for the first conversion used to survive that: it
/// is written only after verification succeeds, so a conversion that failed after the
/// backup was replaced left the earlier record in place, still asserting a hash, a
/// source codec and a completed installation for bytes that no longer existed.
/// <para>
/// Measured before the fix: convert utf-8 to utf-16 with a backup, then convert to
/// ascii, which cannot represent the text. The second backup replaced the first, the
/// conversion failed, and the surviving record claimed "utf-8 to utf-16, Completed"
/// against a backup that now held the utf-16 file. Not merely unverifiable - wrong
/// about what the backup contained, in the direction that matters, because someone
/// reading it would believe their original was recoverable.
/// </para>
/// </remarks>
public sealed class BackupRecordPairingTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_backuppair_").FullName;

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

    private string Convert(string targetCharset, bool targetWriteBom, string? from = null)
    {
        var options = new ScanDirectoryOptions
        {
            BaseDirectory = _root,
            Action = ScanAction.Convert,
            TargetCharset = targetCharset,
            TargetWriteBom = targetWriteBom,
            SourceCharset = from,
            Backup = true,
        };

        var entries = new EntrySink();
        ScanEngine.ScanDirectory(options, entries.Add, CancellationToken.None);

        return Assert.Single(entries).Result.ToString();
    }

    private string FilePath => Path.Combine(_root, "f.txt");

    private string BackupPath => FilePath + ".bak";

    private string RecordPath => ConversionMetadataStore.MetadataPathFor(FilePath);

    /// <summary>The invariant, asserted the same way everywhere it is checked.</summary>
    private void AssertNoRecordDescribesAMissingBackup()
    {
        if (!File.Exists(RecordPath))
            return;

        Assert.True(File.Exists(BackupPath), "A record exists with no backup beside it.");

        string recorded = JsonDocument.Parse(File.ReadAllText(RecordPath))
            .RootElement.GetProperty("BackupSha256").GetString()!;

        Assert.Equal(
            ConversionMetadataStore.ComputeSha256(BackupPath),
            recorded,
            ignoreCase: true);
    }

    [Fact]
    public void SecondConversionFails_NoRecordIsLeftDescribingTheReplacedBackup()
    {
        File.WriteAllText(FilePath, "café résumé", new UTF8Encoding(false));

        Assert.Equal(nameof(ConversionRowResult.Converted), Convert("utf-16", targetWriteBom: true));
        AssertNoRecordDescribesAMissingBackup();

        string firstBackup = ConversionMetadataStore.ComputeSha256(BackupPath);

        // ascii cannot encode the accented characters, so this fails after the backup
        // has already replaced the first one.
        Assert.Equal(nameof(ConversionRowResult.Error), Convert("ascii", targetWriteBom: false));

        Assert.NotEqual(firstBackup, ConversionMetadataStore.ComputeSha256(BackupPath));
        AssertNoRecordDescribesAMissingBackup();
        Assert.False(
            File.Exists(RecordPath),
            "A failed conversion must leave no record at all, not a stale one.");
    }

    [Fact]
    public void SecondConversionSucceeds_TheRecordDescribesTheNewBackup()
    {
        // The invalidation must not cost a successful conversion its record.
        File.WriteAllText(FilePath, "café résumé", new UTF8Encoding(false));

        Assert.Equal(nameof(ConversionRowResult.Converted), Convert("utf-16", targetWriteBom: true));
        Assert.Equal(nameof(ConversionRowResult.Converted), Convert("utf-8", targetWriteBom: false, from: "utf-16"));

        Assert.True(File.Exists(RecordPath));
        AssertNoRecordDescribesAMissingBackup();

        JsonElement record = JsonDocument.Parse(File.ReadAllText(RecordPath)).RootElement;

        Assert.Equal("utf-16", record.GetProperty("SourceEncodingName").GetString());
        Assert.Equal("utf-8", record.GetProperty("TargetEncodingName").GetString());
        Assert.Equal("Completed", record.GetProperty("InstallationState").GetString());
    }

    [Fact]
    public void TheFailedRunStillLeavesAUsableBackupOfWhatItWasAboutToChange()
    {
        // Removing the record must not be mistaken for removing the restore point.
        // .bak is "the version this run replaced", and after a failed run that is the
        // file as it stands now.
        File.WriteAllText(FilePath, "café résumé", new UTF8Encoding(false));

        Convert("utf-16", targetWriteBom: true);
        byte[] afterFirst = File.ReadAllBytes(FilePath);

        Convert("ascii", targetWriteBom: false);

        Assert.Equal(afterFirst, File.ReadAllBytes(FilePath));
        Assert.Equal(afterFirst, File.ReadAllBytes(BackupPath));
    }

    [Fact]
    public void RemoveBeforeBackupReplacement_RemovesEvenAReadOnlyRecord()
    {
        // The backup path clears ReadOnly before replacing; the record must match, or
        // a read-only sidecar would fail the backup instead of being replaced by it.
        File.WriteAllText(FilePath, "hello", new UTF8Encoding(false));
        File.WriteAllText(RecordPath, "{}");
        File.SetAttributes(RecordPath, FileAttributes.ReadOnly);

        ConversionMetadataStore.RemoveBeforeBackupReplacement(FilePath);

        Assert.False(File.Exists(RecordPath));
    }

    [Fact]
    public void RemoveBeforeBackupReplacement_WithNoRecordPresent_IsANoOp()
    {
        File.WriteAllText(FilePath, "hello", new UTF8Encoding(false));

        ConversionMetadataStore.RemoveBeforeBackupReplacement(FilePath);

        Assert.False(File.Exists(RecordPath));
        Assert.True(File.Exists(FilePath));
    }
}
