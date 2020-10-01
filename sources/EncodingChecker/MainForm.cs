using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Deployment.Application;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

using EncodingUtils;

namespace EncodingChecker
{
    public partial class MainForm : Form
    {
        private sealed class WorkerArgs
        {
            internal CurrentAction Action;
            internal string BaseDirectory;
            internal bool IncludeSubdirectories;
            internal string FileMasks;
            internal List<string> ValidCharsets;
        }

        private sealed class WorkerProgress
        {
            internal string FileName;
            internal string DirectoryName;
            internal string Charset;
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
        private Settings _settings;

        private const int ResultsColumnCharset = 0;
        private const int ResultsColumnFileName = 1;
        private const int ResultsColumnDirectory = 2;

        public MainForm()
        {
            InitializeComponent();

            _lvwColumnSorter = new ListViewColumnSorter();
            lstResults.ListViewItemSorter = _lvwColumnSorter;

            _actionWorker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
            _actionWorker.DoWork += ActionWorkerDoWork;
            _actionWorker.ProgressChanged += ActionWorkerProgressChanged;
            _actionWorker.RunWorkerCompleted += ActionWorkerCompleted;
        }

        #region Form events
        private void OnFormLoad(object sender, EventArgs e)
        {
            lstConvert.BeginUpdate();

            IEnumerable<string> validCharsets = GetSupportedCharsets();
            foreach (string validCharset in validCharsets)
            {
                try
                {   // add only those charsets which are supported by .NET
                    Encoding encoding = Encoding.GetEncoding(validCharset);
                    lstValidCharsets.Items.Add(encoding.WebName);
                    lstConvert.Items.Add(encoding.WebName);
                    // add UTF-8 with BOM, right after UTF-8
                    if (encoding.WebName == "utf-8") lstConvert.Items.Add("utf-8-bom");
                }
                catch
                {
                    // ignored charsets
                }
            }
            if (lstConvert.Items.Count > 0)
                lstConvert.SelectedIndex = 0;

            lstConvert.EndUpdate();

            btnView.Tag = CurrentAction.View;
            btnValidate.Tag = CurrentAction.Validate;
            btnConvert.Tag = CurrentAction.Convert;

            LoadSettings();

            //Size the result list columns based on the initial size of the window
            lstResults.Columns[0].AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);
            int remainingWidth = lstResults.Width - lstResults.Columns[0].Width;
            lstResults.Columns[1].Width = (30 * remainingWidth) / 100;
            lstResults.Columns[2].AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            SaveSettings();
        }

        private void OnBrowseDirectories(object sender, EventArgs e)
        {
            dlgBrowseDirectories.SelectedPath = lstBaseDirectory.Text;
            if (dlgBrowseDirectories.ShowDialog(this) == DialogResult.OK)
            {
                lstBaseDirectory.Text = dlgBrowseDirectories.SelectedPath;
                lstBaseDirectory.Items.Add(dlgBrowseDirectories.SelectedPath);
            }
        }

        private void OnSelectDeselectAll(object sender, EventArgs e)
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

