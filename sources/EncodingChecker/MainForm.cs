using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace EncodingChecker;

public partial class MainForm : Form
{
    private sealed class WorkerArgs
    {
        internal CurrentAction Action;
        internal required string BaseDirectory;
        internal bool IncludeSubdirectories;
        internal required string FileMasks;
        internal required List<string> ValidCharsets;
        internal CancellationToken CancellationToken;
    }

    private sealed class ConvertWorkerArgs
    {
        internal required List<ConversionReportEntry> Entries;
        internal required string BaseDirectory;
        internal required string TargetBaseCharset;
        internal required bool TargetWriteBom;
        internal required bool Preview;
        internal required bool Backup;
        internal required ConcurrentBag<ConversionReportEntry> Completed;
        internal required Func<ConversionPlan, ConfirmationResponse> Confirm;
        internal OrchestrationResult? Outcome;
        internal CancellationToken CancellationToken;
    }

    private enum CurrentAction
    {
        View,
        Validate,
        Convert,
    }

    private readonly ListViewColumnSorter _lvwColumnSorter;
    private readonly BackgroundWorker _actionWorker;
    private readonly ToolStripMenuItem _exportText = new("Export selected rows as text...");
    private readonly ToolStripMenuItem _exportCsv = new("Export all results as CSV...");
    private readonly ToolStripMenuItem _exportJournal = new("Export conversion journal as JSON...");

    private CurrentAction _currentAction;
    private Settings _settings = new();
    private CancellationTokenSource? _actionCancellation;
    private bool _closeRequested;

    // Set by OnConvert and read by ConvertWorkerCompleted; never touched by the worker.
    private Dictionary<string, ListViewItem>? _convertItemsByPath;
    private string? _convertTargetLabel;

    // The worker writes results here because e.Result is unavailable after cancellation.
    private ConcurrentBag<ConversionReportEntry>? _convertResults;

    // Set by OnConvert and read by ConvertWorkerCompleted to distinguish conversion from preview.
    private bool _convertWasPreview;

    // Exact immutable record returned by the most recent completed conversion.
    private ConversionJournal? _lastConversionJournal;

    // Shared with the completion handler so it can report how the run ended.
    private ConvertWorkerArgs? _convertArgs;

    // Indices into imgsResults (see SetKeyName calls in MainForm.Designer.cs).
    // Reuses the existing Failed and Warning icons; Warning marks preview rows.
    private const int ResultIconSuccess = 0;
    private const int ResultIconFailed = 1;
    private const int ResultIconWouldChange = 2;

    private const int ResultsColumnCharset = 0;
    private const int ResultsColumnFileName = 1;
    private const int ResultsColumnFileExt = 2;
    private const int ResultsColumnDirectory = 3;

    public MainForm()
    {
        InitializeComponent();
        ConfigureExportMenu();

        // Keep result ordering deterministic despite parallel processing.
        _lvwColumnSorter = new ListViewColumnSorter
        {
            SortColumn = ResultsColumnFileName,
            Order = SortOrder.Ascending,
        };
        lstResults.ListViewItemSorter = _lvwColumnSorter;

        _actionWorker = new BackgroundWorker
        {
            WorkerReportsProgress = true,
            WorkerSupportsCancellation = true
        };

        _actionWorker.DoWork += ActionWorkerDoWork;
        _actionWorker.ProgressChanged += ActionWorkerProgressChanged;
        _actionWorker.RunWorkerCompleted += ActionWorkerCompleted;
    }

    private void ConfigureExportMenu()
    {
        _exportText.Click += OnExport;
        _exportCsv.Click += OnExportCsvReport;
        _exportJournal.Click += OnExportJournal;

        btnExportReport.DropDownItems.AddRange(_exportText, _exportCsv, _exportJournal);
        btnExportReport.DropDownOpening += OnExportResultsOpening;
    }

    #region Form events

