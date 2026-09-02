using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EncodingChecker;

/// <summary>Processing outcome for one file.</summary>
internal enum ConversionRowResult
{
    /// <summary>No change was needed.</summary>
    Unchanged,

    /// <summary>Encoding could not be determined, so no action was attempted.</summary>
    Skipped,

    /// <summary>Conversion was refused because EC could not prove it was safe.</summary>
    Refused,

    /// <summary>Successfully converted, or would be under a dry run.</summary>
    Converted,

    /// <summary>Validate mode: encoding is not in the accepted set.</summary>
    Invalid,

    /// <summary>Processing failed.</summary>
    Error,
}

/// <summary>Encoding detection/conversion result for one file.</summary>
internal sealed class ConversionReportEntry
{
    internal required string FilePath { get; init; }

    internal required string SourceEncoding { get; set; }

    internal bool SourceHasBom { get; set; }

    internal required string TargetEncoding { get; set; }

    internal bool TargetHasBom { get; set; }

    internal ConversionRowResult Result { get; set; }

    /// <summary>
    /// The charset label currently describing the file on disk, or <see langword="null"/>
    /// while the original source fields still describe it. Internal state; not in CSV.
    /// </summary>
    internal string? CurrentCharsetLabel { get; set; }

    /// <summary>
    /// The source interpretation used by the conversion policy, or <see langword="null"/>
    /// before the entry has been decided. Internal state; not in CSV.
    /// </summary>
    internal SourceInterpretation? SourceInterpretation { get; set; }

    /// <summary>
    /// Whether the source encoding was explicitly supplied rather than detected.
    /// Internal state; not in CSV.
    /// </summary>
    internal bool SourceEncodingWasSpecified { get; set; }

    /// <summary>
    /// Whether automatic detection was confirmed by a complete strict Unicode decode.
    /// Internal state; not in CSV.
    /// </summary>
    internal bool HasReliableUnicodeDetection { get; set; }

    /// <summary>Whether the automatically detected Unicode encoding had a BOM.</summary>
    internal bool DetectedEncodingHasBom { get; set; }

    /// <summary>
    /// Whether a detected BOM-less UTF-16 source also strictly decodes under the opposite
    /// byte order. Internal planning state; not in CSV.
    /// </summary>
    internal bool HasAmbiguousBomlessUtf16 { get; set; }

    /// <summary>
    /// The SHA-256 this file must still have when installed, or <see langword="null"/>
    /// when no plan has committed to its contents. Internal state; not in CSV.
    /// </summary>
    internal string? ExpectedSourceSha256 { get; set; }

    /// <summary>The size paired with <see cref="ExpectedSourceSha256"/>.</summary>
    internal long? ExpectedSourceSize { get; set; }

    /// <summary>
    /// What <see cref="ConversionPolicy"/> decided, or <see langword="null"/> before a
    /// decision. Internal state; not in CSV.
    /// </summary>
    /// <remarks>
    /// Null prevents an undecided entry from being mistaken for permission to convert.
    /// </remarks>
    internal PlannedAction? Action { get; set; }

    /// <summary>
    /// What a reviewed plan decided for this file, or <see langword="null"/> when this
    /// run is not carrying out a saved plan. Internal state; not in CSV.
    /// </summary>
    /// <remarks>
    /// Present only for <c>-Apply</c>. The GUI re-decides in memory and deliberately
    /// clears <see cref="Action"/> so that supplying a source can turn a refusal into a
    /// conversion; a saved plan has no such conversation, so its refusals bind.
    /// </remarks>
    internal ApprovedDecision? Approved { get; set; }

    /// <summary>
    /// The charset label the next conversion will use to read this file.
    /// </summary>
    /// <remarks>
    /// Uses an explicit or updated label when one exists; otherwise uses the original
    /// scan result. This keeps planning and conversion aligned.
    /// </remarks>
    internal string EffectiveSourceLabel =>
        CurrentCharsetLabel
        ?? ScanEngine.FormatCharsetLabel(SourceEncoding, SourceHasBom);

    /// <summary>
    /// What detection concluded, or <see langword="null"/> when it did not run.
    /// </summary>
    /// <remarks>
    /// Kept separate from <see cref="SourceEncoding"/> so an explicit source choice is
    /// not reported as though it had been detected.
    /// </remarks>
    internal string? DetectedEncodingLabel { get; set; }

    /// <summary>
    /// The charset label actually used to read the file during this run.
    /// </summary>
    /// <remarks>
    /// Captured at read time because a completed conversion may later change
    /// <see cref="CurrentCharsetLabel"/> to the target encoding.
    /// </remarks>
    internal string? ResolvedSourceLabel { get; set; }

    /// <summary>
    /// Whether to capture the original bytes' hash for the journal.
    /// </summary>
    /// <remarks>
    /// Disabled unless journaling is requested to avoid an unnecessary full read.
    /// </remarks>
    internal bool CaptureSourceHash { get; set; }

    /// <summary>
    /// The original bytes' SHA-256 captured before this run changed them. Internal state;
    /// not in CSV.
    /// </summary>
    internal string? JournalSourceSha256 { get; set; }

