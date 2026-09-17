namespace Resonalyze
{
    partial class VirtualCrossoverAuditionDialog
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
            labelTrack = new Label();
            buttonChooseSource = new ReleaseClickButton();
            labelSourceFile = new Label();
            labelOutput = new Label();
            buttonChooseTarget = new ReleaseClickButton();
            labelTargetFile = new Label();
            labelCalibration = new Label();
            comboBoxCalibration = new ThemedComboBox();
            labelCabin = new Label();
            comboBoxCabin = new ThemedComboBox();
            labelSpatialAverage = new Label();
            checkBoxSpatialAverage = new ReleaseClickCheckBox();
            buttonRender = new ReleaseClickButton();
            progressBar = new ProgressBar();
            labelStatus = new Label();
            textBoxReport = new TextBox();
            buttonClose = new ReleaseClickButton();
            SuspendLayout();
            // 
            // labelTrack
            // 
            labelTrack.AutoSize = true;
            labelTrack.ForeColor = UiPalette.TextSecondary;
            labelTrack.Location = new Point(12, 19);
            labelTrack.Name = "labelTrack";
            labelTrack.Size = new Size(38, 15);
            labelTrack.TabIndex = 0;
            labelTrack.Text = "Track:";
            // 
            // buttonChooseSource
            // 
            buttonChooseSource.BackColor = UiPalette.ButtonBackground;
            buttonChooseSource.FlatStyle = FlatStyle.Popup;
            buttonChooseSource.ForeColor = UiPalette.TextPrimary;
            buttonChooseSource.Location = new Point(104, 14);
            buttonChooseSource.Name = "buttonChooseSource";
            buttonChooseSource.Size = new Size(110, 26);
            buttonChooseSource.TabIndex = 1;
            buttonChooseSource.Text = "Choose...";
            buttonChooseSource.UseVisualStyleBackColor = false;
            // 
            // labelSourceFile
            // 
            labelSourceFile.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            labelSourceFile.AutoEllipsis = true;
            labelSourceFile.ForeColor = UiPalette.TextSecondary;
            labelSourceFile.Location = new Point(224, 17);
            labelSourceFile.Name = "labelSourceFile";
            labelSourceFile.Size = new Size(360, 19);
            labelSourceFile.TabIndex = 2;
            labelSourceFile.Text = "no file chosen";
            // 
            // labelOutput
            // 
            labelOutput.AutoSize = true;
            labelOutput.ForeColor = UiPalette.TextSecondary;
            labelOutput.Location = new Point(12, 51);
            labelOutput.Name = "labelOutput";
            labelOutput.Size = new Size(48, 15);
            labelOutput.TabIndex = 3;
            labelOutput.Text = "Output:";
            // 
            // buttonChooseTarget
            // 
            buttonChooseTarget.BackColor = UiPalette.ButtonBackground;
            buttonChooseTarget.FlatStyle = FlatStyle.Popup;
            buttonChooseTarget.ForeColor = UiPalette.TextPrimary;
            buttonChooseTarget.Location = new Point(104, 46);
            buttonChooseTarget.Name = "buttonChooseTarget";
            buttonChooseTarget.Size = new Size(110, 26);
            buttonChooseTarget.TabIndex = 4;
            buttonChooseTarget.Text = "Save as...";
            buttonChooseTarget.UseVisualStyleBackColor = false;
            // 
            // labelTargetFile
            // 
            labelTargetFile.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            labelTargetFile.AutoEllipsis = true;
            labelTargetFile.ForeColor = UiPalette.TextSecondary;
            labelTargetFile.Location = new Point(224, 49);
            labelTargetFile.Name = "labelTargetFile";
            labelTargetFile.Size = new Size(360, 19);
            labelTargetFile.TabIndex = 5;
            labelTargetFile.Text = "no file chosen";
            // 
            // labelCalibration
            // 
            labelCalibration.AutoSize = true;
            labelCalibration.ForeColor = UiPalette.TextSecondary;
            labelCalibration.Location = new Point(12, 83);
            labelCalibration.Name = "labelCalibration";
            labelCalibration.Size = new Size(89, 15);
            labelCalibration.TabIndex = 6;
            labelCalibration.Text = "Mic calibration:";
            // 
            // comboBoxCalibration
            // 
            comboBoxCalibration.BackColor = UiPalette.ControlSurface;
            comboBoxCalibration.ForeColor = UiPalette.TextPrimary;
            comboBoxCalibration.Location = new Point(104, 81);
            comboBoxCalibration.MinimumSize = new Size(36, 19);
            comboBoxCalibration.Name = "comboBoxCalibration";
            comboBoxCalibration.Size = new Size(200, 19);
            comboBoxCalibration.TabIndex = 7;
            // 
            // labelCabin
            // 
            labelCabin.AutoSize = true;
            labelCabin.ForeColor = UiPalette.TextSecondary;
            labelCabin.Location = new Point(12, 115);
            labelCabin.Name = "labelCabin";
            labelCabin.Size = new Size(86, 15);
            labelCabin.TabIndex = 8;
            labelCabin.Text = "Subtract cabin:";
            // 
            // comboBoxCabin
            // 
            comboBoxCabin.BackColor = UiPalette.ControlSurface;
            comboBoxCabin.ForeColor = UiPalette.TextPrimary;
            comboBoxCabin.Location = new Point(104, 113);
            comboBoxCabin.MinimumSize = new Size(36, 19);
            comboBoxCabin.Name = "comboBoxCabin";
            comboBoxCabin.Size = new Size(200, 19);
            comboBoxCabin.TabIndex = 9;
            // 
            // labelSpatialAverage
            // 
            labelSpatialAverage.AutoSize = true;
            labelSpatialAverage.ForeColor = UiPalette.TextSecondary;
            labelSpatialAverage.Location = new Point(12, 147);
            labelSpatialAverage.Name = "labelSpatialAverage";
            labelSpatialAverage.Size = new Size(76, 15);
            labelSpatialAverage.TabIndex = 10;
            labelSpatialAverage.Text = "Magnitudes:";
            // 
            // checkBoxSpatialAverage
            // 
            checkBoxSpatialAverage.AutoSize = true;
            checkBoxSpatialAverage.ForeColor = UiPalette.TextDefault;
            checkBoxSpatialAverage.Location = new Point(104, 146);
            checkBoxSpatialAverage.Name = "checkBoxSpatialAverage";
            checkBoxSpatialAverage.Size = new Size(240, 19);
            checkBoxSpatialAverage.TabIndex = 11;
            checkBoxSpatialAverage.Text = "from the spatial averages (MMM / array)";
            checkBoxSpatialAverage.UseVisualStyleBackColor = true;
            // 
            // buttonRender
            // 
            buttonRender.BackColor = UiPalette.ButtonBackground;
            buttonRender.FlatStyle = FlatStyle.Popup;
            buttonRender.ForeColor = UiPalette.TextPrimary;
            buttonRender.Location = new Point(12, 176);
            buttonRender.Name = "buttonRender";
            buttonRender.Size = new Size(120, 26);
            buttonRender.TabIndex = 12;
            buttonRender.Text = "Render";
            buttonRender.UseVisualStyleBackColor = false;
            // 
            // progressBar
            // 
            progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            progressBar.Location = new Point(144, 176);
            progressBar.Maximum = 1000;
            progressBar.Name = "progressBar";
            progressBar.Size = new Size(440, 26);
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.TabIndex = 13;
            // 
            // labelStatus
            // 
            labelStatus.AutoSize = true;
            labelStatus.ForeColor = UiPalette.TextSecondary;
            labelStatus.Location = new Point(12, 210);
            labelStatus.Name = "labelStatus";
            labelStatus.Size = new Size(0, 15);
            labelStatus.TabIndex = 14;
            // 
            // textBoxReport
            // 
            textBoxReport.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            textBoxReport.BackColor = UiPalette.SunkenSurface;
            textBoxReport.BorderStyle = BorderStyle.FixedSingle;
            textBoxReport.Font = new Font("Consolas", 9F);
            textBoxReport.ForeColor = UiPalette.TextDefault;
            textBoxReport.Location = new Point(12, 232);
            textBoxReport.Multiline = true;
            textBoxReport.Name = "textBoxReport";
            textBoxReport.ReadOnly = true;
            textBoxReport.ScrollBars = ScrollBars.Vertical;
            textBoxReport.Size = new Size(572, 302);
            textBoxReport.TabIndex = 15;
            // 
            // buttonClose
            // 
            buttonClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttonClose.DialogResult = DialogResult.Cancel;
            buttonClose.FlatStyle = FlatStyle.Popup;
            buttonClose.ForeColor = UiPalette.TextPrimary;
            buttonClose.Location = new Point(492, 544);
            buttonClose.Name = "buttonClose";
            buttonClose.Size = new Size(92, 26);
            buttonClose.TabIndex = 16;
            buttonClose.Text = "Close";
            buttonClose.UseVisualStyleBackColor = true;
            // 
            // VirtualCrossoverAuditionDialog
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiPalette.ShellSurface;
            CancelButton = buttonClose;
            ClientSize = new Size(596, 582);
            Controls.Add(labelTrack);
            Controls.Add(buttonChooseSource);
            Controls.Add(labelSourceFile);
            Controls.Add(labelOutput);
            Controls.Add(buttonChooseTarget);
            Controls.Add(labelTargetFile);
            Controls.Add(labelCalibration);
            Controls.Add(comboBoxCalibration);
            Controls.Add(labelCabin);
            Controls.Add(comboBoxCabin);
            Controls.Add(labelSpatialAverage);
            Controls.Add(checkBoxSpatialAverage);
            Controls.Add(buttonRender);
            Controls.Add(progressBar);
            Controls.Add(labelStatus);
            Controls.Add(textBoxReport);
            Controls.Add(buttonClose);
            Font = new Font("Segoe UI", 9F);
            ForeColor = UiPalette.TextPrimary;
            MinimizeBox = false;
            MinimumSize = new Size(560, 484);
            Name = "VirtualCrossoverAuditionDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Audition track";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label labelTrack;
        private ReleaseClickButton buttonChooseSource;
        private Label labelSourceFile;
        private Label labelOutput;
        private ReleaseClickButton buttonChooseTarget;
        private Label labelTargetFile;
        private Label labelCalibration;
        private ThemedComboBox comboBoxCalibration;
        private Label labelCabin;
        private ThemedComboBox comboBoxCabin;
        private Label labelSpatialAverage;
        private ReleaseClickCheckBox checkBoxSpatialAverage;
        private ReleaseClickButton buttonRender;
        private ProgressBar progressBar;
        private Label labelStatus;
        private TextBox textBoxReport;
        private ReleaseClickButton buttonClose;
    }
}
