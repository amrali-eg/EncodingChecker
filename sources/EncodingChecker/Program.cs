using System;
using System.Threading;
using System.Windows.Forms;

namespace EncodingChecker
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.ThreadException += OnApplicationThreadException;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private static void OnApplicationThreadException(object sender, ThreadExceptionEventArgs e)
        {
            MessageBox.Show(e.Exception.Message, @"Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}