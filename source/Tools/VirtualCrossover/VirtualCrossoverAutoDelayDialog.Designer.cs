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
            radioLeftHandDrive = new RadioButton();
            radioRightHandDrive = new RadioButton();
            labelSceneOffset = new Label();
            numericSceneOffset = new DarkNumericUpDown();
            checkBoxGains = new CheckBox();
            labelNearSideCut = new Label();
            numericNearSideCut = new DarkNumericUpDown();
            labelNearSideCutHint = new Label();
            buttonRun = new Button();
            labelStatus = new Label();
            textBoxReport = new TextBox();
            buttonApply = new Button();
            buttonCancel = new Button();
            (numericSceneOffset).BeginInit();
            (numericNearSideCut).BeginInit();
            SuspendLayout();
            //
            // radioLeftHandDrive
            //
            radioLeftHandDrive.AutoSize = true;
            radioLeftHandDrive.Checked = true;
            radioLeftHandDrive.ForeColor = Color.White;
            radioLeftHandDrive.Location = new Point(12, 13);
            radioLeftHandDrive.Name = "radioLeftHandDrive";
            radioLeftHandDrive.Size = new Size(48, 19);
            radioLeftHandDrive.TabIndex = 0;
            radioLeftHandDrive.TabStop = true;
            radioLeftHandDrive.Text = "LHD";
            //
            // radioRightHandDrive
            //
            radioRightHandDrive.AutoSize = true;
            radioRightHandDrive.ForeColor = Color.White;
            radioRightHandDrive.Location = new Point(64, 13);
            radioRightHandDrive.Name = "radioRightHandDrive";
            radioRightHandDrive.Size = new Size(50, 19);
            radioRightHandDrive.TabIndex = 1;
            radioRightHandDrive.Text = "RHD";
            //
            // labelSceneOffset
            //
            labelSceneOffset.AutoSize = true;
            labelSceneOffset.ForeColor = Color.FromArgb(185, 190, 200);
            labelSceneOffset.Location = new Point(124, 16);
            labelSceneOffset.Name = "labelSceneOffset";
            labelSceneOffset.Size = new Size(42, 15);
            labelSceneOffset.TabIndex = 2;
            labelSceneOffset.Text = "Offset:";
            //
            // numericSceneOffset
            //
            numericSceneOffset.BackColor = Color.FromArgb(55, 60, 72);
            numericSceneOffset.DecimalPlaces = 2;
            numericSceneOffset.ForeColor = Color.White;
            numericSceneOffset.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            numericSceneOffset.Location = new Point(172, 12);
            numericSceneOffset.Maximum = new decimal(new int[] { 5, 0, 0, 0 });
            numericSceneOffset.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericSceneOffset.MinimumSize = new Size(36, 19);
            numericSceneOffset.Name = "numericSceneOffset";
            numericSceneOffset.Size = new Size(80, 21);
            numericSceneOffset.TabIndex = 3;
            numericSceneOffset.TextAlign = HorizontalAlignment.Right;
            numericSceneOffset.ThousandsSeparator = false;
            numericSceneOffset.Value = new decimal(new int[] { 27, 0, 0, 131072 });
            numericSceneOffset.ValueSuffix = "ms";
            //
            // checkBoxGains
            //
            checkBoxGains.AutoSize = true;
            checkBoxGains.Checked = true;
            checkBoxGains.CheckState = CheckState.Checked;
            checkBoxGains.ForeColor = Color.White;
            checkBoxGains.Location = new Point(268, 14);
            checkBoxGains.Name = "checkBoxGains";
            checkBoxGains.Size = new Size(199, 19);
            checkBoxGains.TabIndex = 4;
            checkBoxGains.Text = "Balance channel gains (cut-only)";
            //
            // labelNearSideCut
            //
            labelNearSideCut.AutoSize = true;
            labelNearSideCut.ForeColor = Color.FromArgb(185, 190, 200);
            labelNearSideCut.Location = new Point(475, 16);
            labelNearSideCut.Name = "labelNearSideCut";
            labelNearSideCut.Size = new Size(84, 15);
            labelNearSideCut.TabIndex = 5;
            labelNearSideCut.Text = "Near side cut:";
            //
            // numericNearSideCut
            //
            numericNearSideCut.BackColor = Color.FromArgb(55, 60, 72);
            numericNearSideCut.DecimalPlaces = 1;
            numericNearSideCut.ForeColor = Color.White;
            numericNearSideCut.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            numericNearSideCut.Location = new Point(565, 12);
            numericNearSideCut.Maximum = new decimal(new int[] { 6, 0, 0, 0 });
            numericNearSideCut.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericNearSideCut.MinimumSize = new Size(36, 19);
            numericNearSideCut.Name = "numericNearSideCut";
            numericNearSideCut.Size = new Size(72, 21);
            numericNearSideCut.TabIndex = 6;
            numericNearSideCut.TextAlign = HorizontalAlignment.Right;
            numericNearSideCut.ThousandsSeparator = false;
            numericNearSideCut.Value = new decimal(new int[] { 1, 0, 0, 0 });
            numericNearSideCut.ValueSuffix = "dB";
            //
            // labelNearSideCutHint
            //
            labelNearSideCutHint.AutoSize = true;
            labelNearSideCutHint.ForeColor = Color.FromArgb(120, 125, 135);
            labelNearSideCutHint.Location = new Point(645, 16);
            labelNearSideCutHint.Name = "labelNearSideCutHint";
            labelNearSideCutHint.Size = new Size(70, 15);
            labelNearSideCutHint.TabIndex = 7;
            labelNearSideCutHint.Text = "typical 1…2";
            //
            // buttonRun
            //
            buttonRun.BackColor = Color.FromArgb(46, 51, 67);
            buttonRun.FlatStyle = FlatStyle.Popup;
            buttonRun.ForeColor = Color.White;
            buttonRun.Location = new Point(12, 44);
            buttonRun.Name = "buttonRun";
            buttonRun.Size = new Size(120, 26);
            buttonRun.TabIndex = 8;
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
            labelStatus.TabIndex = 9;
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
            textBoxReport.Size = new Size(752, 553);
            textBoxReport.TabIndex = 10;
            //
            // buttonApply
            //
            buttonApply.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttonApply.BackColor = Color.FromArgb(46, 51, 67);
            buttonApply.DialogResult = DialogResult.OK;
            buttonApply.FlatStyle = FlatStyle.Popup;
            buttonApply.ForeColor = Color.White;
            buttonApply.Location = new Point(590, 643);
            buttonApply.Name = "buttonApply";
            buttonApply.Size = new Size(84, 26);
            buttonApply.TabIndex = 11;
            buttonApply.Text = "Apply";
            buttonApply.UseVisualStyleBackColor = false;
            //
            // buttonCancel
            //
            buttonCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttonCancel.DialogResult = DialogResult.Cancel;
            buttonCancel.FlatStyle = FlatStyle.Popup;
            buttonCancel.ForeColor = Color.White;
            buttonCancel.Location = new Point(680, 643);
            buttonCancel.Name = "buttonCancel";
            buttonCancel.Size = new Size(84, 26);
            buttonCancel.TabIndex = 12;
            buttonCancel.Text = "Discard";
            buttonCancel.UseVisualStyleBackColor = true;
            //
            // VirtualCrossoverAutoDelayDialog
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(40, 44, 54);
            ClientSize = new Size(776, 681);
            Controls.Add(radioLeftHandDrive);
            Controls.Add(radioRightHandDrive);
            Controls.Add(labelSceneOffset);
            Controls.Add(numericSceneOffset);
            Controls.Add(checkBoxGains);
            Controls.Add(labelNearSideCut);
            Controls.Add(numericNearSideCut);
            Controls.Add(labelNearSideCutHint);
            Controls.Add(buttonRun);
            Controls.Add(labelStatus);
            Controls.Add(textBoxReport);
            Controls.Add(buttonApply);
            Controls.Add(buttonCancel);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            MinimizeBox = false;
            MinimumSize = new Size(792, 360);
            Name = "VirtualCrossoverAutoDelayDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Auto delay";
            (numericSceneOffset).EndInit();
            (numericNearSideCut).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private RadioButton radioLeftHandDrive;
        private RadioButton radioRightHandDrive;
        private Label labelSceneOffset;
        private DarkNumericUpDown numericSceneOffset;
        private CheckBox checkBoxGains;
        private Label labelNearSideCut;
        private DarkNumericUpDown numericNearSideCut;
        private Label labelNearSideCutHint;
        private Button buttonRun;
        private Label labelStatus;
        private TextBox textBoxReport;
        private Button buttonApply;
        private Button buttonCancel;
    }
}
