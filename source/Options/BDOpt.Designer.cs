namespace Resonalyze.Options
{
    partial class BDOpt
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
            label9 = new Label();
            comboSmoothingInverseOctaves = new DarkComboBox();
            numericDbRange = new DarkNumericUpDown();
            label8 = new Label();
            numericCaptureTime = new DarkNumericUpDown();
            label7 = new Label();
            numericWindow = new DarkNumericUpDown();
            numericSampleRate = new DarkNumericUpDown();
            button1 = new Button();
            numericRightWindow = new DarkNumericUpDown();
            numericLeftWindow = new DarkNumericUpDown();
            label5 = new Label();
            label4 = new Label();
            label2 = new Label();
            label1 = new Label();
            numericOffset = new DarkNumericUpDown();
            label11 = new Label();
            numericPeriods = new DarkNumericUpDown();
            label3 = new Label();
            irPlotView = new OxyPlot.WindowsForms.PlotView();
            (numericDbRange).BeginInit();
            (numericCaptureTime).BeginInit();
            (numericWindow).BeginInit();
            (numericSampleRate).BeginInit();
            (numericRightWindow).BeginInit();
            (numericLeftWindow).BeginInit();
            (numericOffset).BeginInit();
            (numericPeriods).BeginInit();
            SuspendLayout();
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.ForeColor = SystemColors.ControlLight;
            label9.Location = new Point(12, 160);
            label9.Name = "label9";
            label9.Size = new Size(161, 15);
            label9.TabIndex = 43;
            label9.Text = "Analysis bandwidth (octaves)";
            // 
            // comboSmoothingInverseOctaves
            // 
            comboSmoothingInverseOctaves.BackColor = Color.FromArgb(55, 60, 72);
            comboSmoothingInverseOctaves.ForeColor = Color.White;
            comboSmoothingInverseOctaves.Location = new Point(193, 159);
            comboSmoothingInverseOctaves.Margin = new Padding(0);
            comboSmoothingInverseOctaves.MinimumSize = new Size(36, 19);
            comboSmoothingInverseOctaves.Name = "comboSmoothingInverseOctaves";
            comboSmoothingInverseOctaves.Size = new Size(60, 23);
            comboSmoothingInverseOctaves.TabIndex = 42;
            // 
            // numericDbRange
            // 
            numericDbRange.BackColor = Color.FromArgb(55, 60, 72);
            numericDbRange.DecimalPlaces = 0;
            numericDbRange.ForeColor = Color.White;
            numericDbRange.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            numericDbRange.Location = new Point(193, 136);
            numericDbRange.Maximum = new decimal(new int[] { 10, 0, 0, int.MinValue });
            numericDbRange.Minimum = new decimal(new int[] { 140, 0, 0, int.MinValue });
            numericDbRange.MinimumSize = new Size(36, 19);
            numericDbRange.Name = "numericDbRange";
            numericDbRange.Size = new Size(60, 19);
            numericDbRange.TabIndex = 41;
            numericDbRange.TextAlign = HorizontalAlignment.Right;
            numericDbRange.ThousandsSeparator = false;
            numericDbRange.Value = new decimal(new int[] { 60, 0, 0, int.MinValue });
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.ForeColor = SystemColors.ControlLight;
            label8.Location = new Point(12, 135);
            label8.Name = "label8";
            label8.Size = new Size(57, 15);
            label8.TabIndex = 40;
            label8.Text = "dB Range";
            // 
            // numericCaptureTime
            // 
            numericCaptureTime.BackColor = Color.FromArgb(55, 60, 72);
            numericCaptureTime.DecimalPlaces = 2;
            numericCaptureTime.Enabled = false;
            numericCaptureTime.ForeColor = Color.White;
            numericCaptureTime.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericCaptureTime.Location = new Point(193, 61);
            numericCaptureTime.Maximum = new decimal(new int[] { 999999999, 0, 0, 0 });
            numericCaptureTime.Minimum = new decimal(new int[] { 999999999, 0, 0, int.MinValue });
            numericCaptureTime.MinimumSize = new Size(36, 19);
            numericCaptureTime.Name = "numericCaptureTime";
            numericCaptureTime.Size = new Size(60, 19);
            numericCaptureTime.TabIndex = 39;
            numericCaptureTime.TextAlign = HorizontalAlignment.Right;
            numericCaptureTime.ThousandsSeparator = false;
            numericCaptureTime.Value = new decimal(new int[] { 4, 0, 0, 0 });
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.ForeColor = SystemColors.ControlLight;
            label7.Location = new Point(12, 60);
            label7.Name = "label7";
            label7.Size = new Size(106, 15);
            label7.TabIndex = 38;
            label7.Text = "Capture Time (ms)";
            // 
            // numericWindow
            // 
            numericWindow.BackColor = Color.FromArgb(55, 60, 72);
            numericWindow.DecimalPlaces = 0;
            numericWindow.ForeColor = Color.White;
            numericWindow.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericWindow.Location = new Point(193, 36);
            numericWindow.Maximum = new decimal(new int[] { 32768, 0, 0, 0 });
            numericWindow.Minimum = new decimal(new int[] { 32, 0, 0, 0 });
            numericWindow.MinimumSize = new Size(36, 19);
            numericWindow.Name = "numericWindow";
            numericWindow.Size = new Size(60, 19);
            numericWindow.TabIndex = 37;
            numericWindow.TextAlign = HorizontalAlignment.Right;
            numericWindow.ThousandsSeparator = false;
            numericWindow.Value = new decimal(new int[] { 4096, 0, 0, 0 });
            numericWindow.ValueChanged += numericWindow_ValueChanged;
            // 
            // numericSampleRate
            // 
            numericSampleRate.BackColor = Color.FromArgb(55, 60, 72);
            numericSampleRate.DecimalPlaces = 0;
            numericSampleRate.Enabled = false;
            numericSampleRate.ForeColor = Color.White;
            numericSampleRate.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericSampleRate.Location = new Point(193, 11);
            numericSampleRate.Maximum = new decimal(new int[] { 192000, 0, 0, 0 });
            numericSampleRate.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericSampleRate.MinimumSize = new Size(36, 19);
            numericSampleRate.Name = "numericSampleRate";
            numericSampleRate.ReadOnly = true;
            numericSampleRate.Size = new Size(60, 19);
            numericSampleRate.TabIndex = 36;
            numericSampleRate.TextAlign = HorizontalAlignment.Right;
            numericSampleRate.ThousandsSeparator = false;
            numericSampleRate.Value = new decimal(new int[] { 44100, 0, 0, 0 });
            // 
            // button1
            // 
            button1.BackColor = Color.FromArgb(50, 55, 80);
            button1.DialogResult = DialogResult.OK;
            button1.FlatStyle = FlatStyle.Popup;
            button1.ForeColor = Color.White;
            button1.Location = new Point(12, 542);
            button1.Name = "button1";
            button1.Size = new Size(240, 23);
            button1.TabIndex = 34;
            button1.Text = "Apply settings";
            button1.UseVisualStyleBackColor = false;
            // 
            // numericRightWindow
            // 
            numericRightWindow.BackColor = Color.FromArgb(55, 60, 72);
            numericRightWindow.DecimalPlaces = 0;
            numericRightWindow.ForeColor = Color.White;
            numericRightWindow.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericRightWindow.Location = new Point(193, 111);
            numericRightWindow.Maximum = new decimal(new int[] { 16384, 0, 0, 0 });
            numericRightWindow.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericRightWindow.MinimumSize = new Size(36, 19);
            numericRightWindow.Name = "numericRightWindow";
            numericRightWindow.Size = new Size(60, 19);
            numericRightWindow.TabIndex = 33;
            numericRightWindow.TextAlign = HorizontalAlignment.Right;
            numericRightWindow.ThousandsSeparator = false;
            numericRightWindow.Value = new decimal(new int[] { 512, 0, 0, 0 });
            // 
            // numericLeftWindow
            // 
            numericLeftWindow.BackColor = Color.FromArgb(55, 60, 72);
            numericLeftWindow.DecimalPlaces = 0;
            numericLeftWindow.ForeColor = Color.White;
            numericLeftWindow.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericLeftWindow.Location = new Point(193, 86);
            numericLeftWindow.Maximum = new decimal(new int[] { 16384, 0, 0, 0 });
            numericLeftWindow.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericLeftWindow.MinimumSize = new Size(36, 19);
            numericLeftWindow.Name = "numericLeftWindow";
            numericLeftWindow.Size = new Size(60, 19);
            numericLeftWindow.TabIndex = 32;
            numericLeftWindow.TextAlign = HorizontalAlignment.Right;
            numericLeftWindow.ThousandsSeparator = false;
            numericLeftWindow.Value = new decimal(new int[] { 8, 0, 0, 0 });
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.ForeColor = SystemColors.ControlLight;
            label5.Location = new Point(12, 110);
            label5.Name = "label5";
            label5.Size = new Size(117, 15);
            label5.TabIndex = 31;
            label5.Text = "Tukey Window Right";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.ForeColor = SystemColors.ControlLight;
            label4.Location = new Point(12, 85);
            label4.Name = "label4";
            label4.Size = new Size(109, 15);
            label4.TabIndex = 30;
            label4.Text = "Tukey Window Left";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.ForeColor = SystemColors.ControlLight;
            label2.Location = new Point(12, 35);
            label2.Name = "label2";
            label2.Size = new Size(98, 15);
            label2.TabIndex = 29;
            label2.Text = "Window Samples";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.ForeColor = SystemColors.ControlLight;
            label1.Location = new Point(12, 10);
            label1.Name = "label1";
            label1.Size = new Size(72, 15);
            label1.TabIndex = 28;
            label1.Text = "Sample Rate";
            // 
            // numericOffset
            // 
            numericOffset.BackColor = Color.FromArgb(55, 60, 72);
            numericOffset.DecimalPlaces = 0;
            numericOffset.ForeColor = Color.White;
            numericOffset.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericOffset.Location = new Point(193, 186);
            numericOffset.Maximum = new decimal(new int[] { 32768, 0, 0, 0 });
            numericOffset.Minimum = new decimal(new int[] { 32768, 0, 0, int.MinValue });
            numericOffset.MinimumSize = new Size(36, 19);
            numericOffset.Name = "numericOffset";
            numericOffset.Size = new Size(60, 19);
            numericOffset.TabIndex = 46;
            numericOffset.TextAlign = HorizontalAlignment.Right;
            numericOffset.ThousandsSeparator = false;
            numericOffset.Value = new decimal(new int[] { 0, 0, 0, 0 });
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.ForeColor = SystemColors.ControlLight;
            label11.Location = new Point(12, 185);
            label11.Name = "label11";
            label11.Size = new Size(39, 15);
            label11.TabIndex = 45;
            label11.Text = "Offset";
            // 
            // numericPeriods
            // 
            numericPeriods.BackColor = Color.FromArgb(55, 60, 72);
            numericPeriods.DecimalPlaces = 0;
            numericPeriods.ForeColor = Color.White;
            numericPeriods.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            numericPeriods.Location = new Point(192, 211);
            numericPeriods.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            numericPeriods.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericPeriods.MinimumSize = new Size(36, 19);
            numericPeriods.Name = "numericPeriods";
            numericPeriods.Size = new Size(60, 19);
            numericPeriods.TabIndex = 48;
            numericPeriods.TextAlign = HorizontalAlignment.Right;
            numericPeriods.ThousandsSeparator = false;
            numericPeriods.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.ForeColor = SystemColors.ControlLight;
            label3.Location = new Point(12, 210);
            label3.Name = "label3";
            label3.Size = new Size(46, 15);
            label3.TabIndex = 47;
            label3.Text = "Periods";
            // 
            // irPlotView
            // 
            irPlotView.BackColor = Color.FromArgb(32, 36, 46);
            irPlotView.Location = new Point(12, 236);
            irPlotView.Name = "irPlotView";
            irPlotView.PanCursor = Cursors.Hand;
            irPlotView.Size = new Size(241, 300);
            irPlotView.TabIndex = 49;
            irPlotView.Text = "plotView1";
            irPlotView.ZoomHorizontalCursor = Cursors.SizeWE;
            irPlotView.ZoomRectangleCursor = Cursors.SizeNWSE;
            irPlotView.ZoomVerticalCursor = Cursors.SizeNS;
            // 
            // BDOpt
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
            ClientSize = new Size(264, 576);
            Controls.Add(irPlotView);
            Controls.Add(numericPeriods);
            Controls.Add(label3);
            Controls.Add(numericOffset);
            Controls.Add(label11);
            Controls.Add(label9);
            Controls.Add(comboSmoothingInverseOctaves);
            Controls.Add(numericDbRange);
            Controls.Add(label8);
            Controls.Add(numericCaptureTime);
            Controls.Add(label7);
            Controls.Add(numericWindow);
            Controls.Add(numericSampleRate);
            Controls.Add(button1);
            Controls.Add(numericRightWindow);
            Controls.Add(numericLeftWindow);
            Controls.Add(label5);
            Controls.Add(label4);
            Controls.Add(label2);
            Controls.Add(label1);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "BDOpt";
            ShowInTaskbar = false;
            Text = "Burst Decay Options";
            (numericDbRange).EndInit();
            (numericCaptureTime).EndInit();
            (numericWindow).EndInit();
            (numericSampleRate).EndInit();
            (numericRightWindow).EndInit();
            (numericLeftWindow).EndInit();
            (numericOffset).EndInit();
            (numericPeriods).EndInit();
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion

        private Label label9;
        private DarkComboBox comboSmoothingInverseOctaves;
        private DarkNumericUpDown numericDbRange;
        private Label label8;
        private DarkNumericUpDown numericCaptureTime;
        private Label label7;
        private DarkNumericUpDown numericWindow;
        private DarkNumericUpDown numericSampleRate;
        private Button button1;
        private DarkNumericUpDown numericRightWindow;
        private DarkNumericUpDown numericLeftWindow;
        private Label label5;
        private Label label4;
        private Label label2;
        private Label label1;
        private DarkNumericUpDown numericOffset;
        private Label label11;
        private DarkNumericUpDown numericPeriods;
        private Label label3;
        private OxyPlot.WindowsForms.PlotView irPlotView;
    }
}
