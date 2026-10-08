using System;
using System.IO;
using System.Windows.Forms;

namespace PotaActivationTool
{
    public partial class AboutForm : Form
    {
        public AboutForm()
        {
            InitializeComponent();
            Icon = AppLogo.Icon ?? Icon;

            textBoxVersion.Text = $"Version {Application.ProductVersion}";

            try
            {
                DateTime publishedDate = File.GetLastWriteTime(Application.ExecutablePath);
                textBoxPublished.Text = $"Published: {publishedDate:MMMM d, yyyy}";
            }
            catch
            {
                textBoxPublished.Text = "Published: (unknown)";
            }

            textBoxLicense.Text =
                "Copyright \u00A9 2026 Jeremy S. Gaynor, K5JSG. All rights reserved." + Environment.NewLine + Environment.NewLine +
                "This software is provided \"as is\", without warranty of any kind, and the author is not liable for any damages arising from its use.";
        }
    }
}