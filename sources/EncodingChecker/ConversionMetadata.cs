using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EncodingChecker;

/// <summary>
/// The sidecar written next to a backup, recording how to reverse a conversion.
/// </summary>
/// <remarks>
/// A conversion is only reversible for someone who still knows which codec produced it,
/// and that knowledge previously existed solely in the conversion report - an artifact a
/// user running the GUI has no reason to keep. Recovery that depends on a file nobody
/// kept is not recovery.
/// <para>
/// Written as a plain JSON file rather than an NTFS alternate data stream: an ADS is lost
/// by ordinary copying, archiving and most cloud sync, which are exactly the operations
/// that separate a backup from its origin. A sidecar survives them and can be read
/// without EncodingChecker.
/// </para>
/// </remarks>
internal sealed record ConversionMetadata
{
    /// <summary>Schema version, so a later reader can tell what it is looking at.</summary>
    [JsonPropertyOrder(0)]
    public int MetadataVersion { get; init; } = 1;

    [JsonPropertyOrder(1)]
    public required string ConversionId { get; init; }

    [JsonPropertyOrder(2)]
    public required string ConversionTimestampUtc { get; init; }

    [JsonPropertyOrder(3)]
    public required string ECVersion { get; init; }

    [JsonPropertyOrder(4)]
    public required string OriginalPath { get; init; }

    [JsonPropertyOrder(5)]
    public required long OriginalSize { get; init; }

    [JsonPropertyOrder(6)]
    public required string OriginalSha256 { get; init; }

    [JsonPropertyOrder(7)]
    public required string BackupPath { get; init; }

    [JsonPropertyOrder(8)]
    public required string BackupSha256 { get; init; }

    [JsonPropertyOrder(9)]
    public required string DetectedEncoding { get; init; }

    /// <summary>
    /// The code page identifies the codec where a name may not: "cp949" and
    /// "ks_c_5601-1987" are one encoding, and only the number says so unambiguously.
    /// </summary>
    [JsonPropertyOrder(10)]
    public required int DetectedCodePage { get; init; }

    [JsonPropertyOrder(11)]
    public required bool DetectedBom { get; init; }

    [JsonPropertyOrder(12)]
    public required string TargetEncoding { get; init; }

    [JsonPropertyOrder(13)]
    public required int TargetCodePage { get; init; }

    [JsonPropertyOrder(14)]
    public required bool TargetBom { get; init; }

    [JsonPropertyOrder(15)]
    public required string SourceTextSha256 { get; init; }

    [JsonPropertyOrder(16)]
    public required string OutputTextSha256 { get; init; }

    [JsonPropertyOrder(17)]
    public required long UnicodeScalars { get; init; }
}

/// <summary>Whether a converted file can be restored from what is on disk.</summary>
internal enum RestoreAvailability
{
    /// <summary>Backup and metadata are present and agree.</summary>
    Available,

    /// <summary>No backup file exists.</summary>
    BackupMissing,

    /// <summary>A backup exists but no metadata describes it.</summary>
    MetadataMissing,

    /// <summary>Metadata exists but cannot be read or is not a version we understand.</summary>
    MetadataUnreadable,

    /// <summary>The backup's hash does not match what the metadata recorded.</summary>
    BackupCorrupted,
}

internal sealed record RestoreStatus
{
    internal required RestoreAvailability Availability { get; init; }

    internal ConversionMetadata? Metadata { get; init; }

    internal string? Detail { get; init; }

    internal bool CanRestore => Availability == RestoreAvailability.Available;
}

/// <summary>Reads and writes the conversion sidecar.</summary>
internal static class ConversionMetadataStore
{
    internal const string Suffix = ".ecmeta.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    internal static string MetadataPathFor(string filePath) => filePath + Suffix;

    internal static string ComputeSha256(string path)
    {
        using FileStream stream = new(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sha = SHA256.Create();
        return Convert.ToHexStringLower(sha.ComputeHash(stream));
    }

    /// <summary>
    /// Writes the sidecar and reads it back, returning <see langword="null"/> on success
    /// or a message describing the failure.
    /// </summary>
    /// <remarks>
    /// Read back deliberately. A write that appeared to succeed but produced a file that
    /// cannot be parsed would leave the caller believing the conversion is reversible
    /// when it is not, which is the failure this whole mechanism exists to prevent.
    /// </remarks>
    internal static string? Write(string filePath, ConversionMetadata metadata)
    {
        string path = MetadataPathFor(filePath);

        try
        {
            File.WriteAllText(
                path, JsonSerializer.Serialize(metadata, Options), new UTF8Encoding(false));

            ConversionMetadata? readBack =
                JsonSerializer.Deserialize<ConversionMetadata>(File.ReadAllText(path));

            if (readBack is null)
                return $"'{path}' was written but could not be read back.";

            if (readBack.BackupSha256 != metadata.BackupSha256 ||
                readBack.OriginalSha256 != metadata.OriginalSha256)
            {
                return $"'{path}' was written but does not describe the expected file.";
            }

            return null;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return $"{ex.Message}";
        }
    }

    /// <summary>
    /// Determines whether <paramref name="filePath"/> can be restored, verifying the
    /// backup against the recorded hash rather than trusting that a ".bak" exists.
    /// </summary>
    internal static RestoreStatus Inspect(string filePath)
    {
        string backupPath = filePath + ".bak";
        string metadataPath = MetadataPathFor(filePath);

        if (!File.Exists(backupPath))
        {
            return new RestoreStatus
            {
                Availability = RestoreAvailability.BackupMissing,
                Detail = $"No backup at '{backupPath}'.",
            };
        }

        if (!File.Exists(metadataPath))
        {
            return new RestoreStatus
            {
                Availability = RestoreAvailability.MetadataMissing,
                Detail =
                    $"A backup exists at '{backupPath}' but no '{Suffix}' describes it, "
                    + "so the encoding it was converted from is unknown.",
            };
        }

        ConversionMetadata? metadata;

        try
        {
            metadata = JsonSerializer.Deserialize<ConversionMetadata>(
                File.ReadAllText(metadataPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new RestoreStatus
            {
                Availability = RestoreAvailability.MetadataUnreadable,
                Detail = ex.Message,
            };
        }

        if (metadata is null || metadata.MetadataVersion != 1)
        {
            return new RestoreStatus
            {
                Availability = RestoreAvailability.MetadataUnreadable,
                Detail = metadata is null
                    ? "The metadata file is empty."
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        "Metadata version {0} is not supported.",
                        metadata.MetadataVersion),
            };
        }

        string actual;

        try
        {
            actual = ComputeSha256(backupPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new RestoreStatus
            {
                Availability = RestoreAvailability.BackupCorrupted,
                Metadata = metadata,
                Detail = ex.Message,
            };
        }

        // Both comparisons matter and they are not the same check. The first says the
        // backup is the file the metadata describes; the second says that file is the
        // original. A backup that matches neither is not a restore candidate.
        if (actual != metadata.BackupSha256 || actual != metadata.OriginalSha256)
        {
            return new RestoreStatus
            {
                Availability = RestoreAvailability.BackupCorrupted,
                Metadata = metadata,
                Detail =
                    $"The backup hashes to {actual}, but the record expects "
                    + $"{metadata.BackupSha256} (original {metadata.OriginalSha256}).",
            };
        }

        return new RestoreStatus
        {
            Availability = RestoreAvailability.Available,
            Metadata = metadata,
        };
    }
}
