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
    /// The charset label the file actually has on disk once this tool has changed it,
    /// or <see langword="null"/> while <see cref="SourceEncoding"/>/<see cref="SourceHasBom"/>
    /// still describe it. A converted row keeps its original scan values for reporting, so
    /// without this a second conversion would decode the new file with the old encoding.
    /// Internal state; not included in CSV output.
    /// </summary>
    internal string? CurrentCharsetLabel { get; set; }

    /// <summary>
    /// How far this file's bytes identify the encoding that wrote them, or
    /// <see langword="null"/> while nothing has looked.
    /// Internal state; not included in CSV output.
    /// </summary>
    /// <remarks>
    /// Nullable because "not classified" and "classified as unambiguous" are different
    /// facts, and defaulting the first to the second is how the GUI came to convert files
    /// the CLI refuses: its entries were never classified, and the gate read the default
    /// as an answer. A missing classification is an internal error, never a safe state.
    /// </remarks>
    internal AmbiguityClass? Ambiguity { get; set; }

    /// <summary>
    /// Encodings that read this file differently from the one detected. Empty unless
    /// <see cref="Ambiguity"/> is <see cref="AmbiguityClass.TextChanging"/>.
    /// Internal state; not included in CSV output.
    /// </summary>
    internal IReadOnlyList<string> CompetingEncodings { get; set; } = [];

    /// <summary>
    /// Why this file received its <see cref="Ambiguity"/> classification.
    /// Internal state; not included in CSV output.
    /// </summary>
    internal AmbiguityReason AmbiguityReason { get; set; } =
        AmbiguityReason.ExplicitlySpecified;

    /// <summary>
    /// Whether the source encoding was chosen by the caller rather than detected.
    /// Recorded because the two are different claims: detection can be wrong in ways an
    /// explicit choice cannot, and a later journal should be able to say which was used.
    /// Internal state; not included in CSV output.
    /// </summary>
    internal bool SourceEncodingWasSpecified { get; set; }

    /// <summary>
    /// The SHA-256 this file is required to still have when it is installed, or
    /// <see langword="null"/> when nothing earlier committed to its contents. Set when
    /// the conversion was approved in advance by a plan.
    /// Internal state; not included in CSV output.
    /// </summary>
    internal string? ExpectedSourceSha256 { get; set; }

    /// <summary>
    /// What <see cref="ConversionPolicy"/> decided for this file, or
    /// <see langword="null"/> while nothing has decided yet.
    /// Internal state; not included in CSV output.
    /// </summary>
    /// <remarks>
    /// Nullable so that "nobody has decided" cannot be mistaken for "convert it". An
    /// entry that reaches a plan undecided is a bug, and one that used to exist: the GUI
    /// built conversions from entries whose ambiguity had never been classified.
    /// </remarks>
    internal PlannedAction? Action { get; set; }

    /// <summary>
    /// Whether converting this file could change its Unicode content rather than only
    /// its encoding label.
    /// </summary>
    internal bool MayChangeText() => Ambiguity == AmbiguityClass.TextChanging;

    /// <summary>
    /// The charset label the next conversion will read this file as.
    /// </summary>
    /// <remarks>
    /// <see cref="CurrentCharsetLabel"/> when something has overridden or superseded the
    /// original detection - a completed conversion, or a user naming the source encoding
    /// - and the scan's own answer otherwise. Both the conversion engine and a written
    /// plan use this, so what a plan says a file will be read as is what it is read as.
    /// </remarks>
    internal string EffectiveSourceLabel =>
        CurrentCharsetLabel
        ?? ScanEngine.FormatCharsetLabel(SourceEncoding, SourceHasBom);

    /// <summary>
    /// The charset label this run actually read the file as, recorded when it was read.
    /// </summary>
    /// <remarks>
    /// Not derivable afterwards. A completed conversion sets
    /// <see cref="CurrentCharsetLabel"/> to the target so a second pass reads the new
    /// bytes correctly, which means that by the time a journal is written the effective
    /// label describes what the file now is, not what it was read as.
    /// </remarks>
    internal string? ResolvedSourceLabel { get; set; }

    /// <summary>
    /// Whether to record this file's bytes before converting it, for the journal.
    /// </summary>
    /// <remarks>
    /// Off unless a journal was asked for. A conversion overwrites the file, so its
    /// original hash cannot be recovered afterwards - but hashing every source on every
    /// run would charge an extra full read to the runs that never wanted one.
    /// </remarks>
    internal bool CaptureSourceHash { get; set; }

    /// <summary>
    /// The file's bytes before this run touched it, when they were recorded.
    /// Internal state; not included in CSV output.
    /// </summary>
    internal string? JournalSourceSha256 { get; set; }

    /// <summary>Additional error detail; not included in CSV output.</summary>
    internal string? Diagnostic { get; set; }
}

/// <summary>Writes the application's standard comma-delimited CSV report.</summary>
internal static class ConversionReport
{
    private const char DELIMITER = ',';

    internal static void WriteCsv(
        IEnumerable<ConversionReportEntry> entries,
        TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(writer);

        // The target BOM column is "TargetBOM", not a second "BOM": duplicate header names
        // make the report unparseable by strict CSV consumers (PowerShell's Import-Csv
        // fails outright with "The member BOM is already present").
        writer.WriteLine("File,Encoding,BOM,Target,TargetBOM,Result");

        foreach (ConversionReportEntry entry in entries)
        {
            WriteField(writer, entry.FilePath);
            writer.Write(DELIMITER);

            WriteField(writer, entry.SourceEncoding);
            writer.Write(DELIMITER);

            WriteField(writer, entry.SourceHasBom ? "Yes" : "No");
            writer.Write(DELIMITER);

            WriteField(writer, entry.TargetEncoding);
            writer.Write(DELIMITER);

            WriteField(writer, entry.TargetHasBom ? "Yes" : "No");
            writer.Write(DELIMITER);

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

        if (value.IndexOfAny([DELIMITER, '"', '\r', '\n']) < 0)
        {
            writer.Write(value);
            return;
        }

        writer.Write('"');
        writer.Write(value.Replace("\"", "\"\""));
        writer.Write('"');
    }
}
