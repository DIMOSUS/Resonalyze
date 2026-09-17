namespace Resonalyze.Options
{
    partial class PROpt
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
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            labelGateOffset = new Label();
            checkAutoFit = new ReleaseClickCheckBox();
            numericGateOffset = new ThemedNumericUpDown();
            label9 = new Label();
            labelMinFrequency = new Label();
            comboSmoothingInverseOctaves = new ThemedComboBox();
            numericRightWindow = new ThemedNumericUpDown();
            numericLeftWindow = new ThemedNumericUpDown();
            label5 = new Label();
            label4 = new Label();
            numericWindow = new ThemedNumericUpDown();
            label1 = new Label();
            numericOffset = new ThemedNumericUpDown();
            label11 = new Label();
            buttonTauSlope = new ReleaseClickButton();
            buttonTauPeak = new ReleaseClickButton();
            checkBoxUnwrap = new ReleaseClickCheckBox();
            label2 = new Label();
            labelCurves = new Label();
            checkBoxShowMeasured = new ReleaseClickCheckBox();
            checkBoxShowMinimum = new ReleaseClickCheckBox();
            checkBoxShowExcess = new ReleaseClickCheckBox();
            checkBoxShowCoherence = new ReleaseClickCheckBox();
            labelWindowMode = new Label();
            comboWindowMode = new ThemedComboBox();
            labelFdwCycles = new Label();
            comboFdwCycles = new ThemedComboBox();
            labelDetrendMode = new Label();
            comboDetrendMode = new ThemedComboBox();
            irPlotView = new OxyPlot.WindowsForms.PlotView();
            (numericGateOffset).BeginInit();
            (numericRightWindow).BeginInit();
            (numericLeftWindow).BeginInit();
            (numericWindow).BeginInit();
            (numericOffset).BeginInit();
            SuspendLayout();
            // 
            // labelGateOffset
            // 
            labelGateOffset.AutoSize = true;
            labelGateOffset.ForeColor = UiPalette.TextDefault;
            labelGateOffset.Location = new Point(12, 106);
            labelGateOffset.Name = "labelGateOffset";
            labelGateOffset.Size = new Size(91, 15);
            labelGateOffset.TabIndex = 60;
            labelGateOffset.Text = "Gate offset (ms)";
            //
            // checkAutoFit
            //
            checkAutoFit.Appearance = Appearance.Button;
            checkAutoFit.BackColor = UiPalette.ControlSurface;
            checkAutoFit.FlatAppearance.CheckedBackColor = UiPalette.ToggleCheckedFill;
            checkAutoFit.FlatStyle = FlatStyle.Flat;
            checkAutoFit.ForeColor = UiPalette.TextPrimary;
            checkAutoFit.Location = new Point(104, 103);
            checkAutoFit.Name = "checkAutoFit";
            checkAutoFit.Size = new Size(46, 21);
            checkAutoFit.TabIndex = 61;
            checkAutoFit.Text = "Auto";
            checkAutoFit.TextAlign = ContentAlignment.MiddleCenter;
            checkAutoFit.UseCompatibleTextRendering = true;
            checkAutoFit.UseVisualStyleBackColor = false;
            // 
            // numericGateOffset
            // 
            numericGateOffset.BackColor = UiPalette.ControlSurface;
            numericGateOffset.DecimalPlaces = 3;
            numericGateOffset.ForeColor = UiPalette.TextPrimary;
            numericGateOffset.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            numericGateOffset.Location = new Point(153, 104);
            numericGateOffset.Maximum = new decimal(new int[] { 2000, 0, 0, 0 });
            numericGateOffset.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericGateOffset.MinimumSize = new Size(36, 19);
            numericGateOffset.Name = "numericGateOffset";
            numericGateOffset.Size = new Size(100, 19);
            numericGateOffset.TabIndex = 62;
            numericGateOffset.TextAlign = HorizontalAlignment.Right;
            numericGateOffset.ThousandsSeparator = false;
            numericGateOffset.Value = new decimal(new int[] { 0, 0, 0, 0 });
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.ForeColor = UiPalette.TextDefault;
            label9.Location = new Point(12, 281);
            label9.Name = "label9";
            label9.Size = new Size(117, 15);
            label9.TabIndex = 40;
            label9.Text = "Smoothing (octaves)";
            // 
            // labelMinFrequency
            // 
            labelMinFrequency.AutoSize = true;
            labelMinFrequency.ForeColor = UiPalette.TextAccent;
            labelMinFrequency.Location = new Point(12, 203);
            labelMinFrequency.Name = "labelMinFrequency";
            labelMinFrequency.Size = new Size(120, 15);
            labelMinFrequency.TabIndex = 55;
            labelMinFrequency.Text = "Reliable from ≈ — Hz";
            // 
            // comboSmoothingInverseOctaves
            // 
            comboSmoothingInverseOctaves.BackColor = UiPalette.ControlSurface;
            comboSmoothingInverseOctaves.ForeColor = UiPalette.TextPrimary;
            comboSmoothingInverseOctaves.Location = new Point(155, 279);
            comboSmoothingInverseOctaves.MinimumSize = new Size(36, 19);
            comboSmoothingInverseOctaves.Name = "comboSmoothingInverseOctaves";
            comboSmoothingInverseOctaves.Size = new Size(100, 23);
            comboSmoothingInverseOctaves.TabIndex = 39;
            // 
            // numericRightWindow
            // 
            numericRightWindow.BackColor = UiPalette.ControlSurface;
            numericRightWindow.DecimalPlaces = 2;
            numericRightWindow.ForeColor = UiPalette.TextPrimary;
            numericRightWindow.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            numericRightWindow.Location = new Point(153, 178);
            numericRightWindow.Maximum = new decimal(new int[] { 680, 0, 0, 0 });
            numericRightWindow.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericRightWindow.MinimumSize = new Size(36, 19);
            numericRightWindow.Name = "numericRightWindow";
            numericRightWindow.Size = new Size(100, 19);
            numericRightWindow.TabIndex = 38;
            numericRightWindow.TextAlign = HorizontalAlignment.Right;
            numericRightWindow.ThousandsSeparator = false;
            numericRightWindow.Value = new decimal(new int[] { 7, 0, 0, 65536 });
            // 
            // numericLeftWindow
            // 
            numericLeftWindow.BackColor = UiPalette.ControlSurface;
            numericLeftWindow.DecimalPlaces = 2;
            numericLeftWindow.ForeColor = UiPalette.TextPrimary;
            numericLeftWindow.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            numericLeftWindow.Location = new Point(153, 153);
            numericLeftWindow.Maximum = new decimal(new int[] { 680, 0, 0, 0 });
            numericLeftWindow.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericLeftWindow.MinimumSize = new Size(36, 19);
            numericLeftWindow.Name = "numericLeftWindow";
            numericLeftWindow.Size = new Size(100, 19);
            numericLeftWindow.TabIndex = 37;
            numericLeftWindow.TextAlign = HorizontalAlignment.Right;
            numericLeftWindow.ThousandsSeparator = false;
            numericLeftWindow.Value = new decimal(new int[] { 3, 0, 0, 65536 });
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.ForeColor = UiPalette.TextDefault;
            label5.Location = new Point(12, 180);
            label5.Name = "label5";
            label5.Size = new Size(97, 15);
            label5.TabIndex = 36;
            label5.Text = "Right Tukey (ms)";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.ForeColor = UiPalette.TextDefault;
            label4.Location = new Point(12, 155);
            label4.Name = "label4";
            label4.Size = new Size(89, 15);
            label4.TabIndex = 35;
            label4.Text = "Left Tukey (ms)";
            // 
            // numericWindow
            // 
            numericWindow.BackColor = UiPalette.ControlSurface;
            numericWindow.DecimalPlaces = 2;
            numericWindow.ForeColor = UiPalette.TextPrimary;
            numericWindow.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            numericWindow.Location = new Point(153, 129);
            numericWindow.Maximum = new decimal(new int[] { 680, 0, 0, 0 });
            numericWindow.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericWindow.MinimumSize = new Size(36, 19);
            numericWindow.Name = "numericWindow";
            numericWindow.Size = new Size(100, 19);
            numericWindow.TabIndex = 34;
            numericWindow.TextAlign = HorizontalAlignment.Right;
            numericWindow.ThousandsSeparator = false;
            numericWindow.Value = new decimal(new int[] { 200, 0, 0, 131072 });
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.ForeColor = UiPalette.TextDefault;
            label1.Location = new Point(12, 131);
            label1.Name = "label1";
            label1.Size = new Size(73, 15);
            label1.TabIndex = 33;
            label1.Text = "Plateau (ms)";
            // 
            // numericOffset
            // 
            numericOffset.BackColor = UiPalette.ControlSurface;
            numericOffset.DecimalPlaces = 3;
            numericOffset.ForeColor = UiPalette.TextPrimary;
            numericOffset.Increment = new decimal(new int[] { 5, 0, 0, 196608 });
            numericOffset.Location = new Point(155, 223);
            numericOffset.Maximum = new decimal(new int[] { 2000, 0, 0, 0 });
            numericOffset.Minimum = new decimal(new int[] { 2000, 0, 0, int.MinValue });
            numericOffset.MinimumSize = new Size(36, 19);
            numericOffset.Name = "numericOffset";
            numericOffset.Size = new Size(100, 19);
            numericOffset.TabIndex = 43;
            numericOffset.TextAlign = HorizontalAlignment.Right;
            numericOffset.ThousandsSeparator = false;
            numericOffset.Value = new decimal(new int[] { 0, 0, 0, 0 });
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.ForeColor = UiPalette.TextDefault;
            label11.Location = new Point(14, 225);
            label11.Name = "label11";
            label11.Size = new Size(40, 15);
            label11.TabIndex = 42;
            label11.Text = "τ (ms)";
            // 
            // buttonTauSlope
            // 
            buttonTauSlope.BackColor = UiPalette.ControlSurface;
            buttonTauSlope.FlatStyle = FlatStyle.Flat;
            buttonTauSlope.ForeColor = UiPalette.TextPrimary;
            buttonTauSlope.Location = new Point(12, 248);
            buttonTauSlope.Name = "buttonTauSlope";
            buttonTauSlope.Size = new Size(116, 23);
            buttonTauSlope.TabIndex = 53;
            buttonTauSlope.Text = "Find τ (slope)";
            buttonTauSlope.UseCompatibleTextRendering = true;
            buttonTauSlope.UseVisualStyleBackColor = false;
            // 
            // buttonTauPeak
            // 
            buttonTauPeak.BackColor = UiPalette.ControlSurface;
            buttonTauPeak.FlatStyle = FlatStyle.Flat;
            buttonTauPeak.ForeColor = UiPalette.TextPrimary;
            buttonTauPeak.Location = new Point(139, 248);
            buttonTauPeak.Name = "buttonTauPeak";
            buttonTauPeak.Size = new Size(116, 23);
            buttonTauPeak.TabIndex = 54;
            buttonTauPeak.Text = "Find τ (peak)";
            buttonTauPeak.UseCompatibleTextRendering = true;
            buttonTauPeak.UseVisualStyleBackColor = false;
            // 
            // checkBoxUnwrap
            // 
            checkBoxUnwrap.AutoSize = true;
            checkBoxUnwrap.ForeColor = UiPalette.TextDefault;
            checkBoxUnwrap.Location = new Point(238, 308);
            checkBoxUnwrap.Name = "checkBoxUnwrap";
            checkBoxUnwrap.Size = new Size(15, 14);
            checkBoxUnwrap.TabIndex = 45;
            checkBoxUnwrap.UseVisualStyleBackColor = true;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.ForeColor = UiPalette.TextDefault;
            label2.Location = new Point(12, 307);
            label2.Name = "label2";
            label2.Size = new Size(48, 15);
            label2.TabIndex = 44;
            label2.Text = "Unwrap";
            // 
            // labelCurves
            // 
            labelCurves.AutoSize = true;
            labelCurves.ForeColor = UiPalette.TextAccent;
            labelCurves.Location = new Point(12, 331);
            labelCurves.Name = "labelCurves";
            labelCurves.Size = new Size(46, 15);
            labelCurves.TabIndex = 56;
            labelCurves.Text = "Curves:";
            // 
            // checkBoxShowMeasured
            // 
            checkBoxShowMeasured.AutoSize = true;
            checkBoxShowMeasured.ForeColor = UiPalette.TextDefault;
            checkBoxShowMeasured.Location = new Point(12, 353);
            checkBoxShowMeasured.Name = "checkBoxShowMeasured";
            checkBoxShowMeasured.Size = new Size(144, 19);
            checkBoxShowMeasured.TabIndex = 46;
            checkBoxShowMeasured.Text = "Show measured phase";
            checkBoxShowMeasured.UseVisualStyleBackColor = true;
            // 
            // checkBoxShowMinimum
            // 
            checkBoxShowMinimum.AutoSize = true;
            checkBoxShowMinimum.ForeColor = UiPalette.TextDefault;
            checkBoxShowMinimum.Location = new Point(12, 375);
            checkBoxShowMinimum.Name = "checkBoxShowMinimum";
            checkBoxShowMinimum.Size = new Size(145, 19);
            checkBoxShowMinimum.TabIndex = 47;
            checkBoxShowMinimum.Text = "Show minimum phase";
            checkBoxShowMinimum.UseVisualStyleBackColor = true;
            // 
            // checkBoxShowExcess
            // 
            checkBoxShowExcess.AutoSize = true;
            checkBoxShowExcess.ForeColor = UiPalette.TextDefault;
            checkBoxShowExcess.Location = new Point(12, 397);
            checkBoxShowExcess.Name = "checkBoxShowExcess";
            checkBoxShowExcess.Size = new Size(125, 19);
            checkBoxShowExcess.TabIndex = 48;
            checkBoxShowExcess.Text = "Show excess phase";
            checkBoxShowExcess.UseVisualStyleBackColor = true;
            // 
            // checkBoxShowCoherence
            // 
            checkBoxShowCoherence.AutoSize = true;
            checkBoxShowCoherence.ForeColor = UiPalette.TextDefault;
            checkBoxShowCoherence.Location = new Point(12, 419);
            checkBoxShowCoherence.Name = "checkBoxShowCoherence";
            checkBoxShowCoherence.Size = new Size(134, 19);
            checkBoxShowCoherence.TabIndex = 57;
            checkBoxShowCoherence.Text = "Show γ² (coherence)";
            checkBoxShowCoherence.UseVisualStyleBackColor = true;
            //
            // labelWindowMode
            //
            labelWindowMode.AutoSize = true;
            labelWindowMode.ForeColor = UiPalette.TextDefault;
            labelWindowMode.Location = new Point(12, 14);
            labelWindowMode.Name = "labelWindowMode";
            labelWindowMode.Size = new Size(52, 15);
            labelWindowMode.TabIndex = 63;
            labelWindowMode.Text = "Window";
            //
            // comboWindowMode
            //
            comboWindowMode.BackColor = UiPalette.ControlSurface;
            comboWindowMode.DropDownStyle = ComboBoxStyle.DropDownList;
            comboWindowMode.ForeColor = UiPalette.TextPrimary;
            comboWindowMode.Items.AddRange(new object[] { "Fixed", "FDW" });
            comboWindowMode.Location = new Point(153, 10);
            comboWindowMode.MinimumSize = new Size(36, 19);
            comboWindowMode.Name = "comboWindowMode";
            comboWindowMode.Size = new Size(100, 19);
            comboWindowMode.TabIndex = 64;
            //
            // labelFdwCycles
            //
            labelFdwCycles.AutoSize = true;
            labelFdwCycles.ForeColor = UiPalette.TextDefault;
            labelFdwCycles.Location = new Point(12, 40);
            labelFdwCycles.Name = "labelFdwCycles";
            labelFdwCycles.Size = new Size(67, 15);
            labelFdwCycles.TabIndex = 65;
            labelFdwCycles.Text = "FDW cycles";
            //
            // comboFdwCycles
            //
            comboFdwCycles.BackColor = UiPalette.ControlSurface;
            comboFdwCycles.DropDownStyle = ComboBoxStyle.DropDownList;
            comboFdwCycles.ForeColor = UiPalette.TextPrimary;
            comboFdwCycles.Items.AddRange(new object[] { 4, 6, 8 });
            comboFdwCycles.Location = new Point(153, 36);
            comboFdwCycles.MinimumSize = new Size(36, 19);
            comboFdwCycles.Name = "comboFdwCycles";
            comboFdwCycles.Size = new Size(100, 19);
            comboFdwCycles.TabIndex = 66;
            //
            // labelDetrendMode
            //
            labelDetrendMode.AutoSize = true;
            labelDetrendMode.ForeColor = UiPalette.TextDefault;
            labelDetrendMode.Location = new Point(12, 66);
            labelDetrendMode.Name = "labelDetrendMode";
            labelDetrendMode.Size = new Size(49, 15);
            labelDetrendMode.TabIndex = 67;
            labelDetrendMode.Text = "Detrend";
            //
            // comboDetrendMode
            //
            comboDetrendMode.BackColor = UiPalette.ControlSurface;
            comboDetrendMode.DropDownStyle = ComboBoxStyle.DropDownList;
            comboDetrendMode.ForeColor = UiPalette.TextPrimary;
            comboDetrendMode.Items.AddRange(new object[] { "Off", "Auto", "Manual" });
            comboDetrendMode.Location = new Point(153, 62);
            comboDetrendMode.MinimumSize = new Size(36, 19);
            comboDetrendMode.Name = "comboDetrendMode";
            comboDetrendMode.Size = new Size(100, 19);
            comboDetrendMode.TabIndex = 68;
            //
            // irPlotView
            //
            irPlotView.BackColor = UiPalette.GraphSurfaceMuted;
            irPlotView.Location = new Point(12, 445);
            irPlotView.Name = "irPlotView";
            irPlotView.PanCursor = Cursors.Hand;
            irPlotView.Size = new Size(241, 300);
            irPlotView.TabIndex = 50;
            irPlotView.Text = "plotView1";
            irPlotView.ZoomHorizontalCursor = Cursors.SizeWE;
            irPlotView.ZoomRectangleCursor = Cursors.SizeNWSE;
            irPlotView.ZoomVerticalCursor = Cursors.SizeNS;
            // 
            // PROpt
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiPalette.AppBackground;
            ClientSize = new Size(265, 752);
            Controls.Add(labelWindowMode);
            Controls.Add(comboWindowMode);
            Controls.Add(labelFdwCycles);
            Controls.Add(comboFdwCycles);
            Controls.Add(labelDetrendMode);
            Controls.Add(comboDetrendMode);
            Controls.Add(irPlotView);
            Controls.Add(checkBoxShowCoherence);
            Controls.Add(numericGateOffset);
            Controls.Add(checkAutoFit);
            Controls.Add(labelGateOffset);
            Controls.Add(labelMinFrequency);
            Controls.Add(labelCurves);
            Controls.Add(checkBoxShowExcess);
            Controls.Add(checkBoxShowMinimum);
            Controls.Add(checkBoxShowMeasured);
            Controls.Add(checkBoxUnwrap);
            Controls.Add(label2);
            Controls.Add(buttonTauPeak);
            Controls.Add(buttonTauSlope);
            Controls.Add(numericOffset);
            Controls.Add(label11);
            Controls.Add(label9);
            Controls.Add(comboSmoothingInverseOctaves);
            Controls.Add(numericRightWindow);
            Controls.Add(numericLeftWindow);
            Controls.Add(label5);
            Controls.Add(label4);
            Controls.Add(numericWindow);
            Controls.Add(label1);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "PROpt";
            ShowInTaskbar = false;
            Text = "Phase Response Options";
            (numericGateOffset).EndInit();
            (numericRightWindow).EndInit();
            (numericLeftWindow).EndInit();
            (numericWindow).EndInit();
            (numericOffset).EndInit();
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion

        private Label labelGateOffset;
        private ReleaseClickCheckBox checkAutoFit;
        private ThemedNumericUpDown numericGateOffset;
        private Label label9;
        private Label labelMinFrequency;
        private ThemedComboBox comboSmoothingInverseOctaves;
        private ThemedNumericUpDown numericRightWindow;
        private ThemedNumericUpDown numericLeftWindow;
        private Label label5;
        private Label label4;
        private ThemedNumericUpDown numericWindow;
        private Label label1;
        private ThemedNumericUpDown numericOffset;
        private Label label11;
        private ReleaseClickButton buttonTauSlope;
        private ReleaseClickButton buttonTauPeak;
        private ReleaseClickCheckBox checkBoxUnwrap;
        private Label label2;
        private Label labelCurves;
        private ReleaseClickCheckBox checkBoxShowMeasured;
        private ReleaseClickCheckBox checkBoxShowMinimum;
        private ReleaseClickCheckBox checkBoxShowExcess;
        private ReleaseClickCheckBox checkBoxShowCoherence;
        private Label labelWindowMode;
        private ThemedComboBox comboWindowMode;
        private Label labelFdwCycles;
        private ThemedComboBox comboFdwCycles;
        private Label labelDetrendMode;
        private ThemedComboBox comboDetrendMode;
        private OxyPlot.WindowsForms.PlotView irPlotView;
    }
}
