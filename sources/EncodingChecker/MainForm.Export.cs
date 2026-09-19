using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace EncodingChecker;

public partial class MainForm
{
    private void OnExport(object? sender, EventArgs e)
    {
        if (lstResults.CheckedItems.Count == 0)
        {
            ShowWarning("Select one or more files to export");
            return;
        }

        ExportToFile(
            title: @"Export to a Text File",
            filter: @"Text files (*.txt)|*.txt",
            defaultFileName: "Encoding.txt",
            encoding: TextExportEncoding,
            failureMessage: "Failed to export the report: {0}",
            write: writer => WriteTextExport(
                lstResults.CheckedItems.Cast<ListViewItem>().Select(item =>
                (
                    item.SubItems[ResultsColumnCharset].Text,
                    item.SubItems[ResultsColumnDirectory].Text,
                    item.SubItems[ResultsColumnFileName].Text
                )),
                writer));
    }

    /// <summary>UTF-8 with a BOM, so the text list opens correctly in Notepad.</summary>
    internal static readonly Encoding TextExportEncoding = new UTF8Encoding(true);

    /// <summary>One tab-separated line per file: its charset, then its full path.</summary>
    internal static void WriteTextExport(
        IEnumerable<(string Charset, string Directory, string FileName)> rows,
        TextWriter writer)
    {
        foreach ((string charset, string directory, string fileName) in rows)
            writer.WriteLine("{0}\t{1}\\{2}", charset, directory, fileName);
    }

    /// <summary>
    /// Shared shape for the text and CSV exports. The journal export is deliberately not
    /// routed through this: its Save method already writes atomically and reports failure by
    /// returning an error string.
    /// </summary>
    private void ExportToFile(
        string title,
        string filter,
        string defaultFileName,
        Encoding encoding,
        string failureMessage,
        Action<StreamWriter> write)
    {
        using var saveFileDialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = defaultFileName,
            RestoreDirectory = true,
        };

        if (saveFileDialog.ShowDialog(this) != DialogResult.OK)
            return;

        string? error = WriteExportFile(saveFileDialog.FileName, encoding, write);

        if (error is not null)
            ShowWarning(failureMessage, error);
    }

    /// <summary>
    /// Writes an export without replacing an existing report until the new one is complete,
    /// so a failed write leaves the previous report as it was.
    /// </summary>
    /// <remarks>
    /// A read-only report or a link is refused rather than replaced: the atomic install would
    /// clear the read-only flag or swap the link for a regular file, where a direct write
    /// would have failed or written through. A fault of an argument or invalid-operation kind
    /// raised by <paramref name="write"/> is reported as a failed export.
    /// </remarks>
    /// <returns><see langword="null"/> on success; otherwise, why the write failed.</returns>
    internal static string? WriteExportFile(
        string path, Encoding encoding, Action<StreamWriter> write)
    {
        if (RefusalForExistingDestination(path) is { } refusal)
            return refusal;

        return AtomicArtifactFile.Write(path, stream =>
        {
            using var writer = new StreamWriter(stream, encoding, leaveOpen: true);
            write(writer);
        });
    }

    private static string? RefusalForExistingDestination(string path)
    {
        FileAttributes attributes;

        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or ArgumentException
                or NotSupportedException)
        {
            // Not there yet, or not readable; the write reports its own failure.
            return null;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
            return $"'{path}' is a link. Choose a regular file for the report.";

        if ((attributes & FileAttributes.ReadOnly) != 0)
            return $"'{path}' is read-only.";

        return null;
    }

    private void OnExportResultsOpening(object? sender, EventArgs e)
    {
        _exportText.Enabled = lstResults.CheckedItems.Count > 0;
        _exportCsv.Enabled = lstResults.Items.Count > 0;
        _exportJournal.Enabled = _lastConversionJournal is not null;
        _exportJournal.ToolTipText = _exportJournal.Enabled
            ? "Save the most recent conversion's decisions and outcomes as JSON."
            : "Available after a conversion has run.";
    }

    private List<ConversionReportEntry> ResultEntries()
    {
        var entries = new List<ConversionReportEntry>(lstResults.Items.Count);

        foreach (ListViewItem item in lstResults.Items)
            entries.Add((ConversionReportEntry)item.Tag!);

        return entries;
    }

    private void OnExportCsvReport(object? sender, EventArgs e)
    {
        if (lstResults.Items.Count == 0)
        {
            ShowWarning("There are no results to export");
            return;
        }

        ExportToFile(
            title: @"Export Results as CSV",
            filter: @"CSV files (*.csv)|*.csv",
            defaultFileName: "EncodingChecker report.csv",
            encoding: ConversionReport.CsvFileEncoding,
            failureMessage: "Failed to export the csv report: {0}",
            write: writer => ConversionReport.WriteCsv(ResultEntries(), writer));
    }

    private void OnExportJournal(object? sender, EventArgs e)
    {
        if (_lastConversionJournal is null)
            return;

        using var saveFileDialog = new SaveFileDialog
        {
            Title = @"Export Conversion Journal",
            Filter = @"JSON files (*.json)|*.json",
            FileName = "EncodingChecker conversion journal.json",
            RestoreDirectory = true,
        };

        if (saveFileDialog.ShowDialog(this) != DialogResult.OK)
            return;

        string? error = _lastConversionJournal.Save(saveFileDialog.FileName);

        if (error is not null)
            ShowWarning("Failed to export the journal: {0}", error);
    }
}
