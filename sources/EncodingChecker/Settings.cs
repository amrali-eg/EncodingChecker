using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
    /// Defaults to <see langword="true"/> so the original remains recoverable if a
    /// conversion needs review. EC also writes a matching <c>.ecmeta.json</c> record
    /// when it creates a backup.
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

    public void ApplyTo(Form form) =>
        ApplyTo(form, Screen.AllScreens.Select(s => s.WorkingArea));

    /// <summary>
    /// Restores the saved bounds, but only onto a monitor that is actually there.
    /// </summary>
    /// <remarks>
    /// The monitor list is a parameter so the decision can be tested against layouts
    /// this machine does not have - a saved position is only ever wrong on a desktop
    /// other than the one that saved it.
    /// </remarks>
    internal void ApplyTo(Form form, IEnumerable<Rectangle> workingAreas)
    {
        ArgumentNullException.ThrowIfNull(form);

        if (Width <= 0 || Height <= 0)
            return;

        if (!IsReachable(new Rectangle(Left, Top, Width, Height), workingAreas))
            return;

        form.SetBounds(Left, Top, Width, Height);
    }

    /// <summary>
    /// Whether enough of the window would land on a monitor to be usable.
    /// </summary>
    /// <remarks>
    /// A position saved on a monitor that is no longer attached restores the window
    /// where nothing can reach it, and there is no way back from inside the app.
    /// <para>
    /// Testing the screens is also what allows negative coordinates. Rejecting those
    /// outright, as this used to, discarded perfectly good positions on any setup with
    /// a monitor placed left of or above the primary one.
    /// </para>
    /// </remarks>
    internal static bool IsReachable(Rectangle bounds, IEnumerable<Rectangle> workingAreas)
    {
        // Enough of the title bar to grab, rather than a single pixel of overlap.
        const int MinimumVisible = 80;

        foreach (Rectangle area in workingAreas)
        {
            Rectangle overlap = Rectangle.Intersect(area, bounds);

            if (overlap.Width >= Math.Min(MinimumVisible, bounds.Width) &&
                overlap.Height >= Math.Min(MinimumVisible, bounds.Height))
            {
                return true;
            }
        }

        return false;
    }
}
