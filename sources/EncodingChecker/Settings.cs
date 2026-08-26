using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace EncodingChecker;

public sealed class Settings
{
    public WindowPosition WindowPosition = new();

    public List<string> RecentDirectories = [];
    public bool IncludeSubdirectories = true;

    /// <summary>
    /// Whether to copy each file to "&lt;file&gt;.bak" before overwriting it.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="true"/>. Conversion from a Unicode or ASCII source is
    /// safe - across a 5,078-file audit not one such file came out with different text -
    /// but roughly one in five files converted from a legacy code page did, because
    /// single-byte code pages are mutually decodable and nothing in the bytes says which
    /// one was intended. Such a conversion is usually reversible, but only for someone who
    /// still knows which codec was used, and that is recorded solely in the conversion
    /// report. Defaulting this on keeps the original recoverable without it.
    /// </remarks>
    public bool CreateBackup = true;

    public string FileMasks = string.Empty;
    public string[] ValidCharsets = [];

    /// <summary>
    /// Adds a directory to the front of the most-recently-used list, removing any existing
    /// occurrence and trimming the list to the 10 most recent entries.
    /// </summary>
    public void AddRecentDirectory(string directory)
    {
        for (int i = RecentDirectories.Count - 1; i >= 0; i--)
        {
            if (RecentDirectories[i].Equals(directory, StringComparison.OrdinalIgnoreCase))
                RecentDirectories.RemoveAt(i);
        }

        RecentDirectories.Insert(0, directory);

        if (RecentDirectories.Count > 10)
            RecentDirectories.RemoveRange(10, RecentDirectories.Count - 10);
    }
}

public sealed class WindowPosition
{
    public int Left = -1;
    public int Top = -1;
    public int Width = -1;
    public int Height = -1;

    public void ApplyTo(Form form)
    {
        if (Left >= 0 && Top >= 0 && Width > 0 && Height > 0)
            form.SetBounds(Left, Top, Width, Height);
    }
}