        private void OnResultItemChecked(object sender, ItemCheckedEventArgs e)
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
                _lvwColumnSorter.Order = _lvwColumnSorter.Order == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
            }
            else
            {
                _lvwColumnSorter.SortColumn = e.Column;
                _lvwColumnSorter.Order = SortOrder.Ascending;
            }
            lstResults.Sort();
        }

        private void OnHelp(object sender, EventArgs e)
        {
            ProcessStartInfo psi =
                new ProcessStartInfo("http://encodingchecker.codeplex.com/documentation") { UseShellExecute = true };
            Process.Start(psi);
        }

        private void OnAbout(object sender, EventArgs e)
        {
            using (AboutForm aboutForm = new AboutForm())
                aboutForm.ShowDialog(this);
        }

        private void OnCopy(object sender, EventArgs e)
        {
            if (lstResults.CheckedItems.Count <= 0)
            {
                return;
            }
            StringBuilder stringBuilder = new StringBuilder();
            foreach (ListViewItem item in lstResults.CheckedItems)
            {
                stringBuilder.AppendLine(item.Text + "\t" + item.SubItems[ResultsColumnDirectory].Text + "\\" + item.SubItems[ResultsColumnFileName].Text);
            }
            Clipboard.SetText(stringBuilder.ToString());
        }
        #endregion

        #region Action button handling
        private void OnAction(object sender, EventArgs e)
        {
            CurrentAction action = (CurrentAction)((Button)sender).Tag;
            StartAction(action);
        }

        private void StartAction(CurrentAction action)
        {
            string directory = lstBaseDirectory.Text;
            if (string.IsNullOrEmpty(directory))
            {
                ShowWarning("Please specify a directory to check");
                return;
            }
            if (!Directory.Exists(directory))
            {
                ShowWarning("The directory you specified '{0}' does not exist", directory);
                return;
            }
            if (action == CurrentAction.Validate && lstValidCharsets.CheckedItems.Count == 0)
            {
                ShowWarning("Select one or more valid character sets to proceed with validation");
                return;
            }

            _currentAction = action;

            _settings.RecentDirectories.Add(directory);

            UpdateControlsOnActionStart();

            List<string> validCharsets = new List<string>(lstValidCharsets.CheckedItems.Count);
            foreach (string validCharset in lstValidCharsets.CheckedItems)
                validCharsets.Add(validCharset);

            WorkerArgs args = new WorkerArgs
            {
                Action = action,
                BaseDirectory = directory,
                IncludeSubdirectories = chkIncludeSubdirectories.Checked,
                FileMasks = txtFileMasks.Text,
                ValidCharsets = validCharsets
            };
            _actionWorker.RunWorkerAsync(args);
        }

        private void OnConvert(object sender, EventArgs e)
        {
            if (lstResults.CheckedItems.Count == 0)
            {
                ShowWarning("Select one or more files to convert");
                return;
            }

            // stop drawing of the results list view control
            lstResults.BeginUpdate();
            lstResults.ItemChecked -= OnResultItemChecked;

            foreach (ListViewItem item in lstResults.CheckedItems)
            {
                string charset = item.SubItems[ResultsColumnCharset].Text;
                if (charset == "(Unknown)")
                    continue;
                string fileName = item.SubItems[ResultsColumnFileName].Text;
                string directory = item.SubItems[ResultsColumnDirectory].Text;
                string filePath = Path.Combine(directory, fileName);

                FileAttributes attributes = File.GetAttributes(filePath);
                if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    attributes ^= FileAttributes.ReadOnly;
                    File.SetAttributes(filePath, attributes);
                }

                if (!Encoding.GetEncoding(charset).Validate(File.ReadAllBytes(filePath)))
                {
                    Debug.WriteLine("Decoding error. " + filePath);
                    continue;
                }

                string content;
                using (StreamReader reader = new StreamReader(filePath, Encoding.GetEncoding(charset)))
                    content = reader.ReadToEnd();

                string targetCharset = (string)lstConvert.SelectedItem;
                Encoding encoding;
                // handle UTF-8 and UTF-8 with BOM
                if (targetCharset == "utf-8")
                {
                    encoding = new UTF8Encoding(false);
                }
                else if (targetCharset == "utf-8-bom")
                {
                    encoding = new UTF8Encoding(true);
                }
                else
                {
                    encoding = Encoding.GetEncoding(targetCharset);
                }
                using (StreamWriter writer = new StreamWriter(filePath, append: false, encoding))
                {
                    // TODO: catch exceptions
                    writer.Write(content);
                    writer.Flush();
                }

                item.Checked = false;
                item.ImageIndex = 0;
                item.SubItems[ResultsColumnCharset].Text = targetCharset;
            }

            // resume drawing of the results list view control
            lstResults.ItemChecked += OnResultItemChecked;
            lstResults.EndUpdate();

            // execute handler of the 'ItemChecked' event
            OnResultItemChecked(lstResults, new ItemCheckedEventArgs(lstResults.Items[0]));
        }

        private void OnCancelAction(object sender, EventArgs e)
        {
            if (_actionWorker.IsBusy)
            {
                btnCancel.Visible = false;
                _actionWorker.CancelAsync();
            }
        }
        #endregion

        #region Background worker event handlers and helper methods
        private static void ActionWorkerDoWork(object sender, DoWorkEventArgs e)
        {
            const int progressBufferSize = 5;

            BackgroundWorker worker = (BackgroundWorker)sender;
            WorkerArgs args = (WorkerArgs)e.Argument;

            string[] allFiles = Directory.GetFiles(args.BaseDirectory, "*.*",
                args.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

            WorkerProgress[] progressBuffer = new WorkerProgress[progressBufferSize];
            int reportBufferCounter = 1;

            IEnumerable<Regex> maskPatterns = GenerateMaskPatterns(args.FileMasks);
            for (int i = 0; i < allFiles.Length; i++)
            {
                if (worker.CancellationPending)
                {
                    e.Cancel = true;
                    break;
                }

                string path = allFiles[i];
                string fileName = Path.GetFileName(path);
                if (!SatisfiesMaskPatterns(fileName, maskPatterns))
                    continue;

                Encoding encoding = TextEncoding.GetFileEncoding(path);
                string charset = encoding?.WebName ?? "(Unknown)";

                if (args.Action == CurrentAction.Validate)
                {
                    if (args.ValidCharsets.Contains(charset))
                        continue;
                }

                string directoryName = Path.GetDirectoryName(path);

                progressBuffer[reportBufferCounter - 1] = new WorkerProgress
                {
                    Charset = charset,
                    FileName = fileName,
                    DirectoryName = directoryName
                };
                reportBufferCounter++;
                if (reportBufferCounter > progressBufferSize)
                {
                    reportBufferCounter = 1;
                    int percentageCompleted = (i * 100) / allFiles.Length;
                    WorkerProgress[] reportProgress = new WorkerProgress[progressBufferSize];
                    Array.Copy(progressBuffer, reportProgress, progressBufferSize);
                    worker.ReportProgress(percentageCompleted, reportProgress);
                    Array.Clear(progressBuffer, 0, progressBufferSize);
                }
            }

            // Copy remaining results from buffer, if any.
            if (reportBufferCounter > 1)
            {
                reportBufferCounter--;
                int percentageCompleted = 100;
                WorkerProgress[] reportProgress = new WorkerProgress[reportBufferCounter];
                Array.Copy(progressBuffer, reportProgress, reportBufferCounter);
                worker.ReportProgress(percentageCompleted, reportProgress);
                Array.Clear(progressBuffer, 0, reportBufferCounter);
            }
        }

        private static IEnumerable<Regex> GenerateMaskPatterns(string fileMaskString)
        {
            string[] fileMasks = fileMaskString.Split(new[] { Environment.NewLine },
                StringSplitOptions.RemoveEmptyEntries);
            string[] processedFileMasks = Array.FindAll(fileMasks, mask => mask.Trim().Length > 0);
            if (processedFileMasks.Length == 0)
                processedFileMasks = new[] { "*.*" };

            List<Regex> maskPatterns = new List<Regex>(processedFileMasks.Length);
            foreach (string fileMask in processedFileMasks)
            {
                if (string.IsNullOrEmpty(fileMask))
                    continue;
                Regex maskPattern =
                    new Regex("^" + fileMask.Replace(".", "[.]").Replace("*", ".*").Replace("?", ".") + "$",
                        RegexOptions.IgnoreCase);
                maskPatterns.Add(maskPattern);
            }
            return maskPatterns;
        }

        private static bool SatisfiesMaskPatterns(string fileName, IEnumerable<Regex> maskPatterns)
        {
            foreach (Regex maskPattern in maskPatterns)
            {
                if (maskPattern.IsMatch(fileName))
                    return true;
            }
            return false;
        }

        private void ActionWorkerProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            WorkerProgress[] progresses = (WorkerProgress[])e.UserState;

            foreach (WorkerProgress progress in progresses)
            {
                if (progress == null)
                    break;
                ListViewItem resultItem = new ListViewItem(new[] { progress.Charset, progress.FileName, progress.DirectoryName }, -1);
                lstResults.Items.Add(resultItem);
                actionStatus.Text = progress.FileName;
            }

            actionProgress.Value = e.ProgressPercentage;
        }

        private void ActionWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (lstResults.Items.Count > 0)
            {
                foreach (ColumnHeader columnHeader in lstResults.Columns)
                    columnHeader.AutoResize(ColumnHeaderAutoResizeStyle.ColumnContent);
            }
            UpdateControlsOnActionDone();
        }
        #endregion

        #region Loading and saving of settings
        private void LoadSettings()
        {
            string settingsFileName = GetSettingsFileName();
            if (!File.Exists(settingsFileName))
                return;
            using (FileStream settingsFile = new FileStream(settingsFileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                object settingsInstance = formatter.Deserialize(settingsFile);
                _settings = (Settings)settingsInstance;
            }

            if (_settings.RecentDirectories != null && _settings.RecentDirectories.Count > 0)
            {
                foreach (string recentDirectory in _settings.RecentDirectories)
                    lstBaseDirectory.Items.Add(recentDirectory);
                lstBaseDirectory.SelectedIndex = 0;
            }
            else
                lstBaseDirectory.Text = Environment.CurrentDirectory;
            chkIncludeSubdirectories.Checked = _settings.IncludeSubdirectories;
            txtFileMasks.Text = _settings.FileMasks;
            if (_settings.ValidCharsets != null && _settings.ValidCharsets.Length > 0)
            {
                for (int i = 0; i < lstValidCharsets.Items.Count; i++)
                    if (Array.Exists(_settings.ValidCharsets,
                        charset => charset.Equals((string)lstValidCharsets.Items[i])))
                        lstValidCharsets.SetItemChecked(i, true);
            }

            _settings.WindowPosition?.ApplyTo(this);
        }

        private void SaveSettings()
        {
            if (_settings == null)
                _settings = new Settings();
            _settings.IncludeSubdirectories = chkIncludeSubdirectories.Checked;
            _settings.FileMasks = txtFileMasks.Text;

            _settings.ValidCharsets = new string[lstValidCharsets.CheckedItems.Count];
            for (int i = 0; i < lstValidCharsets.CheckedItems.Count; i++)
                _settings.ValidCharsets[i] = (string)lstValidCharsets.CheckedItems[i];

            _settings.WindowPosition = new WindowPosition { Left = Left, Top = Top, Width = Width, Height = Height };

            string settingsFileName = GetSettingsFileName();
            using (
                FileStream settingsFile = new FileStream(settingsFileName, FileMode.Create, FileAccess.Write,
                    FileShare.None))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(settingsFile, _settings);
                settingsFile.Flush();
            }
        }

        private static string GetSettingsFileName()
        {
            string dataDirectory = ApplicationDeployment.IsNetworkDeployed
                ? ApplicationDeployment.CurrentDeployment.DataDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(dataDirectory) || !Directory.Exists(dataDirectory))
                dataDirectory = Environment.CurrentDirectory;
            dataDirectory = Path.Combine(dataDirectory, "EncodingChecker");
            if (!Directory.Exists(dataDirectory))
                Directory.CreateDirectory(dataDirectory);
            return Path.Combine(dataDirectory, "Settings.bin");
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
            chkSelectDeselectAll.CheckState = CheckState.Unchecked;

            btnCancel.Visible = true;

            // stop drawing of the results list view control
            lstResults.BeginUpdate();
            lstResults.ListViewItemSorter = null;
            lstResults.ItemChecked -= OnResultItemChecked;
            lstResults.Items.Clear();

            actionProgress.Value = 0;
            actionProgress.Visible = true;
            actionStatus.Text = string.Empty;
        }

        private void UpdateControlsOnActionDone()
        {
            btnView.Enabled = true;
            btnValidate.Enabled = true;

            if (lstResults.Items.Count > 0)
            {
                lblConvert.Enabled = true;
                lstConvert.Enabled = true;
                btnConvert.Enabled = true;
                chkSelectDeselectAll.Enabled = true;

                if (lstValidCharsets.CheckedItems.Count > 0)
                {
                    string firstValidCharset = (string)lstValidCharsets.CheckedItems[0];
                    for (int i = 0; i < lstConvert.Items.Count; i++)
                    {
                        string convertCharset = (string)lstConvert.Items[i];
                        if (firstValidCharset.Equals(convertCharset, StringComparison.OrdinalIgnoreCase))
                        {
                            lstConvert.SelectedIndex = i;
                            break;
                        }
                    }
                }
            }

            btnCancel.Visible = false;

            // resume drawing of the results list view control
            lstResults.ListViewItemSorter = _lvwColumnSorter;
            lstResults.ItemChecked += OnResultItemChecked;
            lstResults.Sort();
            lstResults.EndUpdate();

            actionProgress.Visible = false;

            string statusMessage = _currentAction == CurrentAction.View
                ? "{0} files processed" : "{0} files do not have the correct encoding";
            actionStatus.Text = string.Format(statusMessage, lstResults.Items.Count);
        }

        private static IEnumerable<string> GetSupportedCharsets()
        {
            //Using reflection, figure out all the charsets that the UtfUnknown framework supports by reflecting
            //over all the strings constants in the UtfUnknown.Core.CodepageName class. These represent all the encodings
            //that can be detected by the program.
            Assembly assembly = Assembly.LoadFrom("EncodingUtils.dll");
            Type codepageName = assembly.GetType("UtfUnknown.Core.CodepageName");
            FieldInfo[] charsetConstants = codepageName.GetFields(BindingFlags.GetField | BindingFlags.Static | BindingFlags.NonPublic);
            foreach (FieldInfo charsetConstant in charsetConstants)
            {
                if (charsetConstant.FieldType == typeof(string))
                    yield return (string)charsetConstant.GetValue(null);
            }
        }

        private void ShowWarning(string message, params object[] args)
        {
            MessageBox.Show(this, string.Format(message, args), @"Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}