    private void OnFormLoad(object? sender, EventArgs e)
    {
        lstConvert.BeginUpdate();

        foreach (Encoding encoding in TextEncoding.SupportedEncodings)
        {
            lstValidCharsets.Items.Add(encoding.WebName);
            lstConvert.Items.Add(encoding.WebName);

            // Add BOM variants for encodings where BOM is meaningful.
            if (ScanEngine.IsBomCapable(encoding.WebName))
            {
                lstValidCharsets.Items.Add(encoding.WebName + "-bom");
                lstConvert.Items.Add(encoding.WebName + "-bom");
            }
        }

        int utf8Index = lstConvert.FindStringExact("utf-8");

        if (utf8Index >= 0)
            lstConvert.SelectedIndex = utf8Index;
        else if (lstConvert.Items.Count > 0)
            lstConvert.SelectedIndex = 0;

        lstConvert.EndUpdate();

        btnView.Tag = CurrentAction.View;
        btnValidate.Tag = CurrentAction.Validate;
        btnConvert.Tag = CurrentAction.Convert;

        LoadSettings();

        // Match the sorter state shown in the header.
        lstResults.SetSortIcon(_lvwColumnSorter.SortColumn, _lvwColumnSorter.Order);

        // Size columns for the initial window.
        lstResults.Columns[ResultsColumnCharset]
            .AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);

        int remainingWidth =
            lstResults.Width - lstResults.Columns[ResultsColumnCharset].Width;

        lstResults.Columns[ResultsColumnFileName].Width =
            30 * remainingWidth / 100;

