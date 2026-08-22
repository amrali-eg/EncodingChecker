using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;

namespace EncodingChecker;

public partial class AboutForm : Form
{
    public AboutForm()
    {
        InitializeComponent();
    }

    private void OnFormLoad(object? sender, System.EventArgs e)
    {
        // Overrides the designer's static text so this can't drift from the real version.
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version is not null)
            lblVersion.Text = $"Version {version.Major}.{version.Minor}";

        lblHomepage.Links[0].LinkData = "https://github.com/amrali-eg/EncodingChecker";
        lblAuthor.Links[0].LinkData = "https://github.com/JeevanJames";
        lblLicense.Links[0].LinkData = "https://www.mozilla.org/en-US/MPL/2.0/";
        lblCreditsUde.Links[0].LinkData = "https://github.com/CharsetDetector/UTF-unknown";
        lblCreditsCodePlex.Links[0].LinkData = "http://encodingchecker.codeplex.com";
    }

    private void OnLinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        string url = (string)e.Link!.LinkData!;
        var startInfo = new ProcessStartInfo(url) {UseShellExecute = true};
        Process.Start(startInfo);
    }
}
