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
            radioLeftHandDrive = new ReleaseClickRadioButton();
            radioRightHandDrive = new ReleaseClickRadioButton();
            labelSceneOffset = new Label();
            numericSceneOffset = new ThemedNumericUpDown();
            labelRearFill = new Label();
            numericRearFill = new ThemedNumericUpDown();
            labelRearFillHint = new Label();
            checkBoxGains = new ReleaseClickCheckBox();
            labelNearSideCut = new Label();
            numericNearSideCut = new ThemedNumericUpDown();
            buttonRun = new ReleaseClickButton();
            labelStatus = new Label();
            textBoxReport = new TextBox();
            buttonApply = new ReleaseClickButton();
            buttonCancel = new ReleaseClickButton();
            buttonUndo = new ReleaseClickButton();
            (numericSceneOffset).BeginInit();
            (numericRearFill).BeginInit();
            (numericNearSideCut).BeginInit();
            SuspendLayout();
            // 
            // radioLeftHandDrive
            // 
            radioLeftHandDrive.AutoSize = true;
            radioLeftHandDrive.Checked = true;
            radioLeftHandDrive.ForeColor = UiPalette.TextPrimary;
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
            radioRightHandDrive.ForeColor = UiPalette.TextPrimary;
            radioRightHandDrive.Location = new Point(64, 13);
            radioRightHandDrive.Name = "radioRightHandDrive";
            radioRightHandDrive.Size = new Size(49, 19);
            radioRightHandDrive.TabIndex = 1;
            radioRightHandDrive.Text = "RHD";
            // 
            // labelSceneOffset
            // 
            labelSceneOffset.AutoSize = true;
            labelSceneOffset.ForeColor = UiPalette.TextSecondary;
            labelSceneOffset.Location = new Point(124, 16);
            labelSceneOffset.Name = "labelSceneOffset";
            labelSceneOffset.Size = new Size(42, 15);
            labelSceneOffset.TabIndex = 2;
            labelSceneOffset.Text = "Offset:";
            // 
            // numericSceneOffset
            // 
            numericSceneOffset.BackColor = UiPalette.ControlSurface;
            numericSceneOffset.DecimalPlaces = 2;
            numericSceneOffset.ForeColor = UiPalette.TextPrimary;
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
            checkBoxGains.ForeColor = UiPalette.TextPrimary;
            checkBoxGains.Location = new Point(268, 14);
            checkBoxGains.Name = "checkBoxGains";
            checkBoxGains.Size = new Size(199, 19);
            checkBoxGains.TabIndex = 4;
            checkBoxGains.Text = "Balance channel gains (cut-only)";
            // 
            // labelNearSideCut
            // 
            labelNearSideCut.AutoSize = true;
            labelNearSideCut.ForeColor = UiPalette.TextSecondary;
            labelNearSideCut.Location = new Point(475, 16);
            labelNearSideCut.Name = "labelNearSideCut";
            labelNearSideCut.Size = new Size(79, 15);
            labelNearSideCut.TabIndex = 5;
            labelNearSideCut.Text = "Near side cut:";
            // 
            // numericNearSideCut
            // 
            numericNearSideCut.BackColor = UiPalette.ControlSurface;
            numericNearSideCut.DecimalPlaces = 1;
            numericNearSideCut.ForeColor = UiPalette.TextPrimary;
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
            // labelRearFill
            //
            labelRearFill.AutoSize = true;
            labelRearFill.ForeColor = UiPalette.TextDefault;
            labelRearFill.Location = new Point(12, 50);
            labelRearFill.Name = "labelRearFill";
            labelRearFill.Size = new Size(78, 15);
            labelRearFill.Text = "Rear fill ms";
            //
            // numericRearFill
            //
            numericRearFill.BackColor = UiPalette.ControlSurface;
            numericRearFill.DecimalPlaces = 1;
            numericRearFill.ForeColor = UiPalette.TextPrimary;
            numericRearFill.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            numericRearFill.Location = new Point(100, 46);
            numericRearFill.Maximum = new decimal(new int[] { 30, 0, 0, 0 });
            numericRearFill.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericRearFill.MinimumSize = new Size(36, 19);
            numericRearFill.Name = "numericRearFill";
            numericRearFill.Size = new Size(66, 19);
            numericRearFill.TextAlign = HorizontalAlignment.Right;
            numericRearFill.ThousandsSeparator = false;
            numericRearFill.Value = new decimal(new int[] { 15, 0, 0, 0 });
            //
            // labelRearFillHint
            //
            labelRearFillHint.AutoSize = true;
            labelRearFillHint.ForeColor = UiPalette.TextMuted;
            labelRearFillHint.Location = new Point(176, 50);
            labelRearFillHint.Name = "labelRearFillHint";
            labelRearFillHint.Size = new Size(470, 15);
            labelRearFillHint.Text =
                "how far behind the front the rear arrives - 10-20 ms holds the image; 0 co-arrives";
            // 
            // buttonRun
            // 
            buttonRun.BackColor = UiPalette.ButtonBackground;
            buttonRun.FlatStyle = FlatStyle.Popup;
            buttonRun.ForeColor = UiPalette.TextPrimary;
            buttonRun.Location = new Point(12, 78);
            buttonRun.Name = "buttonRun";
            buttonRun.Size = new Size(120, 26);
            buttonRun.TabIndex = 8;
            buttonRun.Text = "Run";
            buttonRun.UseVisualStyleBackColor = false;
            // 
            // labelStatus
            // 
            labelStatus.AutoSize = true;
            labelStatus.ForeColor = UiPalette.TextSecondary;
            labelStatus.Location = new Point(144, 84);
            labelStatus.Name = "labelStatus";
            labelStatus.Size = new Size(0, 15);
            labelStatus.TabIndex = 9;
            // 
            // textBoxReport
            // 
            textBoxReport.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            textBoxReport.BackColor = UiPalette.SunkenSurface;
            textBoxReport.BorderStyle = BorderStyle.FixedSingle;
            textBoxReport.Font = new Font("Consolas", 9F);
            textBoxReport.ForeColor = UiPalette.TextDefault;
            textBoxReport.Location = new Point(12, 114);
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
            buttonApply.BackColor = UiPalette.ButtonBackground;
            buttonApply.DialogResult = DialogResult.OK;
            buttonApply.FlatStyle = FlatStyle.Popup;
            buttonApply.ForeColor = UiPalette.TextPrimary;
            buttonApply.Location = new Point(590, 677);
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
            buttonCancel.ForeColor = UiPalette.TextPrimary;
            buttonCancel.Location = new Point(680, 677);
            buttonCancel.Name = "buttonCancel";
            buttonCancel.Size = new Size(84, 26);
            buttonCancel.TabIndex = 12;
            buttonCancel.Text = "Discard";
            buttonCancel.UseVisualStyleBackColor = true;
            // 
            // buttonUndo
            // 
            buttonUndo.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            buttonUndo.Enabled = false;
            buttonUndo.FlatStyle = FlatStyle.Popup;
            buttonUndo.ForeColor = UiPalette.TextPrimary;
            buttonUndo.Location = new Point(12, 677);
            buttonUndo.Name = "buttonUndo";
            buttonUndo.Size = new Size(130, 26);
            buttonUndo.TabIndex = 13;
            buttonUndo.Text = "Undo last Apply";
            buttonUndo.UseVisualStyleBackColor = true;
            // 
            // VirtualCrossoverAutoDelayDialog
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiPalette.ShellSurface;
            ClientSize = new Size(776, 715);
            Controls.Add(radioLeftHandDrive);
            Controls.Add(radioRightHandDrive);
            Controls.Add(labelSceneOffset);
            Controls.Add(numericSceneOffset);
            Controls.Add(labelRearFill);
            Controls.Add(numericRearFill);
            Controls.Add(labelRearFillHint);
            Controls.Add(checkBoxGains);
            Controls.Add(labelNearSideCut);
            Controls.Add(numericNearSideCut);
            Controls.Add(buttonRun);
            Controls.Add(labelStatus);
            Controls.Add(textBoxReport);
            Controls.Add(buttonApply);
            Controls.Add(buttonCancel);
            Controls.Add(buttonUndo);
            Font = new Font("Segoe UI", 9F);
            ForeColor = UiPalette.TextPrimary;
            MinimizeBox = false;
            MinimumSize = new Size(792, 360);
            Name = "VirtualCrossoverAutoDelayDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Auto delay";
            (numericSceneOffset).EndInit();
            (numericRearFill).EndInit();
            (numericNearSideCut).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ReleaseClickRadioButton radioLeftHandDrive;
        private ReleaseClickRadioButton radioRightHandDrive;
        private Label labelSceneOffset;
        private ThemedNumericUpDown numericSceneOffset;
        private Label labelRearFill;
        private ThemedNumericUpDown numericRearFill;
        private Label labelRearFillHint;
        private ReleaseClickCheckBox checkBoxGains;
        private Label labelNearSideCut;
        private ThemedNumericUpDown numericNearSideCut;
        private ReleaseClickButton buttonRun;
        private Label labelStatus;
        private TextBox textBoxReport;
        private ReleaseClickButton buttonApply;
        private ReleaseClickButton buttonCancel;
        private ReleaseClickButton buttonUndo;
    }
}
