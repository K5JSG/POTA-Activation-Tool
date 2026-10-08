namespace PotaActivationTool
{
    partial class AboutForm
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
            this.labelAppName = new System.Windows.Forms.Label();
            this.textBoxVersion = new System.Windows.Forms.TextBox();
            this.textBoxPublished = new System.Windows.Forms.TextBox();
            this.textBoxLicense = new System.Windows.Forms.TextBox();
            this.buttonOk = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // labelAppName
            //
            this.labelAppName.AutoSize = true;
            this.labelAppName.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this.labelAppName.Location = new System.Drawing.Point(20, 20);
            this.labelAppName.Name = "labelAppName";
            this.labelAppName.Size = new System.Drawing.Size(340, 27);
            this.labelAppName.Text = "POTA Activation Tool";
            //
            // textBoxVersion
            //
            // Borderless, read-only TextBoxes rather than Labels - look identical,
            // but the version/build-date text can be selected and copied (e.g. into
            // a bug report).
            this.textBoxVersion.AutoSize = false;
            this.textBoxVersion.BackColor = System.Drawing.SystemColors.Control;
            this.textBoxVersion.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.textBoxVersion.ForeColor = System.Drawing.SystemColors.ControlText;
            this.textBoxVersion.Location = new System.Drawing.Point(22, 62);
            this.textBoxVersion.Name = "textBoxVersion";
            this.textBoxVersion.ReadOnly = true;
            this.textBoxVersion.Size = new System.Drawing.Size(300, 15);
            this.textBoxVersion.TabIndex = 4;
            this.textBoxVersion.TabStop = false;
            this.textBoxVersion.Text = "Version 0.0.0";
            //
            // textBoxPublished
            //
            this.textBoxPublished.AutoSize = false;
            this.textBoxPublished.BackColor = System.Drawing.SystemColors.Control;
            this.textBoxPublished.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.textBoxPublished.ForeColor = System.Drawing.SystemColors.ControlText;
            this.textBoxPublished.Location = new System.Drawing.Point(22, 82);
            this.textBoxPublished.Name = "textBoxPublished";
            this.textBoxPublished.ReadOnly = true;
            this.textBoxPublished.Size = new System.Drawing.Size(300, 15);
            this.textBoxPublished.TabIndex = 5;
            this.textBoxPublished.TabStop = false;
            this.textBoxPublished.Text = "Published: ";
            //
            // textBoxLicense
            //
            this.textBoxLicense.Location = new System.Drawing.Point(22, 108);
            this.textBoxLicense.Multiline = true;
            this.textBoxLicense.Name = "textBoxLicense";
            this.textBoxLicense.ReadOnly = true;
            this.textBoxLicense.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBoxLicense.Size = new System.Drawing.Size(440, 150);
            this.textBoxLicense.TabIndex = 3;
            //
            // buttonOk
            //
            this.buttonOk.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonOk.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.buttonOk.Location = new System.Drawing.Point(387, 272);
            this.buttonOk.Name = "buttonOk";
            this.buttonOk.Size = new System.Drawing.Size(75, 25);
            this.buttonOk.Text = "OK";
            this.buttonOk.UseVisualStyleBackColor = true;
            //
            // AboutForm
            //
            this.AcceptButton = this.buttonOk;
            this.ClientSize = new System.Drawing.Size(484, 310);
            this.Controls.Add(this.buttonOk);
            this.Controls.Add(this.textBoxLicense);
            this.Controls.Add(this.textBoxPublished);
            this.Controls.Add(this.textBoxVersion);
            this.Controls.Add(this.labelAppName);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "AboutForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "About POTA Activation Tool";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.Label labelAppName;
        private System.Windows.Forms.TextBox textBoxVersion;
        private System.Windows.Forms.TextBox textBoxPublished;
        private System.Windows.Forms.TextBox textBoxLicense;
        private System.Windows.Forms.Button buttonOk;
    }
}