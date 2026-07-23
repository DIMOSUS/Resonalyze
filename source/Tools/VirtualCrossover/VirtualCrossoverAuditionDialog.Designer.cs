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
            buttonChooseSource = new Button();
            labelSourceFile = new Label();
            labelOutput = new Label();
            buttonChooseTarget = new Button();
            labelTargetFile = new Label();
            labelCalibration = new Label();
            comboBoxCalibration = new DarkComboBox();
            labelCabin = new Label();
            comboBoxCabin = new DarkComboBox();
            buttonRender = new Button();
            progressBar = new ProgressBar();
            labelStatus = new Label();
            textBoxReport = new TextBox();
            buttonClose = new Button();
            SuspendLayout();
            // 
            // labelTrack
            // 
            labelTrack.AutoSize = true;
            labelTrack.ForeColor = Color.FromArgb(185, 190, 200);
            labelTrack.Location = new Point(12, 19);
            labelTrack.Name = "labelTrack";
            labelTrack.Size = new Size(38, 15);
            labelTrack.TabIndex = 0;
            labelTrack.Text = "Track:";
            // 
            // buttonChooseSource
            // 
            buttonChooseSource.BackColor = Color.FromArgb(46, 51, 67);
            buttonChooseSource.FlatStyle = FlatStyle.Popup;
            buttonChooseSource.ForeColor = Color.White;
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
            labelSourceFile.ForeColor = Color.FromArgb(185, 190, 200);
            labelSourceFile.Location = new Point(224, 19);
            labelSourceFile.Name = "labelSourceFile";
            labelSourceFile.Size = new Size(360, 15);
            labelSourceFile.TabIndex = 2;
            labelSourceFile.Text = "no file chosen";
            // 
            // labelOutput
            // 
            labelOutput.AutoSize = true;
            labelOutput.ForeColor = Color.FromArgb(185, 190, 200);
            labelOutput.Location = new Point(12, 51);
            labelOutput.Name = "labelOutput";
            labelOutput.Size = new Size(48, 15);
            labelOutput.TabIndex = 3;
            labelOutput.Text = "Output:";
            // 
            // buttonChooseTarget
            // 
            buttonChooseTarget.BackColor = Color.FromArgb(46, 51, 67);
            buttonChooseTarget.FlatStyle = FlatStyle.Popup;
            buttonChooseTarget.ForeColor = Color.White;
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
            labelTargetFile.ForeColor = Color.FromArgb(185, 190, 200);
            labelTargetFile.Location = new Point(224, 51);
            labelTargetFile.Name = "labelTargetFile";
            labelTargetFile.Size = new Size(360, 15);
            labelTargetFile.TabIndex = 5;
            labelTargetFile.Text = "no file chosen";
            // 
            // labelCalibration
            // 
            labelCalibration.AutoSize = true;
            labelCalibration.ForeColor = Color.FromArgb(185, 190, 200);
            labelCalibration.Location = new Point(12, 83);
            labelCalibration.Name = "labelCalibration";
            labelCalibration.Size = new Size(89, 15);
            labelCalibration.TabIndex = 6;
            labelCalibration.Text = "Mic calibration:";
            // 
            // comboBoxCalibration
            // 
            comboBoxCalibration.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxCalibration.ForeColor = Color.White;
            comboBoxCalibration.Location = new Point(104, 81);
            comboBoxCalibration.MinimumSize = new Size(36, 19);
            comboBoxCalibration.Name = "comboBoxCalibration";
            comboBoxCalibration.Size = new Size(200, 19);
            comboBoxCalibration.TabIndex = 7;
            // 
            // labelCabin
            // 
            labelCabin.AutoSize = true;
            labelCabin.ForeColor = Color.FromArgb(185, 190, 200);
            labelCabin.Location = new Point(12, 115);
            labelCabin.Name = "labelCabin";
            labelCabin.Size = new Size(86, 15);
            labelCabin.TabIndex = 8;
            labelCabin.Text = "Subtract cabin:";
            // 
            // comboBoxCabin
            // 
            comboBoxCabin.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxCabin.ForeColor = Color.White;
            comboBoxCabin.Location = new Point(104, 113);
            comboBoxCabin.MinimumSize = new Size(36, 19);
            comboBoxCabin.Name = "comboBoxCabin";
            comboBoxCabin.Size = new Size(200, 19);
            comboBoxCabin.TabIndex = 9;
            // 
            // buttonRender
            // 
            buttonRender.BackColor = Color.FromArgb(46, 51, 67);
            buttonRender.FlatStyle = FlatStyle.Popup;
            buttonRender.ForeColor = Color.White;
            buttonRender.Location = new Point(12, 144);
            buttonRender.Name = "buttonRender";
            buttonRender.Size = new Size(120, 26);
            buttonRender.TabIndex = 10;
            buttonRender.Text = "Render";
            buttonRender.UseVisualStyleBackColor = false;
            // 
            // progressBar
            // 
            progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            progressBar.Location = new Point(144, 144);
            progressBar.Maximum = 1000;
            progressBar.Name = "progressBar";
            progressBar.Size = new Size(440, 26);
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.TabIndex = 11;
            // 
            // labelStatus
            // 
            labelStatus.AutoSize = true;
            labelStatus.ForeColor = Color.FromArgb(185, 190, 200);
            labelStatus.Location = new Point(12, 178);
            labelStatus.Name = "labelStatus";
            labelStatus.Size = new Size(0, 15);
            labelStatus.TabIndex = 12;
            // 
            // textBoxReport
            // 
            textBoxReport.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            textBoxReport.BackColor = Color.FromArgb(33, 36, 45);
            textBoxReport.BorderStyle = BorderStyle.FixedSingle;
            textBoxReport.Font = new Font("Consolas", 9F);
            textBoxReport.ForeColor = Color.FromArgb(210, 214, 222);
            textBoxReport.Location = new Point(12, 178);
            textBoxReport.Multiline = true;
            textBoxReport.Name = "textBoxReport";
            textBoxReport.ReadOnly = true;
            textBoxReport.ScrollBars = ScrollBars.Vertical;
            textBoxReport.Size = new Size(572, 324);
            textBoxReport.TabIndex = 13;
            // 
            // buttonClose
            // 
            buttonClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttonClose.DialogResult = DialogResult.Cancel;
            buttonClose.FlatStyle = FlatStyle.Popup;
            buttonClose.ForeColor = Color.White;
            buttonClose.Location = new Point(492, 512);
            buttonClose.Name = "buttonClose";
            buttonClose.Size = new Size(92, 26);
            buttonClose.TabIndex = 14;
            buttonClose.Text = "Close";
            buttonClose.UseVisualStyleBackColor = true;
            // 
            // VirtualCrossoverAuditionDialog
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(40, 44, 54);
            CancelButton = buttonClose;
            ClientSize = new Size(596, 550);
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
            Controls.Add(buttonRender);
            Controls.Add(progressBar);
            Controls.Add(labelStatus);
            Controls.Add(textBoxReport);
            Controls.Add(buttonClose);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            MinimizeBox = false;
            MinimumSize = new Size(560, 452);
            Name = "VirtualCrossoverAuditionDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Audition track";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label labelTrack;
        private Button buttonChooseSource;
        private Label labelSourceFile;
        private Label labelOutput;
        private Button buttonChooseTarget;
        private Label labelTargetFile;
        private Label labelCalibration;
        private DarkComboBox comboBoxCalibration;
        private Label labelCabin;
        private DarkComboBox comboBoxCabin;
        private Button buttonRender;
        private ProgressBar progressBar;
        private Label labelStatus;
        private TextBox textBoxReport;
        private Button buttonClose;
    }
}
