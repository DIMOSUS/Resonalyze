namespace Resonalyze
{
    partial class VirtualCrossoverAutoDelayDialog
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
                toolTip.Dispose();
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
            labelSceneOffset = new Label();
            numericSceneOffset = new DarkNumericUpDown();
            checkBoxGains = new CheckBox();
            labelLevelDifference = new Label();
            numericLevelDifference = new DarkNumericUpDown();
            buttonRun = new Button();
            labelStatus = new Label();
            textBoxReport = new TextBox();
            buttonApply = new Button();
            buttonCancel = new Button();
            (numericSceneOffset).BeginInit();
            (numericLevelDifference).BeginInit();
            SuspendLayout();
            // 
            // labelSceneOffset
            // 
            labelSceneOffset.AutoSize = true;
            labelSceneOffset.ForeColor = Color.FromArgb(185, 190, 200);
            labelSceneOffset.Location = new Point(12, 16);
            labelSceneOffset.Name = "labelSceneOffset";
            labelSceneOffset.Size = new Size(83, 15);
            labelSceneOffset.TabIndex = 0;
            labelSceneOffset.Text = "L/R offset, ms:";
            // 
            // numericSceneOffset
            // 
            numericSceneOffset.BackColor = Color.FromArgb(55, 60, 72);
            numericSceneOffset.DecimalPlaces = 2;
            numericSceneOffset.ForeColor = Color.White;
            numericSceneOffset.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            numericSceneOffset.Location = new Point(104, 12);
            numericSceneOffset.Maximum = new decimal(new int[] { 5, 0, 0, 0 });
            numericSceneOffset.Minimum = new decimal(new int[] { 5, 0, 0, int.MinValue });
            numericSceneOffset.MinimumSize = new Size(36, 19);
            numericSceneOffset.Name = "numericSceneOffset";
            numericSceneOffset.Size = new Size(72, 21);
            numericSceneOffset.TabIndex = 1;
            numericSceneOffset.TextAlign = HorizontalAlignment.Right;
            numericSceneOffset.ThousandsSeparator = false;
            numericSceneOffset.Value = new decimal(new int[] { 27, 0, 0, 131072 });
            // 
            // checkBoxGains
            // 
            checkBoxGains.AutoSize = true;
            checkBoxGains.Checked = true;
            checkBoxGains.CheckState = CheckState.Checked;
            checkBoxGains.ForeColor = Color.White;
            checkBoxGains.Location = new Point(196, 14);
            checkBoxGains.Name = "checkBoxGains";
            checkBoxGains.Size = new Size(199, 19);
            checkBoxGains.TabIndex = 2;
            checkBoxGains.Text = "Balance channel gains (cut-only)";
            //
            // labelLevelDifference
            //
            labelLevelDifference.AutoSize = true;
            labelLevelDifference.ForeColor = Color.FromArgb(185, 190, 200);
            labelLevelDifference.Location = new Point(411, 16);
            labelLevelDifference.Name = "labelLevelDifference";
            labelLevelDifference.Size = new Size(80, 15);
            labelLevelDifference.TabIndex = 3;
            labelLevelDifference.Text = "L-R level, dB:";
            //
            // numericLevelDifference
            //
            numericLevelDifference.BackColor = Color.FromArgb(55, 60, 72);
            numericLevelDifference.DecimalPlaces = 1;
            numericLevelDifference.ForeColor = Color.White;
            numericLevelDifference.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            numericLevelDifference.Location = new Point(500, 12);
            numericLevelDifference.Maximum = new decimal(new int[] { 6, 0, 0, 0 });
            numericLevelDifference.Minimum = new decimal(new int[] { 6, 0, 0, int.MinValue });
            numericLevelDifference.MinimumSize = new Size(36, 19);
            numericLevelDifference.Name = "numericLevelDifference";
            numericLevelDifference.Size = new Size(72, 21);
            numericLevelDifference.TabIndex = 4;
            numericLevelDifference.TextAlign = HorizontalAlignment.Right;
            numericLevelDifference.ThousandsSeparator = false;
            numericLevelDifference.Value = new decimal(new int[] { 1, 0, 0, int.MinValue });
            //
            // buttonRun
            //
            buttonRun.BackColor = Color.FromArgb(46, 51, 67);
            buttonRun.FlatStyle = FlatStyle.Popup;
            buttonRun.ForeColor = Color.White;
            buttonRun.Location = new Point(12, 44);
            buttonRun.Name = "buttonRun";
            buttonRun.Size = new Size(120, 26);
            buttonRun.TabIndex = 5;
            buttonRun.Text = "Run";
            buttonRun.UseVisualStyleBackColor = false;
            //
            // labelStatus
            //
            labelStatus.AutoSize = true;
            labelStatus.ForeColor = Color.FromArgb(185, 190, 200);
            labelStatus.Location = new Point(144, 50);
            labelStatus.Name = "labelStatus";
            labelStatus.Size = new Size(0, 15);
            labelStatus.TabIndex = 6;
            // 
            // textBoxReport
            // 
            textBoxReport.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            textBoxReport.BackColor = Color.FromArgb(33, 36, 45);
            textBoxReport.BorderStyle = BorderStyle.FixedSingle;
            textBoxReport.Font = new Font("Consolas", 9F);
            textBoxReport.ForeColor = Color.FromArgb(210, 214, 222);
            textBoxReport.Location = new Point(12, 80);
            textBoxReport.Multiline = true;
            textBoxReport.Name = "textBoxReport";
            textBoxReport.ReadOnly = true;
            textBoxReport.ScrollBars = ScrollBars.Vertical;
            textBoxReport.Size = new Size(700, 553);
            textBoxReport.TabIndex = 7;
            // 
            // buttonApply
            // 
            buttonApply.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttonApply.BackColor = Color.FromArgb(46, 51, 67);
            buttonApply.DialogResult = DialogResult.OK;
            buttonApply.FlatStyle = FlatStyle.Popup;
            buttonApply.ForeColor = Color.White;
            buttonApply.Location = new Point(538, 643);
            buttonApply.Name = "buttonApply";
            buttonApply.Size = new Size(84, 26);
            buttonApply.TabIndex = 8;
            buttonApply.Text = "Apply";
            buttonApply.UseVisualStyleBackColor = false;
            // 
            // buttonCancel
            // 
            buttonCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttonCancel.DialogResult = DialogResult.Cancel;
            buttonCancel.FlatStyle = FlatStyle.Popup;
            buttonCancel.ForeColor = Color.White;
            buttonCancel.Location = new Point(628, 643);
            buttonCancel.Name = "buttonCancel";
            buttonCancel.Size = new Size(84, 26);
            buttonCancel.TabIndex = 9;
            buttonCancel.Text = "Discard";
            buttonCancel.UseVisualStyleBackColor = true;
            // 
            // VirtualCrossoverAutoDelayDialog
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(40, 44, 54);
            ClientSize = new Size(724, 681);
            Controls.Add(labelSceneOffset);
            Controls.Add(numericSceneOffset);
            Controls.Add(checkBoxGains);
            Controls.Add(labelLevelDifference);
            Controls.Add(numericLevelDifference);
            Controls.Add(buttonRun);
            Controls.Add(labelStatus);
            Controls.Add(textBoxReport);
            Controls.Add(buttonApply);
            Controls.Add(buttonCancel);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            MinimizeBox = false;
            MinimumSize = new Size(740, 360);
            Name = "VirtualCrossoverAutoDelayDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Auto delay";
            (numericSceneOffset).EndInit();
            (numericLevelDifference).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label labelSceneOffset;
        private DarkNumericUpDown numericSceneOffset;
        private CheckBox checkBoxGains;
        private Label labelLevelDifference;
        private DarkNumericUpDown numericLevelDifference;
        private Button buttonRun;
        private Label labelStatus;
        private TextBox textBoxReport;
        private Button buttonApply;
        private Button buttonCancel;
    }
}
