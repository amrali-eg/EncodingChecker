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

    internal required string SourceEncoding { get; init; }

    internal bool SourceHasBom { get; init; }

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
    /// The SHA-256 this file must still have when installed, or <see langword="null"/>
    /// when no plan has committed to its contents. Internal state; not in CSV.
    /// </summary>
    internal string? ExpectedSourceSha256 { get; set; }

    /// <summary>
    /// What <see cref="ConversionPolicy"/> decided, or <see langword="null"/> before a
    /// decision. Internal state; not in CSV.
    /// </summary>
    /// <remarks>
    /// Null prevents an undecided entry from being mistaken for permission to convert.
    /// </remarks>
    internal PlannedAction? Action { get; set; }

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

    /// <summary>Additional error detail; not in CSV.</summary>
    internal string? Diagnostic { get; set; }
}

/// <summary>Writes the application's standard comma-delimited CSV report.</summary>
internal static class ConversionReport
{
    private const char Delimiter = ',';

    internal static void WriteCsv(
        IEnumerable<ConversionReportEntry> entries,
        TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(writer);

        // Keep header names unique so strict CSV readers can import the report.
        writer.WriteLine("File,Encoding,BOM,Target,TargetBOM,Result");

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

            writer.WriteLine(entry.Result);
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
