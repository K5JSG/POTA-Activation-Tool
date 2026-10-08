using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PotaActivationTool
{
    public partial class HelpForm : Form
    {
        // The name of the file we look for next to the .exe. You can open this file
        // in WordPad (or Word) any time and change the instructions, including
        // adding real clickable hyperlinks - nothing needs to be recompiled for
        // your edits to show up.
        private const string HelpFileName = "HelpContent.rtf";

        public HelpForm()
        {
            InitializeComponent();
            Icon = AppLogo.Icon ?? Icon;
            LoadHelpContent();

            // HelpContent.rtf is authored (in WordPad/Word) assuming a normal
            // white page, with its own explicit black text color baked into the
            // file - that color wins over the app's dark mode regardless of this
            // control's own background, so leaving the background dark would
            // make the text nearly unreadable. Keeping this one control a fixed
            // white page - like a document viewer would - avoids that instead of
            // fighting arbitrary user-edited RTF content.
            textBoxHelp.BackColor = Color.White;
            textBoxHelp.ForeColor = Color.Black;

            textBoxHelp.LinkClicked += TextBoxHelp_LinkClicked;
        }

        private void TextBoxHelp_LinkClicked(object? sender, LinkClickedEventArgs e)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = e.LinkText,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open that link: " + ex.Message);
            }
        }

        private void LoadHelpContent()
        {
            try
            {
                // Application.StartupPath is the folder the .exe lives in, so the
                // help file just needs to sit right alongside
                // POTA Activation Tool.exe.
                string path = Path.Combine(Application.StartupPath, HelpFileName);

                if (File.Exists(path))
                {
                    textBoxHelp.LoadFile(path, RichTextBoxStreamType.RichText);
                    textBoxHelp.DetectUrls = true;
                    return;
                }

                textBoxHelp.Text = "Help file not found." + Environment.NewLine + Environment.NewLine +
                       $"Expected to find it here:{Environment.NewLine}{path}" + Environment.NewLine +
                       Environment.NewLine +
                       $"Create a file named \"{HelpFileName}\" in that folder (WordPad or Word can save one) and put your instructions in it.";
            }
            catch (Exception ex)
            {
                textBoxHelp.Text = "Could not load the help file: " + ex.Message;
            }
        }
    }
}