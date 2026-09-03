using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EncodingChecker;

/// <summary>
/// The sidecar written next to a backup, recording how to reverse a conversion.
/// </summary>
/// <remarks>
/// The sidecar preserves the source codec needed for recovery independently of the
/// conversion report.
/// <para>
/// It is a separate JSON file rather than an NTFS alternate data stream so it survives
/// normal copying, archiving, and cloud synchronization.
///</para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum SourceEncodingMode
{
    /// <summary>EC determined it from the file's bytes.</summary>
    Detected,

    /// <summary>The user supplied it explicitly.</summary>
    Explicit,
}

/// <summary>Whether verified output is prepared or installed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum ConversionInstallationState
{
    /// <summary>The output passed verification but installation is not yet recorded.</summary>
    Prepared,

    /// <summary>The verified output was installed.</summary>
    Completed,
}

internal sealed record ConversionMetadata
{
    /// <summary>The schema version written and understood by this build.</summary>
    internal const int CurrentMetadataVersion = 3;

    /// <summary>Schema version for future readers.</summary>
    [JsonPropertyOrder(0)]
    public int MetadataVersion { get; init; } = CurrentMetadataVersion;

    [JsonPropertyOrder(1)]
    public required string ConversionId { get; init; }

    [JsonPropertyOrder(2)]
    public required string ConversionTimestampUtc { get; init; }

    [JsonPropertyOrder(3)]
    public required string EcVersion { get; init; }

