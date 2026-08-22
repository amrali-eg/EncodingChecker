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

        writer.WriteLine("File,Encoding,BOM,Target,BOM,Result");

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
