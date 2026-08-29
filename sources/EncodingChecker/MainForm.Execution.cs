using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace EncodingChecker;

public partial class MainForm
{
    #region Action button handling

    private void OnAction(object? sender, EventArgs e)
    {
        // View and Validate store their action in Tag.
        CurrentAction action =
            (CurrentAction)((Button)sender!).Tag!;

        StartAction(action);
    }

    private void StartAction(CurrentAction action)
    {
        if (_actionWorker.IsBusy)
            return;

        string directory = lstBaseDirectory.Text;

        if (string.IsNullOrEmpty(directory))
        {
            ShowWarning("Please specify a directory to check");
            return;
        }

        if (!Directory.Exists(directory))
        {
            ShowWarning(
                "The directory you specified '{0}' does not exist",
                directory);
            return;
        }

        if (action == CurrentAction.Validate &&
            lstValidCharsets.CheckedItems.Count == 0)
        {
            ShowWarning(
                "Select one or more valid character sets to proceed with validation");
            return;
        }

        // A new scan replaces the results table, so it cannot share the previous run's
        // conversion history.
        _lastConversionStartedUtc = null;
        _currentAction = action;
        _settings.AddRecentDirectory(directory);

        UpdateControlsOnActionStart();

        // Suspend redraw until all streamed results have arrived.
        lstResults.BeginUpdate();
        lstResults.ListViewItemSorter = null;
        lstResults.ItemChecked -= OnResultItemChecked;
        lstResults.Items.Clear();

        var validCharsets =
            new List<string>(lstValidCharsets.CheckedItems.Count);

        foreach (string validCharset in lstValidCharsets.CheckedItems)
            validCharsets.Add(validCharset);

        _actionCancellation?.Dispose();
        _actionCancellation = new CancellationTokenSource();

        var args = new WorkerArgs
        {
            Action = action,
            BaseDirectory = directory,
            IncludeSubdirectories = chkIncludeSubdirectories.Checked,
            FileMasks = txtFileMasks.Text,
            ValidCharsets = validCharsets,
            CancellationToken = _actionCancellation.Token,
        };

        _actionWorker.RunWorkerAsync(args);
    }

    private void OnConvert(object? sender, EventArgs e)
    {
        if (_actionWorker.IsBusy)
            return;

        if (lstResults.CheckedItems.Count == 0)
        {
            ShowWarning("Select one or more files to convert");
            return;
        }

        // BOM is represented separately from the base charset.
        string targetLabel = (string)lstConvert.SelectedItem!;

        ScanEngine.ParseCharsetLabel(
            targetLabel,
            out string targetBaseCharset,
            out bool writeBom);

        var itemsByPath =
            new Dictionary<string, ListViewItem>(
                StringComparer.OrdinalIgnoreCase);

        var entries =
            new List<ConversionReportEntry>(
                lstResults.CheckedItems.Count);

        // Each row keeps its source entry in Tag.
        foreach (ListViewItem item in lstResults.CheckedItems)
        {
            var entry = (ConversionReportEntry)item.Tag!;

            itemsByPath[entry.FilePath] = item;
            entries.Add(entry);
        }

        var completed = new ConcurrentBag<ConversionReportEntry>();

        _convertItemsByPath = itemsByPath;
        _convertTargetLabel = targetLabel;
        _convertResults = completed;
        _convertWasPreview = chkPreviewChanges.Checked;
        _currentAction = CurrentAction.Convert;

        UpdateControlsOnActionStart();

        _actionCancellation?.Dispose();
        _actionCancellation = new CancellationTokenSource();

        var args = new ConvertWorkerArgs
        {
            Entries = entries,
            BaseDirectory = lstBaseDirectory.Text,
            TargetBaseCharset = targetBaseCharset,
            TargetWriteBom = writeBom,
            Preview = chkPreviewChanges.Checked,
            Backup = chkCreateBackup.Checked,
            Completed = completed,

            // The worker marshals the confirmation back to the UI thread.
            Confirm = plan => (ConfirmationResponse)Invoke(() => Confirm(plan)),
            CancellationToken = _actionCancellation.Token,
        };

        _convertArgs = args;
        _lastConversionStartedUtc = _convertWasPreview ? null : DateTime.UtcNow;
        _actionWorker.RunWorkerAsync(args);
    }

    /// <summary>Shows a decided plan and returns the user's choice.</summary>
    private ConfirmationResponse Confirm(ConversionPlan plan)
    {
        using var dialog = new ConversionConfirmationForm(plan);

        return dialog.ShowDialog(this) switch
        {
            DialogResult.OK => ConfirmationResponse.Proceed,
            DialogResult.Retry when dialog.ChosenSourceEncoding is { } chosen =>
                new ConfirmationResponse(
                    ConfirmationChoice.ChooseSourceEncoding,
                    chosen,
                    dialog.ChosenFiles),
            _ => ConfirmationResponse.Cancel,
        };
    }

