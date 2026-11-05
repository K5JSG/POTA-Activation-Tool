namespace PotaActivationTool
{
    // Settings (menu bar): currently just the ADIF log to use every time. With
    // a file chosen, Form1 hides its Load ADIF File button and reads that file
    // after every Load Parks for State; left blank, the button shows as before.
    public class SettingsForm : Form
    {
        private readonly TextBox _adifPath = new() { Width = 460, Anchor = AnchorStyles.Left | AnchorStyles.Right };

        public string AdifPath => _adifPath.Text.Trim();

        public SettingsForm(AppSettings settings)
        {
            Text = "Settings";
            Icon = AppLogo.Icon;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            AutoScaleMode = AutoScaleMode.Dpi;
            Padding = new Padding(10);

            _adifPath.Text = settings.AdifPath;

            var browse = new Button { Text = "Browse...", AutoSize = true };
            browse.Click += (s, e) => Browse();
            var clear = new Button { Text = "Clear", AutoSize = true };
            clear.Click += (s, e) => _adifPath.Clear();

            var ok = new Button { Text = "OK", AutoSize = true, MinimumSize = new Size(80, 0) };
            ok.Click += (s, e) => Ok();
            var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(80, 0), DialogResult = DialogResult.Cancel };
            AcceptButton = ok;
            CancelButton = cancel;

            var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Fill };
            layout.Controls.Add(new Label { Text = "ADIF log file:", AutoSize = true, Margin = new Padding(3, 3, 3, 0) }, 0, 0);
            layout.SetColumnSpan(layout.GetControlFromPosition(0, 0)!, 3);
            layout.Controls.Add(_adifPath, 0, 1);
            layout.Controls.Add(browse, 1, 1);
            layout.Controls.Add(clear, 2, 1);
            var note = new Label
            {
                Text = "With a file chosen, it is loaded automatically every time you load parks, and the " +
                       "Load ADIF File button is hidden. Leave it blank to pick a file with that button instead.",
                AutoSize = true,
                MaximumSize = new Size(620, 0),
                Margin = new Padding(3, 6, 3, 12)
            };
            layout.Controls.Add(note, 0, 2);
            layout.SetColumnSpan(note, 3);
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = Padding.Empty };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            layout.Controls.Add(buttons, 0, 3);
            layout.SetColumnSpan(buttons, 3);
            Controls.Add(layout);
        }

        private void Browse()
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "ADIF files (*.adi;*.adif)|*.adi;*.adif|All files (*.*)|*.*",
                Title = "Select your ADIF log file"
            };
            if (File.Exists(AdifPath)) dlg.FileName = AdifPath;
            if (dlg.ShowDialog(this) == DialogResult.OK) _adifPath.Text = dlg.FileName;
        }

        private void Ok()
        {
            if (AdifPath.Length > 0 && !File.Exists(AdifPath))
            {
                MessageBox.Show(this, "That ADIF file doesn't exist:\n" + AdifPath, "Settings");
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }
}
