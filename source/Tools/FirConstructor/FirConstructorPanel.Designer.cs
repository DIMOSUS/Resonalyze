namespace Resonalyze
{
    partial class FirConstructorPanel
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
            titleLabel = new Label();
            labelSession = new Label();
            labelType = new Label();
            comboBoxType = new DarkComboBox();
            labelMethod = new Label();
            comboBoxMethod = new DarkComboBox();
            cardHighPass = new RoundedPanel();
            labelHighPass = new Label();
            numericHighPassHz = new DarkNumericUpDown();
            comboBoxHighPassFamily = new DarkComboBox();
            comboBoxHighPassSlope = new DarkComboBox();
            cardLowPass = new RoundedPanel();
            labelLowPass = new Label();
            numericLowPassHz = new DarkNumericUpDown();
            comboBoxLowPassFamily = new DarkComboBox();
            comboBoxLowPassSlope = new DarkComboBox();
            labelWindow = new Label();
            comboBoxWindow = new DarkComboBox();
            labelKaiserBeta = new Label();
            numericKaiserBeta = new DarkNumericUpDown();
            labelTaps = new Label();
            numericTaps = new DarkNumericUpDown();
            labelTapsHint = new Label();
            labelSampleRate = new Label();
            comboBoxSampleRate = new DarkComboBox();
            labelLatency = new Label();
            labelDeviation = new Label();
            labelProblem = new Label();
            buttonImport = new ReleaseClickButton();
            buttonExport = new ReleaseClickButton();
            buttonReturnToDsp = new ReleaseClickButton();
            buttonBackToDsp = new ReleaseClickButton();
            plotMagnitude = new OxyPlot.WindowsForms.PlotView();
            plotPhase = new OxyPlot.WindowsForms.PlotView();
            ((System.ComponentModel.ISupportInitialize)numericHighPassHz).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericLowPassHz).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericKaiserBeta).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericTaps).BeginInit();
            cardHighPass.SuspendLayout();
            cardLowPass.SuspendLayout();
            SuspendLayout();
            // 
            // titleLabel
            // 
            titleLabel.AutoSize = true;
            titleLabel.ForeColor = Color.FromArgb(210, 214, 222);
            titleLabel.Location = new Point(18, 18);
            titleLabel.Name = "titleLabel";
            titleLabel.Size = new Size(128, 21);
            titleLabel.Text = "FIR Constructor";
            titleLabel.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold, GraphicsUnit.Point, 204);
            titleLabel.TabIndex = 0;
            // 
            // labelSession
            // 
            labelSession.AutoEllipsis = true;
            labelSession.ForeColor = Color.FromArgb(190, 220, 255);
            labelSession.Location = new Point(18, 46);
            labelSession.Name = "labelSession";
            labelSession.Size = new Size(314, 48);
            labelSession.Text = "Standalone: export the kernel to a file.";
            labelSession.TabIndex = 1;
            // 
            // labelType
            // 
            labelType.AutoSize = true;
            labelType.ForeColor = Color.FromArgb(210, 214, 222);
            labelType.Location = new Point(18, 108);
            labelType.Name = "labelType";
            labelType.Size = new Size(31, 15);
            labelType.Text = "Type";
            labelType.TabIndex = 2;
            // 
            // comboBoxType
            // 
            comboBoxType.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxType.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxType.ForeColor = Color.White;
            comboBoxType.FormattingEnabled = true;
            comboBoxType.Location = new Point(118, 104);
            comboBoxType.MinimumSize = new Size(36, 19);
            comboBoxType.Name = "comboBoxType";
            comboBoxType.Size = new Size(150, 23);
            comboBoxType.TabIndex = 3;
            // 
            // labelMethod
            // 
            labelMethod.AutoSize = true;
            labelMethod.ForeColor = Color.FromArgb(210, 214, 222);
            labelMethod.Location = new Point(18, 138);
            labelMethod.Name = "labelMethod";
            labelMethod.Size = new Size(49, 15);
            labelMethod.Text = "Method";
            labelMethod.TabIndex = 4;
            // 
            // comboBoxMethod
            // 
            comboBoxMethod.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxMethod.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxMethod.ForeColor = Color.White;
            comboBoxMethod.FormattingEnabled = true;
            comboBoxMethod.Location = new Point(118, 134);
            comboBoxMethod.MinimumSize = new Size(36, 19);
            comboBoxMethod.Name = "comboBoxMethod";
            comboBoxMethod.Size = new Size(150, 23);
            comboBoxMethod.TabIndex = 5;
            // 
            // cardHighPass
            // 
            cardHighPass.BackColor = Color.FromArgb(50, 55, 66);
            cardHighPass.Location = new Point(18, 166);
            cardHighPass.Name = "cardHighPass";
            cardHighPass.Size = new Size(314, 64);
            cardHighPass.Controls.Add(comboBoxHighPassSlope);
            cardHighPass.Controls.Add(comboBoxHighPassFamily);
            cardHighPass.Controls.Add(numericHighPassHz);
            cardHighPass.Controls.Add(labelHighPass);
            cardHighPass.TabIndex = 6;
            // 
            // labelHighPass
            // 
            labelHighPass.AutoSize = true;
            labelHighPass.ForeColor = Color.FromArgb(210, 214, 222);
            labelHighPass.Location = new Point(10, 10);
            labelHighPass.Name = "labelHighPass";
            labelHighPass.Size = new Size(56, 15);
            labelHighPass.Text = "High-pass";
            labelHighPass.TabIndex = 7;
            // 
            // numericHighPassHz
            // 
            numericHighPassHz.BackColor = Color.FromArgb(55, 60, 72);
            numericHighPassHz.DecimalPlaces = 0;
            numericHighPassHz.ForeColor = Color.White;
            numericHighPassHz.LogarithmicFrequencyStep = true;
            numericHighPassHz.Location = new Point(100, 6);
            numericHighPassHz.Maximum = new decimal(new int[] { 24000, 0, 0, 0 });
            numericHighPassHz.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            numericHighPassHz.MinimumSize = new Size(36, 19);
            numericHighPassHz.Name = "numericHighPassHz";
            numericHighPassHz.Size = new Size(90, 23);
            numericHighPassHz.TextAlign = HorizontalAlignment.Right;
            numericHighPassHz.ThousandsSeparator = false;
            numericHighPassHz.Value = new decimal(new int[] { 80, 0, 0, 0 });
            numericHighPassHz.ValueSuffix = "Hz";
            numericHighPassHz.TabIndex = 8;
            // 
            // comboBoxHighPassFamily
            // 
            comboBoxHighPassFamily.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxHighPassFamily.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxHighPassFamily.ForeColor = Color.White;
            comboBoxHighPassFamily.FormattingEnabled = true;
            comboBoxHighPassFamily.Location = new Point(100, 35);
            comboBoxHighPassFamily.MinimumSize = new Size(36, 19);
            comboBoxHighPassFamily.Name = "comboBoxHighPassFamily";
            comboBoxHighPassFamily.Size = new Size(110, 23);
            comboBoxHighPassFamily.TabIndex = 9;
            // 
            // comboBoxHighPassSlope
            // 
            comboBoxHighPassSlope.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxHighPassSlope.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxHighPassSlope.ForeColor = Color.White;
            comboBoxHighPassSlope.FormattingEnabled = true;
            comboBoxHighPassSlope.Location = new Point(216, 35);
            comboBoxHighPassSlope.MinimumSize = new Size(36, 19);
            comboBoxHighPassSlope.Name = "comboBoxHighPassSlope";
            comboBoxHighPassSlope.Size = new Size(88, 23);
            comboBoxHighPassSlope.TabIndex = 10;
            // 
            // cardLowPass
            // 
            cardLowPass.BackColor = Color.FromArgb(50, 55, 66);
            cardLowPass.Location = new Point(18, 238);
            cardLowPass.Name = "cardLowPass";
            cardLowPass.Size = new Size(314, 64);
            cardLowPass.Controls.Add(comboBoxLowPassSlope);
            cardLowPass.Controls.Add(comboBoxLowPassFamily);
            cardLowPass.Controls.Add(numericLowPassHz);
            cardLowPass.Controls.Add(labelLowPass);
            cardLowPass.TabIndex = 11;
            // 
            // labelLowPass
            // 
            labelLowPass.AutoSize = true;
            labelLowPass.ForeColor = Color.FromArgb(210, 214, 222);
            labelLowPass.Location = new Point(10, 10);
            labelLowPass.Name = "labelLowPass";
            labelLowPass.Size = new Size(53, 15);
            labelLowPass.Text = "Low-pass";
            labelLowPass.TabIndex = 12;
            // 
            // numericLowPassHz
            // 
            numericLowPassHz.BackColor = Color.FromArgb(55, 60, 72);
            numericLowPassHz.DecimalPlaces = 0;
            numericLowPassHz.ForeColor = Color.White;
            numericLowPassHz.LogarithmicFrequencyStep = true;
            numericLowPassHz.Location = new Point(100, 6);
            numericLowPassHz.Maximum = new decimal(new int[] { 24000, 0, 0, 0 });
            numericLowPassHz.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            numericLowPassHz.MinimumSize = new Size(36, 19);
            numericLowPassHz.Name = "numericLowPassHz";
            numericLowPassHz.Size = new Size(90, 23);
            numericLowPassHz.TextAlign = HorizontalAlignment.Right;
            numericLowPassHz.ThousandsSeparator = false;
            numericLowPassHz.Value = new decimal(new int[] { 2000, 0, 0, 0 });
            numericLowPassHz.ValueSuffix = "Hz";
            numericLowPassHz.TabIndex = 13;
            // 
            // comboBoxLowPassFamily
            // 
            comboBoxLowPassFamily.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxLowPassFamily.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxLowPassFamily.ForeColor = Color.White;
            comboBoxLowPassFamily.FormattingEnabled = true;
            comboBoxLowPassFamily.Location = new Point(100, 35);
            comboBoxLowPassFamily.MinimumSize = new Size(36, 19);
            comboBoxLowPassFamily.Name = "comboBoxLowPassFamily";
            comboBoxLowPassFamily.Size = new Size(110, 23);
            comboBoxLowPassFamily.TabIndex = 14;
            // 
            // comboBoxLowPassSlope
            // 
            comboBoxLowPassSlope.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxLowPassSlope.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxLowPassSlope.ForeColor = Color.White;
            comboBoxLowPassSlope.FormattingEnabled = true;
            comboBoxLowPassSlope.Location = new Point(216, 35);
            comboBoxLowPassSlope.MinimumSize = new Size(36, 19);
            comboBoxLowPassSlope.Name = "comboBoxLowPassSlope";
            comboBoxLowPassSlope.Size = new Size(88, 23);
            comboBoxLowPassSlope.TabIndex = 15;
            // 
            // labelWindow
            // 
            labelWindow.AutoSize = true;
            labelWindow.ForeColor = Color.FromArgb(210, 214, 222);
            labelWindow.Location = new Point(18, 318);
            labelWindow.Name = "labelWindow";
            labelWindow.Size = new Size(51, 15);
            labelWindow.Text = "Window";
            labelWindow.TabIndex = 16;
            // 
            // comboBoxWindow
            // 
            comboBoxWindow.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxWindow.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxWindow.ForeColor = Color.White;
            comboBoxWindow.FormattingEnabled = true;
            comboBoxWindow.Location = new Point(118, 314);
            comboBoxWindow.MinimumSize = new Size(36, 19);
            comboBoxWindow.Name = "comboBoxWindow";
            comboBoxWindow.Size = new Size(110, 23);
            comboBoxWindow.TabIndex = 17;
            // 
            // labelKaiserBeta
            // 
            labelKaiserBeta.AutoSize = true;
            labelKaiserBeta.ForeColor = Color.FromArgb(210, 214, 222);
            labelKaiserBeta.Location = new Point(240, 318);
            labelKaiserBeta.Name = "labelKaiserBeta";
            labelKaiserBeta.Size = new Size(12, 15);
            labelKaiserBeta.Text = "β";
            labelKaiserBeta.TabIndex = 18;
            // 
            // numericKaiserBeta
            // 
            numericKaiserBeta.BackColor = Color.FromArgb(55, 60, 72);
            numericKaiserBeta.DecimalPlaces = 1;
            numericKaiserBeta.ForeColor = Color.White;
            numericKaiserBeta.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            numericKaiserBeta.Location = new Point(258, 314);
            numericKaiserBeta.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            numericKaiserBeta.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericKaiserBeta.MinimumSize = new Size(36, 19);
            numericKaiserBeta.Name = "numericKaiserBeta";
            numericKaiserBeta.Size = new Size(64, 23);
            numericKaiserBeta.TextAlign = HorizontalAlignment.Right;
            numericKaiserBeta.ThousandsSeparator = false;
            numericKaiserBeta.Value = new decimal(new int[] { 8, 0, 0, 0 });
            numericKaiserBeta.TabIndex = 19;
            // 
            // labelTaps
            // 
            labelTaps.AutoSize = true;
            labelTaps.ForeColor = Color.FromArgb(210, 214, 222);
            labelTaps.Location = new Point(18, 348);
            labelTaps.Name = "labelTaps";
            labelTaps.Size = new Size(30, 15);
            labelTaps.Text = "Taps";
            labelTaps.TabIndex = 20;
            // 
            // numericTaps
            // 
            numericTaps.BackColor = Color.FromArgb(55, 60, 72);
            numericTaps.DecimalPlaces = 0;
            numericTaps.ForeColor = Color.White;
            numericTaps.Increment = new decimal(new int[] { 2, 0, 0, 0 });
            numericTaps.Location = new Point(118, 344);
            numericTaps.Maximum = new decimal(new int[] { 16383, 0, 0, 0 });
            numericTaps.Minimum = new decimal(new int[] { 3, 0, 0, 0 });
            numericTaps.MinimumSize = new Size(36, 19);
            numericTaps.Name = "numericTaps";
            numericTaps.Size = new Size(90, 23);
            numericTaps.TextAlign = HorizontalAlignment.Right;
            numericTaps.ThousandsSeparator = false;
            numericTaps.Value = new decimal(new int[] { 4095, 0, 0, 0 });
            numericTaps.TabIndex = 21;
            // 
            // labelTapsHint
            // 
            labelTapsHint.AutoSize = true;
            labelTapsHint.ForeColor = Color.FromArgb(150, 156, 170);
            labelTapsHint.Location = new Point(214, 348);
            labelTapsHint.Name = "labelTapsHint";
            labelTapsHint.Size = new Size(96, 15);
            labelTapsHint.Text = "odd, up to 16383";
            labelTapsHint.TabIndex = 22;
            // 
            // labelSampleRate
            // 
            labelSampleRate.AutoSize = true;
            labelSampleRate.ForeColor = Color.FromArgb(210, 214, 222);
            labelSampleRate.Location = new Point(18, 378);
            labelSampleRate.Name = "labelSampleRate";
            labelSampleRate.Size = new Size(69, 15);
            labelSampleRate.Text = "Sample rate";
            labelSampleRate.TabIndex = 23;
            // 
            // comboBoxSampleRate
            // 
            comboBoxSampleRate.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxSampleRate.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxSampleRate.ForeColor = Color.White;
            comboBoxSampleRate.FormattingEnabled = true;
            comboBoxSampleRate.Location = new Point(118, 374);
            comboBoxSampleRate.MinimumSize = new Size(36, 19);
            comboBoxSampleRate.Name = "comboBoxSampleRate";
            comboBoxSampleRate.Size = new Size(90, 23);
            comboBoxSampleRate.TabIndex = 24;
            // 
            // labelLatency
            // 
            labelLatency.AutoEllipsis = true;
            labelLatency.ForeColor = Color.FromArgb(210, 214, 222);
            labelLatency.Location = new Point(18, 414);
            labelLatency.Name = "labelLatency";
            labelLatency.Size = new Size(314, 32);
            labelLatency.Text = "Latency";
            labelLatency.TabIndex = 25;
            // 
            // labelDeviation
            // 
            labelDeviation.AutoEllipsis = true;
            labelDeviation.ForeColor = Color.FromArgb(210, 214, 222);
            labelDeviation.Location = new Point(18, 448);
            labelDeviation.Name = "labelDeviation";
            labelDeviation.Size = new Size(314, 32);
            labelDeviation.Text = "Deviation";
            labelDeviation.TabIndex = 26;
            // 
            // labelProblem
            // 
            labelProblem.AutoEllipsis = true;
            labelProblem.ForeColor = Color.FromArgb(255, 130, 130);
            labelProblem.Location = new Point(18, 482);
            labelProblem.Name = "labelProblem";
            labelProblem.Size = new Size(314, 34);
            labelProblem.Text = "";
            labelProblem.TabIndex = 27;
            // 
            // buttonImport
            // 
            buttonImport.BackColor = Color.FromArgb(50, 55, 80);
            buttonImport.FlatStyle = FlatStyle.Popup;
            buttonImport.ForeColor = Color.White;
            buttonImport.Location = new Point(18, 524);
            buttonImport.Name = "buttonImport";
            buttonImport.Size = new Size(120, 26);
            buttonImport.Text = "Import file…";
            buttonImport.UseVisualStyleBackColor = false;
            buttonImport.TabIndex = 28;
            // 
            // buttonExport
            // 
            buttonExport.BackColor = Color.FromArgb(50, 55, 80);
            buttonExport.FlatStyle = FlatStyle.Popup;
            buttonExport.ForeColor = Color.White;
            buttonExport.Location = new Point(144, 524);
            buttonExport.Name = "buttonExport";
            buttonExport.Size = new Size(120, 26);
            buttonExport.Text = "Export file…";
            buttonExport.UseVisualStyleBackColor = false;
            buttonExport.TabIndex = 29;
            // 
            // buttonReturnToDsp
            // 
            buttonReturnToDsp.BackColor = Color.FromArgb(50, 55, 80);
            buttonReturnToDsp.FlatStyle = FlatStyle.Popup;
            buttonReturnToDsp.ForeColor = Color.White;
            buttonReturnToDsp.Location = new Point(18, 562);
            buttonReturnToDsp.Name = "buttonReturnToDsp";
            buttonReturnToDsp.Size = new Size(246, 26);
            buttonReturnToDsp.Text = "Return FIR to Virtual DSP";
            buttonReturnToDsp.UseVisualStyleBackColor = false;
            buttonReturnToDsp.Visible = false;
            buttonReturnToDsp.TabIndex = 30;
            // 
            // buttonBackToDsp
            // 
            buttonBackToDsp.BackColor = Color.FromArgb(50, 55, 80);
            buttonBackToDsp.FlatStyle = FlatStyle.Popup;
            buttonBackToDsp.ForeColor = Color.White;
            buttonBackToDsp.Location = new Point(18, 592);
            buttonBackToDsp.Name = "buttonBackToDsp";
            buttonBackToDsp.Size = new Size(246, 24);
            buttonBackToDsp.Text = "Back without applying";
            buttonBackToDsp.UseVisualStyleBackColor = false;
            buttonBackToDsp.Visible = false;
            buttonBackToDsp.TabIndex = 31;
            // 
            // plotMagnitude
            // 
            plotMagnitude.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            plotMagnitude.BackColor = Color.FromArgb(32, 36, 46);
            plotMagnitude.Location = new Point(350, 14);
            plotMagnitude.Name = "plotMagnitude";
            plotMagnitude.PanCursor = Cursors.Hand;
            plotMagnitude.Size = new Size(880, 364);
            plotMagnitude.ZoomHorizontalCursor = Cursors.SizeWE;
            plotMagnitude.ZoomRectangleCursor = Cursors.SizeNWSE;
            plotMagnitude.ZoomVerticalCursor = Cursors.SizeNS;
            plotMagnitude.TabIndex = 32;
            // 
            // plotPhase
            // 
            plotPhase.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            plotPhase.BackColor = Color.FromArgb(32, 36, 46);
            plotPhase.Location = new Point(350, 390);
            plotPhase.Name = "plotPhase";
            plotPhase.PanCursor = Cursors.Hand;
            plotPhase.Size = new Size(880, 364);
            plotPhase.ZoomHorizontalCursor = Cursors.SizeWE;
            plotPhase.ZoomRectangleCursor = Cursors.SizeNWSE;
            plotPhase.ZoomVerticalCursor = Cursors.SizeNS;
            plotPhase.TabIndex = 33;
            // 
            // FirConstructorPanel
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScroll = true;
            BackColor = Color.FromArgb(40, 44, 54);
            BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(plotPhase);
            Controls.Add(plotMagnitude);
            Controls.Add(buttonBackToDsp);
            Controls.Add(buttonReturnToDsp);
            Controls.Add(buttonExport);
            Controls.Add(buttonImport);
            Controls.Add(labelProblem);
            Controls.Add(labelDeviation);
            Controls.Add(labelLatency);
            Controls.Add(comboBoxSampleRate);
            Controls.Add(labelSampleRate);
            Controls.Add(labelTapsHint);
            Controls.Add(numericTaps);
            Controls.Add(labelTaps);
            Controls.Add(numericKaiserBeta);
            Controls.Add(labelKaiserBeta);
            Controls.Add(comboBoxWindow);
            Controls.Add(labelWindow);
            Controls.Add(cardLowPass);
            Controls.Add(cardHighPass);
            Controls.Add(comboBoxMethod);
            Controls.Add(labelMethod);
            Controls.Add(comboBoxType);
            Controls.Add(labelType);
            Controls.Add(labelSession);
            Controls.Add(titleLabel);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            Name = "FirConstructorPanel";
            Size = new Size(1244, 768);
            ((System.ComponentModel.ISupportInitialize)numericHighPassHz).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericLowPassHz).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericKaiserBeta).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericTaps).EndInit();
            cardHighPass.ResumeLayout(false);
            cardHighPass.PerformLayout();
            cardLowPass.ResumeLayout(false);
            cardLowPass.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label titleLabel;
        private Label labelSession;
        private Label labelType;
        private DarkComboBox comboBoxType;
        private Label labelMethod;
        private DarkComboBox comboBoxMethod;
        private RoundedPanel cardHighPass;
        private Label labelHighPass;
        private DarkNumericUpDown numericHighPassHz;
        private DarkComboBox comboBoxHighPassFamily;
        private DarkComboBox comboBoxHighPassSlope;
        private RoundedPanel cardLowPass;
        private Label labelLowPass;
        private DarkNumericUpDown numericLowPassHz;
        private DarkComboBox comboBoxLowPassFamily;
        private DarkComboBox comboBoxLowPassSlope;
        private Label labelWindow;
        private DarkComboBox comboBoxWindow;
        private Label labelKaiserBeta;
        private DarkNumericUpDown numericKaiserBeta;
        private Label labelTaps;
        private DarkNumericUpDown numericTaps;
        private Label labelTapsHint;
        private Label labelSampleRate;
        private DarkComboBox comboBoxSampleRate;
        private Label labelLatency;
        private Label labelDeviation;
        private Label labelProblem;
        private ReleaseClickButton buttonImport;
        private ReleaseClickButton buttonExport;
        private ReleaseClickButton buttonReturnToDsp;
        private ReleaseClickButton buttonBackToDsp;
        private OxyPlot.WindowsForms.PlotView plotMagnitude;
        private OxyPlot.WindowsForms.PlotView plotPhase;
    }
}
