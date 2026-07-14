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
            darkComboBoxSource = new DarkComboBox();
            labelSource = new Label();
            darkComboBoxBands = new DarkComboBox();
            NumericTargetOffset = new DarkNumericUpDown();
            labelTargetOffset = new Label();
            NumericGain = new DarkNumericUpDown();
            labelGain = new Label();
            buttonAutoTune = new Button();
            comboBoxBandsLimit = new DarkComboBox();
            labelBandsLimit = new Label();
            numericToHz = new DarkNumericUpDown();
            numericFromHz = new DarkNumericUpDown();
            labelFromHz = new Label();
            labelToHz = new Label();
            numericGainMin = new DarkNumericUpDown();
            labelGainMin = new Label();
            numericGainMax = new DarkNumericUpDown();
            labelGainMax = new Label();
            checkBoxBypass = new CheckBox();
            panelAutoTune = new Panel();
            buttonOverlaySettings = new Button();
            comboBoxSmooth = new DarkComboBox();
            labelSmooth = new Label();
            buttonImport = new Button();
            buttonExport = new Button();
            (NumericTargetOffset).BeginInit();
            (NumericGain).BeginInit();
            (numericToHz).BeginInit();
            (numericFromHz).BeginInit();
            (numericGainMin).BeginInit();
            (numericGainMax).BeginInit();
            panelAutoTune.SuspendLayout();
            SuspendLayout();
            //
            // plotWizard
            //
            plotWizard.BackColor = Color.FromArgb(50, 55, 100);
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
            panelPEQ.BackColor = Color.FromArgb(20, 22, 30);
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
            labelBands.ForeColor = Color.FromArgb(210, 214, 222);
            labelBands.Location = new Point(8, 91);
            labelBands.Margin = new Padding(3);
            labelBands.Name = "labelBands";
            labelBands.Size = new Size(56, 15);
            labelBands.TabIndex = 4;
            labelBands.Text = "EQ Filters";
            //
            // darkComboBoxSource
            //
            darkComboBoxSource.BackColor = Color.FromArgb(55, 60, 72);
            darkComboBoxSource.ForeColor = Color.White;
            darkComboBoxSource.Location = new Point(108, 14);
            darkComboBoxSource.MinimumSize = new Size(36, 19);
            darkComboBoxSource.Name = "darkComboBoxSource";
            darkComboBoxSource.Size = new Size(80, 19);
            darkComboBoxSource.TabIndex = 5;
            //
            // labelSource
            //
            labelSource.AutoSize = true;
            labelSource.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelSource.ForeColor = Color.FromArgb(210, 214, 222);
            labelSource.Location = new Point(9, 16);
            labelSource.Margin = new Padding(3);
            labelSource.Name = "labelSource";
            labelSource.Size = new Size(59, 15);
            labelSource.TabIndex = 6;
            labelSource.Text = "Reference";
            //
            // darkComboBoxBands
            //
            darkComboBoxBands.BackColor = Color.FromArgb(55, 60, 72);
            darkComboBoxBands.ForeColor = Color.White;
            darkComboBoxBands.Location = new Point(108, 89);
            darkComboBoxBands.MinimumSize = new Size(36, 19);
            darkComboBoxBands.Name = "darkComboBoxBands";
            darkComboBoxBands.Size = new Size(80, 19);
            darkComboBoxBands.TabIndex = 7;
            //
            // NumericTargetOffset
            //
            NumericTargetOffset.BackColor = Color.FromArgb(55, 60, 72);
            NumericTargetOffset.DecimalPlaces = 0;
            NumericTargetOffset.ForeColor = Color.White;
            NumericTargetOffset.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            NumericTargetOffset.Location = new Point(108, 39);
            NumericTargetOffset.Maximum = new decimal(new int[] { 180, 0, 0, 0 });
            NumericTargetOffset.Minimum = new decimal(new int[] { 180, 0, 0, int.MinValue });
            NumericTargetOffset.MinimumSize = new Size(36, 19);
            NumericTargetOffset.Name = "NumericTargetOffset";
            NumericTargetOffset.Size = new Size(80, 19);
            NumericTargetOffset.TabIndex = 8;
            NumericTargetOffset.TextAlign = HorizontalAlignment.Right;
            NumericTargetOffset.ThousandsSeparator = false;
            NumericTargetOffset.Value = new decimal(new int[] { 0, 0, 0, 0 });
            //
            // labelTargetOffset
            //
            labelTargetOffset.AutoSize = true;
            labelTargetOffset.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelTargetOffset.ForeColor = Color.FromArgb(210, 214, 222);
            labelTargetOffset.Location = new Point(9, 41);
            labelTargetOffset.Margin = new Padding(3);
            labelTargetOffset.Name = "labelTargetOffset";
            labelTargetOffset.Size = new Size(70, 15);
            labelTargetOffset.TabIndex = 9;
            labelTargetOffset.Text = "Target Level";
            //
            // NumericGain
            //
            NumericGain.BackColor = Color.FromArgb(55, 60, 72);
            NumericGain.DecimalPlaces = 1;
            NumericGain.ForeColor = Color.White;
            NumericGain.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            NumericGain.Location = new Point(108, 114);
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
            labelGain.ForeColor = Color.FromArgb(210, 214, 222);
            labelGain.Location = new Point(9, 116);
            labelGain.Margin = new Padding(3);
            labelGain.Name = "labelGain";
            labelGain.Size = new Size(48, 15);
            labelGain.TabIndex = 11;
            labelGain.Text = "Preamp";
            //
            // buttonAutoTune
            //
            buttonAutoTune.BackColor = Color.FromArgb(46, 51, 67);
            buttonAutoTune.FlatStyle = FlatStyle.Popup;
            buttonAutoTune.ForeColor = Color.White;
            buttonAutoTune.Location = new Point(6, 130);
            buttonAutoTune.Name = "buttonAutoTune";
            buttonAutoTune.Size = new Size(173, 24);
            buttonAutoTune.TabIndex = 46;
            buttonAutoTune.Text = "Auto Tune";
            buttonAutoTune.UseVisualStyleBackColor = false;
            //
            // comboBoxBandsLimit
            //
            comboBoxBandsLimit.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxBandsLimit.ForeColor = Color.White;
            comboBoxBandsLimit.Location = new Point(93, 106);
            comboBoxBandsLimit.MinimumSize = new Size(36, 19);
            comboBoxBandsLimit.Name = "comboBoxBandsLimit";
            comboBoxBandsLimit.Size = new Size(90, 19);
            comboBoxBandsLimit.TabIndex = 47;
            //
            // labelBandsLimit
            //
            labelBandsLimit.AutoSize = true;
            labelBandsLimit.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelBandsLimit.ForeColor = Color.FromArgb(210, 214, 222);
            labelBandsLimit.Location = new Point(6, 108);
            labelBandsLimit.Margin = new Padding(3);
            labelBandsLimit.Name = "labelBandsLimit";
            labelBandsLimit.Size = new Size(82, 15);
            labelBandsLimit.TabIndex = 48;
            labelBandsLimit.Text = "Max EQ Filters";
            //
            // numericToHz
            //
            numericToHz.BackColor = Color.FromArgb(55, 60, 72);
            numericToHz.DecimalPlaces = 0;
            numericToHz.Font = new Font("Segoe UI", 9F);
            numericToHz.ForeColor = Color.White;
            numericToHz.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            numericToHz.Location = new Point(93, 81);
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
            numericFromHz.BackColor = Color.FromArgb(55, 60, 72);
            numericFromHz.DecimalPlaces = 0;
            numericFromHz.Font = new Font("Segoe UI", 9F);
            numericFromHz.ForeColor = Color.White;
            numericFromHz.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            numericFromHz.Location = new Point(93, 56);
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
            labelFromHz.ForeColor = Color.FromArgb(210, 214, 222);
            labelFromHz.Location = new Point(6, 58);
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
            labelToHz.ForeColor = Color.FromArgb(210, 214, 222);
            labelToHz.Location = new Point(6, 83);
            labelToHz.Margin = new Padding(3);
            labelToHz.Name = "labelToHz";
            labelToHz.Size = new Size(20, 15);
            labelToHz.TabIndex = 52;
            labelToHz.Text = "To";
            //
            // numericGainMin
            //
            numericGainMin.BackColor = Color.FromArgb(55, 60, 72);
            numericGainMin.DecimalPlaces = 0;
            numericGainMin.Font = new Font("Segoe UI", 9F);
            numericGainMin.ForeColor = Color.White;
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
            labelGainMin.ForeColor = Color.FromArgb(210, 214, 222);
            labelGainMin.Location = new Point(6, 33);
            labelGainMin.Margin = new Padding(3);
            labelGainMin.Name = "labelGainMin";
            labelGainMin.Size = new Size(55, 15);
            labelGainMin.TabIndex = 58;
            labelGainMin.Text = "Min Gain";
            //
            // numericGainMax
            //
            numericGainMax.BackColor = Color.FromArgb(55, 60, 72);
            numericGainMax.DecimalPlaces = 0;
            numericGainMax.Font = new Font("Segoe UI", 9F);
            numericGainMax.ForeColor = Color.White;
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
            labelGainMax.ForeColor = Color.FromArgb(210, 214, 222);
            labelGainMax.Location = new Point(6, 8);
            labelGainMax.Margin = new Padding(3);
            labelGainMax.Name = "labelGainMax";
            labelGainMax.Size = new Size(57, 15);
            labelGainMax.TabIndex = 59;
            labelGainMax.Text = "Max Gain";
            //
            // checkBoxBypass
            //
            checkBoxBypass.AutoSize = true;
            checkBoxBypass.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            checkBoxBypass.ForeColor = Color.FromArgb(210, 214, 222);
            checkBoxBypass.Location = new Point(13, 135);
            checkBoxBypass.Name = "checkBoxBypass";
            checkBoxBypass.Size = new Size(62, 19);
            checkBoxBypass.TabIndex = 53;
            checkBoxBypass.Text = "Bypass";
            checkBoxBypass.UseVisualStyleBackColor = true;
            //
            // panelAutoTune
            //
            panelAutoTune.BackColor = Color.FromArgb(46, 51, 62);
            panelAutoTune.BorderStyle = BorderStyle.FixedSingle;
            panelAutoTune.Controls.Add(labelGainMin);
            panelAutoTune.Controls.Add(numericGainMin);
            panelAutoTune.Controls.Add(labelGainMax);
            panelAutoTune.Controls.Add(labelFromHz);
            panelAutoTune.Controls.Add(numericFromHz);
            panelAutoTune.Controls.Add(numericGainMax);
            panelAutoTune.Controls.Add(labelToHz);
            panelAutoTune.Controls.Add(numericToHz);
            panelAutoTune.Controls.Add(labelBandsLimit);
            panelAutoTune.Controls.Add(comboBoxBandsLimit);
            panelAutoTune.Controls.Add(buttonAutoTune);
            panelAutoTune.Location = new Point(5, 601);
            panelAutoTune.Name = "panelAutoTune";
            panelAutoTune.Size = new Size(186, 160);
            panelAutoTune.TabIndex = 54;
            //
            // buttonOverlaySettings
            //
            buttonOverlaySettings.FlatStyle = FlatStyle.Popup;
            buttonOverlaySettings.ForeColor = Color.White;
            buttonOverlaySettings.Location = new Point(83, 14);
            buttonOverlaySettings.Name = "buttonOverlaySettings";
            buttonOverlaySettings.Size = new Size(19, 19);
            buttonOverlaySettings.TabIndex = 53;
            buttonOverlaySettings.Text = "S";
            buttonOverlaySettings.UseCompatibleTextRendering = true;
            buttonOverlaySettings.UseVisualStyleBackColor = true;
            //
            // comboBoxSmooth
            //
            comboBoxSmooth.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxSmooth.ForeColor = Color.White;
            comboBoxSmooth.Location = new Point(108, 64);
            comboBoxSmooth.MinimumSize = new Size(36, 19);
            comboBoxSmooth.Name = "comboBoxSmooth";
            comboBoxSmooth.Size = new Size(80, 19);
            comboBoxSmooth.TabIndex = 55;
            //
            // labelSmooth
            //
            labelSmooth.AutoSize = true;
            labelSmooth.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelSmooth.ForeColor = Color.FromArgb(210, 214, 222);
            labelSmooth.Location = new Point(9, 66);
            labelSmooth.Margin = new Padding(3);
            labelSmooth.Name = "labelSmooth";
            labelSmooth.Size = new Size(50, 15);
            labelSmooth.TabIndex = 56;
            labelSmooth.Text = "Smooth";
            //
            // buttonImport
            //
            buttonImport.FlatStyle = FlatStyle.Popup;
            buttonImport.ForeColor = Color.White;
            buttonImport.Location = new Point(2, 159);
            buttonImport.Name = "buttonImport";
            buttonImport.Size = new Size(87, 24);
            buttonImport.TabIndex = 53;
            buttonImport.Text = "Import";
            buttonImport.UseVisualStyleBackColor = true;
            //
            // buttonExport
            //
            buttonExport.FlatStyle = FlatStyle.Popup;
            buttonExport.ForeColor = Color.White;
            buttonExport.Location = new Point(101, 159);
            buttonExport.Name = "buttonExport";
            buttonExport.Size = new Size(87, 24);
            buttonExport.TabIndex = 57;
            buttonExport.Text = "Export";
            buttonExport.UseVisualStyleBackColor = true;
            //
            // EqWizardPanel
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            AutoScroll = true;
            BackColor = Color.FromArgb(40, 44, 54);
            BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(buttonExport);
            Controls.Add(buttonImport);
            Controls.Add(labelSmooth);
            Controls.Add(comboBoxSmooth);
            Controls.Add(buttonOverlaySettings);
            Controls.Add(panelAutoTune);
            Controls.Add(checkBoxBypass);
            Controls.Add(labelGain);
            Controls.Add(NumericGain);
            Controls.Add(labelTargetOffset);
            Controls.Add(NumericTargetOffset);
            Controls.Add(darkComboBoxBands);
            Controls.Add(labelSource);
            Controls.Add(darkComboBoxSource);
            Controls.Add(labelBands);
            Controls.Add(panelPEQ);
            Controls.Add(plotWizard);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            Name = "EqWizardPanel";
            Padding = new Padding(6);
            Size = new Size(1246, 770);
            (NumericTargetOffset).EndInit();
            (NumericGain).EndInit();
            (numericToHz).EndInit();
            (numericFromHz).EndInit();
            (numericGainMin).EndInit();
            (numericGainMax).EndInit();
            panelAutoTune.ResumeLayout(false);
            panelAutoTune.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private OxyPlot.WindowsForms.PlotView plotWizard;
        private Panel panelPEQ;
        private Label labelBands;
        private DarkComboBox darkComboBoxSource;
        private Label labelSource;
        private DarkComboBox darkComboBoxBands;
        private DarkNumericUpDown NumericTargetOffset;
        private Label labelTargetOffset;
        private DarkNumericUpDown NumericGain;
        private Label labelGain;
        private Button buttonAutoTune;
        private DarkComboBox comboBoxBandsLimit;
        private Label labelBandsLimit;
        private DarkNumericUpDown numericToHz;
        private DarkNumericUpDown numericFromHz;
        private Label labelFromHz;
        private Label labelToHz;
        private DarkNumericUpDown numericGainMin;
        private Label labelGainMin;
        private DarkNumericUpDown numericGainMax;
        private Label labelGainMax;
        private CheckBox checkBoxBypass;
        private Panel panelAutoTune;
        private Button buttonOverlaySettings;
        private DarkComboBox comboBoxSmooth;
        private Label labelSmooth;
        private Button buttonImport;
        private Button buttonExport;
    }
}
