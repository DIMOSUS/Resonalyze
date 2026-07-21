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
            checkAutoFit = new CheckBox();
            numericGateOffset = new DarkNumericUpDown();
            label9 = new Label();
            labelMinFrequency = new Label();
            comboSmoothingInverseOctaves = new DarkComboBox();
            numericRightWindow = new DarkNumericUpDown();
            numericLeftWindow = new DarkNumericUpDown();
            label5 = new Label();
            label4 = new Label();
            numericWindow = new DarkNumericUpDown();
            label1 = new Label();
            numericOffset = new DarkNumericUpDown();
            label11 = new Label();
            buttonTauSlope = new Button();
            buttonTauPeak = new Button();
            checkBoxUnwrap = new CheckBox();
            label2 = new Label();
            labelCurves = new Label();
            checkBoxShowMeasured = new CheckBox();
            checkBoxShowMinimum = new CheckBox();
            checkBoxShowExcess = new CheckBox();
            checkBoxShowCoherence = new CheckBox();
            labelWindowMode = new Label();
            comboWindowMode = new DarkComboBox();
            labelFdwCycles = new Label();
            comboFdwCycles = new DarkComboBox();
            labelDetrendMode = new Label();
            comboDetrendMode = new DarkComboBox();
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
            labelGateOffset.ForeColor = SystemColors.ControlLight;
            labelGateOffset.Location = new Point(12, 106);
            labelGateOffset.Name = "labelGateOffset";
            labelGateOffset.Size = new Size(91, 15);
            labelGateOffset.TabIndex = 60;
            labelGateOffset.Text = "Gate offset (ms)";
            //
            // checkAutoFit
            //
            checkAutoFit.Appearance = Appearance.Button;
            checkAutoFit.BackColor = Color.FromArgb(55, 60, 72);
            checkAutoFit.FlatAppearance.CheckedBackColor = Color.FromArgb(80, 100, 140);
            checkAutoFit.FlatStyle = FlatStyle.Flat;
            checkAutoFit.ForeColor = Color.White;
            checkAutoFit.Location = new Point(110, 103);
            checkAutoFit.Name = "checkAutoFit";
            checkAutoFit.Size = new Size(40, 21);
            checkAutoFit.TabIndex = 61;
            checkAutoFit.Text = "Auto";
            checkAutoFit.TextAlign = ContentAlignment.MiddleCenter;
            checkAutoFit.UseCompatibleTextRendering = true;
            checkAutoFit.UseVisualStyleBackColor = false;
            // 
            // numericGateOffset
            // 
            numericGateOffset.BackColor = Color.FromArgb(55, 60, 72);
            numericGateOffset.DecimalPlaces = 3;
            numericGateOffset.ForeColor = Color.White;
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
            label9.ForeColor = SystemColors.ControlLight;
            label9.Location = new Point(12, 281);
            label9.Name = "label9";
            label9.Size = new Size(117, 15);
            label9.TabIndex = 40;
            label9.Text = "Smoothing (octaves)";
            // 
            // labelMinFrequency
            // 
            labelMinFrequency.AutoSize = true;
            labelMinFrequency.ForeColor = Color.FromArgb(150, 170, 205);
            labelMinFrequency.Location = new Point(12, 203);
            labelMinFrequency.Name = "labelMinFrequency";
            labelMinFrequency.Size = new Size(120, 15);
            labelMinFrequency.TabIndex = 55;
            labelMinFrequency.Text = "Reliable from ≈ — Hz";
            // 
            // comboSmoothingInverseOctaves
            // 
            comboSmoothingInverseOctaves.BackColor = Color.FromArgb(55, 60, 72);
            comboSmoothingInverseOctaves.ForeColor = Color.White;
            comboSmoothingInverseOctaves.Location = new Point(155, 279);
            comboSmoothingInverseOctaves.MinimumSize = new Size(36, 19);
            comboSmoothingInverseOctaves.Name = "comboSmoothingInverseOctaves";
            comboSmoothingInverseOctaves.Size = new Size(100, 23);
            comboSmoothingInverseOctaves.TabIndex = 39;
            // 
            // numericRightWindow
            // 
            numericRightWindow.BackColor = Color.FromArgb(55, 60, 72);
            numericRightWindow.DecimalPlaces = 2;
            numericRightWindow.ForeColor = Color.White;
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
            numericLeftWindow.BackColor = Color.FromArgb(55, 60, 72);
            numericLeftWindow.DecimalPlaces = 2;
            numericLeftWindow.ForeColor = Color.White;
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
            label5.ForeColor = SystemColors.ControlLight;
            label5.Location = new Point(12, 180);
            label5.Name = "label5";
            label5.Size = new Size(97, 15);
            label5.TabIndex = 36;
            label5.Text = "Right Tukey (ms)";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.ForeColor = SystemColors.ControlLight;
            label4.Location = new Point(12, 155);
            label4.Name = "label4";
            label4.Size = new Size(89, 15);
            label4.TabIndex = 35;
            label4.Text = "Left Tukey (ms)";
            // 
            // numericWindow
            // 
            numericWindow.BackColor = Color.FromArgb(55, 60, 72);
            numericWindow.DecimalPlaces = 2;
            numericWindow.ForeColor = Color.White;
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
            numericWindow.ValueChanged += numericWindow_ValueChanged;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.ForeColor = SystemColors.ControlLight;
            label1.Location = new Point(12, 131);
            label1.Name = "label1";
            label1.Size = new Size(73, 15);
            label1.TabIndex = 33;
            label1.Text = "Plateau (ms)";
            // 
            // numericOffset
            // 
            numericOffset.BackColor = Color.FromArgb(55, 60, 72);
            numericOffset.DecimalPlaces = 3;
            numericOffset.ForeColor = Color.White;
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
            label11.ForeColor = SystemColors.ControlLight;
            label11.Location = new Point(14, 225);
            label11.Name = "label11";
            label11.Size = new Size(40, 15);
            label11.TabIndex = 42;
            label11.Text = "τ (ms)";
            // 
            // buttonTauSlope
            // 
            buttonTauSlope.BackColor = Color.FromArgb(55, 60, 72);
            buttonTauSlope.FlatStyle = FlatStyle.Flat;
            buttonTauSlope.ForeColor = Color.White;
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
            buttonTauPeak.BackColor = Color.FromArgb(55, 60, 72);
            buttonTauPeak.FlatStyle = FlatStyle.Flat;
            buttonTauPeak.ForeColor = Color.White;
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
            checkBoxUnwrap.ForeColor = SystemColors.ControlLight;
            checkBoxUnwrap.Location = new Point(238, 308);
            checkBoxUnwrap.Name = "checkBoxUnwrap";
            checkBoxUnwrap.Size = new Size(15, 14);
            checkBoxUnwrap.TabIndex = 45;
            checkBoxUnwrap.UseVisualStyleBackColor = true;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.ForeColor = SystemColors.ControlLight;
            label2.Location = new Point(12, 307);
            label2.Name = "label2";
            label2.Size = new Size(48, 15);
            label2.TabIndex = 44;
            label2.Text = "Unwrap";
            // 
            // labelCurves
            // 
            labelCurves.AutoSize = true;
            labelCurves.ForeColor = Color.FromArgb(150, 170, 205);
            labelCurves.Location = new Point(12, 331);
            labelCurves.Name = "labelCurves";
            labelCurves.Size = new Size(46, 15);
            labelCurves.TabIndex = 56;
            labelCurves.Text = "Curves:";
            // 
            // checkBoxShowMeasured
            // 
            checkBoxShowMeasured.AutoSize = true;
            checkBoxShowMeasured.ForeColor = SystemColors.ControlLight;
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
            checkBoxShowMinimum.ForeColor = SystemColors.ControlLight;
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
            checkBoxShowExcess.ForeColor = SystemColors.ControlLight;
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
            checkBoxShowCoherence.ForeColor = SystemColors.ControlLight;
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
            labelWindowMode.ForeColor = Color.Gainsboro;
            labelWindowMode.Location = new Point(12, 14);
            labelWindowMode.Name = "labelWindowMode";
            labelWindowMode.Size = new Size(52, 15);
            labelWindowMode.TabIndex = 63;
            labelWindowMode.Text = "Window";
            //
            // comboWindowMode
            //
            comboWindowMode.BackColor = Color.FromArgb(55, 60, 72);
            comboWindowMode.DropDownStyle = ComboBoxStyle.DropDownList;
            comboWindowMode.ForeColor = Color.White;
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
            labelFdwCycles.ForeColor = Color.Gainsboro;
            labelFdwCycles.Location = new Point(12, 40);
            labelFdwCycles.Name = "labelFdwCycles";
            labelFdwCycles.Size = new Size(67, 15);
            labelFdwCycles.TabIndex = 65;
            labelFdwCycles.Text = "FDW cycles";
            //
            // comboFdwCycles
            //
            comboFdwCycles.BackColor = Color.FromArgb(55, 60, 72);
            comboFdwCycles.DropDownStyle = ComboBoxStyle.DropDownList;
            comboFdwCycles.ForeColor = Color.White;
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
            labelDetrendMode.ForeColor = Color.Gainsboro;
            labelDetrendMode.Location = new Point(12, 66);
            labelDetrendMode.Name = "labelDetrendMode";
            labelDetrendMode.Size = new Size(49, 15);
            labelDetrendMode.TabIndex = 67;
            labelDetrendMode.Text = "Detrend";
            //
            // comboDetrendMode
            //
            comboDetrendMode.BackColor = Color.FromArgb(55, 60, 72);
            comboDetrendMode.DropDownStyle = ComboBoxStyle.DropDownList;
            comboDetrendMode.ForeColor = Color.White;
            comboDetrendMode.Items.AddRange(new object[] { "Off", "Auto", "Manual" });
            comboDetrendMode.Location = new Point(153, 62);
            comboDetrendMode.MinimumSize = new Size(36, 19);
            comboDetrendMode.Name = "comboDetrendMode";
            comboDetrendMode.Size = new Size(100, 19);
            comboDetrendMode.TabIndex = 68;
            //
            // irPlotView
            //
            irPlotView.BackColor = Color.FromArgb(32, 36, 46);
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
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
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
        private CheckBox checkAutoFit;
        private DarkNumericUpDown numericGateOffset;
        private Label label9;
        private Label labelMinFrequency;
        private DarkComboBox comboSmoothingInverseOctaves;
        private DarkNumericUpDown numericRightWindow;
        private DarkNumericUpDown numericLeftWindow;
        private Label label5;
        private Label label4;
        private DarkNumericUpDown numericWindow;
        private Label label1;
        private DarkNumericUpDown numericOffset;
        private Label label11;
        private Button buttonTauSlope;
        private Button buttonTauPeak;
        private CheckBox checkBoxUnwrap;
        private Label label2;
        private Label labelCurves;
        private CheckBox checkBoxShowMeasured;
        private CheckBox checkBoxShowMinimum;
        private CheckBox checkBoxShowExcess;
        private CheckBox checkBoxShowCoherence;
        private Label labelWindowMode;
        private DarkComboBox comboWindowMode;
        private Label labelFdwCycles;
        private DarkComboBox comboFdwCycles;
        private Label labelDetrendMode;
        private DarkComboBox comboDetrendMode;
        private OxyPlot.WindowsForms.PlotView irPlotView;
    }
}
