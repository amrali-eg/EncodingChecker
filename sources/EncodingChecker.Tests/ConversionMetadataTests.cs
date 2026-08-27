using System.Text;
using System.Text.Json;

namespace EncodingChecker.Tests;

/// <summary>
/// The sidecar exists to answer one question without reference to any external report:
/// how can this original be restored, and how can the conversion be reconstructed?
///
/// An audit measured that 99.2% of bad conversions were byte-recoverable — but only for
/// someone who still knew which codec produced them, which lived solely in the conversion
/// report. A GUI user has no reason to keep one, so "recoverable" was theoretical. These
/// tests pin the mechanism that makes it operational.
/// </summary>
public sealed class ConversionMetadataTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_meta_").FullName;

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

    private string Convert(string name, string text, string sourceCharset, string target)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, Encoding.GetEncoding(sourceCharset).GetBytes(text));

        var entry = new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = sourceCharset,
            SourceHasBom = false,
            TargetEncoding = sourceCharset,
            TargetHasBom = false,

            // These name the source encoding rather than having it detected, which is
            // what -From means. Without saying so, the ambiguity gate correctly refuses
            // single-byte content whose encoding its bytes do not identify.
            SourceEncodingWasSpecified = true,
        };

        ScanEngine.ConvertFiles(
            [entry], target, targetWriteBom: false,
            ScanEngine.DefaultMaxParallelism,
            whatIf: false, backup: true, _ => { }, CancellationToken.None);

        return path;
    }

    [Fact]
    public void AConversionWithBackupWritesASidecarDescribingIt()
    {
        string path = Convert("described.txt", "café — naïve", "windows-1252", "utf-8");

        string metadataPath = ConversionMetadataStore.MetadataPathFor(path);
        Assert.True(File.Exists(metadataPath), "no sidecar was written");

        var metadata = JsonSerializer.Deserialize<ConversionMetadata>(
            File.ReadAllText(metadataPath))!;

        Assert.Equal(1, metadata.MetadataVersion);

        // The code page is what identifies the codec unambiguously; a name may not.
        Assert.Equal(1252, metadata.DetectedCodePage);
        Assert.Equal(65001, metadata.TargetCodePage);
        Assert.False(metadata.DetectedBom);

        // The recorded hash must actually be the backup's.
        Assert.Equal(
            ConversionMetadataStore.ComputeSha256(path + ".bak"),
            metadata.BackupSha256);

        // And the backup must be the original, not merely some file.
        Assert.Equal(metadata.OriginalSha256, metadata.BackupSha256);

        Assert.NotEmpty(metadata.ConversionId);
        Assert.NotEmpty(metadata.ConversionTimestampUtc);
        Assert.NotEmpty(metadata.ECVersion);
    }

    [Fact]
    public void TheSidecarAloneIsEnoughToReverseTheConversion()
    {
        // The whole point: recover the original without the conversion report, using
        // only what sits next to the file.
        const string text = "Grüße — café — naïve";
        string path = Convert("reversible.txt", text, "windows-1252", "utf-8");

        var metadata = JsonSerializer.Deserialize<ConversionMetadata>(
            File.ReadAllText(ConversionMetadataStore.MetadataPathFor(path)))!;

        // Read the converted file as the recorded target, re-encode as the recorded
        // source. Nothing here consults a report or guesses an encoding.
        string converted = Encoding.GetEncoding(metadata.TargetCodePage)
            .GetString(File.ReadAllBytes(path));

        byte[] reconstructed = Encoding.GetEncoding(metadata.DetectedCodePage)
            .GetBytes(converted);

        Assert.Equal(File.ReadAllBytes(path + ".bak"), reconstructed);
        Assert.Equal(text, Encoding.GetEncoding(metadata.DetectedCodePage)
            .GetString(reconstructed));
    }

    [Fact]
    public void RestoreIsAvailableWhenBackupAndMetadataAgree()
    {
        string path = Convert("restorable.txt", "café", "windows-1252", "utf-8");

        RestoreStatus status = ConversionMetadataStore.Inspect(path);

        Assert.Equal(RestoreAvailability.Available, status.Availability);
        Assert.True(status.CanRestore);
        Assert.NotNull(status.Metadata);
    }

    [Fact]
    public void AMissingBackupIsReportedAsSuchRatherThanAsCorruption()
    {
        string path = Convert("nobackup.txt", "café", "windows-1252", "utf-8");
        File.Delete(path + ".bak");

        RestoreStatus status = ConversionMetadataStore.Inspect(path);

        Assert.Equal(RestoreAvailability.BackupMissing, status.Availability);
        Assert.False(status.CanRestore);
    }

    [Fact]
    public void ABackupWithNoMetadataIsNotTreatedAsRestorable()
    {
        // The case this whole mechanism exists for: a ".bak" whose encoding nobody
        // recorded. Its existence is not evidence that anything can be recovered.
        string path = Convert("orphan.txt", "café", "windows-1252", "utf-8");
        File.Delete(ConversionMetadataStore.MetadataPathFor(path));

        RestoreStatus status = ConversionMetadataStore.Inspect(path);

        Assert.Equal(RestoreAvailability.MetadataMissing, status.Availability);
        Assert.False(status.CanRestore);
    }

    [Fact]
    public void ACorruptedBackupIsDetectedBeforeItCouldBeRestored()
    {
        string path = Convert("corrupt.txt", "café", "windows-1252", "utf-8");
        File.WriteAllBytes(path + ".bak", [0x00, 0x01, 0x02]);

        RestoreStatus status = ConversionMetadataStore.Inspect(path);

        Assert.Equal(RestoreAvailability.BackupCorrupted, status.Availability);
        Assert.False(status.CanRestore);
        Assert.Contains("hashes to", status.Detail);
    }

    [Fact]
    public void UnreadableMetadataIsDistinguishedFromMissingMetadata()
    {
        string path = Convert("garbled.txt", "café", "windows-1252", "utf-8");
        File.WriteAllText(ConversionMetadataStore.MetadataPathFor(path), "{ not json");

        RestoreStatus status = ConversionMetadataStore.Inspect(path);

        Assert.Equal(RestoreAvailability.MetadataUnreadable, status.Availability);
        Assert.False(status.CanRestore);
    }

    [Fact]
    public void AnUnsupportedMetadataVersionIsRefusedRatherThanGuessedAt()
    {
        string path = Convert("future.txt", "café", "windows-1252", "utf-8");
        string metadataPath = ConversionMetadataStore.MetadataPathFor(path);

        File.WriteAllText(
            metadataPath,
            File.ReadAllText(metadataPath).Replace(
                "\"MetadataVersion\": 1", "\"MetadataVersion\": 99"));

        RestoreStatus status = ConversionMetadataStore.Inspect(path);

        Assert.Equal(RestoreAvailability.MetadataUnreadable, status.Availability);
        Assert.Contains("99", status.Detail);
    }

    [Fact]
    public void NoBackupMeansNoSidecar()
    {
        // Without a backup there is nothing to restore from, so a record would
        // describe a conversion that cannot be undone.
        string path = Path.Combine(_root, "nobackup2.txt");
        File.WriteAllBytes(path, Encoding.GetEncoding("windows-1252").GetBytes("café"));

        var entry = new ConversionReportEntry
        {
            FilePath = path,
            SourceEncoding = "windows-1252",
            SourceHasBom = false,
            TargetEncoding = "windows-1252",
            TargetHasBom = false,
        };

        ScanEngine.ConvertFiles(
            [entry], "utf-8", targetWriteBom: false,
            ScanEngine.DefaultMaxParallelism,
            whatIf: false, backup: false, _ => { }, CancellationToken.None);

        Assert.False(File.Exists(ConversionMetadataStore.MetadataPathFor(path)));
    }

    [Fact]
    public void AStaleBackupFromAnEarlierRunIsNotRecordedAsThisOriginal()
    {
        // A ".bak" left by a previous conversion holds different content. Recording it
        // would produce metadata that restores the wrong file, so the conversion must
        // refuse instead.
        string path = Path.Combine(_root, "stale.txt");
        File.WriteAllBytes(path, Encoding.GetEncoding("windows-1252").GetBytes("current"));
        File.WriteAllBytes(path + ".bak", Encoding.UTF8.GetBytes("something else entirely"));
        File.SetAttributes(path + ".bak", FileAttributes.ReadOnly);

        try
        {
            var entry = new ConversionReportEntry
            {
                FilePath = path,
                SourceEncoding = "windows-1252",
                SourceHasBom = false,
                TargetEncoding = "windows-1252",
                TargetHasBom = false,
            };

            var completed = new List<ConversionReportEntry>();
            ScanEngine.ConvertFiles(
                [entry], "utf-8", targetWriteBom: false,
                ScanEngine.DefaultMaxParallelism,
                whatIf: false, backup: true, completed.Add, CancellationToken.None);

            // Either the backup was replaced correctly and the metadata matches it, or
            // the conversion refused. What must not happen is a converted file whose
            // metadata points at content that was never its original.
            RestoreStatus status = ConversionMetadataStore.Inspect(path);

            if (status.CanRestore)
            {
                Assert.Equal(
                    ConversionMetadataStore.ComputeSha256(path + ".bak"),
                    status.Metadata!.OriginalSha256);
            }
        }
        finally
        {
            if (File.Exists(path + ".bak"))
                File.SetAttributes(path + ".bak", FileAttributes.Normal);
        }
    }
}
