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
using System.Xml.Serialization;

namespace EncodingChecker
{
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
            internal required string TargetBaseCharset;
            internal required bool TargetWriteBom;
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

        private CurrentAction _currentAction;
        private Settings _settings = new();
        private CancellationTokenSource? _actionCancellation;
        private bool _closeRequested;

        // Set by OnConvert, read back by ConvertWorkerCompleted; never touched by the worker thread.
        private Dictionary<string, ListViewItem>? _convertItemsByPath;
        private string? _convertTargetLabel;

        private const int RESULTS_COLUMN_CHARSET = 0;
        private const int RESULTS_COLUMN_FILE_NAME = 1;
        private const int RESULTS_COLUMN_FILE_EXT = 2;
        private const int RESULTS_COLUMN_DIRECTORY = 3;

        public MainForm()
        {
            InitializeComponent();

			// Default to deterministic filename sorting instead of parallel completion order.
			_lvwColumnSorter = new ListViewColumnSorter
			{
			    SortColumn = RESULTS_COLUMN_FILE_NAME,
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

        #region Form events

        private void OnFormLoad(object? sender, EventArgs e)
        {
            lstConvert.BeginUpdate();

            IEnumerable<string> validCharsets = GetSupportedCharsets();

            foreach (string validCharset in validCharsets)
            {
                try
                {
                    // Add only charsets supported by .NET.
                    var encoding = Encoding.GetEncoding(validCharset);

                    lstValidCharsets.Items.Add(encoding.WebName);
                    lstConvert.Items.Add(encoding.WebName);

                    // Add BOM variants after UTF-8/16/32.
                    if (encoding.WebName is "utf-16BE" or "utf-16" or "utf-8" or "utf-32" or "utf-32BE")
                    {
                        lstValidCharsets.Items.Add(encoding.WebName + "-bom");
                        lstConvert.Items.Add(encoding.WebName + "-bom");
                    }
                }
                catch (Exception ex) when (
                    ex is ArgumentException or NotSupportedException)
                {
                    // Ignore unsupported charsets.
                }
            }

            if (lstConvert.Items.Count > 0)
                lstConvert.SelectedIndex = 0;

            lstConvert.EndUpdate();

            btnView.Tag = CurrentAction.View;
            btnValidate.Tag = CurrentAction.Validate;
            btnConvert.Tag = CurrentAction.Convert;

            LoadSettings();

            // Reflect the default sort column/order set in the constructor.
            lstResults.SetSortIcon(_lvwColumnSorter.SortColumn, _lvwColumnSorter.Order);

            // Size columns for the initial window size.
            lstResults.Columns[RESULTS_COLUMN_CHARSET]
                .AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);

            int remainingWidth =
                lstResults.Width - lstResults.Columns[RESULTS_COLUMN_CHARSET].Width;

            lstResults.Columns[RESULTS_COLUMN_FILE_NAME].Width =
                30 * remainingWidth / 100;

            lstResults.Columns[RESULTS_COLUMN_FILE_EXT]
                .AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);

            lstResults.Columns[RESULTS_COLUMN_DIRECTORY]
                .AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            if (_actionWorker.IsBusy)
            {
                // Cancel and defer the close until the worker settles, not mid-operation.
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
                    string charset =
                        item.SubItems[RESULTS_COLUMN_CHARSET].Text;

                    string fileName =
                        item.SubItems[RESULTS_COLUMN_FILE_NAME].Text;

                    string directory =
                        item.SubItems[RESULTS_COLUMN_DIRECTORY].Text;

                    writer.WriteLine(
                        "{0}\t{1}\\{2}",
                        charset,
                        directory,
                        fileName);
                }
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                ShowWarning(
                    "Failed to export the report: {0}",
                    ex.Message);
            }
        }

        private void OnExportReport(object? sender, EventArgs e)
        {
            if (lstResults.Items.Count == 0)
            {
                ShowWarning("There are no results to export");
                return;
            }

            var saveFileDialog = new SaveFileDialog
            {
                Title = @"Export Conversion Report",
                Filter = @"CSV files (*.csv)|*.csv",
                FileName = "Conversion report.csv",
                RestoreDirectory = true,
            };

            if (saveFileDialog.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                var entries =
                    new List<ConversionReportEntry>(lstResults.Items.Count);

                // Tag is always set to the entry when the row is added (ActionWorkerProgressChanged).
                foreach (ListViewItem item in lstResults.Items)
                    entries.Add((ConversionReportEntry)item.Tag!);

                using var writer =
                    new StreamWriter(saveFileDialog.FileName, false, Encoding.UTF8);

                ConversionReport.WriteCsv(entries, writer);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                ShowWarning(
                    "Failed to export the csv report: {0}",
                    ex.Message);
            }
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

        #region Action button handling

        private void OnAction(object? sender, EventArgs e)
        {
            // Only btnView/btnValidate are wired to this handler, and both have Tag set below.
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

            _currentAction = action;
            _settings.AddRecentDirectory(directory);

            UpdateControlsOnActionStart();

            // Suspend redraw until ScanWorkerCompleted, once results stop streaming in.
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

            // BOM is handled separately from the charset name.
            // lstConvert is DropDownList-style and OnFormLoad selects index 0 once
            // populated, so SelectedItem is never null here.
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

            // Tag is always set to the entry when the row is added (ActionWorkerProgressChanged).
            foreach (ListViewItem item in lstResults.CheckedItems)
            {
                var entry = (ConversionReportEntry)item.Tag!;

                itemsByPath[entry.FilePath] = item;
                entries.Add(entry);
            }

            _convertItemsByPath = itemsByPath;
            _convertTargetLabel = targetLabel;
            _currentAction = CurrentAction.Convert;

            UpdateControlsOnActionStart();

            _actionCancellation?.Dispose();
            _actionCancellation = new CancellationTokenSource();

            var args = new ConvertWorkerArgs
            {
                Entries = entries,
                TargetBaseCharset = targetBaseCharset,
                TargetWriteBom = writeBom,
                CancellationToken = _actionCancellation.Token,
            };

            _actionWorker.RunWorkerAsync(args);
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

        // No per-file progress to report; results are applied in one batch in ConvertWorkerCompleted.
        private static void ConvertWorkerDoWork(
            ConvertWorkerArgs args,
            DoWorkEventArgs e)
        {
            var completed = new ConcurrentBag<ConversionReportEntry>();

            try
            {
                ScanEngine.ConvertFiles(
                    args.Entries,
                    args.TargetBaseCharset,
                    args.TargetWriteBom,
                    ScanEngine.DefaultMaxParallelism,
                    onEntry: completed.Add,
                    args.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                e.Cancel = true;
            }

            e.Result = completed;
        }

        // GUI masks are newline-separated; ScanEngine receives split patterns.
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

            // Deferred from OnFormClosing while this operation was still running.
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

            // Redraw was suspended in StartAction; restore sorting and resume now.
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

            _convertItemsByPath = null;
            _convertTargetLabel = null;

            if (e.Error != null)
            {
                ShowWarning(
                    "An unexpected error occurred while converting: {0}",
                    e.Error.Message);

                UpdateControlsOnActionDone("Conversion failed.");
                return;
            }

            // ConvertWorkerDoWork always assigns e.Result, even when cancelled.
            var completed = (ConcurrentBag<ConversionReportEntry>)e.Result!;

            int convertedCount = 0;
            int unchangedCount = 0;
            int errorCount = 0;

            // Update the UI only after parallel processing completes.
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

                UpdateResultItem(item, entry, targetLabel);

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

            // The Charset column changed for converted rows.
            lstResults.Sort();

            lstResults.ItemChecked += OnResultItemChecked;
            lstResults.EndUpdate();

            OnResultItemChecked(
                lstResults,
                new ItemCheckedEventArgs(lstResults.Items[0]));

            btnExportReport.Visible = lstResults.Items.Count > 0;

            string statusMessage = e.Cancelled
                ? $"Conversion cancelled: {convertedCount} converted, " +
                  $"{unchangedCount} unchanged, {errorCount} failed"
                : $"Conversion complete: {convertedCount} converted, " +
                  $"{unchangedCount} unchanged, {errorCount} failed";

            UpdateControlsOnActionDone(statusMessage);
        }

        // Single place that formats a row from its ConversionReportEntry.
        private static void UpdateResultItem(
            ListViewItem item,
            ConversionReportEntry entry,
            string targetLabel)
        {
            if (entry.Result == ConversionRowResult.Converted)
            {
                item.Checked = false;
                item.ImageIndex = 0;
                item.SubItems[RESULTS_COLUMN_CHARSET].Text = targetLabel;
            }
            else if (entry.Result == ConversionRowResult.Error)
            {
                Debug.WriteLine(
                    $"Conversion failed for {entry.FilePath}: {entry.Diagnostic}");
            }
            else if (entry.Result == ConversionRowResult.Skipped)
            {
                Debug.WriteLine(
                    $"Conversion skipped for {entry.FilePath}: encoding could not be determined.");
            }
            // Unchanged: already matched the target; nothing was written, row stays as-is.
        }

        #endregion

        #region Loading and saving settings

        private void LoadSettings()
        {
            string settingsFileName = GetSettingsFileName();

            if (!File.Exists(settingsFileName))
                return;

            Settings settings;

            try
            {
                using (var settingsFile = new FileStream(
                    settingsFileName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read))
                {
                    var serializer = new XmlSerializer(typeof(Settings));

                    settings =
                        serializer.Deserialize(settingsFile) as Settings
                        ?? new Settings();
                }
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                InvalidOperationException)
            {
                // Corrupt or unreadable settings shouldn't prevent startup.
                settings = new Settings();
            }

            _settings = settings;

            if (settings.RecentDirectories.Count > 0)
            {
                foreach (string recentDirectory in
                         settings.RecentDirectories)
                {
                    lstBaseDirectory.Items.Add(recentDirectory);
                }

                lstBaseDirectory.SelectedIndex = 0;
            }
            else
            {
                lstBaseDirectory.Text =
                    Environment.CurrentDirectory;
            }

            chkIncludeSubdirectories.Checked =
                settings.IncludeSubdirectories;

            // Restore CRLF for the multiline textbox; XmlSerializer writes LF.
            txtFileMasks.Text = settings.FileMasks
                .Replace("\r\n", "\n")
                .Replace("\n", "\r\n");

            if (settings.ValidCharsets.Length > 0)
            {
                for (int i = 0;
                     i < lstValidCharsets.Items.Count;
                     i++)
                {
                    if (Array.Exists(
                        settings.ValidCharsets,
                        charset => charset.Equals(
                            (string)lstValidCharsets.Items[i],
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        lstValidCharsets.SetItemChecked(i, true);
                    }
                }
            }

            settings.WindowPosition.ApplyTo(this);
        }

        private void SaveSettings()
        {
            _settings.IncludeSubdirectories =
                chkIncludeSubdirectories.Checked;

            _settings.FileMasks =
                txtFileMasks.Text;

            _settings.ValidCharsets =
                new string[lstValidCharsets.CheckedItems.Count];

            for (int i = 0;
                 i < lstValidCharsets.CheckedItems.Count;
                 i++)
            {
                _settings.ValidCharsets[i] =
                    (string)lstValidCharsets.CheckedItems[i]!;
            }

            _settings.WindowPosition =
                new WindowPosition
                {
                    Left = Left,
                    Top = Top,
                    Width = Width,
                    Height = Height
                };

            string settingsFileName =
                GetSettingsFileName();

            try
            {
                using var settingsFile = new FileStream(
                    settingsFileName,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None);

                var serializer =
                    new XmlSerializer(typeof(Settings));

                serializer.Serialize(
                    settingsFile,
                    _settings);

                settingsFile.Flush();
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                InvalidOperationException)
            {
                // Settings are non-critical; don't block closing the app over this.
            }
        }

        private static string GetSettingsFileName()
        {
            string dataDirectory =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData);

            if (string.IsNullOrEmpty(dataDirectory) ||
                !Directory.Exists(dataDirectory))
            {
                dataDirectory = Environment.CurrentDirectory;
            }

            dataDirectory =
                Path.Combine(
                    dataDirectory,
                    "EncodingChecker");

            if (!Directory.Exists(dataDirectory))
                Directory.CreateDirectory(dataDirectory);

            return Path.Combine(
                dataDirectory,
                "Settings.xml");
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
            chkSelectDeselectAll.CheckState =
                CheckState.Unchecked;
            btnExportReport.Visible = false;

            btnCancel.Visible = true;

            // Total file count isn't known up front, so show activity, not a percentage.
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

            actionProgress.Visible = false;
            actionProgress.Style =
                ProgressBarStyle.Continuous;
            actionProgress.Value = 0;

            actionStatus.Text = statusMessage;
        }

        // Matches the encodings reported by UtfUnknown.Core.CodepageName. UTF-7 is
        // deliberately excluded: .NET disabled it by default for security reasons (see
        // SYSLIB0001 - UTF-7 content can be crafted to evade validation that assumes a
        // different encoding), so Encoding.GetEncoding throws NotSupportedException for
        // it. Omitting it here documents that instead of leaving it implicit.
        private static readonly string[] SupportedCharsets =
        [
            "ascii", "utf-8", "utf-16le", "utf-16be",
            "utf-32le", "utf-32be",
            "euc-jp", "euc-kr", "euc-tw",
            "iso-2022-cn", "iso-2022-kr", "iso-2022-jp",
            "x-cp50227",
            "big5", "gb18030", "hz-gb-2312", "shift-jis",
            "ks_c_5601-1987", "cp949",
            "ibm852", "ibm855", "ibm866",
            "iso-8859-1", "iso-8859-2", "iso-8859-3",
            "iso-8859-4", "iso-8859-5", "iso-8859-6",
            "iso-8859-7", "iso-8859-8", "iso-8859-9",
            "iso-8859-10", "iso-8859-11", "iso-8859-13",
            "iso-8859-15", "iso-8859-16",
            "windows-1250", "windows-1251", "windows-1252",
            "windows-1253", "windows-1255", "windows-1256",
            "windows-1257", "windows-1258",
            "x-mac-ce", "x-mac-cyrillic",
            "koi8-r", "tis-620", "viscii",
            "X-ISO-10646-UCS-4-3412",
            "X-ISO-10646-UCS-4-2143"
        ];

        private static string[] GetSupportedCharsets() =>
            SupportedCharsets;

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
}
