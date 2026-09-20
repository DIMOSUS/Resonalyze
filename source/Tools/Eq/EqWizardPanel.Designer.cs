namespace Resonalyze
{
    partial class EqWizardPanel
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
                // Rebuilt on every open (see ShowSourceMenu), so the last one is not
                // owned by the designer container and would otherwise leak its handle.
                sourceMenu?.Dispose();
                sourceMenu = null;
                targetMenu?.Dispose();
                targetMenu = null;
                bandTypeMenu?.Dispose();
                bandTypeMenu = null;
                // Created in code (see EqWizardPanel.Bank.cs), so it is not in the
                // designer container either.
                bankEditTimer.Dispose();
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
            plotWizard = new OxyPlot.WindowsForms.PlotView();
            panelPEQ = new Panel();
            labelBands = new Label();
            buttonSource = new ReleaseClickButton();
            darkComboBoxBands = new ThemedComboBox();
            NumericTargetOffset = new ThemedNumericUpDown();
            labelTargetOffset = new Label();
            NumericGain = new ThemedNumericUpDown();
            labelGain = new Label();
            buttonAutoTune = new ReleaseClickButton();
            comboBoxBandsLimit = new ThemedComboBox();
            labelBandsLimit = new Label();
            numericToHz = new ThemedNumericUpDown();
            numericFromHz = new ThemedNumericUpDown();
            labelFromHz = new Label();
            labelToHz = new Label();
            numericGainMin = new ThemedNumericUpDown();
            labelGainMin = new Label();
            numericGainMax = new ThemedNumericUpDown();
            labelGainMax = new Label();
            numericQMax = new ThemedNumericUpDown();
            labelQMax = new Label();
            checkBoxBypass = new ReleaseClickCheckBox();
            checkBoxEqPhase = new ReleaseClickCheckBox();
            checkBoxEqCurve = new ReleaseClickCheckBox();
            comboBoxBoosts = new ThemedComboBox();
            labelBoosts = new Label();
            checkBoxShelves = new ReleaseClickCheckBox();
            panelAutoTune = new RoundedPanel();
            buttonOverlaySettings = new ReleaseClickButton();
            comboBoxCalibration = new ThemedComboBox();
            labelCalibration = new Label();
            comboBoxSmooth = new ThemedComboBox();
            labelSmooth = new Label();
            comboBoxSampleRate = new ThemedComboBox();
            labelSampleRate = new Label();
            buttonPhaseGate = new ReleaseClickButton();
            buttonImport = new ReleaseClickButton();
            buttonExport = new ReleaseClickButton();
            buttonResetBands = new ReleaseClickButton();
            buttonUndo = new ReleaseClickButton();
            buttonRedo = new ReleaseClickButton();
            comboBoxQConvention = new ThemedComboBox();
            labelQConvention = new Label();
            buttonReturnToDsp = new ReleaseClickButton();
            buttonBackToDsp = new ReleaseClickButton();
            (NumericTargetOffset).BeginInit();
            (NumericGain).BeginInit();
            (numericToHz).BeginInit();
            (numericFromHz).BeginInit();
            (numericGainMin).BeginInit();
            (numericGainMax).BeginInit();
            (numericQMax).BeginInit();
            panelAutoTune.SuspendLayout();
            SuspendLayout();
            // 
            // plotWizard
            // 
            plotWizard.BackColor = UiPalette.GraphSurface;
            plotWizard.Location = new Point(197, 14);
            plotWizard.Margin = new Padding(6);
            plotWizard.Name = "plotWizard";
            plotWizard.PanCursor = Cursors.Hand;
            plotWizard.Size = new Size(1034, 348);
            plotWizard.TabIndex = 1;
            plotWizard.Text = "plotView1";
            plotWizard.ZoomHorizontalCursor = Cursors.SizeWE;
            plotWizard.ZoomRectangleCursor = Cursors.SizeNWSE;
            plotWizard.ZoomVerticalCursor = Cursors.SizeNS;
            // 
            // panelPEQ
            // 
            panelPEQ.BackColor = UiPalette.PanelSurfaceDeep;
            panelPEQ.BorderStyle = BorderStyle.FixedSingle;
            panelPEQ.Location = new Point(197, 371);
            panelPEQ.Name = "panelPEQ";
            panelPEQ.Size = new Size(1034, 390);
            panelPEQ.TabIndex = 2;
            // 
            // labelBands
            // 
            labelBands.AutoSize = true;
            labelBands.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelBands.ForeColor = UiPalette.TextDefault;
            labelBands.Location = new Point(9, 172);
            labelBands.Margin = new Padding(3);
            labelBands.Name = "labelBands";
            labelBands.Size = new Size(56, 15);
            labelBands.TabIndex = 4;
            labelBands.Text = "EQ Filters";
            // 
            // buttonSource
            // 
            buttonSource.FlatStyle = FlatStyle.Popup;
            buttonSource.ForeColor = UiPalette.TextPrimary;
            buttonSource.Location = new Point(6, 12);
            buttonSource.Name = "buttonSource";
            buttonSource.Size = new Size(182, 24);
            buttonSource.TabIndex = 5;
            buttonSource.Text = "Source…";
            buttonSource.UseVisualStyleBackColor = true;
            // 
            // darkComboBoxBands
            // 
            darkComboBoxBands.BackColor = UiPalette.ControlSurface;
            darkComboBoxBands.ForeColor = UiPalette.TextPrimary;
            darkComboBoxBands.Location = new Point(108, 170);
            darkComboBoxBands.MinimumSize = new Size(36, 19);
            darkComboBoxBands.Name = "darkComboBoxBands";
            darkComboBoxBands.Size = new Size(80, 19);
            darkComboBoxBands.TabIndex = 7;
            // 
            // NumericTargetOffset
            // 
            NumericTargetOffset.BackColor = UiPalette.ControlSurface;
            NumericTargetOffset.DecimalPlaces = 0;
            NumericTargetOffset.ForeColor = UiPalette.TextPrimary;
            NumericTargetOffset.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            NumericTargetOffset.Location = new Point(108, 95);
            NumericTargetOffset.Maximum = new decimal(new int[] { 180, 0, 0, 0 });
            NumericTargetOffset.Minimum = new decimal(new int[] { 180, 0, 0, int.MinValue });
            NumericTargetOffset.MinimumSize = new Size(36, 19);
            NumericTargetOffset.Name = "NumericTargetOffset";
            NumericTargetOffset.Size = new Size(80, 19);
            NumericTargetOffset.TabIndex = 8;
            NumericTargetOffset.TextAlign = HorizontalAlignment.Right;
            NumericTargetOffset.ThousandsSeparator = false;
            NumericTargetOffset.Value = new decimal(new int[] { 0, 0, 0, 0 });
            NumericTargetOffset.ValueSuffix = "dB";
            // 
            // labelTargetOffset
            // 
            labelTargetOffset.AutoSize = true;
            labelTargetOffset.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelTargetOffset.ForeColor = UiPalette.TextDefault;
            labelTargetOffset.Location = new Point(9, 97);
            labelTargetOffset.Margin = new Padding(3);
            labelTargetOffset.Name = "labelTargetOffset";
            labelTargetOffset.Size = new Size(70, 15);
            labelTargetOffset.TabIndex = 9;
            labelTargetOffset.Text = "Target Level";
            // 
            // NumericGain
            // 
            NumericGain.BackColor = UiPalette.ControlSurface;
            NumericGain.DecimalPlaces = 1;
            NumericGain.ForeColor = UiPalette.TextPrimary;
            NumericGain.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            NumericGain.Location = new Point(108, 195);
            NumericGain.Maximum = new decimal(new int[] { 80, 0, 0, 0 });
            NumericGain.Minimum = new decimal(new int[] { 80, 0, 0, int.MinValue });
            NumericGain.MinimumSize = new Size(36, 19);
            NumericGain.Name = "NumericGain";
            NumericGain.Size = new Size(80, 19);
            NumericGain.TabIndex = 10;
            NumericGain.TextAlign = HorizontalAlignment.Right;
            NumericGain.ThousandsSeparator = false;
            NumericGain.Value = new decimal(new int[] { 0, 0, 0, 0 });
            NumericGain.ValueSuffix = "dB";
            // 
            // labelGain
            // 
            labelGain.AutoSize = true;
            labelGain.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelGain.ForeColor = UiPalette.TextDefault;
            labelGain.Location = new Point(9, 197);
            labelGain.Margin = new Padding(3);
            labelGain.Name = "labelGain";
            labelGain.Size = new Size(48, 15);
            labelGain.TabIndex = 11;
            labelGain.Text = "Preamp";
            // 
            // buttonAutoTune
            // 
            buttonAutoTune.BackColor = UiPalette.ButtonBackground;
            buttonAutoTune.FlatStyle = FlatStyle.Popup;
            buttonAutoTune.ForeColor = UiPalette.TextPrimary;
            buttonAutoTune.Location = new Point(6, 206);
            buttonAutoTune.Name = "buttonAutoTune";
            buttonAutoTune.Size = new Size(173, 24);
            buttonAutoTune.TabIndex = 46;
            buttonAutoTune.Text = "Auto Tune";
            buttonAutoTune.UseVisualStyleBackColor = false;
            // 
            // comboBoxBandsLimit
            // 
            comboBoxBandsLimit.BackColor = UiPalette.ControlSurface;
            comboBoxBandsLimit.ForeColor = UiPalette.TextPrimary;
            comboBoxBandsLimit.Location = new Point(93, 131);
            comboBoxBandsLimit.MinimumSize = new Size(36, 19);
            comboBoxBandsLimit.Name = "comboBoxBandsLimit";
            comboBoxBandsLimit.Size = new Size(90, 19);
            comboBoxBandsLimit.TabIndex = 47;
            // 
            // labelBandsLimit
            // 
            labelBandsLimit.AutoSize = true;
            labelBandsLimit.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelBandsLimit.ForeColor = UiPalette.TextDefault;
            labelBandsLimit.Location = new Point(6, 133);
            labelBandsLimit.Margin = new Padding(3);
            labelBandsLimit.Name = "labelBandsLimit";
            labelBandsLimit.Size = new Size(82, 15);
            labelBandsLimit.TabIndex = 48;
            labelBandsLimit.Text = "Max EQ Filters";
            // 
            // numericToHz
            // 
            numericToHz.BackColor = UiPalette.ControlSurface;
            numericToHz.DecimalPlaces = 0;
            numericToHz.Font = new Font("Segoe UI", 9F);
            numericToHz.ForeColor = UiPalette.TextPrimary;
            numericToHz.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            numericToHz.Location = new Point(93, 106);
            numericToHz.LogarithmicFrequencyStep = true;
            numericToHz.Maximum = new decimal(new int[] { 20000, 0, 0, 0 });
            numericToHz.Minimum = new decimal(new int[] { 20, 0, 0, 0 });
            numericToHz.MinimumSize = new Size(36, 19);
            numericToHz.Name = "numericToHz";
            numericToHz.Size = new Size(90, 19);
            numericToHz.TabIndex = 49;
            numericToHz.TextAlign = HorizontalAlignment.Right;
            numericToHz.ThousandsSeparator = false;
            numericToHz.Value = new decimal(new int[] { 20000, 0, 0, 0 });
            numericToHz.ValueSuffix = "Hz";
            // 
            // numericFromHz
            // 
            numericFromHz.BackColor = UiPalette.ControlSurface;
            numericFromHz.DecimalPlaces = 0;
            numericFromHz.Font = new Font("Segoe UI", 9F);
            numericFromHz.ForeColor = UiPalette.TextPrimary;
            numericFromHz.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            numericFromHz.Location = new Point(93, 81);
            numericFromHz.LogarithmicFrequencyStep = true;
            numericFromHz.Maximum = new decimal(new int[] { 20000, 0, 0, 0 });
            numericFromHz.Minimum = new decimal(new int[] { 20, 0, 0, 0 });
            numericFromHz.MinimumSize = new Size(36, 19);
            numericFromHz.Name = "numericFromHz";
            numericFromHz.Size = new Size(90, 19);
            numericFromHz.TabIndex = 50;
            numericFromHz.TextAlign = HorizontalAlignment.Right;
            numericFromHz.ThousandsSeparator = false;
            numericFromHz.Value = new decimal(new int[] { 20, 0, 0, 0 });
            numericFromHz.ValueSuffix = "Hz";
            // 
            // labelFromHz
            // 
            labelFromHz.AutoSize = true;
            labelFromHz.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelFromHz.ForeColor = UiPalette.TextDefault;
            labelFromHz.Location = new Point(6, 83);
            labelFromHz.Margin = new Padding(3);
            labelFromHz.Name = "labelFromHz";
            labelFromHz.Size = new Size(35, 15);
            labelFromHz.TabIndex = 51;
            labelFromHz.Text = "From";
            // 
            // labelToHz
            // 
            labelToHz.AutoSize = true;
            labelToHz.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelToHz.ForeColor = UiPalette.TextDefault;
            labelToHz.Location = new Point(6, 108);
            labelToHz.Margin = new Padding(3);
            labelToHz.Name = "labelToHz";
            labelToHz.Size = new Size(20, 15);
            labelToHz.TabIndex = 52;
            labelToHz.Text = "To";
            // 
            // numericGainMin
            // 
            numericGainMin.BackColor = UiPalette.ControlSurface;
            numericGainMin.DecimalPlaces = 0;
            numericGainMin.Font = new Font("Segoe UI", 9F);
            numericGainMin.ForeColor = UiPalette.TextPrimary;
            numericGainMin.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericGainMin.Location = new Point(93, 31);
            numericGainMin.Maximum = new decimal(new int[] { 0, 0, 0, 0 });
            numericGainMin.Minimum = new decimal(new int[] { 60, 0, 0, int.MinValue });
            numericGainMin.MinimumSize = new Size(36, 19);
            numericGainMin.Name = "numericGainMin";
            numericGainMin.Size = new Size(90, 19);
            numericGainMin.TabIndex = 44;
            numericGainMin.TextAlign = HorizontalAlignment.Right;
            numericGainMin.ThousandsSeparator = false;
            numericGainMin.Value = new decimal(new int[] { 15, 0, 0, int.MinValue });
            numericGainMin.ValueSuffix = "dB";
            // 
            // labelGainMin
            // 
            labelGainMin.AutoSize = true;
            labelGainMin.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelGainMin.ForeColor = UiPalette.TextDefault;
            labelGainMin.Location = new Point(6, 33);
            labelGainMin.Margin = new Padding(3);
            labelGainMin.Name = "labelGainMin";
            labelGainMin.Size = new Size(55, 15);
            labelGainMin.TabIndex = 58;
            labelGainMin.Text = "Min Gain";
            // 
            // numericGainMax
            // 
            numericGainMax.BackColor = UiPalette.ControlSurface;
            numericGainMax.DecimalPlaces = 0;
            numericGainMax.Font = new Font("Segoe UI", 9F);
            numericGainMax.ForeColor = UiPalette.TextPrimary;
            numericGainMax.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericGainMax.Location = new Point(93, 6);
            numericGainMax.Maximum = new decimal(new int[] { 24, 0, 0, 0 });
            numericGainMax.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericGainMax.MinimumSize = new Size(36, 19);
            numericGainMax.Name = "numericGainMax";
            numericGainMax.Size = new Size(90, 19);
            numericGainMax.TabIndex = 45;
            numericGainMax.TextAlign = HorizontalAlignment.Right;
            numericGainMax.ThousandsSeparator = false;
            numericGainMax.Value = new decimal(new int[] { 6, 0, 0, 0 });
            numericGainMax.ValueSuffix = "dB";
            // 
            // labelGainMax
            // 
            labelGainMax.AutoSize = true;
            labelGainMax.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelGainMax.ForeColor = UiPalette.TextDefault;
            labelGainMax.Location = new Point(6, 8);
            labelGainMax.Margin = new Padding(3);
            labelGainMax.Name = "labelGainMax";
            labelGainMax.Size = new Size(57, 15);
            labelGainMax.TabIndex = 59;
            labelGainMax.Text = "Max Gain";
            // 
            // numericQMax
            // 
            numericQMax.BackColor = UiPalette.ControlSurface;
            numericQMax.DecimalPlaces = 1;
            numericQMax.Font = new Font("Segoe UI", 9F);
            numericQMax.ForeColor = UiPalette.TextPrimary;
            numericQMax.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            numericQMax.Location = new Point(93, 56);
            numericQMax.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            numericQMax.Minimum = new decimal(new int[] { 5, 0, 0, 65536 });
            numericQMax.MinimumSize = new Size(36, 19);
            numericQMax.Name = "numericQMax";
            numericQMax.Size = new Size(90, 19);
            numericQMax.TabIndex = 68;
            numericQMax.TextAlign = HorizontalAlignment.Right;
            numericQMax.ThousandsSeparator = false;
            numericQMax.Value = new decimal(new int[] { 60, 0, 0, 65536 });
            // 
            // labelQMax
            // 
            labelQMax.AutoSize = true;
            labelQMax.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelQMax.ForeColor = UiPalette.TextDefault;
            labelQMax.Location = new Point(6, 58);
            labelQMax.Margin = new Padding(3);
            labelQMax.Name = "labelQMax";
            labelQMax.Size = new Size(41, 15);
            labelQMax.TabIndex = 69;
            labelQMax.Text = "Max Q";
            //
            // checkBoxBypass
            //
            checkBoxBypass.AutoSize = true;
            checkBoxBypass.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            checkBoxBypass.ForeColor = UiPalette.TextDefault;
            checkBoxBypass.Location = new Point(13, 220);
            checkBoxBypass.Name = "checkBoxBypass";
            checkBoxBypass.Size = new Size(62, 19);
            checkBoxBypass.TabIndex = 53;
            checkBoxBypass.Text = "Bypass";
            checkBoxBypass.UseVisualStyleBackColor = true;
            //
            // checkBoxEqPhase
            //
            checkBoxEqPhase.AutoSize = true;
            checkBoxEqPhase.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            checkBoxEqPhase.ForeColor = UiPalette.TextDefault;
            checkBoxEqPhase.Location = new Point(108, 220);
            checkBoxEqPhase.Name = "checkBoxEqPhase";
            checkBoxEqPhase.Size = new Size(75, 19);
            checkBoxEqPhase.TabIndex = 66;
            checkBoxEqPhase.Text = "Phase";
            checkBoxEqPhase.UseVisualStyleBackColor = true;
            // 
            // checkBoxEqCurve
            // 
            checkBoxEqCurve.AutoSize = true;
            checkBoxEqCurve.Checked = true;
            checkBoxEqCurve.CheckState = CheckState.Checked;
            checkBoxEqCurve.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            checkBoxEqCurve.ForeColor = UiPalette.TextDefault;
            checkBoxEqCurve.Location = new Point(13, 246);
            checkBoxEqCurve.Name = "checkBoxEqCurve";
            checkBoxEqCurve.Size = new Size(78, 19);
            checkBoxEqCurve.TabIndex = 67;
            checkBoxEqCurve.Text = "EQ curve";
            checkBoxEqCurve.UseVisualStyleBackColor = true;
            // 
            // comboBoxBoosts
            // 
            comboBoxBoosts.BackColor = UiPalette.ControlSurface;
            comboBoxBoosts.ForeColor = UiPalette.TextPrimary;
            comboBoxBoosts.Location = new Point(93, 156);
            comboBoxBoosts.MinimumSize = new Size(36, 19);
            comboBoxBoosts.Name = "comboBoxBoosts";
            comboBoxBoosts.Size = new Size(90, 19);
            comboBoxBoosts.TabIndex = 48;
            // 
            // labelBoosts
            // 
            labelBoosts.AutoSize = true;
            labelBoosts.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelBoosts.ForeColor = UiPalette.TextDefault;
            labelBoosts.Location = new Point(6, 158);
            labelBoosts.Margin = new Padding(3);
            labelBoosts.Name = "labelBoosts";
            labelBoosts.Size = new Size(43, 15);
            labelBoosts.TabIndex = 70;
            labelBoosts.Text = "Boosts";
            // 
            // checkBoxShelves
            // 
            checkBoxShelves.AutoSize = true;
            checkBoxShelves.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            checkBoxShelves.ForeColor = UiPalette.TextDefault;
            checkBoxShelves.Location = new Point(6, 182);
            checkBoxShelves.Name = "checkBoxShelves";
            checkBoxShelves.Size = new Size(66, 19);
            checkBoxShelves.TabIndex = 69;
            checkBoxShelves.Text = "Shelves";
            checkBoxShelves.UseVisualStyleBackColor = true;
            // 
            // panelAutoTune
            // 
            panelAutoTune.BackColor = UiPalette.PanelSurface;
            panelAutoTune.Controls.Add(labelGainMin);
            panelAutoTune.Controls.Add(numericGainMin);
            panelAutoTune.Controls.Add(labelGainMax);
            panelAutoTune.Controls.Add(labelQMax);
            panelAutoTune.Controls.Add(numericQMax);
            panelAutoTune.Controls.Add(labelFromHz);
            panelAutoTune.Controls.Add(numericFromHz);
            panelAutoTune.Controls.Add(numericGainMax);
            panelAutoTune.Controls.Add(labelToHz);
            panelAutoTune.Controls.Add(numericToHz);
            panelAutoTune.Controls.Add(labelBandsLimit);
            panelAutoTune.Controls.Add(comboBoxBandsLimit);
            panelAutoTune.Controls.Add(labelBoosts);
            panelAutoTune.Controls.Add(comboBoxBoosts);
            panelAutoTune.Controls.Add(checkBoxShelves);
            panelAutoTune.Controls.Add(buttonAutoTune);
            panelAutoTune.Location = new Point(6, 525);
            panelAutoTune.Name = "panelAutoTune";
            panelAutoTune.Size = new Size(186, 236);
            panelAutoTune.TabIndex = 54;
            // 
            // buttonOverlaySettings
            // 
            buttonOverlaySettings.FlatStyle = FlatStyle.Popup;
            buttonOverlaySettings.ForeColor = UiPalette.TextPrimary;
            buttonOverlaySettings.Location = new Point(6, 40);
            buttonOverlaySettings.Name = "buttonOverlaySettings";
            buttonOverlaySettings.Size = new Size(182, 24);
            buttonOverlaySettings.TabIndex = 6;
            buttonOverlaySettings.Text = "Target Curve…";
            buttonOverlaySettings.UseVisualStyleBackColor = true;
            // 
            // comboBoxCalibration
            // 
            comboBoxCalibration.BackColor = UiPalette.ControlSurface;
            comboBoxCalibration.ForeColor = UiPalette.TextPrimary;
            comboBoxCalibration.Location = new Point(108, 70);
            comboBoxCalibration.MinimumSize = new Size(36, 19);
            comboBoxCalibration.Name = "comboBoxCalibration";
            comboBoxCalibration.Size = new Size(80, 19);
            comboBoxCalibration.TabIndex = 12;
            // 
            // labelCalibration
            // 
            labelCalibration.AutoSize = true;
            labelCalibration.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelCalibration.ForeColor = UiPalette.TextDefault;
            labelCalibration.Location = new Point(9, 72);
            labelCalibration.Margin = new Padding(3);
            labelCalibration.Name = "labelCalibration";
            labelCalibration.Size = new Size(64, 15);
            labelCalibration.TabIndex = 13;
            labelCalibration.Text = "Calibration";
            // 
            // comboBoxSmooth
            // 
            comboBoxSmooth.BackColor = UiPalette.ControlSurface;
            comboBoxSmooth.ForeColor = UiPalette.TextPrimary;
            comboBoxSmooth.Location = new Point(108, 120);
            comboBoxSmooth.MinimumSize = new Size(36, 19);
            comboBoxSmooth.Name = "comboBoxSmooth";
            comboBoxSmooth.Size = new Size(80, 19);
            comboBoxSmooth.TabIndex = 55;
            // 
            // labelSmooth
            // 
            labelSmooth.AutoSize = true;
            labelSmooth.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelSmooth.ForeColor = UiPalette.TextDefault;
            labelSmooth.Location = new Point(9, 122);
            labelSmooth.Margin = new Padding(3);
            labelSmooth.Name = "labelSmooth";
            labelSmooth.Size = new Size(50, 15);
            labelSmooth.TabIndex = 56;
            labelSmooth.Text = "Smooth";
            //
            // comboBoxSampleRate
            //
            comboBoxSampleRate.BackColor = UiPalette.ControlSurface;
            comboBoxSampleRate.ForeColor = UiPalette.TextPrimary;
            comboBoxSampleRate.Location = new Point(108, 145);
            comboBoxSampleRate.MinimumSize = new Size(36, 19);
            comboBoxSampleRate.Name = "comboBoxSampleRate";
            comboBoxSampleRate.Size = new Size(80, 19);
            comboBoxSampleRate.TabIndex = 59;
            //
            // labelSampleRate
            //
            labelSampleRate.AutoSize = true;
            labelSampleRate.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelSampleRate.ForeColor = UiPalette.TextDefault;
            labelSampleRate.Location = new Point(9, 147);
            labelSampleRate.Margin = new Padding(3);
            labelSampleRate.Name = "labelSampleRate";
            labelSampleRate.Size = new Size(66, 15);
            labelSampleRate.TabIndex = 60;
            labelSampleRate.Text = "Rate";
            //
            // buttonPhaseGate
            //
            buttonPhaseGate.Enabled = false;
            buttonPhaseGate.FlatStyle = FlatStyle.Popup;
            buttonPhaseGate.ForeColor = UiPalette.TextPrimary;
            buttonPhaseGate.Location = new Point(2, 271);
            buttonPhaseGate.Name = "buttonPhaseGate";
            buttonPhaseGate.Size = new Size(186, 24);
            buttonPhaseGate.TabIndex = 68;
            buttonPhaseGate.Text = "Phase gate...";
            buttonPhaseGate.UseVisualStyleBackColor = true;
            //
            // buttonImport
            //
            buttonImport.FlatStyle = FlatStyle.Popup;
            buttonImport.ForeColor = UiPalette.TextPrimary;
            buttonImport.Location = new Point(2, 300);
            buttonImport.Name = "buttonImport";
            buttonImport.Size = new Size(87, 24);
            buttonImport.TabIndex = 57;
            buttonImport.Text = "Import";
            buttonImport.UseVisualStyleBackColor = true;
            // 
            // buttonExport
            // 
            buttonExport.FlatStyle = FlatStyle.Popup;
            buttonExport.ForeColor = UiPalette.TextPrimary;
            buttonExport.Location = new Point(101, 300);
            buttonExport.Name = "buttonExport";
            buttonExport.Size = new Size(87, 24);
            buttonExport.TabIndex = 58;
            buttonExport.Text = "Export";
            buttonExport.UseVisualStyleBackColor = true;
            //
            // buttonResetBands
            //
            buttonResetBands.FlatStyle = FlatStyle.Popup;
            buttonResetBands.ForeColor = UiPalette.TextPrimary;
            buttonResetBands.Location = new Point(2, 329);
            buttonResetBands.Name = "buttonResetBands";
            buttonResetBands.Size = new Size(186, 24);
            buttonResetBands.TabIndex = 59;
            buttonResetBands.Text = "Reset filters";
            buttonResetBands.UseVisualStyleBackColor = true;
            //
            // buttonUndo
            //
            buttonUndo.Enabled = false;
            buttonUndo.FlatStyle = FlatStyle.Popup;
            buttonUndo.ForeColor = UiPalette.TextPrimary;
            buttonUndo.Location = new Point(2, 389);
            buttonUndo.Name = "buttonUndo";
            buttonUndo.Size = new Size(87, 24);
            buttonUndo.TabIndex = 62;
            buttonUndo.Text = "Undo";
            buttonUndo.UseVisualStyleBackColor = true;
            //
            // buttonRedo
            //
            buttonRedo.Enabled = false;
            buttonRedo.FlatStyle = FlatStyle.Popup;
            buttonRedo.ForeColor = UiPalette.TextPrimary;
            buttonRedo.Location = new Point(101, 389);
            buttonRedo.Name = "buttonRedo";
            buttonRedo.Size = new Size(87, 24);
            buttonRedo.TabIndex = 63;
            buttonRedo.Text = "Redo";
            buttonRedo.UseVisualStyleBackColor = true;
            //
            // buttonReturnToDsp
            //
            buttonReturnToDsp.BackColor = UiPalette.ButtonBackground;
            buttonReturnToDsp.FlatStyle = FlatStyle.Popup;
            buttonReturnToDsp.ForeColor = UiPalette.TextPrimary;
            buttonReturnToDsp.Location = new Point(2, 425);
            buttonReturnToDsp.Name = "buttonReturnToDsp";
            buttonReturnToDsp.Size = new Size(186, 26);
            buttonReturnToDsp.TabIndex = 64;
            buttonReturnToDsp.Text = "Return PEQ to Virtual DSP";
            buttonReturnToDsp.UseVisualStyleBackColor = false;
            buttonReturnToDsp.Visible = false;
            //
            // buttonBackToDsp
            //
            buttonBackToDsp.FlatStyle = FlatStyle.Popup;
            buttonBackToDsp.ForeColor = UiPalette.TextPrimary;
            buttonBackToDsp.Location = new Point(2, 455);
            buttonBackToDsp.Name = "buttonBackToDsp";
            buttonBackToDsp.Size = new Size(186, 24);
            buttonBackToDsp.TabIndex = 65;
            buttonBackToDsp.Text = "Back without applying";
            buttonBackToDsp.UseVisualStyleBackColor = true;
            buttonBackToDsp.Visible = false;
            //
            // comboBoxQConvention
            //
            comboBoxQConvention.BackColor = UiPalette.ControlSurface;
            comboBoxQConvention.ForeColor = UiPalette.TextPrimary;
            comboBoxQConvention.Location = new Point(108, 361);
            comboBoxQConvention.MinimumSize = new Size(36, 19);
            comboBoxQConvention.Name = "comboBoxQConvention";
            comboBoxQConvention.Size = new Size(80, 19);
            comboBoxQConvention.TabIndex = 60;
            //
            // labelQConvention
            //
            labelQConvention.AutoSize = true;
            labelQConvention.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelQConvention.ForeColor = UiPalette.TextDefault;
            labelQConvention.Location = new Point(9, 363);
            labelQConvention.Margin = new Padding(3);
            labelQConvention.Name = "labelQConvention";
            labelQConvention.Size = new Size(66, 15);
            labelQConvention.TabIndex = 61;
            labelQConvention.Text = "DSP Q";
            //
            // EqWizardPanel
            //
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScroll = true;
            BackColor = UiPalette.ShellSurface;
            BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(labelQConvention);
            Controls.Add(comboBoxQConvention);
            Controls.Add(buttonReturnToDsp);
            Controls.Add(buttonBackToDsp);
            Controls.Add(buttonUndo);
            Controls.Add(buttonRedo);
            Controls.Add(buttonResetBands);
            Controls.Add(buttonExport);
            Controls.Add(buttonImport);
            Controls.Add(labelCalibration);
            Controls.Add(comboBoxCalibration);
            Controls.Add(labelSmooth);
            Controls.Add(comboBoxSmooth);
            Controls.Add(labelSampleRate);
            Controls.Add(comboBoxSampleRate);
            Controls.Add(buttonOverlaySettings);
            Controls.Add(panelAutoTune);
            Controls.Add(checkBoxBypass);
            Controls.Add(checkBoxEqPhase);
            Controls.Add(checkBoxEqCurve);
            Controls.Add(buttonPhaseGate);
            Controls.Add(labelGain);
            Controls.Add(NumericGain);
            Controls.Add(labelTargetOffset);
            Controls.Add(NumericTargetOffset);
            Controls.Add(darkComboBoxBands);
            Controls.Add(buttonSource);
            Controls.Add(labelBands);
            Controls.Add(panelPEQ);
            Controls.Add(plotWizard);
            Font = new Font("Segoe UI", 9F);
            ForeColor = UiPalette.TextPrimary;
            Name = "EqWizardPanel";
            Padding = new Padding(6);
            Size = new Size(1246, 770);
            (NumericTargetOffset).EndInit();
            (NumericGain).EndInit();
            (numericToHz).EndInit();
            (numericFromHz).EndInit();
            (numericGainMin).EndInit();
            (numericGainMax).EndInit();
            (numericQMax).EndInit();
            panelAutoTune.ResumeLayout(false);
            panelAutoTune.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private OxyPlot.WindowsForms.PlotView plotWizard;
        private Panel panelPEQ;
        private Label labelBands;
        private ReleaseClickButton buttonSource;
        private ThemedComboBox darkComboBoxBands;
        private ThemedNumericUpDown NumericTargetOffset;
        private Label labelTargetOffset;
        private ThemedNumericUpDown NumericGain;
        private Label labelGain;
        private ReleaseClickButton buttonAutoTune;
        private ThemedComboBox comboBoxBandsLimit;
        private Label labelBandsLimit;
        private ThemedNumericUpDown numericToHz;
        private ThemedNumericUpDown numericFromHz;
        private Label labelFromHz;
        private Label labelToHz;
        private ThemedNumericUpDown numericGainMin;
        private Label labelGainMin;
        private ThemedNumericUpDown numericGainMax;
        private Label labelGainMax;
        private ThemedNumericUpDown numericQMax;
        private Label labelQMax;
        private ReleaseClickCheckBox checkBoxBypass;
        private ReleaseClickCheckBox checkBoxEqPhase;
        private ReleaseClickCheckBox checkBoxEqCurve;
        private ThemedComboBox comboBoxBoosts;
        private Label labelBoosts;
        private ReleaseClickCheckBox checkBoxShelves;
        private RoundedPanel panelAutoTune;
        private ReleaseClickButton buttonOverlaySettings;
        private ThemedComboBox comboBoxCalibration;
        private Label labelCalibration;
        private ThemedComboBox comboBoxSampleRate;
        private Label labelSampleRate;
        private ThemedComboBox comboBoxSmooth;
        private Label labelSmooth;
        private ReleaseClickButton buttonPhaseGate;
        private ReleaseClickButton buttonImport;
        private ReleaseClickButton buttonExport;
        private ReleaseClickButton buttonResetBands;
        private ReleaseClickButton buttonUndo;
        private ReleaseClickButton buttonRedo;
        private ThemedComboBox comboBoxQConvention;
        private Label labelQConvention;
        private ReleaseClickButton buttonReturnToDsp;
        private ReleaseClickButton buttonBackToDsp;
    }
}
