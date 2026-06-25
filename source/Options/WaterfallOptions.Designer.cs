namespace Resonalyze.Options
{
    partial class WaterfallOptions
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
            label2 = new Label();
            label4 = new Label();
            label5 = new Label();
            numericLeftWindow = new DarkNumericUpDown();
            numericRightWindow = new DarkNumericUpDown();
            button1 = new Button();
            label6 = new Label();
            numericSampleRate = new DarkNumericUpDown();
            numericWindow = new DarkNumericUpDown();
            numericSlices = new DarkNumericUpDown();
            numericStep = new DarkNumericUpDown();
            label3 = new Label();
            label7 = new Label();
            numericCaptureTime = new DarkNumericUpDown();
            label8 = new Label();
            numericDbRange = new DarkNumericUpDown();
            comboSmoothingInverseOctaves = new DarkComboBox();
            label9 = new Label();
            label11 = new Label();
            numericOffset = new DarkNumericUpDown();
            irPlotView = new OxyPlot.WindowsForms.PlotView();
            (numericLeftWindow).BeginInit();
            (numericRightWindow).BeginInit();
            (numericSampleRate).BeginInit();
            (numericWindow).BeginInit();
            (numericSlices).BeginInit();
            (numericStep).BeginInit();
            (numericCaptureTime).BeginInit();
            (numericDbRange).BeginInit();
            (numericOffset).BeginInit();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.ForeColor = SystemColors.ControlLight;
            label1.Location = new Point(12, 11);
            label1.Name = "label1";
            label1.Size = new Size(72, 15);
            label1.TabIndex = 1;
            label1.Text = "Sample Rate";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.ForeColor = SystemColors.ControlLight;
            label2.Location = new Point(12, 36);
            label2.Name = "label2";
            label2.Size = new Size(98, 15);
            label2.TabIndex = 3;
            label2.Text = "Window Samples";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.ForeColor = SystemColors.ControlLight;
            label4.Location = new Point(12, 161);
            label4.Name = "label4";
            label4.Size = new Size(109, 15);
            label4.TabIndex = 7;
            label4.Text = "Tukey Window Left";
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.ForeColor = SystemColors.ControlLight;
            label5.Location = new Point(12, 186);
            label5.Name = "label5";
            label5.Size = new Size(117, 15);
            label5.TabIndex = 9;
            label5.Text = "Tukey Window Right";
            // 
            // numericLeftWindow
            // 
            numericLeftWindow.BackColor = Color.FromArgb(55, 60, 72);
            numericLeftWindow.DecimalPlaces = 0;
            numericLeftWindow.ForeColor = Color.White;
            numericLeftWindow.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericLeftWindow.Location = new Point(193, 162);
            numericLeftWindow.Maximum = new decimal(new int[] { 16384, 0, 0, 0 });
            numericLeftWindow.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericLeftWindow.MinimumSize = new Size(36, 19);
            numericLeftWindow.Name = "numericLeftWindow";
            numericLeftWindow.Size = new Size(60, 19);
            numericLeftWindow.TabIndex = 10;
            numericLeftWindow.TextAlign = HorizontalAlignment.Right;
            numericLeftWindow.ThousandsSeparator = false;
            numericLeftWindow.Value = new decimal(new int[] { 8, 0, 0, 0 });
            // 
            // numericRightWindow
            // 
            numericRightWindow.BackColor = Color.FromArgb(55, 60, 72);
            numericRightWindow.DecimalPlaces = 0;
            numericRightWindow.ForeColor = Color.White;
            numericRightWindow.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericRightWindow.Location = new Point(193, 187);
            numericRightWindow.Maximum = new decimal(new int[] { 16384, 0, 0, 0 });
            numericRightWindow.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericRightWindow.MinimumSize = new Size(36, 19);
            numericRightWindow.Name = "numericRightWindow";
            numericRightWindow.Size = new Size(60, 19);
            numericRightWindow.TabIndex = 11;
            numericRightWindow.TextAlign = HorizontalAlignment.Right;
            numericRightWindow.ThousandsSeparator = false;
            numericRightWindow.Value = new decimal(new int[] { 512, 0, 0, 0 });
            // 
            // button1
            // 
            button1.BackColor = Color.FromArgb(50, 55, 80);
            button1.DialogResult = DialogResult.OK;
            button1.FlatStyle = FlatStyle.Popup;
            button1.ForeColor = Color.White;
            button1.Location = new Point(12, 568);
            button1.Name = "button1";
            button1.Size = new Size(241, 23);
            button1.TabIndex = 12;
            button1.Text = "Apply settings";
            button1.UseVisualStyleBackColor = false;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.ForeColor = SystemColors.ControlLight;
            label6.Location = new Point(12, 61);
            label6.Name = "label6";
            label6.Size = new Size(36, 15);
            label6.TabIndex = 15;
            label6.Text = "Slices";
            // 
            // numericSampleRate
            // 
            numericSampleRate.BackColor = Color.FromArgb(55, 60, 72);
            numericSampleRate.DecimalPlaces = 0;
            numericSampleRate.Enabled = false;
            numericSampleRate.ForeColor = Color.White;
            numericSampleRate.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericSampleRate.Location = new Point(193, 12);
            numericSampleRate.Maximum = new decimal(new int[] { 192000, 0, 0, 0 });
            numericSampleRate.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericSampleRate.MinimumSize = new Size(36, 19);
            numericSampleRate.Name = "numericSampleRate";
            numericSampleRate.ReadOnly = true;
            numericSampleRate.Size = new Size(60, 19);
            numericSampleRate.TabIndex = 16;
            numericSampleRate.TextAlign = HorizontalAlignment.Right;
            numericSampleRate.ThousandsSeparator = false;
            numericSampleRate.Value = new decimal(new int[] { 44100, 0, 0, 0 });
            // 
            // numericWindow
            // 
            numericWindow.BackColor = Color.FromArgb(55, 60, 72);
            numericWindow.DecimalPlaces = 0;
            numericWindow.ForeColor = Color.White;
            numericWindow.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericWindow.Location = new Point(193, 37);
            numericWindow.Maximum = new decimal(new int[] { 32768, 0, 0, 0 });
            numericWindow.Minimum = new decimal(new int[] { 32, 0, 0, 0 });
            numericWindow.MinimumSize = new Size(36, 19);
            numericWindow.Name = "numericWindow";
            numericWindow.Size = new Size(60, 19);
            numericWindow.TabIndex = 17;
            numericWindow.TextAlign = HorizontalAlignment.Right;
            numericWindow.ThousandsSeparator = false;
            numericWindow.Value = new decimal(new int[] { 4096, 0, 0, 0 });
            numericWindow.ValueChanged += numericWindow_ValueChanged;
            // 
            // numericSlices
            // 
            numericSlices.BackColor = Color.FromArgb(55, 60, 72);
            numericSlices.DecimalPlaces = 0;
            numericSlices.ForeColor = Color.White;
            numericSlices.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericSlices.Location = new Point(193, 62);
            numericSlices.Maximum = new decimal(new int[] { 512, 0, 0, 0 });
            numericSlices.Minimum = new decimal(new int[] { 4, 0, 0, 0 });
            numericSlices.MinimumSize = new Size(36, 19);
            numericSlices.Name = "numericSlices";
            numericSlices.Size = new Size(60, 19);
            numericSlices.TabIndex = 18;
            numericSlices.TextAlign = HorizontalAlignment.Right;
            numericSlices.ThousandsSeparator = false;
            numericSlices.Value = new decimal(new int[] { 64, 0, 0, 0 });
            numericSlices.ValueChanged += numericSlices_ValueChanged;
            // 
            // numericStep
            // 
            numericStep.BackColor = Color.FromArgb(55, 60, 72);
            numericStep.DecimalPlaces = 0;
            numericStep.ForeColor = Color.White;
            numericStep.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericStep.Location = new Point(193, 87);
            numericStep.Maximum = new decimal(new int[] { 512, 0, 0, 0 });
            numericStep.Minimum = new decimal(new int[] { 512, 0, 0, int.MinValue });
            numericStep.MinimumSize = new Size(36, 19);
            numericStep.Name = "numericStep";
            numericStep.Size = new Size(60, 19);
            numericStep.TabIndex = 19;
            numericStep.TextAlign = HorizontalAlignment.Right;
            numericStep.ThousandsSeparator = false;
            numericStep.Value = new decimal(new int[] { 4, 0, 0, 0 });
            numericStep.ValueChanged += numericStep_ValueChanged;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.ForeColor = SystemColors.ControlLight;
            label3.Location = new Point(12, 86);
            label3.Name = "label3";
            label3.Size = new Size(30, 15);
            label3.TabIndex = 20;
            label3.Text = "Step";
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.ForeColor = SystemColors.ControlLight;
            label7.Location = new Point(12, 136);
            label7.Name = "label7";
            label7.Size = new Size(106, 15);
            label7.TabIndex = 21;
            label7.Text = "Capture Time (ms)";
            // 
            // numericCaptureTime
            // 
            numericCaptureTime.BackColor = Color.FromArgb(55, 60, 72);
            numericCaptureTime.DecimalPlaces = 2;
            numericCaptureTime.Enabled = false;
            numericCaptureTime.ForeColor = Color.White;
            numericCaptureTime.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericCaptureTime.Location = new Point(193, 137);
            numericCaptureTime.Maximum = new decimal(new int[] { 999999999, 0, 0, 0 });
            numericCaptureTime.Minimum = new decimal(new int[] { 999999999, 0, 0, int.MinValue });
            numericCaptureTime.MinimumSize = new Size(36, 19);
            numericCaptureTime.Name = "numericCaptureTime";
            numericCaptureTime.Size = new Size(60, 19);
            numericCaptureTime.TabIndex = 22;
            numericCaptureTime.TextAlign = HorizontalAlignment.Right;
            numericCaptureTime.ThousandsSeparator = false;
            numericCaptureTime.Value = new decimal(new int[] { 4, 0, 0, 0 });
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.ForeColor = SystemColors.ControlLight;
            label8.Location = new Point(12, 211);
            label8.Name = "label8";
            label8.Size = new Size(57, 15);
            label8.TabIndex = 23;
            label8.Text = "dB Range";
            // 
            // numericDbRange
            // 
            numericDbRange.BackColor = Color.FromArgb(55, 60, 72);
            numericDbRange.DecimalPlaces = 0;
            numericDbRange.ForeColor = Color.White;
            numericDbRange.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            numericDbRange.Location = new Point(193, 212);
            numericDbRange.Maximum = new decimal(new int[] { 10, 0, 0, int.MinValue });
            numericDbRange.Minimum = new decimal(new int[] { 140, 0, 0, int.MinValue });
            numericDbRange.MinimumSize = new Size(36, 19);
            numericDbRange.Name = "numericDbRange";
            numericDbRange.Size = new Size(60, 19);
            numericDbRange.TabIndex = 24;
            numericDbRange.TextAlign = HorizontalAlignment.Right;
            numericDbRange.ThousandsSeparator = false;
            numericDbRange.Value = new decimal(new int[] { 60, 0, 0, int.MinValue });
            // 
            // comboSmoothingInverseOctaves
            // 
            comboSmoothingInverseOctaves.BackColor = Color.FromArgb(55, 60, 72);
            comboSmoothingInverseOctaves.ForeColor = Color.White;
            comboSmoothingInverseOctaves.Location = new Point(193, 234);
            comboSmoothingInverseOctaves.Margin = new Padding(0);
            comboSmoothingInverseOctaves.MinimumSize = new Size(36, 19);
            comboSmoothingInverseOctaves.Name = "comboSmoothingInverseOctaves";
            comboSmoothingInverseOctaves.Size = new Size(60, 23);
            comboSmoothingInverseOctaves.TabIndex = 25;
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.ForeColor = SystemColors.ControlLight;
            label9.Location = new Point(12, 236);
            label9.Name = "label9";
            label9.Size = new Size(117, 15);
            label9.TabIndex = 26;
            label9.Text = "Smoothing (octaves)";
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.ForeColor = SystemColors.ControlLight;
            label11.Location = new Point(12, 111);
            label11.Name = "label11";
            label11.Size = new Size(39, 15);
            label11.TabIndex = 28;
            label11.Text = "Offset";
            // 
            // numericOffset
            // 
            numericOffset.BackColor = Color.FromArgb(55, 60, 72);
            numericOffset.DecimalPlaces = 0;
            numericOffset.ForeColor = Color.White;
            numericOffset.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericOffset.Location = new Point(193, 112);
            numericOffset.Maximum = new decimal(new int[] { 32768, 0, 0, 0 });
            numericOffset.Minimum = new decimal(new int[] { 32768, 0, 0, int.MinValue });
            numericOffset.MinimumSize = new Size(36, 19);
            numericOffset.Name = "numericOffset";
            numericOffset.Size = new Size(60, 19);
            numericOffset.TabIndex = 29;
            numericOffset.TextAlign = HorizontalAlignment.Right;
            numericOffset.ThousandsSeparator = false;
            numericOffset.Value = new decimal(new int[] { 0, 0, 0, 0 });
            // 
            // irPlotView
            // 
            irPlotView.BackColor = Color.FromArgb(32, 36, 46);
            irPlotView.Location = new Point(12, 262);
            irPlotView.Name = "irPlotView";
            irPlotView.PanCursor = Cursors.Hand;
            irPlotView.Size = new Size(241, 300);
            irPlotView.TabIndex = 50;
            irPlotView.Text = "plotView1";
            irPlotView.ZoomHorizontalCursor = Cursors.SizeWE;
            irPlotView.ZoomRectangleCursor = Cursors.SizeNWSE;
            irPlotView.ZoomVerticalCursor = Cursors.SizeNS;
            // 
            // WaterfallOptions
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
            ClientSize = new Size(264, 601);
            Controls.Add(irPlotView);
            Controls.Add(numericOffset);
            Controls.Add(label11);
            Controls.Add(label9);
            Controls.Add(comboSmoothingInverseOctaves);
            Controls.Add(numericDbRange);
            Controls.Add(label8);
            Controls.Add(numericCaptureTime);
            Controls.Add(label7);
            Controls.Add(label3);
            Controls.Add(numericStep);
            Controls.Add(numericSlices);
            Controls.Add(numericWindow);
            Controls.Add(numericSampleRate);
            Controls.Add(label6);
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
            Name = "WaterfallOptions";
            ShowInTaskbar = false;
            Text = "Waterfall Options";
            (numericLeftWindow).EndInit();
            (numericRightWindow).EndInit();
            (numericSampleRate).EndInit();
            (numericWindow).EndInit();
            (numericSlices).EndInit();
            (numericStep).EndInit();
            (numericCaptureTime).EndInit();
            (numericDbRange).EndInit();
            (numericOffset).EndInit();
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion
        private Label label1;
        private Label label2;
        private Label label4;
        private Label label5;
        private DarkNumericUpDown numericLeftWindow;
        private DarkNumericUpDown numericRightWindow;
        private Button button1;
        private Label label6;
        private DarkNumericUpDown numericSampleRate;
        private DarkNumericUpDown numericWindow;
        private DarkNumericUpDown numericSlices;
        private DarkNumericUpDown numericStep;
        private Label label3;
        private Label label7;
        private DarkNumericUpDown numericCaptureTime;
        private Label label8;
        private DarkNumericUpDown numericDbRange;
        private DarkComboBox comboSmoothingInverseOctaves;
        private Label label9;
        private Label label11;
        private DarkNumericUpDown numericOffset;
        private OxyPlot.WindowsForms.PlotView irPlotView;
    }
}
