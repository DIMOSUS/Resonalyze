namespace Resonalyze
{
    partial class DspProcessorDialog
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
            }

            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            labelCaption = new Label();
            labelModel = new Label();
            comboBoxModel = new DarkComboBox();
            labelSampleRate = new Label();
            comboBoxSampleRate = new DarkComboBox();
            labelQConvention = new Label();
            comboBoxQConvention = new DarkComboBox();
            labelStatus = new Label();
            labelHint = new Label();
            buttonOk = new ReleaseClickButton();
            buttonCancel = new ReleaseClickButton();
            SuspendLayout();
            //
            // labelCaption
            //
            labelCaption.AutoSize = true;
            labelCaption.ForeColor = Color.FromArgb(210, 214, 222);
            labelCaption.Location = new Point(12, 12);
            labelCaption.MaximumSize = new Size(456, 0);
            labelCaption.Name = "labelCaption";
            labelCaption.Size = new Size(456, 30);
            labelCaption.TabIndex = 0;
            labelCaption.Text = "The processor this project is designed for. Its rate is what every " +
                "simulated filter is built at — the measurements keep their own.";
            //
            // labelModel
            //
            labelModel.AutoSize = true;
            labelModel.ForeColor = Color.FromArgb(185, 190, 200);
            labelModel.Location = new Point(12, 60);
            labelModel.Name = "labelModel";
            labelModel.Size = new Size(43, 15);
            labelModel.TabIndex = 1;
            labelModel.Text = "Model:";
            //
            // comboBoxModel
            //
            comboBoxModel.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxModel.ForeColor = Color.White;
            comboBoxModel.Location = new Point(148, 56);
            comboBoxModel.MaxDropDownItems = 14;
            comboBoxModel.MinimumSize = new Size(36, 19);
            comboBoxModel.Name = "comboBoxModel";
            comboBoxModel.Size = new Size(320, 23);
            comboBoxModel.TabIndex = 2;
            //
            // labelSampleRate
            //
            labelSampleRate.AutoSize = true;
            labelSampleRate.ForeColor = Color.FromArgb(185, 190, 200);
            labelSampleRate.Location = new Point(12, 94);
            labelSampleRate.Name = "labelSampleRate";
            labelSampleRate.Size = new Size(95, 15);
            labelSampleRate.TabIndex = 3;
            labelSampleRate.Text = "Processing rate:";
            //
            // comboBoxSampleRate
            //
            comboBoxSampleRate.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxSampleRate.ForeColor = Color.White;
            comboBoxSampleRate.Location = new Point(148, 90);
            comboBoxSampleRate.MinimumSize = new Size(36, 19);
            comboBoxSampleRate.Name = "comboBoxSampleRate";
            comboBoxSampleRate.Size = new Size(230, 23);
            comboBoxSampleRate.TabIndex = 4;
            //
            // labelQConvention
            //
            labelQConvention.AutoSize = true;
            labelQConvention.ForeColor = Color.FromArgb(185, 190, 200);
            labelQConvention.Location = new Point(12, 128);
            labelQConvention.Name = "labelQConvention";
            labelQConvention.Size = new Size(80, 15);
            labelQConvention.TabIndex = 5;
            labelQConvention.Text = "PEQ Q reads:";
            //
            // comboBoxQConvention
            //
            comboBoxQConvention.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxQConvention.ForeColor = Color.White;
            comboBoxQConvention.Location = new Point(148, 124);
            comboBoxQConvention.MinimumSize = new Size(36, 19);
            comboBoxQConvention.Name = "comboBoxQConvention";
            comboBoxQConvention.Size = new Size(320, 23);
            comboBoxQConvention.TabIndex = 6;
            //
            // labelStatus
            //
            labelStatus.AutoSize = true;
            labelStatus.ForeColor = Color.FromArgb(185, 190, 200);
            labelStatus.Location = new Point(12, 164);
            labelStatus.MaximumSize = new Size(456, 0);
            labelStatus.Name = "labelStatus";
            labelStatus.Size = new Size(456, 60);
            labelStatus.TabIndex = 7;
            labelStatus.Text = "status";
            //
            // labelHint
            //
            labelHint.AutoSize = true;
            labelHint.ForeColor = Color.FromArgb(150, 156, 168);
            labelHint.Location = new Point(12, 248);
            labelHint.MaximumSize = new Size(456, 0);
            labelHint.Name = "labelHint";
            labelHint.Size = new Size(456, 30);
            labelHint.TabIndex = 8;
            labelHint.Text = "A PEQ bank handed to the EQ Wizard carries this processor with " +
                "it, and is realized there at its rate.";
            //
            // buttonOk
            //
            buttonOk.BackColor = Color.FromArgb(46, 51, 67);
            buttonOk.DialogResult = DialogResult.OK;
            buttonOk.FlatStyle = FlatStyle.Popup;
            buttonOk.ForeColor = Color.White;
            buttonOk.Location = new Point(292, 302);
            buttonOk.Name = "buttonOk";
            buttonOk.Size = new Size(84, 26);
            buttonOk.TabIndex = 9;
            buttonOk.Text = "OK";
            buttonOk.UseVisualStyleBackColor = false;
            //
            // buttonCancel
            //
            buttonCancel.DialogResult = DialogResult.Cancel;
            buttonCancel.FlatStyle = FlatStyle.Popup;
            buttonCancel.ForeColor = Color.White;
            buttonCancel.Location = new Point(384, 302);
            buttonCancel.Name = "buttonCancel";
            buttonCancel.Size = new Size(84, 26);
            buttonCancel.TabIndex = 10;
            buttonCancel.Text = "Cancel";
            buttonCancel.UseVisualStyleBackColor = true;
            //
            // DspProcessorDialog
            //
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(40, 44, 54);
            ClientSize = new Size(480, 340);
            Controls.Add(labelCaption);
            Controls.Add(labelModel);
            Controls.Add(comboBoxModel);
            Controls.Add(labelSampleRate);
            Controls.Add(comboBoxSampleRate);
            Controls.Add(labelQConvention);
            Controls.Add(comboBoxQConvention);
            Controls.Add(labelStatus);
            Controls.Add(labelHint);
            Controls.Add(buttonOk);
            Controls.Add(buttonCancel);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "DspProcessorDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "DSP processor";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label labelCaption;
        private Label labelModel;
        private DarkComboBox comboBoxModel;
        private Label labelSampleRate;
        private DarkComboBox comboBoxSampleRate;
        private Label labelQConvention;
        private DarkComboBox comboBoxQConvention;
        private Label labelStatus;
        private Label labelHint;
        private ReleaseClickButton buttonOk;
        private ReleaseClickButton buttonCancel;
    }
}
