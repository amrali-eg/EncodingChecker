using System.Diagnostics;
using System.Windows.Forms;

namespace EncodingChecker
{
    public partial class AboutForm : Form
    {
        public AboutForm()
        {
            InitializeComponent();
        }

        private void OnFormLoad(object sender, System.EventArgs e)
        {
            lblHomepage.Links[0].LinkData = "https://github.com/amrali-eg/EncodingChecker";
            lblAuthor.Links[0].LinkData = "https://github.com/JeevanJames";
            lblLicense.Links[0].LinkData = "http://www.mozilla.org/MPL/MPL-1.1.html";
            lblCreditsUde.Links[0].LinkData = "https://github.com/CharsetDetector/UTF-unknown";
            lblCreditsCodePlex.Links[0].LinkData = "http://encodingchecker.codeplex.com";
        }

        private void OnLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            string url = (string)e.Link.LinkData;
            ProcessStartInfo startInfo = new ProcessStartInfo(url) {UseShellExecute = true};
            Process.Start(startInfo);
        }
    }
}