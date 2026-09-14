using System;
using System.Collections.Generic;
using System.IO;
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
            encoding: new UTF8Encoding(true),
            failureMessage: "Failed to export the report: {0}",
            write: writer =>
            {
                foreach (ListViewItem item in lstResults.CheckedItems)
                {
                    string charset = item.SubItems[ResultsColumnCharset].Text;
                    string fileName = item.SubItems[ResultsColumnFileName].Text;
                    string directory = item.SubItems[ResultsColumnDirectory].Text;

                    writer.WriteLine("{0}\t{1}\\{2}", charset, directory, fileName);
                }
            });
    }

    /// <summary>
    /// Shared shape for the two exports whose failure is an I/O exception. The journal
    /// export is deliberately not routed through this: its Save method reports failure
    /// by returning an error string, and forcing that into a thrown exception here would
    /// manufacture one that was never raised.
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

        try
        {
            using var writer = new StreamWriter(saveFileDialog.FileName, false, encoding);
            write(writer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowWarning(failureMessage, ex.Message);
        }
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
