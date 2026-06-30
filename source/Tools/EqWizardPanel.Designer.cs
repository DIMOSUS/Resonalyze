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
            panelAutoTune.SuspendLayout();
            SuspendLayout();
            // 
            // plotWizard
            // 
            plotWizard.BackColor = Color.FromArgb(50, 55, 100);
            plotWizard.Location = new Point(12, 12);
            plotWizard.Margin = new Padding(6, 6, 6, 6);
            plotWizard.Name = "plotWizard";
            plotWizard.PanCursor = Cursors.Hand;
            plotWizard.Size = new Size(1155, 391);
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
            panelPEQ.Location = new Point(197, 412);
            panelPEQ.Name = "panelPEQ";
            panelPEQ.Size = new Size(970, 285);
            panelPEQ.TabIndex = 2;
            // 
            // labelBands
            // 
            labelBands.AutoSize = true;
            labelBands.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelBands.ForeColor = Color.FromArgb(210, 214, 222);
            labelBands.Location = new Point(11, 489);
            labelBands.Margin = new Padding(3, 3, 3, 3);
            labelBands.Name = "labelBands";
            labelBands.Size = new Size(56, 15);
            labelBands.TabIndex = 4;
            labelBands.Text = "EQ Filters";
            // 
            // darkComboBoxSource
            // 
            darkComboBoxSource.BackColor = Color.FromArgb(55, 60, 72);
            darkComboBoxSource.ForeColor = Color.White;
            darkComboBoxSource.Location = new Point(111, 412);
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
            labelSource.Location = new Point(12, 414);
            labelSource.Margin = new Padding(3, 3, 3, 3);
            labelSource.Name = "labelSource";
            labelSource.Size = new Size(59, 15);
            labelSource.TabIndex = 6;
            labelSource.Text = "Reference";
            // 
            // darkComboBoxBands
            // 
            darkComboBoxBands.BackColor = Color.FromArgb(55, 60, 72);
            darkComboBoxBands.ForeColor = Color.White;
            darkComboBoxBands.Location = new Point(111, 487);
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
            NumericTargetOffset.Location = new Point(111, 437);
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
            labelTargetOffset.Location = new Point(12, 439);
            labelTargetOffset.Margin = new Padding(3, 3, 3, 3);
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
            NumericGain.Location = new Point(111, 512);
            NumericGain.Maximum = new decimal(new int[] { 80, 0, 0, 0 });
            NumericGain.Minimum = new decimal(new int[] { 80, 0, 0, int.MinValue });
            NumericGain.MinimumSize = new Size(36, 19);
            NumericGain.Name = "NumericGain";
            NumericGain.Size = new Size(80, 19);
            NumericGain.TabIndex = 10;
            NumericGain.TextAlign = HorizontalAlignment.Right;
            NumericGain.ThousandsSeparator = false;
            NumericGain.Value = new decimal(new int[] { 0, 0, 0, 0 });
            // 
            // labelGain
            // 
            labelGain.AutoSize = true;
            labelGain.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelGain.ForeColor = Color.FromArgb(210, 214, 222);
            labelGain.Location = new Point(12, 514);
            labelGain.Margin = new Padding(3, 3, 3, 3);
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
            buttonAutoTune.Location = new Point(6, 80);
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
            comboBoxBandsLimit.Location = new Point(105, 56);
            comboBoxBandsLimit.MinimumSize = new Size(36, 19);
            comboBoxBandsLimit.Name = "comboBoxBandsLimit";
            comboBoxBandsLimit.Size = new Size(74, 19);
            comboBoxBandsLimit.TabIndex = 47;
            // 
            // labelBandsLimit
            // 
            labelBandsLimit.AutoSize = true;
            labelBandsLimit.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelBandsLimit.ForeColor = Color.FromArgb(210, 214, 222);
            labelBandsLimit.Location = new Point(6, 58);
            labelBandsLimit.Margin = new Padding(3, 3, 3, 3);
            labelBandsLimit.Name = "labelBandsLimit";
            labelBandsLimit.Size = new Size(82, 15);
            labelBandsLimit.TabIndex = 48;
            labelBandsLimit.Text = "Max EQ Filters";
            // 
            // numericToHz
            // 
            numericToHz.BackColor = Color.FromArgb(55, 60, 72);
            numericToHz.DecimalPlaces = 0;
            numericToHz.ForeColor = Color.White;
            numericToHz.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            numericToHz.Location = new Point(105, 31);
            numericToHz.Maximum = new decimal(new int[] { 20000, 0, 0, 0 });
            numericToHz.Minimum = new decimal(new int[] { 20, 0, 0, 0 });
            numericToHz.MinimumSize = new Size(36, 19);
            numericToHz.Name = "numericToHz";
            numericToHz.Size = new Size(74, 19);
            numericToHz.TabIndex = 49;
            numericToHz.TextAlign = HorizontalAlignment.Right;
            numericToHz.ThousandsSeparator = false;
            numericToHz.Value = new decimal(new int[] { 20000, 0, 0, 0 });
            // 
            // numericFromHz
            // 
            numericFromHz.BackColor = Color.FromArgb(55, 60, 72);
            numericFromHz.DecimalPlaces = 0;
            numericFromHz.ForeColor = Color.White;
            numericFromHz.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            numericFromHz.Location = new Point(105, 6);
            numericFromHz.Maximum = new decimal(new int[] { 20000, 0, 0, 0 });
            numericFromHz.Minimum = new decimal(new int[] { 20, 0, 0, 0 });
            numericFromHz.MinimumSize = new Size(36, 19);
            numericFromHz.Name = "numericFromHz";
            numericFromHz.Size = new Size(74, 19);
            numericFromHz.TabIndex = 50;
            numericFromHz.TextAlign = HorizontalAlignment.Right;
            numericFromHz.ThousandsSeparator = false;
            numericFromHz.Value = new decimal(new int[] { 20, 0, 0, 0 });
            // 
            // labelFromHz
            // 
            labelFromHz.AutoSize = true;
            labelFromHz.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelFromHz.ForeColor = Color.FromArgb(210, 214, 222);
            labelFromHz.Location = new Point(6, 8);
            labelFromHz.Margin = new Padding(3, 3, 3, 3);
            labelFromHz.Name = "labelFromHz";
            labelFromHz.Size = new Size(53, 15);
            labelFromHz.TabIndex = 51;
            labelFromHz.Text = "From Hz";
            // 
            // labelToHz
            // 
            labelToHz.AutoSize = true;
            labelToHz.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelToHz.ForeColor = Color.FromArgb(210, 214, 222);
            labelToHz.Location = new Point(6, 33);
            labelToHz.Margin = new Padding(3, 3, 3, 3);
            labelToHz.Name = "labelToHz";
            labelToHz.Size = new Size(38, 15);
            labelToHz.TabIndex = 52;
            labelToHz.Text = "To Hz";
            // 
            // checkBoxBypass
            // 
            checkBoxBypass.AutoSize = true;
            checkBoxBypass.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            checkBoxBypass.ForeColor = Color.FromArgb(210, 214, 222);
            checkBoxBypass.Location = new Point(16, 533);
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
            panelAutoTune.Controls.Add(labelFromHz);
            panelAutoTune.Controls.Add(numericFromHz);
            panelAutoTune.Controls.Add(labelToHz);
            panelAutoTune.Controls.Add(numericToHz);
            panelAutoTune.Controls.Add(labelBandsLimit);
            panelAutoTune.Controls.Add(comboBoxBandsLimit);
            panelAutoTune.Controls.Add(buttonAutoTune);
            panelAutoTune.Location = new Point(5, 587);
            panelAutoTune.Name = "panelAutoTune";
            panelAutoTune.Size = new Size(186, 110);
            panelAutoTune.TabIndex = 54;
            // 
            // buttonOverlaySettings
            // 
            buttonOverlaySettings.FlatStyle = FlatStyle.Popup;
            buttonOverlaySettings.ForeColor = Color.White;
            buttonOverlaySettings.Location = new Point(86, 412);
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
            comboBoxSmooth.Location = new Point(111, 462);
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
            labelSmooth.Location = new Point(12, 464);
            labelSmooth.Margin = new Padding(3, 3, 3, 3);
            labelSmooth.Name = "labelSmooth";
            labelSmooth.Size = new Size(50, 15);
            labelSmooth.TabIndex = 56;
            labelSmooth.Text = "Smooth";
            // 
            // buttonImport
            // 
            buttonImport.FlatStyle = FlatStyle.Popup;
            buttonImport.ForeColor = Color.White;
            buttonImport.Location = new Point(5, 557);
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
            buttonExport.Location = new Point(104, 557);
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
            Padding = new Padding(6, 6, 6, 6);
            Size = new Size(1182, 706);
            (NumericTargetOffset).EndInit();
            (NumericGain).EndInit();
            (numericToHz).EndInit();
            (numericFromHz).EndInit();
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
        private CheckBox checkBoxBypass;
        private Panel panelAutoTune;
        private Button buttonOverlaySettings;
        private DarkComboBox comboBoxSmooth;
        private Label labelSmooth;
        private Button buttonImport;
        private Button buttonExport;
    }
}