    private void OnCancelAction(object? sender, EventArgs e)
    {
        if (_actionWorker.IsBusy)
        {
            btnCancel.Visible = false;
            _actionWorker.CancelAsync();
            _actionCancellation?.Cancel();
        }
    }

    #endregion

    #region Background worker event handlers

    private static void ActionWorkerDoWork(
        object? sender,
        DoWorkEventArgs e)
    {
        if (e.Argument is ConvertWorkerArgs convertArgs)
        {
            ConvertWorkerDoWork(convertArgs, e);
            return;
        }

        var worker = (BackgroundWorker)sender!;
        var args = (WorkerArgs)e.Argument!;

        List<string> includePatterns =
            SplitFileMasks(args.FileMasks);

        var scanOptions = new ScanDirectoryOptions
        {
            BaseDirectory = args.BaseDirectory,
            IncludeSubdirectories = args.IncludeSubdirectories,
            IncludePatterns = includePatterns,
            Action =
                args.Action == CurrentAction.Validate
                    ? ScanAction.Validate
                    : ScanAction.Detect,
            ValidCharsets = args.ValidCharsets,
        };

        try
        {
            ScanEngine.ScanDirectory(
                scanOptions,
                onEntry: entry =>
                {
                    // Hide files that already pass validation.
                    if (args.Action == CurrentAction.Validate &&
                        entry.Result == ConversionRowResult.Unchanged)
                    {
                        return;
                    }

                    worker.ReportProgress(0, entry);
                },
                args.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            e.Cancel = true;
        }
    }

    // Results are collected and applied together when conversion finishes.
    private static void ConvertWorkerDoWork(
        ConvertWorkerArgs args,
        DoWorkEventArgs e)
    {
        try
        {
            args.Outcome = new ConversionOrchestrator(args.Confirm).Run(
                args.Entries,
                args.BaseDirectory,
                args.TargetBaseCharset,
                args.TargetWriteBom,
                backup: args.Backup,
                preview: args.Preview,
                ScanEngine.DefaultMaxParallelism,
                onEntry: args.Completed.Add,
                args.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            e.Cancel = true;
        }
    }

    // The GUI stores masks as newline-separated values; the engine uses a list.
    private static List<string> SplitFileMasks(string fileMaskString)
    {
        if (string.IsNullOrWhiteSpace(fileMaskString))
            return [];

        return
        [
            .. fileMaskString
                .Split(
                    [Environment.NewLine],
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(mask => mask.Trim())
                .Where(mask => mask.Length > 0)
        ];
    }

    private void ActionWorkerProgressChanged(
        object? sender,
        ProgressChangedEventArgs e)
    {
        if (e.UserState is not ConversionReportEntry entry)
            return;

        string charsetLabel =
            ScanEngine.FormatCharsetLabel(
                entry.SourceEncoding,
                entry.SourceHasBom);

        var resultItem = new ListViewItem(
            [
                charsetLabel,
                Path.GetFileName(entry.FilePath),
                Path.GetExtension(entry.FilePath),
                Path.GetDirectoryName(entry.FilePath) ?? string.Empty
            ],
            -1)
        {
            Tag = entry,
        };

        lstResults.Items.Add(resultItem);
        actionStatus.Text = entry.FilePath;
    }

    private void ActionWorkerCompleted(
        object? sender,
        RunWorkerCompletedEventArgs e)
    {
        if (_currentAction == CurrentAction.Convert)
            ConvertWorkerCompleted(e);
        else
            ScanWorkerCompleted(e);

        // Complete the deferred close after the worker has settled.
        if (_closeRequested)
            Close();
    }

    private void ScanWorkerCompleted(RunWorkerCompletedEventArgs e)
    {
        if (e.Error != null)
        {
            ShowWarning(
                "An unexpected error occurred while scanning: {0}",
                e.Error.Message);
        }

        if (lstResults.Items.Count > 0)
        {
            foreach (ColumnHeader columnHeader in lstResults.Columns)
            {
                columnHeader.AutoResize(
                    ColumnHeaderAutoResizeStyle.ColumnContent);
            }
        }

        // Restore normal sorting and redraw.
        lstResults.ListViewItemSorter = _lvwColumnSorter;
        lstResults.ItemChecked += OnResultItemChecked;
        lstResults.Sort();
        lstResults.EndUpdate();

        string statusMessage = e.Cancelled
            ? "Cancelled - {0} files processed"
            : _currentAction == CurrentAction.View
                ? "{0} files processed"
                : "{0} files do not have the correct encoding";

        UpdateControlsOnActionDone(
            string.Format(statusMessage, lstResults.Items.Count));
    }

    private void ConvertWorkerCompleted(RunWorkerCompletedEventArgs e)
    {
        string targetLabel = _convertTargetLabel!;
        Dictionary<string, ListViewItem> itemsByPath = _convertItemsByPath!;
        ConcurrentBag<ConversionReportEntry> completed = _convertResults ?? [];
        bool wasPreview = _convertWasPreview;
        OrchestrationResult? outcome = _convertArgs?.Outcome;

        _convertItemsByPath = null;
        _convertTargetLabel = null;
        _convertResults = null;
        _convertWasPreview = false;
        _convertArgs = null;

        // These outcomes leave all files untouched, so there are no result rows to update.
        if (e.Error is null && !e.Cancelled && outcome is not null &&
            outcome.Outcome is not (OrchestrationOutcome.Converted
                                    or OrchestrationOutcome.Previewed))
        {
            // Starting conversion temporarily clears this control's visual state. A
            // cancelled review keeps all rows selected, so restore the matching state.
            UpdateSelectDeselectAllState();

            if (outcome.Message is not null)
                ShowWarning("{0}", outcome.Message);

            UpdateControlsOnActionDone(
                outcome.Outcome == OrchestrationOutcome.Cancelled
                    ? "Conversion cancelled. No files were modified."
                    : "Conversion did not run. No files were modified.");

            return;
        }

        if (e.Error != null)
        {
            ShowWarning(
                "An unexpected error occurred while converting: {0}",
                e.Error.Message);

            UpdateControlsOnActionDone("Conversion failed.");
            return;
        }

        int convertedCount = 0;
        int unchangedCount = 0;
        int errorCount = 0;

        // Update the UI only after all worker results are available.
        lstResults.BeginUpdate();
        lstResults.ItemChecked -= OnResultItemChecked;

        foreach (ConversionReportEntry entry in completed)
        {
            if (!itemsByPath.TryGetValue(
                    entry.FilePath,
                    out ListViewItem? item))
            {
                continue;
            }

            UpdateResultItem(item, entry, targetLabel, wasPreview);

            switch (entry.Result)
            {
                case ConversionRowResult.Converted:
                    convertedCount++;
                    break;
                case ConversionRowResult.Error:
                    errorCount++;
                    break;
                default:
                    unchangedCount++;
                    break;
            }
        }

        lstResults.Sort();

        lstResults.ItemChecked += OnResultItemChecked;
        lstResults.EndUpdate();

        UpdateSelectDeselectAllState();

        btnExportReport.Visible = lstResults.Items.Count > 0;

        // A preview reports intended changes, not completed conversions.
        string statusMessage = wasPreview
            ? (e.Cancelled
                ? $"Preview cancelled: {convertedCount} file(s) would be converted, " +
                  $"{unchangedCount} unchanged, {errorCount} failed"
                : $"Preview complete: {convertedCount} file(s) would be converted, " +
                  $"{unchangedCount} unchanged, {errorCount} failed")
            : (e.Cancelled
                ? $"Conversion cancelled: {convertedCount} converted, " +
                  $"{unchangedCount} unchanged, {errorCount} failed"
                : $"Conversion complete: {convertedCount} converted, " +
                  $"{unchangedCount} unchanged, {errorCount} failed");

        UpdateControlsOnActionDone(statusMessage);
    }

    // Kept separate so row presentation can be tested without creating the form.
    internal static void UpdateResultItem(
        ListViewItem item,
        ConversionReportEntry entry,
        string targetLabel,
        bool wasPreview)
    {
        // Keep the row tied to the entry that produced its current state.
        item.Tag = entry;

        if (entry.Result == ConversionRowResult.Converted)
        {
            // Preview leaves the file unchanged, so only the icon changes.
            if (wasPreview)
            {
                item.ImageIndex = RESULT_ICON_WOULD_CHANGE;
                return;
            }

            item.Checked = false;
            item.ImageIndex = RESULT_ICON_SUCCESS;
            item.SubItems[RESULTS_COLUMN_CHARSET].Text = targetLabel;
        }
        else if (entry.Result == ConversionRowResult.Error)
        {
            // Keep failed files selected so they can be retried.
            item.ImageIndex = RESULT_ICON_FAILED;

            Debug.WriteLine(
                $"Conversion failed for {entry.FilePath}: {entry.Diagnostic}");
        }
        else if (entry.Result == ConversionRowResult.Skipped)
        {
            Debug.WriteLine(
                $"Conversion skipped for {entry.FilePath}: encoding could not be determined.");
        }
        // Unchanged: already matches the target; leave the row unchanged.
    }

    #endregion
}
