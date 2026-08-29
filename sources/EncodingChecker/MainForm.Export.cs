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

        var saveFileDialog = new SaveFileDialog
        {
            Title = @"Export to a Text File",
            Filter = @"Text files (*.txt)|*.txt",
            FileName = "Encoding.txt",
            RestoreDirectory = true,
        };

        if (saveFileDialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            using var writer = new StreamWriter(saveFileDialog.FileName, false, Encoding.UTF8);

            foreach (ListViewItem item in lstResults.CheckedItems)
            {
                string charset = item.SubItems[RESULTS_COLUMN_CHARSET].Text;
                string fileName = item.SubItems[RESULTS_COLUMN_FILE_NAME].Text;
                string directory = item.SubItems[RESULTS_COLUMN_DIRECTORY].Text;

                writer.WriteLine("{0}\t{1}\\{2}", charset, directory, fileName);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowWarning("Failed to export the report: {0}", ex.Message);
        }
    }

    private void OnExportResultsOpening(object? sender, EventArgs e)
    {
        _exportCsv.Enabled = lstResults.Items.Count > 0;
        _exportJournal.Enabled = _lastConversionStartedUtc.HasValue;
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

        var saveFileDialog = new SaveFileDialog
        {
            Title = @"Export CSV Report",
            Filter = @"CSV files (*.csv)|*.csv",
            FileName = "EncodingChecker report.csv",
            RestoreDirectory = true,
        };

        if (saveFileDialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            using var writer = new StreamWriter(saveFileDialog.FileName, false, Encoding.UTF8);
            ConversionReport.WriteCsv(ResultEntries(), writer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowWarning("Failed to export the csv report: {0}", ex.Message);
        }
    }

    private void OnExportJournal(object? sender, EventArgs e)
    {
        if (_lastConversionStartedUtc is null)
            return;

        var saveFileDialog = new SaveFileDialog
        {
            Title = @"Export Conversion History",
            Filter = @"JSON files (*.json)|*.json",
            FileName = "EncodingChecker conversion history.json",
            RestoreDirectory = true,
        };

        if (saveFileDialog.ShowDialog(this) == DialogResult.OK)
            ExportJournal(ResultEntries(), saveFileDialog.FileName);
    }

    private void ExportJournal(List<ConversionReportEntry> entries, string path)
    {
        if (_lastConversionStartedUtc is not { } startedUtc)
        {
            ShowWarning(
                "There is no conversion to journal yet. Run Convert first; a journal "
                + "records what a conversion did, which a detection scan has not.");
            return;
        }

        ScanEngine.ParseCharsetLabel(
            (string)lstConvert.SelectedItem!,
            out string targetCharset,
            out bool targetWriteBom);

        string? error = ConversionJournal.FromRun(
                entries,
                lstBaseDirectory.Text,
                targetCharset,
                targetWriteBom,
                chkCreateBackup.Checked,
                explicitSource: entries.Count > 0
                                && entries.TrueForAll(e => e.SourceEncodingWasSpecified)
                    ? entries[0].ResolvedSourceLabel
                    : null,
                surface: "Gui",
                startedUtc)
            .Save(path);

        if (error is not null)
            ShowWarning("Failed to export the journal: {0}", error);
    }
}