        lstResults.Columns[ResultsColumnFileExt]
            .AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);

        lstResults.Columns[ResultsColumnDirectory]
            .AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_actionWorker.IsBusy)
        {
            // Let the worker finish its cancellation path before closing.
            e.Cancel = true;

            if (!_closeRequested)
            {
                _closeRequested = true;
                _actionCancellation?.Cancel();
            }

            return;
        }

        SaveSettings();
    }

    private void OnBrowseDirectories(object? sender, EventArgs e)
    {
        if (Directory.Exists(lstBaseDirectory.Text))
            dlgBrowseDirectories.SelectedPath = lstBaseDirectory.Text;

        if (dlgBrowseDirectories.ShowDialog(this) == DialogResult.OK)
        {
            lstBaseDirectory.Text = dlgBrowseDirectories.SelectedPath;
            lstBaseDirectory.Items.Add(dlgBrowseDirectories.SelectedPath);
        }
    }

    private void OnSelectDeselectAll(object? sender, EventArgs e)
    {
        lstResults.ItemChecked -= OnResultItemChecked;

        try
        {
            bool isChecked = chkSelectDeselectAll.Checked;

            foreach (ListViewItem item in lstResults.Items)
                item.Checked = isChecked;
        }
        finally
        {
            lstResults.ItemChecked += OnResultItemChecked;
        }
    }

    private void OnResultItemChecked(object? sender, ItemCheckedEventArgs e)
        => UpdateSelectDeselectAllState();

    /// <summary>Synchronizes the tri-state selector with the individual result rows.</summary>
    private void UpdateSelectDeselectAllState()
    {
        chkSelectDeselectAll.CheckedChanged -= OnSelectDeselectAll;

        try
        {
            if (lstResults.CheckedItems.Count == 0)
                chkSelectDeselectAll.CheckState = CheckState.Unchecked;
            else if (lstResults.CheckedItems.Count == lstResults.Items.Count)
                chkSelectDeselectAll.CheckState = CheckState.Checked;
            else
                chkSelectDeselectAll.CheckState = CheckState.Indeterminate;
        }
        finally
        {
            chkSelectDeselectAll.CheckedChanged += OnSelectDeselectAll;
        }
    }

    private void OnResultColumnClick(object o, ColumnClickEventArgs e)
    {
        if (e.Column == _lvwColumnSorter.SortColumn)
        {
            _lvwColumnSorter.Order =
                _lvwColumnSorter.Order == SortOrder.Ascending
                    ? SortOrder.Descending
                    : SortOrder.Ascending;
        }
        else
        {
            _lvwColumnSorter.SortColumn = e.Column;
            _lvwColumnSorter.Order = SortOrder.Ascending;
        }

        lstResults.Sort();
        lstResults.SetSortIcon(
            _lvwColumnSorter.SortColumn,
            _lvwColumnSorter.Order);
    }

    private void OnHelp(object? sender, EventArgs e)
    {
        var psi = new ProcessStartInfo(
            "https://github.com/amrali-eg/EncodingChecker")
        {
            UseShellExecute = true
        };

        Process.Start(psi);
    }

    private void OnAbout(object? sender, EventArgs e)
    {
        using var aboutForm = new AboutForm();
        aboutForm.ShowDialog(this);
    }

    private void OnBaseDirectoryDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect =
            TryGetDroppedDirectory(e.Data, out _)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
    }

    private void OnBaseDirectoryDragDrop(object? sender, DragEventArgs e)
    {
        if (!TryGetDroppedDirectory(e.Data, out string? directory))
            return;

        lstBaseDirectory.Text = directory;
        lstBaseDirectory.Items.Add(directory);
    }

    private static bool TryGetDroppedDirectory(
        IDataObject? data,
        [NotNullWhen(true)] out string? directory)
    {
        directory = null;

        if (data == null ||
            !data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        var paths =
            data.GetData(DataFormats.FileDrop) as string[];

        string? firstDirectory =
            paths?.FirstOrDefault(Directory.Exists);

        if (firstDirectory == null)
            return false;

        directory = firstDirectory;
        return true;
    }

    #endregion



    private void UpdateControlsOnActionStart()
    {
        btnView.Enabled = false;
        btnValidate.Enabled = false;

        lblConvert.Enabled = false;
        lstConvert.Enabled = false;
        btnConvert.Enabled = false;
        chkSelectDeselectAll.Enabled = false;

        // Reset the tri-state control without changing individual row selections.
        chkSelectDeselectAll.CheckedChanged -= OnSelectDeselectAll;

        try
        {
            chkSelectDeselectAll.CheckState =
                CheckState.Unchecked;
        }
        finally
        {
            chkSelectDeselectAll.CheckedChanged += OnSelectDeselectAll;
        }

        // Preserve user options; disable them only while processing.
        chkCreateBackup.Enabled = false;
        chkPreviewChanges.Enabled = false;

        btnExportReport.Visible = false;

        btnCancel.Visible = true;

        // Total work is unknown, so use an activity indicator rather than a percentage.
        actionProgress.Style =
            ProgressBarStyle.Marquee;

        actionProgress.Visible = true;
        actionStatus.Text = string.Empty;
    }

    private void UpdateControlsOnActionDone(string statusMessage)
    {
        btnView.Enabled = true;
        btnValidate.Enabled = true;

        if (lstResults.Items.Count > 0)
        {
            lblConvert.Enabled = true;
            lstConvert.Enabled = true;
            btnConvert.Enabled = true;
            chkSelectDeselectAll.Enabled = true;
            chkCreateBackup.Enabled = true;
            chkPreviewChanges.Enabled = true;

            if (_currentAction == CurrentAction.Validate &&
                lstValidCharsets.CheckedItems.Count > 0)
            {
                string firstValidCharset =
                    (string)lstValidCharsets.CheckedItems[0]!;

                for (int i = 0;
                     i < lstConvert.Items.Count;
                     i++)
                {
                    string convertCharset =
                        (string)lstConvert.Items[i]!;

                    if (firstValidCharset.Equals(
                        convertCharset,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        lstConvert.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        btnCancel.Visible = false;

        btnExportReport.Visible = lstResults.Items.Count > 0;

        actionProgress.Visible = false;
        actionProgress.Style =
            ProgressBarStyle.Continuous;
        actionProgress.Value = 0;

        actionStatus.Text = statusMessage;
    }

    // Matches encodings reported by UtfUnknown.Core.CodepageName.
    // UTF-7 is intentionally excluded because .NET disables it by default (SYSLIB0001)
    // and Encoding.GetEncoding throws NotSupportedException.

    private void ShowWarning(
        string message,
        params object[] args)
    {
        MessageBox.Show(
            this,
            string.Format(message, args),
            @"Warning",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

}
