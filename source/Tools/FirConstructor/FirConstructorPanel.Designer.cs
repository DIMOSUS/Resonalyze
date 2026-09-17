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
            comboBoxType = new ThemedComboBox();
            labelMethod = new Label();
            comboBoxMethod = new ThemedComboBox();
            cardHighPass = new RoundedPanel();
            labelHighPass = new Label();
            numericHighPassHz = new ThemedNumericUpDown();
            comboBoxHighPassFamily = new ThemedComboBox();
            comboBoxHighPassSlope = new ThemedComboBox();
            cardLowPass = new RoundedPanel();
            labelLowPass = new Label();
            numericLowPassHz = new ThemedNumericUpDown();
            comboBoxLowPassFamily = new ThemedComboBox();
            comboBoxLowPassSlope = new ThemedComboBox();
            labelWindow = new Label();
            comboBoxWindow = new ThemedComboBox();
            labelKaiserBeta = new Label();
            numericKaiserBeta = new ThemedNumericUpDown();
            labelTaps = new Label();
            numericTaps = new ThemedNumericUpDown();
            labelTapsHint = new Label();
            labelSampleRate = new Label();
            comboBoxSampleRate = new ThemedComboBox();
            labelImpulseScale = new Label();
            checkBoxImpulseDb = new ReleaseClickCheckBox();
            labelLatency = new Label();
            labelDeviation = new Label();
            labelProblem = new Label();
            buttonImport = new ReleaseClickButton();
            buttonExport = new ReleaseClickButton();
            buttonReturnToDsp = new ReleaseClickButton();
            buttonBackToDsp = new ReleaseClickButton();
            plotResponse = new OxyPlot.WindowsForms.PlotView();
            plotImpulse = new OxyPlot.WindowsForms.PlotView();
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
            titleLabel.ForeColor = UiPalette.TextDefault;
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
            labelSession.ForeColor = UiPalette.TextAccent;
            labelSession.Location = new Point(18, 46);
            labelSession.Name = "labelSession";
            labelSession.Size = new Size(314, 48);
            labelSession.Text = "Standalone: export the kernel to a file.";
            labelSession.TabIndex = 1;
            // 
            // labelType
            // 
            labelType.AutoSize = true;
            labelType.ForeColor = UiPalette.TextDefault;
            labelType.Location = new Point(18, 108);
            labelType.Name = "labelType";
            labelType.Size = new Size(31, 15);
            labelType.Text = "Type";
            labelType.TabIndex = 2;
            // 
            // comboBoxType
            // 
            comboBoxType.BackColor = UiPalette.ControlSurface;
            comboBoxType.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxType.ForeColor = UiPalette.TextPrimary;
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
            labelMethod.ForeColor = UiPalette.TextDefault;
            labelMethod.Location = new Point(18, 138);
            labelMethod.Name = "labelMethod";
            labelMethod.Size = new Size(49, 15);
            labelMethod.Text = "Method";
            labelMethod.TabIndex = 4;
            // 
            // comboBoxMethod
            // 
            comboBoxMethod.BackColor = UiPalette.ControlSurface;
            comboBoxMethod.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxMethod.ForeColor = UiPalette.TextPrimary;
            comboBoxMethod.FormattingEnabled = true;
            comboBoxMethod.Location = new Point(118, 134);
            comboBoxMethod.MinimumSize = new Size(36, 19);
            comboBoxMethod.Name = "comboBoxMethod";
            comboBoxMethod.Size = new Size(150, 23);
            comboBoxMethod.TabIndex = 5;
            // 
            // cardHighPass
            // 
            cardHighPass.BackColor = UiPalette.PanelSurface;
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
            labelHighPass.ForeColor = UiPalette.TextDefault;
            labelHighPass.Location = new Point(10, 10);
            labelHighPass.Name = "labelHighPass";
            labelHighPass.Size = new Size(56, 15);
            labelHighPass.Text = "High-pass";
            labelHighPass.TabIndex = 7;
            // 
            // numericHighPassHz
            // 
            numericHighPassHz.BackColor = UiPalette.ControlSurface;
            numericHighPassHz.DecimalPlaces = 0;
            numericHighPassHz.ForeColor = UiPalette.TextPrimary;
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
            comboBoxHighPassFamily.BackColor = UiPalette.ControlSurface;
            comboBoxHighPassFamily.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxHighPassFamily.ForeColor = UiPalette.TextPrimary;
            comboBoxHighPassFamily.FormattingEnabled = true;
            comboBoxHighPassFamily.Location = new Point(100, 35);
            comboBoxHighPassFamily.MinimumSize = new Size(36, 19);
            comboBoxHighPassFamily.Name = "comboBoxHighPassFamily";
            comboBoxHighPassFamily.Size = new Size(110, 23);
            comboBoxHighPassFamily.TabIndex = 9;
            // 
            // comboBoxHighPassSlope
            // 
            comboBoxHighPassSlope.BackColor = UiPalette.ControlSurface;
            comboBoxHighPassSlope.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxHighPassSlope.ForeColor = UiPalette.TextPrimary;
            comboBoxHighPassSlope.FormattingEnabled = true;
            comboBoxHighPassSlope.Location = new Point(216, 35);
            comboBoxHighPassSlope.MinimumSize = new Size(36, 19);
            comboBoxHighPassSlope.Name = "comboBoxHighPassSlope";
            comboBoxHighPassSlope.Size = new Size(88, 23);
            comboBoxHighPassSlope.TabIndex = 10;
            // 
            // cardLowPass
            // 
            cardLowPass.BackColor = UiPalette.PanelSurface;
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
            labelLowPass.ForeColor = UiPalette.TextDefault;
            labelLowPass.Location = new Point(10, 10);
            labelLowPass.Name = "labelLowPass";
            labelLowPass.Size = new Size(53, 15);
            labelLowPass.Text = "Low-pass";
            labelLowPass.TabIndex = 12;
            // 
            // numericLowPassHz
            // 
            numericLowPassHz.BackColor = UiPalette.ControlSurface;
            numericLowPassHz.DecimalPlaces = 0;
            numericLowPassHz.ForeColor = UiPalette.TextPrimary;
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
            comboBoxLowPassFamily.BackColor = UiPalette.ControlSurface;
            comboBoxLowPassFamily.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxLowPassFamily.ForeColor = UiPalette.TextPrimary;
            comboBoxLowPassFamily.FormattingEnabled = true;
            comboBoxLowPassFamily.Location = new Point(100, 35);
            comboBoxLowPassFamily.MinimumSize = new Size(36, 19);
            comboBoxLowPassFamily.Name = "comboBoxLowPassFamily";
            comboBoxLowPassFamily.Size = new Size(110, 23);
            comboBoxLowPassFamily.TabIndex = 14;
            // 
            // comboBoxLowPassSlope
            // 
            comboBoxLowPassSlope.BackColor = UiPalette.ControlSurface;
            comboBoxLowPassSlope.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxLowPassSlope.ForeColor = UiPalette.TextPrimary;
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
            labelWindow.ForeColor = UiPalette.TextDefault;
            labelWindow.Location = new Point(18, 318);
            labelWindow.Name = "labelWindow";
            labelWindow.Size = new Size(51, 15);
            labelWindow.Text = "Window";
            labelWindow.TabIndex = 16;
            // 
            // comboBoxWindow
            // 
            comboBoxWindow.BackColor = UiPalette.ControlSurface;
            comboBoxWindow.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxWindow.ForeColor = UiPalette.TextPrimary;
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
            labelKaiserBeta.ForeColor = UiPalette.TextDefault;
            labelKaiserBeta.Location = new Point(240, 318);
            labelKaiserBeta.Name = "labelKaiserBeta";
            labelKaiserBeta.Size = new Size(12, 15);
            labelKaiserBeta.Text = "β";
            labelKaiserBeta.TabIndex = 18;
            // 
            // numericKaiserBeta
            // 
            numericKaiserBeta.BackColor = UiPalette.ControlSurface;
            numericKaiserBeta.DecimalPlaces = 1;
            numericKaiserBeta.ForeColor = UiPalette.TextPrimary;
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
            labelTaps.ForeColor = UiPalette.TextDefault;
            labelTaps.Location = new Point(18, 348);
            labelTaps.Name = "labelTaps";
            labelTaps.Size = new Size(30, 15);
            labelTaps.Text = "Taps";
            labelTaps.TabIndex = 20;
            // 
            // numericTaps
            // 
            numericTaps.BackColor = UiPalette.ControlSurface;
            numericTaps.DecimalPlaces = 0;
            numericTaps.ForeColor = UiPalette.TextPrimary;
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
            labelTapsHint.ForeColor = UiPalette.TextMuted;
            labelTapsHint.Location = new Point(214, 348);
            labelTapsHint.Name = "labelTapsHint";
            labelTapsHint.Size = new Size(96, 15);
            labelTapsHint.Text = "odd, up to 16383";
            labelTapsHint.TabIndex = 22;
            // 
            // labelSampleRate
            // 
            labelSampleRate.AutoSize = true;
            labelSampleRate.ForeColor = UiPalette.TextDefault;
            labelSampleRate.Location = new Point(18, 378);
            labelSampleRate.Name = "labelSampleRate";
            labelSampleRate.Size = new Size(69, 15);
            labelSampleRate.Text = "Sample rate";
            labelSampleRate.TabIndex = 23;
            // 
            // comboBoxSampleRate
            // 
            comboBoxSampleRate.BackColor = UiPalette.ControlSurface;
            comboBoxSampleRate.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxSampleRate.ForeColor = UiPalette.TextPrimary;
            comboBoxSampleRate.FormattingEnabled = true;
            comboBoxSampleRate.Location = new Point(118, 374);
            comboBoxSampleRate.MinimumSize = new Size(36, 19);
            comboBoxSampleRate.Name = "comboBoxSampleRate";
            comboBoxSampleRate.Size = new Size(90, 23);
            comboBoxSampleRate.TabIndex = 24;
            // 
            // labelImpulseScale
            // 
            labelImpulseScale.AutoSize = true;
            labelImpulseScale.ForeColor = UiPalette.TextDefault;
            labelImpulseScale.Location = new Point(18, 408);
            labelImpulseScale.Name = "labelImpulseScale";
            labelImpulseScale.Size = new Size(47, 15);
            labelImpulseScale.Text = "Impulse";
            labelImpulseScale.TabIndex = 25;
            // 
            // checkBoxImpulseDb
            // 
            checkBoxImpulseDb.AutoSize = true;
            checkBoxImpulseDb.ForeColor = UiPalette.TextDefault;
            checkBoxImpulseDb.Location = new Point(118, 406);
            checkBoxImpulseDb.Name = "checkBoxImpulseDb";
            checkBoxImpulseDb.Size = new Size(55, 19);
            checkBoxImpulseDb.Text = "in dB";
            checkBoxImpulseDb.UseVisualStyleBackColor = true;
            checkBoxImpulseDb.TabIndex = 26;
            // 
            // labelLatency
            // 
            labelLatency.AutoEllipsis = true;
            labelLatency.ForeColor = UiPalette.TextDefault;
            labelLatency.Location = new Point(18, 444);
            labelLatency.Name = "labelLatency";
            labelLatency.Size = new Size(314, 32);
            labelLatency.Text = "Latency";
            labelLatency.TabIndex = 27;
            // 
            // labelDeviation
            // 
            labelDeviation.AutoEllipsis = true;
            labelDeviation.ForeColor = UiPalette.TextDefault;
            labelDeviation.Location = new Point(18, 478);
            labelDeviation.Name = "labelDeviation";
            labelDeviation.Size = new Size(314, 32);
            labelDeviation.Text = "Deviation";
            labelDeviation.TabIndex = 28;
            // 
            // labelProblem
            // 
            labelProblem.AutoEllipsis = true;
            labelProblem.ForeColor = UiPalette.Error;
            labelProblem.Location = new Point(18, 512);
            labelProblem.Name = "labelProblem";
            labelProblem.Size = new Size(314, 34);
            labelProblem.Text = "";
            labelProblem.TabIndex = 29;
            // 
            // buttonImport
            // 
            buttonImport.BackColor = UiPalette.ButtonBackground;
            buttonImport.FlatStyle = FlatStyle.Popup;
            buttonImport.ForeColor = UiPalette.TextPrimary;
            buttonImport.Location = new Point(18, 554);
            buttonImport.Name = "buttonImport";
            buttonImport.Size = new Size(120, 26);
            buttonImport.Text = "Import file…";
            buttonImport.UseVisualStyleBackColor = false;
            buttonImport.TabIndex = 30;
            // 
            // buttonExport
            // 
            buttonExport.BackColor = UiPalette.ButtonBackground;
            buttonExport.FlatStyle = FlatStyle.Popup;
            buttonExport.ForeColor = UiPalette.TextPrimary;
            buttonExport.Location = new Point(144, 554);
            buttonExport.Name = "buttonExport";
            buttonExport.Size = new Size(120, 26);
            buttonExport.Text = "Export file…";
            buttonExport.UseVisualStyleBackColor = false;
            buttonExport.TabIndex = 31;
            // 
            // buttonReturnToDsp
            // 
            buttonReturnToDsp.BackColor = UiPalette.ButtonBackground;
            buttonReturnToDsp.FlatStyle = FlatStyle.Popup;
            buttonReturnToDsp.ForeColor = UiPalette.TextPrimary;
            buttonReturnToDsp.Location = new Point(18, 592);
            buttonReturnToDsp.Name = "buttonReturnToDsp";
            buttonReturnToDsp.Size = new Size(246, 26);
            buttonReturnToDsp.Text = "Return FIR to Virtual DSP";
            buttonReturnToDsp.UseVisualStyleBackColor = false;
            buttonReturnToDsp.Visible = false;
            buttonReturnToDsp.TabIndex = 32;
            // 
            // buttonBackToDsp
            // 
            buttonBackToDsp.BackColor = UiPalette.ButtonBackground;
            buttonBackToDsp.FlatStyle = FlatStyle.Popup;
            buttonBackToDsp.ForeColor = UiPalette.TextPrimary;
            buttonBackToDsp.Location = new Point(18, 622);
            buttonBackToDsp.Name = "buttonBackToDsp";
            buttonBackToDsp.Size = new Size(246, 24);
            buttonBackToDsp.Text = "Back without applying";
            buttonBackToDsp.UseVisualStyleBackColor = false;
            buttonBackToDsp.Visible = false;
            buttonBackToDsp.TabIndex = 33;
            // 
            // plotResponse
            // 
            plotResponse.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            plotResponse.BackColor = UiPalette.GraphSurfaceMuted;
            plotResponse.Location = new Point(350, 14);
            plotResponse.Name = "plotResponse";
            plotResponse.PanCursor = Cursors.Hand;
            plotResponse.Size = new Size(880, 364);
            plotResponse.ZoomHorizontalCursor = Cursors.SizeWE;
            plotResponse.ZoomRectangleCursor = Cursors.SizeNWSE;
            plotResponse.ZoomVerticalCursor = Cursors.SizeNS;
            plotResponse.TabIndex = 34;
            // 
            // plotImpulse
            // 
            plotImpulse.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            plotImpulse.BackColor = UiPalette.GraphSurfaceMuted;
            plotImpulse.Location = new Point(350, 390);
            plotImpulse.Name = "plotImpulse";
            plotImpulse.PanCursor = Cursors.Hand;
            plotImpulse.Size = new Size(880, 364);
            plotImpulse.ZoomHorizontalCursor = Cursors.SizeWE;
            plotImpulse.ZoomRectangleCursor = Cursors.SizeNWSE;
            plotImpulse.ZoomVerticalCursor = Cursors.SizeNS;
            plotImpulse.TabIndex = 35;
            // 
            // FirConstructorPanel
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScroll = true;
            BackColor = UiPalette.ShellSurface;
            BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(plotImpulse);
            Controls.Add(plotResponse);
            Controls.Add(buttonBackToDsp);
            Controls.Add(buttonReturnToDsp);
            Controls.Add(buttonExport);
            Controls.Add(buttonImport);
            Controls.Add(labelProblem);
            Controls.Add(labelDeviation);
            Controls.Add(labelLatency);
            Controls.Add(checkBoxImpulseDb);
            Controls.Add(labelImpulseScale);
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
            ForeColor = UiPalette.TextPrimary;
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
        private ThemedComboBox comboBoxType;
        private Label labelMethod;
        private ThemedComboBox comboBoxMethod;
        private RoundedPanel cardHighPass;
        private Label labelHighPass;
        private ThemedNumericUpDown numericHighPassHz;
        private ThemedComboBox comboBoxHighPassFamily;
        private ThemedComboBox comboBoxHighPassSlope;
        private RoundedPanel cardLowPass;
        private Label labelLowPass;
        private ThemedNumericUpDown numericLowPassHz;
        private ThemedComboBox comboBoxLowPassFamily;
        private ThemedComboBox comboBoxLowPassSlope;
        private Label labelWindow;
        private ThemedComboBox comboBoxWindow;
        private Label labelKaiserBeta;
        private ThemedNumericUpDown numericKaiserBeta;
        private Label labelTaps;
        private ThemedNumericUpDown numericTaps;
        private Label labelTapsHint;
        private Label labelSampleRate;
        private ThemedComboBox comboBoxSampleRate;
        private Label labelImpulseScale;
        private ReleaseClickCheckBox checkBoxImpulseDb;
        private Label labelLatency;
        private Label labelDeviation;
        private Label labelProblem;
        private ReleaseClickButton buttonImport;
        private ReleaseClickButton buttonExport;
        private ReleaseClickButton buttonReturnToDsp;
        private ReleaseClickButton buttonBackToDsp;
        private OxyPlot.WindowsForms.PlotView plotResponse;
        private OxyPlot.WindowsForms.PlotView plotImpulse;
    }
}
