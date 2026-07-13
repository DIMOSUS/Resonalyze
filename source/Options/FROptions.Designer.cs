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
            labelCurves = new Label();
            checkBoxShowPrimary = new CheckBox();
            checkBoxShowCoherence = new CheckBox();
            checkBoxShowHd2 = new CheckBox();
            checkBoxShowHd3 = new CheckBox();
            checkBoxShowHd4 = new CheckBox();
            checkBoxShowThdPlusNoise = new CheckBox();
            checkBoxShowNoiseFloor = new CheckBox();
            irPlotView = new OxyPlot.WindowsForms.PlotView();
            (numericWindow).BeginInit();
            (numericRightWindow).BeginInit();
            (numericLeftWindow).BeginInit();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.ForeColor = SystemColors.ControlLight;
            label1.Location = new Point(12, 14);
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
            numericWindow.Location = new Point(193, 12);
            numericWindow.Maximum = new decimal(new int[] { 32768, 0, 0, 0 });
            numericWindow.Minimum = new decimal(new int[] { 4, 0, 0, 0 });
            numericWindow.MinimumSize = new Size(36, 19);
            numericWindow.Name = "numericWindow";
            numericWindow.Size = new Size(60, 19);
            numericWindow.TabIndex = 17;
            numericWindow.TextAlign = HorizontalAlignment.Right;
            numericWindow.ThousandsSeparator = false;
            numericWindow.Value = new decimal(new int[] { 8192, 0, 0, 0 });
            numericWindow.ValueChanged += numericWindow_ValueChanged;
            // 
            // numericRightWindow
            // 
            numericRightWindow.BackColor = Color.FromArgb(55, 60, 72);
            numericRightWindow.DecimalPlaces = 0;
            numericRightWindow.ForeColor = Color.White;
            numericRightWindow.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericRightWindow.Location = new Point(193, 61);
            numericRightWindow.Maximum = new decimal(new int[] { 16384, 0, 0, 0 });
            numericRightWindow.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericRightWindow.MinimumSize = new Size(36, 19);
            numericRightWindow.Name = "numericRightWindow";
            numericRightWindow.Size = new Size(60, 19);
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
            numericLeftWindow.Location = new Point(193, 36);
            numericLeftWindow.Maximum = new decimal(new int[] { 16384, 0, 0, 0 });
            numericLeftWindow.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericLeftWindow.MinimumSize = new Size(36, 19);
            numericLeftWindow.Name = "numericLeftWindow";
            numericLeftWindow.Size = new Size(60, 19);
            numericLeftWindow.TabIndex = 20;
            numericLeftWindow.TextAlign = HorizontalAlignment.Right;
            numericLeftWindow.ThousandsSeparator = false;
            numericLeftWindow.Value = new decimal(new int[] { 256, 0, 0, 0 });
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.ForeColor = SystemColors.ControlLight;
            label5.Location = new Point(12, 60);
            label5.Name = "label5";
            label5.Size = new Size(117, 15);
            label5.TabIndex = 19;
            label5.Text = "Tukey Window Right";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.ForeColor = SystemColors.ControlLight;
            label4.Location = new Point(12, 35);
            label4.Name = "label4";
            label4.Size = new Size(109, 15);
            label4.TabIndex = 18;
            label4.Text = "Tukey Window Left";
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.ForeColor = SystemColors.ControlLight;
            label9.Location = new Point(12, 85);
            label9.Name = "label9";
            label9.Size = new Size(117, 15);
            label9.TabIndex = 29;
            label9.Text = "Smoothing (octaves)";
            // 
            // comboSmoothingInverseOctaves
            // 
            comboSmoothingInverseOctaves.BackColor = Color.FromArgb(55, 60, 72);
            comboSmoothingInverseOctaves.ForeColor = Color.White;
            comboSmoothingInverseOctaves.Location = new Point(193, 84);
            comboSmoothingInverseOctaves.Margin = new Padding(0);
            comboSmoothingInverseOctaves.MinimumSize = new Size(36, 19);
            comboSmoothingInverseOctaves.Name = "comboSmoothingInverseOctaves";
            comboSmoothingInverseOctaves.Size = new Size(60, 23);
            comboSmoothingInverseOctaves.TabIndex = 28;
            // 
            // comboCalibration
            // 
            comboCalibration.BackColor = Color.FromArgb(55, 60, 72);
            comboCalibration.ForeColor = Color.White;
            comboCalibration.Location = new Point(153, 108);
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
            label2.Location = new Point(12, 110);
            label2.Name = "label2";
            label2.Size = new Size(65, 15);
            label2.TabIndex = 46;
            label2.Text = "Calibration";
            // 
            // labelCurves
            // 
            labelCurves.AutoSize = true;
            labelCurves.ForeColor = Color.FromArgb(150, 170, 205);
            labelCurves.Location = new Point(12, 136);
            labelCurves.Name = "labelCurves";
            labelCurves.Size = new Size(46, 15);
            labelCurves.TabIndex = 53;
            labelCurves.Text = "Curves:";
            // 
            // checkBoxShowPrimary
            // 
            checkBoxShowPrimary.AutoSize = true;
            checkBoxShowPrimary.ForeColor = SystemColors.ControlLight;
            checkBoxShowPrimary.Location = new Point(12, 158);
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
            checkBoxShowCoherence.Location = new Point(12, 290);
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
            checkBoxShowHd2.Location = new Point(12, 180);
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
            checkBoxShowHd3.Location = new Point(12, 202);
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
            checkBoxShowHd4.Location = new Point(12, 224);
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
            checkBoxShowThdPlusNoise.Location = new Point(12, 246);
            checkBoxShowThdPlusNoise.Name = "checkBoxShowThdPlusNoise";
            checkBoxShowThdPlusNoise.Size = new Size(99, 19);
            checkBoxShowThdPlusNoise.TabIndex = 52;
            checkBoxShowThdPlusNoise.Text = "Show THD";
            checkBoxShowThdPlusNoise.UseVisualStyleBackColor = true;
            //
            // checkBoxShowNoiseFloor
            //
            checkBoxShowNoiseFloor.AutoSize = true;
            checkBoxShowNoiseFloor.ForeColor = SystemColors.ControlLight;
            checkBoxShowNoiseFloor.Location = new Point(12, 268);
            checkBoxShowNoiseFloor.Name = "checkBoxShowNoiseFloor";
            checkBoxShowNoiseFloor.Size = new Size(114, 19);
            checkBoxShowNoiseFloor.TabIndex = 53;
            checkBoxShowNoiseFloor.Text = "Show noise floor";
            checkBoxShowNoiseFloor.UseVisualStyleBackColor = true;
            //
            // irPlotView
            //
            irPlotView.BackColor = Color.FromArgb(32, 36, 46);
            irPlotView.Location = new Point(12, 316);
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
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
            ClientSize = new Size(265, 623);
            Controls.Add(irPlotView);
            Controls.Add(checkBoxShowNoiseFloor);
            Controls.Add(checkBoxShowThdPlusNoise);
            Controls.Add(checkBoxShowHd4);
            Controls.Add(checkBoxShowHd3);
            Controls.Add(checkBoxShowHd2);
            Controls.Add(checkBoxShowCoherence);
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
        private Label labelCurves;
        private CheckBox checkBoxShowPrimary;
        private CheckBox checkBoxShowCoherence;
        private CheckBox checkBoxShowHd2;
        private CheckBox checkBoxShowHd3;
        private CheckBox checkBoxShowHd4;
        private CheckBox checkBoxShowThdPlusNoise;
        private CheckBox checkBoxShowNoiseFloor;
        private OxyPlot.WindowsForms.PlotView irPlotView;
    }
}
