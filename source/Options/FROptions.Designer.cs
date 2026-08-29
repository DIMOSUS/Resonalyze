namespace Resonalyze.Options
{
    partial class FROptions
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
            labelWindowMode = new Label();
            comboWindowMode = new DarkComboBox();
            labelFdwCycles = new Label();
            comboFdwCycles = new DarkComboBox();
            label1 = new Label();
            numericWindow = new DarkNumericUpDown();
            numericRightWindow = new DarkNumericUpDown();
            numericLeftWindow = new DarkNumericUpDown();
            label5 = new Label();
            label4 = new Label();
            label9 = new Label();
            comboSmoothingInverseOctaves = new DarkComboBox();
            comboCalibration = new DarkComboBox();
            label2 = new Label();
            labelScale = new Label();
            radioMagnitudeRelative = new ReleaseClickRadioButton();
            radioMagnitudeSpl = new ReleaseClickRadioButton();
            labelCurves = new Label();
            checkBoxShowPrimary = new ReleaseClickCheckBox();
            checkBoxShowCoherence = new ReleaseClickCheckBox();
            checkBoxShowHd2 = new ReleaseClickCheckBox();
            checkBoxShowHd3 = new ReleaseClickCheckBox();
            checkBoxShowHd4 = new ReleaseClickCheckBox();
            checkBoxShowThdPlusNoise = new ReleaseClickCheckBox();
            checkBoxShowNoiseFloor = new ReleaseClickCheckBox();
            checkBoxShowArrayAverage = new ReleaseClickCheckBox();
            checkBoxShowArrayMicrophones = new ReleaseClickCheckBox();
            checkBoxShowArraySpread = new ReleaseClickCheckBox();
            irPlotView = new OxyPlot.WindowsForms.PlotView();
            (numericWindow).BeginInit();
            (numericRightWindow).BeginInit();
            (numericLeftWindow).BeginInit();
            SuspendLayout();
            //
            // labelWindowMode
            //
            labelWindowMode.AutoSize = true;
            labelWindowMode.ForeColor = SystemColors.ControlLight;
            labelWindowMode.Location = new Point(12, 14);
            labelWindowMode.Name = "labelWindowMode";
            labelWindowMode.Size = new Size(83, 15);
            labelWindowMode.TabIndex = 58;
            labelWindowMode.Text = "Window mode";
            //
            // comboWindowMode
            //
            comboWindowMode.BackColor = Color.FromArgb(55, 60, 72);
            comboWindowMode.DropDownStyle = ComboBoxStyle.DropDownList;
            comboWindowMode.ForeColor = Color.White;
            comboWindowMode.Items.AddRange(new object[] { "Fixed", "FDW" });
            comboWindowMode.Location = new Point(153, 13);
            comboWindowMode.MinimumSize = new Size(36, 19);
            comboWindowMode.Name = "comboWindowMode";
            comboWindowMode.Size = new Size(100, 23);
            comboWindowMode.TabIndex = 59;
            //
            // labelFdwCycles
            //
            labelFdwCycles.AutoSize = true;
            labelFdwCycles.ForeColor = SystemColors.ControlLight;
            labelFdwCycles.Location = new Point(12, 39);
            labelFdwCycles.Name = "labelFdwCycles";
            labelFdwCycles.Size = new Size(67, 15);
            labelFdwCycles.TabIndex = 60;
            labelFdwCycles.Text = "FDW cycles";
            //
            // comboFdwCycles
            //
            comboFdwCycles.BackColor = Color.FromArgb(55, 60, 72);
            comboFdwCycles.DropDownStyle = ComboBoxStyle.DropDownList;
            comboFdwCycles.ForeColor = Color.White;
            comboFdwCycles.Items.AddRange(new object[] { 4, 6, 8 });
            comboFdwCycles.Location = new Point(153, 38);
            comboFdwCycles.MinimumSize = new Size(36, 19);
            comboFdwCycles.Name = "comboFdwCycles";
            comboFdwCycles.Size = new Size(100, 23);
            comboFdwCycles.TabIndex = 61;
            //
            // label1
            //
            label1.AutoSize = true;
            label1.ForeColor = SystemColors.ControlLight;
            label1.Location = new Point(12, 64);
            label1.Name = "label1";
            label1.Size = new Size(51, 15);
            label1.TabIndex = 16;
            label1.Text = "Window";
            //
            // numericWindow
            //
            numericWindow.BackColor = Color.FromArgb(55, 60, 72);
            numericWindow.DecimalPlaces = 0;
            numericWindow.ForeColor = Color.White;
            numericWindow.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericWindow.Location = new Point(153, 62);
            numericWindow.Maximum = new decimal(new int[] { 32768, 0, 0, 0 });
            numericWindow.Minimum = new decimal(new int[] { 4, 0, 0, 0 });
            numericWindow.MinimumSize = new Size(36, 19);
            numericWindow.Name = "numericWindow";
            numericWindow.Size = new Size(100, 19);
            numericWindow.TabIndex = 17;
            numericWindow.TextAlign = HorizontalAlignment.Right;
            numericWindow.ThousandsSeparator = false;
            numericWindow.Value = new decimal(new int[] { 8192, 0, 0, 0 });
            //
            // numericRightWindow
            //
            numericRightWindow.BackColor = Color.FromArgb(55, 60, 72);
            numericRightWindow.DecimalPlaces = 0;
            numericRightWindow.ForeColor = Color.White;
            numericRightWindow.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericRightWindow.Location = new Point(153, 111);
            numericRightWindow.Maximum = new decimal(new int[] { 16384, 0, 0, 0 });
            numericRightWindow.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericRightWindow.MinimumSize = new Size(36, 19);
            numericRightWindow.Name = "numericRightWindow";
            numericRightWindow.Size = new Size(100, 19);
            numericRightWindow.TabIndex = 21;
            numericRightWindow.TextAlign = HorizontalAlignment.Right;
            numericRightWindow.ThousandsSeparator = false;
            numericRightWindow.Value = new decimal(new int[] { 256, 0, 0, 0 });
            //
            // numericLeftWindow
            //
            numericLeftWindow.BackColor = Color.FromArgb(55, 60, 72);
            numericLeftWindow.DecimalPlaces = 0;
            numericLeftWindow.ForeColor = Color.White;
            numericLeftWindow.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericLeftWindow.Location = new Point(153, 86);
            numericLeftWindow.Maximum = new decimal(new int[] { 16384, 0, 0, 0 });
            numericLeftWindow.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericLeftWindow.MinimumSize = new Size(36, 19);
            numericLeftWindow.Name = "numericLeftWindow";
            numericLeftWindow.Size = new Size(100, 19);
            numericLeftWindow.TabIndex = 20;
            numericLeftWindow.TextAlign = HorizontalAlignment.Right;
            numericLeftWindow.ThousandsSeparator = false;
            numericLeftWindow.Value = new decimal(new int[] { 256, 0, 0, 0 });
            //
            // label5
            //
            label5.AutoSize = true;
            label5.ForeColor = SystemColors.ControlLight;
            label5.Location = new Point(12, 110);
            label5.Name = "label5";
            label5.Size = new Size(117, 15);
            label5.TabIndex = 19;
            label5.Text = "Tukey Window Right";
            //
            // label4
            //
            label4.AutoSize = true;
            label4.ForeColor = SystemColors.ControlLight;
            label4.Location = new Point(12, 85);
            label4.Name = "label4";
            label4.Size = new Size(109, 15);
            label4.TabIndex = 18;
            label4.Text = "Tukey Window Left";
            //
            // label9
            //
            label9.AutoSize = true;
            label9.ForeColor = SystemColors.ControlLight;
            label9.Location = new Point(12, 135);
            label9.Name = "label9";
            label9.Size = new Size(117, 15);
            label9.TabIndex = 29;
            label9.Text = "Smoothing (octaves)";
            //
            // comboSmoothingInverseOctaves
            //
            comboSmoothingInverseOctaves.BackColor = Color.FromArgb(55, 60, 72);
            comboSmoothingInverseOctaves.ForeColor = Color.White;
            comboSmoothingInverseOctaves.Location = new Point(153, 134);
            comboSmoothingInverseOctaves.Margin = new Padding(0);
            comboSmoothingInverseOctaves.MinimumSize = new Size(36, 19);
            comboSmoothingInverseOctaves.Name = "comboSmoothingInverseOctaves";
            comboSmoothingInverseOctaves.Size = new Size(100, 23);
            comboSmoothingInverseOctaves.TabIndex = 28;
            //
            // comboCalibration
            //
            comboCalibration.BackColor = Color.FromArgb(55, 60, 72);
            comboCalibration.ForeColor = Color.White;
            comboCalibration.Location = new Point(153, 158);
            comboCalibration.Margin = new Padding(0);
            comboCalibration.MinimumSize = new Size(36, 19);
            comboCalibration.Name = "comboCalibration";
            comboCalibration.Size = new Size(100, 23);
            comboCalibration.TabIndex = 47;
            //
            // label2
            //
            label2.AutoSize = true;
            label2.ForeColor = SystemColors.ControlLight;
            label2.Location = new Point(12, 160);
            label2.Name = "label2";
            label2.Size = new Size(65, 15);
            label2.TabIndex = 46;
            label2.Text = "Calibration";
            //
            // labelScale
            //
            labelScale.AutoSize = true;
            labelScale.ForeColor = SystemColors.ControlLight;
            labelScale.Location = new Point(12, 187);
            labelScale.Name = "labelScale";
            labelScale.Size = new Size(35, 15);
            labelScale.TabIndex = 55;
            labelScale.Text = "Scale";
            //
            // radioMagnitudeRelative
            //
            radioMagnitudeRelative.AutoSize = true;
            radioMagnitudeRelative.Checked = true;
            radioMagnitudeRelative.ForeColor = SystemColors.ControlLight;
            radioMagnitudeRelative.Location = new Point(72, 185);
            radioMagnitudeRelative.Name = "radioMagnitudeRelative";
            radioMagnitudeRelative.Size = new Size(72, 19);
            radioMagnitudeRelative.TabIndex = 56;
            radioMagnitudeRelative.TabStop = true;
            radioMagnitudeRelative.Text = "dBr/dBc";
            radioMagnitudeRelative.UseVisualStyleBackColor = true;
            //
            // radioMagnitudeSpl
            //
            radioMagnitudeSpl.AutoSize = true;
            radioMagnitudeSpl.ForeColor = SystemColors.ControlLight;
            radioMagnitudeSpl.Location = new Point(153, 185);
            radioMagnitudeSpl.Name = "radioMagnitudeSpl";
            radioMagnitudeSpl.Size = new Size(66, 19);
            radioMagnitudeSpl.TabIndex = 57;
            radioMagnitudeSpl.Text = "dB SPL";
            radioMagnitudeSpl.UseVisualStyleBackColor = true;
            //
            // labelCurves
            //
            labelCurves.AutoSize = true;
            labelCurves.ForeColor = Color.FromArgb(150, 170, 205);
            labelCurves.Location = new Point(12, 215);
            labelCurves.Name = "labelCurves";
            labelCurves.Size = new Size(46, 15);
            labelCurves.TabIndex = 53;
            labelCurves.Text = "Curves:";
            //
            // checkBoxShowPrimary
            //
            checkBoxShowPrimary.AutoSize = true;
            checkBoxShowPrimary.ForeColor = SystemColors.ControlLight;
            checkBoxShowPrimary.Location = new Point(12, 237);
            checkBoxShowPrimary.Name = "checkBoxShowPrimary";
            checkBoxShowPrimary.Size = new Size(161, 19);
            checkBoxShowPrimary.TabIndex = 48;
            checkBoxShowPrimary.Text = "Show frequency response";
            checkBoxShowPrimary.UseVisualStyleBackColor = true;
            //
            // checkBoxShowCoherence
            //
            checkBoxShowCoherence.AutoSize = true;
            checkBoxShowCoherence.ForeColor = SystemColors.ControlLight;
            checkBoxShowCoherence.Location = new Point(12, 369);
            checkBoxShowCoherence.Name = "checkBoxShowCoherence";
            checkBoxShowCoherence.Size = new Size(134, 19);
            checkBoxShowCoherence.TabIndex = 54;
            checkBoxShowCoherence.Text = "Show γ² (coherence)";
            checkBoxShowCoherence.UseVisualStyleBackColor = true;
            //
            // checkBoxShowHd2
            //
            checkBoxShowHd2.AutoSize = true;
            checkBoxShowHd2.ForeColor = SystemColors.ControlLight;
            checkBoxShowHd2.Location = new Point(12, 259);
            checkBoxShowHd2.Name = "checkBoxShowHd2";
            checkBoxShowHd2.Size = new Size(81, 19);
            checkBoxShowHd2.TabIndex = 49;
            checkBoxShowHd2.Text = "Show HD2";
            checkBoxShowHd2.UseVisualStyleBackColor = true;
            //
            // checkBoxShowHd3
            //
            checkBoxShowHd3.AutoSize = true;
            checkBoxShowHd3.ForeColor = SystemColors.ControlLight;
            checkBoxShowHd3.Location = new Point(12, 281);
            checkBoxShowHd3.Name = "checkBoxShowHd3";
            checkBoxShowHd3.Size = new Size(81, 19);
            checkBoxShowHd3.TabIndex = 50;
            checkBoxShowHd3.Text = "Show HD3";
            checkBoxShowHd3.UseVisualStyleBackColor = true;
            //
            // checkBoxShowHd4
            //
            checkBoxShowHd4.AutoSize = true;
            checkBoxShowHd4.ForeColor = SystemColors.ControlLight;
            checkBoxShowHd4.Location = new Point(12, 303);
            checkBoxShowHd4.Name = "checkBoxShowHd4";
            checkBoxShowHd4.Size = new Size(81, 19);
            checkBoxShowHd4.TabIndex = 51;
            checkBoxShowHd4.Text = "Show HD4";
            checkBoxShowHd4.UseVisualStyleBackColor = true;
            //
            // checkBoxShowThdPlusNoise
            //
            checkBoxShowThdPlusNoise.AutoSize = true;
            checkBoxShowThdPlusNoise.ForeColor = SystemColors.ControlLight;
            checkBoxShowThdPlusNoise.Location = new Point(12, 325);
            checkBoxShowThdPlusNoise.Name = "checkBoxShowThdPlusNoise";
            checkBoxShowThdPlusNoise.Size = new Size(82, 19);
            checkBoxShowThdPlusNoise.TabIndex = 52;
            checkBoxShowThdPlusNoise.Text = "Show THD";
            checkBoxShowThdPlusNoise.UseVisualStyleBackColor = true;
            //
            // checkBoxShowNoiseFloor
            //
            checkBoxShowNoiseFloor.AutoSize = true;
            checkBoxShowNoiseFloor.ForeColor = SystemColors.ControlLight;
            checkBoxShowNoiseFloor.Location = new Point(12, 347);
            checkBoxShowNoiseFloor.Name = "checkBoxShowNoiseFloor";
            checkBoxShowNoiseFloor.Size = new Size(114, 19);
            checkBoxShowNoiseFloor.TabIndex = 53;
            checkBoxShowNoiseFloor.Text = "Show noise floor";
            checkBoxShowNoiseFloor.UseVisualStyleBackColor = true;
            //
            // checkBoxShowArrayAverage
            //
            checkBoxShowArrayAverage.AutoSize = true;
            checkBoxShowArrayAverage.ForeColor = SystemColors.ControlLight;
            checkBoxShowArrayAverage.Location = new Point(12, 391);
            checkBoxShowArrayAverage.Name = "checkBoxShowArrayAverage";
            checkBoxShowArrayAverage.Size = new Size(140, 19);
            checkBoxShowArrayAverage.TabIndex = 55;
            checkBoxShowArrayAverage.Text = "Show array average";
            checkBoxShowArrayAverage.UseVisualStyleBackColor = true;
            //
            // checkBoxShowArrayMicrophones
            //
            checkBoxShowArrayMicrophones.AutoSize = true;
            checkBoxShowArrayMicrophones.ForeColor = SystemColors.ControlLight;
            checkBoxShowArrayMicrophones.Location = new Point(12, 413);
            checkBoxShowArrayMicrophones.Name = "checkBoxShowArrayMicrophones";
            checkBoxShowArrayMicrophones.Size = new Size(165, 19);
            checkBoxShowArrayMicrophones.TabIndex = 56;
            checkBoxShowArrayMicrophones.Text = "Show array microphones";
            checkBoxShowArrayMicrophones.UseVisualStyleBackColor = true;
            //
            // checkBoxShowArraySpread
            //
            checkBoxShowArraySpread.AutoSize = true;
            checkBoxShowArraySpread.ForeColor = SystemColors.ControlLight;
            checkBoxShowArraySpread.Location = new Point(12, 435);
            checkBoxShowArraySpread.Name = "checkBoxShowArraySpread";
            checkBoxShowArraySpread.Size = new Size(135, 19);
            checkBoxShowArraySpread.TabIndex = 57;
            checkBoxShowArraySpread.Text = "Show array spread";
            checkBoxShowArraySpread.UseVisualStyleBackColor = true;
            //
            // irPlotView
            //
            irPlotView.BackColor = Color.FromArgb(32, 36, 46);
            irPlotView.Location = new Point(12, 461);
            irPlotView.Name = "irPlotView";
            irPlotView.PanCursor = Cursors.Hand;
            irPlotView.Size = new Size(241, 300);
            irPlotView.TabIndex = 50;
            irPlotView.Text = "plotView1";
            irPlotView.ZoomHorizontalCursor = Cursors.SizeWE;
            irPlotView.ZoomRectangleCursor = Cursors.SizeNWSE;
            irPlotView.ZoomVerticalCursor = Cursors.SizeNS;
            //
            // FROptions
            //
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(45, 50, 60);
            ClientSize = new Size(265, 768);
            Controls.Add(labelWindowMode);
            Controls.Add(comboWindowMode);
            Controls.Add(labelFdwCycles);
            Controls.Add(comboFdwCycles);
            Controls.Add(irPlotView);
            Controls.Add(labelScale);
            Controls.Add(radioMagnitudeRelative);
            Controls.Add(radioMagnitudeSpl);
            Controls.Add(checkBoxShowNoiseFloor);
            Controls.Add(checkBoxShowThdPlusNoise);
            Controls.Add(checkBoxShowHd4);
            Controls.Add(checkBoxShowHd3);
            Controls.Add(checkBoxShowHd2);
            Controls.Add(checkBoxShowCoherence);
            Controls.Add(checkBoxShowArrayAverage);
            Controls.Add(checkBoxShowArrayMicrophones);
            Controls.Add(checkBoxShowArraySpread);
            Controls.Add(checkBoxShowPrimary);
            Controls.Add(labelCurves);
            Controls.Add(comboCalibration);
            Controls.Add(label2);
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
            Name = "FROptions";
            ShowInTaskbar = false;
            Text = "Frequency Response Options";
            (numericWindow).EndInit();
            (numericRightWindow).EndInit();
            (numericLeftWindow).EndInit();
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion
        private Label labelWindowMode;
        private DarkComboBox comboWindowMode;
        private Label labelFdwCycles;
        private DarkComboBox comboFdwCycles;
        private Label label1;
        private DarkNumericUpDown numericWindow;
        private DarkNumericUpDown numericRightWindow;
        private DarkNumericUpDown numericLeftWindow;
        private Label label5;
        private Label label4;
        private Label label9;
        private DarkComboBox comboSmoothingInverseOctaves;
        private DarkComboBox comboCalibration;
        private Label label2;
        private Label labelScale;
        private ReleaseClickRadioButton radioMagnitudeRelative;
        private ReleaseClickRadioButton radioMagnitudeSpl;
        private Label labelCurves;
        private ReleaseClickCheckBox checkBoxShowPrimary;
        private ReleaseClickCheckBox checkBoxShowCoherence;
        private ReleaseClickCheckBox checkBoxShowArrayAverage;
        private ReleaseClickCheckBox checkBoxShowArrayMicrophones;
        private ReleaseClickCheckBox checkBoxShowArraySpread;
        private ReleaseClickCheckBox checkBoxShowHd2;
        private ReleaseClickCheckBox checkBoxShowHd3;
        private ReleaseClickCheckBox checkBoxShowHd4;
        private ReleaseClickCheckBox checkBoxShowThdPlusNoise;
        private ReleaseClickCheckBox checkBoxShowNoiseFloor;
        private OxyPlot.WindowsForms.PlotView irPlotView;
    }
}
