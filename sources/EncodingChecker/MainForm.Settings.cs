using System;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace EncodingChecker;

public partial class MainForm
{
    private void LoadSettings()
    {
        string settingsFileName = GetSettingsFileName();
        Settings settings = new();

        if (!File.Exists(settingsFileName))
        {
            ApplySettings(settings);
            return;
        }

        try
        {
            using var settingsFile = new FileStream(
                settingsFileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            var serializer = new XmlSerializer(typeof(Settings));
            settings = serializer.Deserialize(settingsFile) as Settings ?? new Settings();
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Settings are optional; defaults keep the application usable.
            settings = new Settings();
        }

        ApplySettings(settings);
    }

    private void ApplySettings(Settings settings)
    {
        _settings = settings;

        if (settings.RecentDirectories.Count > 0)
        {
            foreach (string recentDirectory in settings.RecentDirectories)
                lstBaseDirectory.Items.Add(recentDirectory);

            lstBaseDirectory.SelectedIndex = 0;
        }
        else
        {
            lstBaseDirectory.Text = Environment.CurrentDirectory;
        }

        chkIncludeSubdirectories.Checked = settings.IncludeSubdirectories;
        chkCreateBackup.Checked = settings.CreateBackup;

        // XML stores newlines as LF; the multiline Windows control displays CRLF.
        txtFileMasks.Text = settings.FileMasks.Replace("\r\n", "\n").Replace("\n", "\r\n");

        for (int i = 0; i < lstValidCharsets.Items.Count; i++)
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

        settings.WindowPosition.ApplyTo(this);
    }

    private void SaveSettings()
    {
        _settings.IncludeSubdirectories = chkIncludeSubdirectories.Checked;
        _settings.CreateBackup = chkCreateBackup.Checked;
        _settings.FileMasks = txtFileMasks.Text;
        _settings.ValidCharsets = new string[lstValidCharsets.CheckedItems.Count];

        for (int i = 0; i < lstValidCharsets.CheckedItems.Count; i++)
            _settings.ValidCharsets[i] = (string)lstValidCharsets.CheckedItems[i]!;

        _settings.WindowPosition = new WindowPosition
        {
            Left = Left,
            Top = Top,
            Width = Width,
            Height = Height,
        };

        try
        {
            using var settingsFile = new FileStream(
                GetSettingsFileName(), FileMode.Create, FileAccess.Write, FileShare.None);
            new XmlSerializer(typeof(Settings)).Serialize(settingsFile, _settings);
            settingsFile.Flush();
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Closing the application must not depend on saving preferences.
        }
    }

    private static string GetSettingsFileName()
    {
        string dataDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);

        if (string.IsNullOrEmpty(dataDirectory) || !Directory.Exists(dataDirectory))
            dataDirectory = Environment.CurrentDirectory;

        dataDirectory = Path.Combine(dataDirectory, "EncodingChecker");
        Directory.CreateDirectory(dataDirectory);
        return Path.Combine(dataDirectory, "Settings.xml");
    }
}