    /// <summary>Human-readable detail written to reports and journals.</summary>
    internal string? Diagnostic { get; set; }

    /// <summary>Stable machine-readable reason for a non-success outcome.</summary>
    internal string? ReasonCode { get; set; }

    /// <summary>
    /// Whether the converted file reached its destination, or <see langword="null"/>
    /// when the outcome is unknown or nothing was attempted.
    /// </summary>
    /// <remarks>
    /// Conversion can fail after installation succeeds - completing the recovery record
    /// or restoring attributes. Without this the journal reported such a file as
    /// untouched, which is the opposite of what happened.
    /// </remarks>
    internal bool? ReplacementCommitted { get; set; }

    /// <summary>
    /// SHA-256 of the exact bytes verified before installation, when a recovery record
    /// was written. Internal state; not in CSV.
    /// </summary>
    /// <remarks>
    /// Preferred over re-reading the file afterwards, which records whatever is on disk
    /// at journal time rather than what this run verified and installed.
    /// </remarks>
    internal string? OutputSha256 { get; set; }

    /// <summary>The backup created by this run, even if conversion later failed.</summary>
    internal string? BackupPath { get; set; }

    /// <summary>
    /// The recovery sidecar prepared by this run. Its state says whether installation completed.
    /// </summary>
    internal string? RecoveryMetadataPath { get; set; }
}

/// <summary>Stable reason codes written to reports and journals.</summary>
internal static class ConversionReasonCodes
{
    internal const string UnknownEncoding = nameof(UnknownEncoding);
    internal const string UnsupportedSourceEncoding = nameof(UnsupportedSourceEncoding);
    internal const string LegacySourceRequired = nameof(LegacySourceRequired);
    internal const string ExplicitSourceConflictsWithDetection =
        nameof(ExplicitSourceConflictsWithDetection);
    internal const string ExplicitSourceDiffersFromBomlessUnicodeEstimate =
        nameof(ExplicitSourceDiffersFromBomlessUnicodeEstimate);
    /// <summary>
    /// An explicit source was supplied for a file whose BOM-less UTF-16/32 byte order
    /// could not be established from its bytes, and it agrees with EC's estimate.
    /// Distinct from <see cref="ExplicitSourceDiffersFromBomlessUnicodeEstimate"/>:
    /// agreeing with an estimate EC has already declared unprovable is not evidence,
    /// so the caller is told the order was taken on trust either way.
    /// </summary>
    internal const string ExplicitSourceOnUnprovableBomlessUnicode =
        nameof(ExplicitSourceOnUnprovableBomlessUnicode);
    internal const string AmbiguousBomlessUtf16 = BomlessUnicodeSafety.AmbiguousReasonCode;
    internal const string StrictValidationFailed = nameof(StrictValidationFailed);
    internal const string SourceSnapshotFailed = nameof(SourceSnapshotFailed);
    internal const string BackupFailed = nameof(BackupFailed);
    internal const string MultipleLeadingByteOrderMarks = nameof(MultipleLeadingByteOrderMarks);
    internal const string ScanFailed = nameof(ScanFailed);
}

/// <summary>Writes the application's standard comma-delimited CSV report.</summary>
internal static class ConversionReport
{
    /// <summary>UTF-8 with BOM so CSV reports open correctly in Excel.</summary>
    internal static readonly Encoding CsvFileEncoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: true);

    private const char Delimiter = ',';

    internal static void WriteCsv(
        IEnumerable<ConversionReportEntry> entries,
        TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(writer);

        // Keep header names unique so strict CSV readers can import the report.
        writer.WriteLine(
            "File,Encoding,BOM,Target,TargetBOM,Result,ReasonCode,Diagnostic");

        foreach (ConversionReportEntry entry in entries)
        {
            WriteField(writer, entry.FilePath);
            writer.Write(Delimiter);

            WriteField(writer, entry.SourceEncoding);
            writer.Write(Delimiter);

            WriteField(writer, entry.SourceHasBom ? "Yes" : "No");
            writer.Write(Delimiter);

            WriteField(writer, entry.TargetEncoding);
            writer.Write(Delimiter);

            WriteField(writer, entry.TargetHasBom ? "Yes" : "No");
            writer.Write(Delimiter);

            writer.Write(entry.Result);
            writer.Write(Delimiter);

            WriteField(writer, entry.ReasonCode);
            writer.Write(Delimiter);

            WriteField(writer, entry.Diagnostic);
            writer.WriteLine();
        }
    }

    internal static string ToCsvString(IEnumerable<ConversionReportEntry> entries)
    {
        var builder = new StringBuilder();

        using var writer = new StringWriter(builder);
        WriteCsv(entries, writer);

        return builder.ToString();
    }

    private static void WriteField(TextWriter writer, string? value)
    {
        value ??= string.Empty;

        if (value.IndexOfAny([Delimiter, '"', '\r', '\n']) < 0)
        {
            writer.Write(value);
            return;
        }

        writer.Write('"');
        writer.Write(value.Replace("\"", "\"\""));
        writer.Write('"');
    }
}
