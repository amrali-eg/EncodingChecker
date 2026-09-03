using System.Collections.Concurrent;
using System.Security.Cryptography;
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
            // what -From means. Without saying so, the legacy safety rule correctly
            // refuses detected legacy content until its source is specified.
            SourceEncodingWasSpecified = true,
        };

        ScanEngine.ConvertFiles(
            [entry], target, targetWriteBom: false,
            ScanEngine.DefaultMaxParallelism,
            whatIf: false, backup: true, _ => { }, CancellationToken.None);

        return path;
    }

    [Fact]
    public void TheSidecarKeepsNonAsciiNamesAndApostrophesReadable()
    {
        string path = Convert(
            "عربي-l'été.txt", "café — naïve", "windows-1252", "utf-8");

    // These files are read by a person recovering from a bad run, so the names have to
    // survive as names. The default encoder escapes every non-ASCII character and the
    // apostrophe too, turning the text EC exists to convert into \uXXXX.
    //
    // The assertion is on the raw bytes on disk, not on a deserialized value: the round
    // trip succeeds either way, so only the file itself shows whether it is readable.
        string json = File.ReadAllText(ConversionMetadataStore.MetadataPathFor(path));

        Assert.Contains("عربي-l'été.txt", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", json, StringComparison.Ordinal);
    }

    [Fact]
    public void AConversionWithBackupWritesASidecarDescribingIt()
    {
        string path = Convert("described.txt", "café — naïve", "windows-1252", "utf-8");

        string metadataPath = ConversionMetadataStore.MetadataPathFor(path);
        Assert.True(File.Exists(metadataPath), "no sidecar was written");

        var metadata = JsonSerializer.Deserialize<ConversionMetadata>(
            File.ReadAllText(metadataPath))!;

        Assert.Equal(3, metadata.MetadataVersion);
        Assert.Equal(ConversionInstallationState.Completed, metadata.InstallationState);
        Assert.Equal(
            ConversionMetadataStore.ComputeSha256(path),
            metadata.ExpectedOutputSha256);

        // The recovery key is the codec actually used. This conversion used an
        // explicit source, so it deliberately has no detector provenance.
        Assert.Equal(1252, metadata.SourceEncodingId);
        Assert.Equal("windows-1252", metadata.SourceEncodingName);
        Assert.Equal(SourceEncodingMode.Explicit, metadata.SourceEncodingMode);
        Assert.False(metadata.SourceHasBom);
        Assert.Null(metadata.DetectedEncodingId);
        Assert.Null(metadata.DetectedEncodingName);
        Assert.Equal(65001, metadata.TargetEncodingId);

        // The recorded hash must actually be the backup's.
        Assert.Equal(
            ConversionMetadataStore.ComputeSha256(path + ".bak"),
            metadata.BackupSha256);

        // And the backup must be the original, not merely some file.
        Assert.Equal(metadata.OriginalSha256, metadata.BackupSha256);

        Assert.NotEmpty(metadata.ConversionId);
        Assert.NotEmpty(metadata.ConversionTimestampUtc);
        Assert.NotEmpty(metadata.EcVersion);
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
        string converted = Encoding.GetEncoding(metadata.TargetEncodingId)
            .GetString(File.ReadAllBytes(path));

        byte[] reconstructed = Encoding.GetEncoding(metadata.SourceEncodingId)
            .GetBytes(converted);

        Assert.Equal(File.ReadAllBytes(path + ".bak"), reconstructed);
        Assert.Equal(text, Encoding.GetEncoding(metadata.SourceEncodingId)
            .GetString(reconstructed));
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
    public void RecoveryHashesRefuseAnUnavailableSourceHash()
    {
        string? error = ConversionMetadataStore.ValidateRecoveryHashes(
            sourceSha256: string.Empty,
            backupSha256: new string('a', 64),
            backupPath: "example.txt.bak");

        Assert.NotNull(error);
        Assert.Contains("source file could not be hashed", error);
    }

    [Fact]
    public void RecoveryHashesAcceptOnlyAnExactBackup()
    {
        string hash = new('a', 64);

        Assert.Null(ConversionMetadataStore.ValidateRecoveryHashes(
            hash, hash.ToUpperInvariant(), "example.txt.bak"));

        Assert.Contains("does not match", ConversionMetadataStore.ValidateRecoveryHashes(
            hash, new string('b', 64), "example.txt.bak"));
    }

    [Fact]
    public void FailedInstallationLeavesATruthfulPreparedRecord()
    {
        const string text = "café";
        string path = Path.Combine(_root, "locked.txt");
        byte[] original = Encoding.GetEncoding("windows-1252").GetBytes(text);
        File.WriteAllBytes(path, original);

        // Allow reads and backup creation but deny replacement. This reaches the real
        // failure window after the sidecar is prepared and before output installation.
        using FileStream locked = new(
            path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var entries = new ConcurrentBag<ConversionReportEntry>();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                IncludeSubdirectories = true,
                IncludePatterns = ["locked.txt"],
                Action = ScanAction.Convert,
                SourceCharset = "windows-1252",
                TargetCharset = "utf-8",
                TargetWriteBom = false,
                Backup = true,
            },
            entries.Add,
            CancellationToken.None);

        ConversionReportEntry entry = Assert.Single(entries);
        Assert.Equal(ConversionRowResult.Error, entry.Result);
        Assert.Equal(original, File.ReadAllBytes(path));

        var metadata = JsonSerializer.Deserialize<ConversionMetadata>(
            File.ReadAllText(ConversionMetadataStore.MetadataPathFor(path)))!;

        Assert.Equal(ConversionInstallationState.Prepared, metadata.InstallationState);
        Assert.Equal(metadata.OriginalSha256, ConversionMetadataStore.ComputeSha256(path));
        Assert.Equal(
            System.Convert.ToHexStringLower(
                SHA256.HashData(new UTF8Encoding(false).GetBytes(text))),
            metadata.ExpectedOutputSha256);
        Assert.NotEqual(metadata.OriginalSha256, metadata.ExpectedOutputSha256);
    }

    [Fact]
    public void FailedSidecarUpdatePreservesThePreviousValidRecord()
    {
        const string text = "café";
        string path = Path.Combine(_root, "sidecar-locked.txt");
        File.WriteAllBytes(path, Encoding.GetEncoding("windows-1252").GetBytes(text));

        var entries = new ConcurrentBag<ConversionReportEntry>();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                IncludeSubdirectories = true,
                IncludePatterns = ["sidecar-locked.txt"],
                Action = ScanAction.Convert,
                SourceCharset = "windows-1252",
                TargetCharset = "utf-8",
                TargetWriteBom = false,
                Backup = true,
            },
            entries.Add,
            CancellationToken.None);

        Assert.Equal(ConversionRowResult.Converted, Assert.Single(entries).Result);

        string metadataPath = ConversionMetadataStore.MetadataPathFor(path);
        var completed = JsonSerializer.Deserialize<ConversionMetadata>(
            File.ReadAllBytes(metadataPath))!;
        ConversionMetadata prepared = completed with
        {
            InstallationState = ConversionInstallationState.Prepared,
        };

        Assert.Null(ConversionMetadataStore.Write(path, prepared));
        byte[] validPreparedRecord = File.ReadAllBytes(metadataPath);

        using FileStream locked = new(
            metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        string? error = ConversionMetadataStore.Write(path, completed);

        Assert.NotNull(error);
        Assert.Equal(validPreparedRecord, File.ReadAllBytes(metadataPath));
        Assert.Equal(
            ConversionInstallationState.Prepared,
            JsonSerializer.Deserialize<ConversionMetadata>(validPreparedRecord)!
                .InstallationState);
    }

}
