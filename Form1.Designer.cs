namespace PotaActivationTool
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            menuStrip1 = new MenuStrip();
            aboutToolStripMenuItem = new ToolStripMenuItem();
            helpToolStripMenuItem = new ToolStripMenuItem();
            labelState = new Label();
            pictureBoxLogo = new PictureBox();
            comboBoxState = new ComboBox();
            buttonLoadParks = new Button();
            buttonLoadAdif = new Button();
            buttonExportCsv = new Button();
            buttonExportExcel = new Button();
            buttonShowMap = new Button();
            checkBoxOfflineMap = new CheckBox();
            buttonMapOk = new Button();
            labelGps = new Label();
            comboBoxGps = new ComboBox();
            flowLayoutPanelButtons = new FlowLayoutPanel();
            textBoxWwffDate = new TextBox();
            progressBar1 = new ProgressBar();
            textBoxStatus = new TextBox();
            dataGridView1 = new DataGridView();
            labelSearch = new Label();
            textBoxSearch = new TextBox();
            menuStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dataGridView1).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pictureBoxLogo).BeginInit();
            SuspendLayout();
            // 
            // menuStrip1
            // 
            menuStrip1.Items.AddRange(new ToolStripItem[] { helpToolStripMenuItem, aboutToolStripMenuItem });
            menuStrip1.Location = new Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Size = new Size(1460, 24);
            menuStrip1.TabIndex = 10;
            menuStrip1.Text = "menuStrip1";
            // 
            // aboutToolStripMenuItem
            // 
            aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
            aboutToolStripMenuItem.Size = new Size(52, 20);
            aboutToolStripMenuItem.Text = "About";
            aboutToolStripMenuItem.Click += aboutToolStripMenuItem_Click;
            // 
            // helpToolStripMenuItem
            // 
            helpToolStripMenuItem.Name = "helpToolStripMenuItem";
            helpToolStripMenuItem.Size = new Size(44, 20);
            helpToolStripMenuItem.Text = "Help";
            helpToolStripMenuItem.Click += helpToolStripMenuItem_Click;
            // 
            // pictureBoxLogo
            // 
            // The program's logo, in the otherwise-empty top-right corner,
            // as tall as the header allows: from just under the menu bar to
            // just above the grid (y 25-175; the grid starts at 178). Image is
            // set in Form1's constructor from AppLogo (built into the exe).
            // Anchored right so it stays in the corner as the window resizes;
            // flowLayoutPanelButtons, progressBar1 and textBoxStatus all stop
            // short of it (see their Sizes) so none of them runs under it.
            pictureBoxLogo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            pictureBoxLogo.BackColor = Color.Transparent;
            pictureBoxLogo.Location = new Point(1298, 25);
            pictureBoxLogo.Name = "pictureBoxLogo";
            pictureBoxLogo.Size = new Size(150, 150);
            pictureBoxLogo.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBoxLogo.TabIndex = 19;
            pictureBoxLogo.TabStop = false;
            // 
            // labelState
            // 
            labelState.AutoSize = true;
            labelState.Location = new Point(12, 43);
            labelState.Name = "labelState";
            labelState.Size = new Size(36, 16);
            labelState.TabIndex = 9;
            labelState.Text = "State:";
            // 
            // comboBoxState
            // 
            comboBoxState.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxState.Location = new Point(55, 40);
            comboBoxState.Name = "comboBoxState";
            comboBoxState.Size = new Size(200, 24);
            comboBoxState.TabIndex = 8;
            //
            // buttonLoadParks
            //
            buttonLoadParks.Margin = new Padding(0, 0, 10, 10);
            buttonLoadParks.Name = "buttonLoadParks";
            buttonLoadParks.Size = new Size(150, 25);
            buttonLoadParks.TabIndex = 7;
            buttonLoadParks.Text = "Load Parks for State";
            buttonLoadParks.UseVisualStyleBackColor = true;
            buttonLoadParks.Click += buttonLoadParks_Click;
            //
            // buttonLoadAdif
            //
            buttonLoadAdif.Margin = new Padding(0, 0, 10, 10);
            buttonLoadAdif.Name = "buttonLoadAdif";
            buttonLoadAdif.Size = new Size(150, 25);
            buttonLoadAdif.TabIndex = 6;
            buttonLoadAdif.Text = "Load ADIF File...";
            buttonLoadAdif.UseVisualStyleBackColor = true;
            buttonLoadAdif.Click += buttonLoadAdif_Click;
            //
            // buttonExportCsv
            //
            buttonExportCsv.Margin = new Padding(0, 0, 10, 10);
            buttonExportCsv.Name = "buttonExportCsv";
            buttonExportCsv.Size = new Size(150, 25);
            buttonExportCsv.TabIndex = 5;
            buttonExportCsv.Text = "Export CSV...";
            buttonExportCsv.UseVisualStyleBackColor = true;
            buttonExportCsv.Click += buttonExportCsv_Click;
            //
            // buttonExportExcel
            //
            buttonExportExcel.Margin = new Padding(0, 0, 10, 10);
            buttonExportExcel.Name = "buttonExportExcel";
            buttonExportExcel.Size = new Size(150, 25);
            buttonExportExcel.TabIndex = 4;
            buttonExportExcel.Text = "Export Excel...";
            buttonExportExcel.UseVisualStyleBackColor = true;
            buttonExportExcel.Click += buttonExportExcel_Click;
            //
            // buttonShowMap
            //
            buttonShowMap.Margin = new Padding(0, 0, 10, 10);
            buttonShowMap.Name = "buttonShowMap";
            buttonShowMap.Size = new Size(150, 25);
            buttonShowMap.TabIndex = 3;
            buttonShowMap.Text = "Show Map...";
            buttonShowMap.UseVisualStyleBackColor = true;
            buttonShowMap.Click += buttonShowMap_Click;
            //
            // checkBoxOfflineMap
            //
            // This row sits under the button row (below Load ADIF File):
            // check Offline Map, then OK downloads it - see buttonMapOk_Click.
            // (Saving a copy of the map is in the map's own Layers panel.)
            checkBoxOfflineMap.AutoSize = true;
            checkBoxOfflineMap.Location = new Point(425, 74);
            checkBoxOfflineMap.Name = "checkBoxOfflineMap";
            checkBoxOfflineMap.Size = new Size(92, 20);
            checkBoxOfflineMap.TabIndex = 15;
            checkBoxOfflineMap.Text = "Offline Map";
            checkBoxOfflineMap.UseVisualStyleBackColor = true;
            checkBoxOfflineMap.CheckedChanged += MapOptionCheckBox_CheckedChanged;
            //
            // buttonMapOk
            //
            buttonMapOk.Enabled = false;
            buttonMapOk.Location = new Point(525, 71);
            buttonMapOk.Name = "buttonMapOk";
            buttonMapOk.Size = new Size(60, 25);
            buttonMapOk.TabIndex = 16;
            buttonMapOk.Text = "OK";
            buttonMapOk.UseVisualStyleBackColor = true;
            buttonMapOk.Click += buttonMapOk_Click;
            //
            // labelGps
            //
            labelGps.AutoSize = true;
            labelGps.Location = new Point(603, 75);
            labelGps.Name = "labelGps";
            labelGps.Size = new Size(34, 16);
            labelGps.TabIndex = 17;
            labelGps.Text = "GPS:";
            //
            // comboBoxGps
            //
            comboBoxGps.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxGps.Location = new Point(641, 71);
            comboBoxGps.Name = "comboBoxGps";
            comboBoxGps.Size = new Size(160, 24);
            comboBoxGps.TabIndex = 18;
            comboBoxGps.DropDown += comboBoxGps_DropDown;
            comboBoxGps.SelectionChangeCommitted += comboBoxGps_SelectionChangeCommitted;
            //
            // flowLayoutPanelButtons
            //
            // Wraps the action buttons onto additional lines instead of
            // clipping them or forcing a horizontal scrollbar when the
            // window (or the screen it's on) is narrower than one row
            // needs. Anchor stretches its width with the form; AutoSize +
            // WrapContents grows its height as buttons wrap to more rows.
            // Form1.cs's RepositionBelowButtonRow keeps every control below
            // it (search box, WWFF date, progress bar, the grid) from
            // overlapping as that height changes - plain Anchor alone only
            // handles a control's own position/size relative to the form's
            // edges, not reflowing around a sibling that grew taller.
            flowLayoutPanelButtons.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            flowLayoutPanelButtons.AutoSize = true;
            flowLayoutPanelButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flowLayoutPanelButtons.Controls.Add(buttonLoadParks);
            flowLayoutPanelButtons.Controls.Add(buttonLoadAdif);
            flowLayoutPanelButtons.Controls.Add(buttonExportCsv);
            flowLayoutPanelButtons.Controls.Add(buttonExportExcel);
            flowLayoutPanelButtons.Controls.Add(buttonShowMap);
            flowLayoutPanelButtons.Location = new Point(265, 36);
            flowLayoutPanelButtons.Margin = new Padding(0);
            flowLayoutPanelButtons.Name = "flowLayoutPanelButtons";
            flowLayoutPanelButtons.Size = new Size(1025, 31);
            flowLayoutPanelButtons.TabIndex = 13;
            flowLayoutPanelButtons.WrapContents = true;
            //
            // textBoxWwffDate
            //
            // A borderless, read-only TextBox rather than a Label - looks the same
            // (colors matched to the form so there's no visible box) but, unlike a
            // Label, its text can be selected and copied to the clipboard.
            textBoxWwffDate.AutoSize = false;
            textBoxWwffDate.BackColor = SystemColors.Control;
            textBoxWwffDate.BorderStyle = BorderStyle.None;
            textBoxWwffDate.ForeColor = SystemColors.ControlText;
            textBoxWwffDate.Location = new Point(814, 70);
            textBoxWwffDate.Name = "textBoxWwffDate";
            textBoxWwffDate.ReadOnly = true;
            textBoxWwffDate.Size = new Size(220, 16);
            textBoxWwffDate.TabIndex = 12;
            textBoxWwffDate.TabStop = false;
            textBoxWwffDate.Text = "WWFF data: not loaded";
            //
            // progressBar1
            //
            progressBar1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            progressBar1.Location = new Point(12, 105);
            progressBar1.Name = "progressBar1";
            progressBar1.Size = new Size(1278, 15);
            progressBar1.TabIndex = 2;
            // 
            // textBoxStatus
            //
            // Borderless, read-only TextBox rather than a Label - same reasoning as
            // textBoxWwffDate below: looks identical, but status/result text (park
            // counts, confirmations, error summaries) can now be selected and copied.
            textBoxStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            textBoxStatus.AutoSize = false;
            textBoxStatus.BackColor = SystemColors.Control;
            textBoxStatus.BorderStyle = BorderStyle.None;
            textBoxStatus.ForeColor = SystemColors.ControlText;
            textBoxStatus.Location = new Point(12, 125);
            textBoxStatus.Name = "textBoxStatus";
            textBoxStatus.ReadOnly = true;
            textBoxStatus.Size = new Size(1278, 16);
            textBoxStatus.TabIndex = 1;
            textBoxStatus.TabStop = false;
            textBoxStatus.Text = "Ready.";
            // 
            // dataGridView1
            // 
            dataGridView1.AllowUserToAddRows = false;
            dataGridView1.AllowUserToDeleteRows = false;
            dataGridView1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            dataGridView1.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithAutoHeaderText;
            dataGridView1.Location = new Point(12, 178);
            dataGridView1.MultiSelect = true;
            dataGridView1.Name = "dataGridView1";
            dataGridView1.ReadOnly = true;
            dataGridView1.RowHeadersWidth = 25;
            dataGridView1.SelectionMode = DataGridViewSelectionMode.CellSelect;
            dataGridView1.Size = new Size(1436, 442);
            dataGridView1.TabIndex = 0;
            // 
            // labelSearch
            // 
            labelSearch.AutoSize = true;
            labelSearch.Location = new Point(12, 75);
            labelSearch.Name = "labelSearch";
            labelSearch.Size = new Size(45, 16);
            labelSearch.TabIndex = 10;
            labelSearch.Text = "Search:";
            // 
            // textBoxSearch
            // 
            textBoxSearch.Enabled = false;
            textBoxSearch.Location = new Point(65, 72);
            textBoxSearch.Name = "textBoxSearch";
            textBoxSearch.Size = new Size(300, 23);
            textBoxSearch.TabIndex = 11;
            textBoxSearch.TextChanged += textBoxSearch_TextChanged;
            //
            // Form1
            //
            // Default size still comfortably fits the grid's columns
            // (including colState - hidden most of the time, but shown for
            // a state with an out-of-state/multi-state Xfer, see
            // buttonLoadParks_Click) with no horizontal scrollbar. Unlike
            // before, MinimumSize below no longer has to stay this wide
            // just to keep the button row from clipping - flowLayoutPanelButtons
            // wraps buttons onto more lines instead, so the window (and the
            // grid) can shrink much further for a smaller screen; the grid
            // may need its own horizontal scrollbar at the very smallest
            // sizes, which is an acceptable trade-off there.
            ClientSize = new Size(1460, 639);
            Controls.Add(pictureBoxLogo);
            Controls.Add(dataGridView1);
            Controls.Add(textBoxStatus);
            Controls.Add(progressBar1);
            Controls.Add(textBoxWwffDate);
            Controls.Add(flowLayoutPanelButtons);
            Controls.Add(checkBoxOfflineMap);
            Controls.Add(buttonMapOk);
            Controls.Add(labelGps);
            Controls.Add(comboBoxGps);
            Controls.Add(comboBoxState);
            Controls.Add(labelState);
            Controls.Add(labelSearch);
            Controls.Add(textBoxSearch);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            MinimumSize = new Size(600, 500);
            Name = "Form1";
            Text = "POTA Activation Tool";
            Load += Form1_Load;
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)dataGridView1).EndInit();
            ((System.ComponentModel.ISupportInitialize)pictureBoxLogo).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem helpToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem aboutToolStripMenuItem;
        private System.Windows.Forms.Label labelState;
        private System.Windows.Forms.PictureBox pictureBoxLogo;
        private System.Windows.Forms.ComboBox comboBoxState;
        private System.Windows.Forms.Button buttonLoadParks;
        private System.Windows.Forms.Button buttonLoadAdif;
        private System.Windows.Forms.Button buttonExportCsv;
        private System.Windows.Forms.Button buttonExportExcel;
        private System.Windows.Forms.Button buttonShowMap;
        private System.Windows.Forms.CheckBox checkBoxOfflineMap;
        private System.Windows.Forms.Button buttonMapOk;
        private System.Windows.Forms.Label labelGps;
        private System.Windows.Forms.ComboBox comboBoxGps;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanelButtons;
        private System.Windows.Forms.TextBox textBoxWwffDate;
        private System.Windows.Forms.ProgressBar progressBar1;
        private System.Windows.Forms.TextBox textBoxStatus;
        private System.Windows.Forms.DataGridView dataGridView1;
        private System.Windows.Forms.Label labelSearch;
        private System.Windows.Forms.TextBox textBoxSearch;
    }
}