    [JsonPropertyOrder(4)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required ConversionInstallationState InstallationState { get; init; }

    [JsonPropertyOrder(5)]
    public required string OriginalPath { get; init; }

    [JsonPropertyOrder(6)]
    public required long OriginalSize { get; init; }

    [JsonPropertyOrder(7)]
    public required string OriginalSha256 { get; init; }

    [JsonPropertyOrder(8)]
    public required string BackupPath { get; init; }

    [JsonPropertyOrder(9)]
    public required string BackupSha256 { get; init; }

    /// <summary>SHA-256 of the exact verified bytes prepared for installation.</summary>
    [JsonPropertyOrder(10)]
    public required string ExpectedOutputSha256 { get; init; }

    /// <summary>
    /// The codec that actually read the file. This is the authoritative recovery key.
    /// </summary>
    /// <remarks>
    /// The numeric identifier is authoritative because encoding names can have aliases.
    /// </remarks>
    [JsonPropertyOrder(11)]
    public required int SourceEncodingId { get; init; }

    /// <summary>The source codec's human-readable name.</summary>
    [JsonPropertyOrder(12)]
    public required string SourceEncodingName { get; init; }

    /// <summary>Whether the source codec was detected or explicitly supplied.</summary>
    [JsonPropertyOrder(13)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required SourceEncodingMode SourceEncodingMode { get; init; }

    [JsonPropertyOrder(14)]
    public required bool SourceHasBom { get; init; }

    /// <summary>
    /// What detection concluded, or <see langword="null"/> when detection did not run.
    /// </summary>
    /// <remarks>
    /// This is null only when detection did not run, such as a CLI <c>-From</c>
    /// conversion. A GUI user can explicitly choose a source after a scan; in that
    /// case the sidecar preserves both the detector's earlier conclusion and the
    /// codec the conversion actually used.
    /// </remarks>
    [JsonPropertyOrder(15)]
    public int? DetectedEncodingId { get; init; }

    /// <summary>The detected codec's name, or <see langword="null"/> when none exists.</summary>
    [JsonPropertyOrder(16)]
    public string? DetectedEncodingName { get; init; }

    /// <summary>The codec the file was converted to.</summary>
    [JsonPropertyOrder(17)]
    public required int TargetEncodingId { get; init; }

    [JsonPropertyOrder(18)]
    public required string TargetEncodingName { get; init; }

    [JsonPropertyOrder(19)]
    public required bool TargetHasBom { get; init; }

    [JsonPropertyOrder(20)]
    public required string SourceTextSha256 { get; init; }

    [JsonPropertyOrder(21)]
    public required string OutputTextSha256 { get; init; }

    [JsonPropertyOrder(22)]
    public required long UnicodeScalars { get; init; }
}

/// <summary>Reads and writes conversion sidecars.</summary>
internal static class ConversionMetadataStore
{
    internal const string Suffix = ".ecmeta.json";

    // These files exist to be read by a person recovering from a bad run. The
    // default encoder escapes every non-ASCII character, which turns the text EC
    // exists to handle into \uXXXX. Relaxed escaping is safe here because none of
    // this JSON is ever embedded in HTML.
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static string MetadataPathFor(string filePath) => filePath + Suffix;

    /// <summary>
    /// Removes the recovery record immediately before its fixed-name backup is replaced.
    /// </summary>
    /// <remarks>
    /// A record must not survive replacement of the backup it describes. Deletion
    /// failures propagate so conversion stops instead of leaving a misleading pair.
    /// </remarks>
    /// <exception cref="IOException">The record exists and could not be removed.</exception>
    /// <exception cref="UnauthorizedAccessException">The record could not be removed.</exception>
    internal static void RemoveBeforeBackupReplacement(string filePath)
    {
        string path = MetadataPathFor(filePath);

        if (!File.Exists(path))
            return;

        // Match the backup's own handling of a read-only destination.
        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
    }

    internal static string ComputeSha256(string path)
    {
        using FileStream stream = new(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sha = SHA256.Create();
        return Convert.ToHexStringLower(sha.ComputeHash(stream));
    }

    /// <summary>
    /// Proves that the recovery copy is the exact source used by the conversion.
    /// </summary>
    internal static string? ValidateRecoveryHashes(
        string sourceSha256,
        string backupSha256,
        string backupPath)
    {
        if (string.IsNullOrWhiteSpace(sourceSha256))
        {
            return "the source file could not be hashed, so the backup cannot be "
                   + "verified against the exact bytes used by the conversion.";
        }

        if (string.IsNullOrWhiteSpace(backupSha256))
            return $"the backup at '{backupPath}' could not be hashed.";

        if (!backupSha256.Equals(sourceSha256, StringComparison.OrdinalIgnoreCase))
        {
            return $"the backup at '{backupPath}' does not match the file being "
                   + "converted, so it is not a valid restore point.";
        }

        return null;
    }

    /// <summary>
    /// Writes the sidecar and verifies that it can be read back.
    /// </summary>
    /// <remarks>
    /// Read-back verification prevents a write that appeared successful from leaving an
    /// apparently reversible conversion with unusable metadata.
    /// </remarks>
    internal static string? Write(string filePath, ConversionMetadata metadata)
    {
        string path = MetadataPathFor(filePath);
        string tempPath =
            $"{path}.{Guid.NewGuid():N}.{EncodingConverter.TempFileSuffix}";

        try
        {
            File.WriteAllText(
                tempPath,
                JsonSerializer.Serialize(metadata, Options),
                new UTF8Encoding(false));

            ConversionMetadata? readBack =
                JsonSerializer.Deserialize<ConversionMetadata>(File.ReadAllText(tempPath));

            if (readBack is null)
                return $"The temporary recovery record for '{path}' could not be read back.";

            if (!MatchesForRecovery(readBack, metadata))
            {
                return $"The temporary recovery record for '{path}' does not describe "
                       + "the expected file.";
            }

            // Keep the previous truthful record intact unless the complete replacement succeeds.
            EncodingConverter.AtomicReplaceForBackup(tempPath, path);

            return null;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException or
                ArgumentException or NotSupportedException)
        {
            return $"{ex.Message}";
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Cleanup failure cannot make the existing sidecar less truthful.
            }
        }
    }

    /// <summary>
    /// Ensures the serialized sidecar still describes the exact conversion that passed
    /// the pre-install safety checks, rather than merely preserving its two hashes.
    /// </summary>
    private static bool MatchesForRecovery(
        ConversionMetadata actual,
        ConversionMetadata expected) =>
        actual.MetadataVersion == ConversionMetadata.CurrentMetadataVersion &&
        actual.ConversionId == expected.ConversionId &&
        actual.InstallationState == expected.InstallationState &&
        actual.OriginalPath == expected.OriginalPath &&
        actual.OriginalSize == expected.OriginalSize &&
        actual.OriginalSha256 == expected.OriginalSha256 &&
        actual.BackupPath == expected.BackupPath &&
        actual.BackupSha256 == expected.BackupSha256 &&
        actual.ExpectedOutputSha256 == expected.ExpectedOutputSha256 &&
        actual.SourceEncodingId == expected.SourceEncodingId &&
        actual.SourceEncodingName == expected.SourceEncodingName &&
        actual.SourceEncodingMode == expected.SourceEncodingMode &&
        actual.SourceHasBom == expected.SourceHasBom &&
        actual.DetectedEncodingId == expected.DetectedEncodingId &&
        actual.DetectedEncodingName == expected.DetectedEncodingName &&
        actual.TargetEncodingId == expected.TargetEncodingId &&
        actual.TargetEncodingName == expected.TargetEncodingName &&
        actual.TargetHasBom == expected.TargetHasBom &&
        actual.SourceTextSha256 == expected.SourceTextSha256 &&
        actual.OutputTextSha256 == expected.OutputTextSha256 &&
        actual.UnicodeScalars == expected.UnicodeScalars;

}
