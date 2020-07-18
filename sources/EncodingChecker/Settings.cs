using System;
using System.Collections.ObjectModel;
using System.Windows.Forms;

namespace EncodingChecker
{
    [Serializable]
    public sealed class Settings
    {
        public WindowPosition WindowPosition = new WindowPosition();

        public RecentDirectories RecentDirectories = new RecentDirectories();
        public bool IncludeSubdirectories = true;

        public string FileMasks;
        public string[] ValidCharsets;
    }

    [Serializable]
    public sealed class WindowPosition
    {
        public int Left = -1;
        public int Top = -1;
        public int Width = -1;
        public int Height = -1;
        public bool Maximized;

        public void ApplyTo(Form form)
        {
            if (Left >= 0 && Top >= 0 && Width > 0 && Height > 0)
                form.SetBounds(Left, Top, Width, Height);
        }
    }

    [Serializable]
    public sealed class RecentDirectories : Collection<string>
    {
        protected override void InsertItem(int index, string item)
        {
            for (int i = Count - 1; i >= 0; i--)
            {
                if (this[i].Equals(item, StringComparison.OrdinalIgnoreCase))
                    RemoveAt(i);
            }

            base.InsertItem(0, item);

            if (Count > 10)
            {
                for (int i = Count - 1; i >= 10; i--)
                    RemoveAt(i);
            }
        }
    }
}