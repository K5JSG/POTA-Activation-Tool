namespace PotaActivationTool
{
    partial class HelpForm
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
            this.labelTitle = new System.Windows.Forms.Label();
            this.textBoxHelp = new System.Windows.Forms.RichTextBox();
            this.buttonOk = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // labelTitle
            //
            this.labelTitle.AutoSize = true;
            this.labelTitle.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this.labelTitle.Location = new System.Drawing.Point(20, 20);
            this.labelTitle.Name = "labelTitle";
            this.labelTitle.Size = new System.Drawing.Size(340, 27);
            this.labelTitle.Text = "POTA Activation Tool Help";
            //
            // textBoxHelp
            //
            this.textBoxHelp.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.textBoxHelp.Location = new System.Drawing.Point(22, 62);
            this.textBoxHelp.Name = "textBoxHelp";
            this.textBoxHelp.ReadOnly = true;
            this.textBoxHelp.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical;
            this.textBoxHelp.DetectUrls = true;
            this.textBoxHelp.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.textBoxHelp.Size = new System.Drawing.Size(536, 400);
            this.textBoxHelp.TabIndex = 1;
            this.textBoxHelp.Text = "";
            //
            // buttonOk
            //
            this.buttonOk.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonOk.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.buttonOk.Location = new System.Drawing.Point(483, 472);
            this.buttonOk.Name = "buttonOk";
            this.buttonOk.Size = new System.Drawing.Size(75, 25);
            this.buttonOk.Text = "OK";
            this.buttonOk.UseVisualStyleBackColor = true;
            //
            // HelpForm
            //
            this.AcceptButton = this.buttonOk;
            this.ClientSize = new System.Drawing.Size(580, 510);
            this.Controls.Add(this.buttonOk);
            this.Controls.Add(this.textBoxHelp);
            this.Controls.Add(this.labelTitle);
            this.MinimumSize = new System.Drawing.Size(420, 320);
            this.Name = "HelpForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "POTA Activation Tool Help";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.Label labelTitle;
        private System.Windows.Forms.RichTextBox textBoxHelp;
        private System.Windows.Forms.Button buttonOk;
    }
}
