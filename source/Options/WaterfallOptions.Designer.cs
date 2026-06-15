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
            numericLeftWindow = new NumericUpDown();
            numericRightWindow = new NumericUpDown();
            button2 = new Button();
            button1 = new Button();
            label6 = new Label();
            numericSampleRate = new NumericUpDown();
            numericWindow = new NumericUpDown();
            numericSlices = new NumericUpDown();
            numericStep = new NumericUpDown();
            label3 = new Label();
            label7 = new Label();
            numericCaptureTime = new NumericUpDown();
            label8 = new Label();
            numericDbRange = new NumericUpDown();
            numericSmoothingInverseOctaves = new NumericUpDown();
            label9 = new Label();
            label10 = new Label();
            label11 = new Label();
            numericOffset = new NumericUpDown();
            ((System.ComponentModel.ISupportInitialize)numericLeftWindow).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericRightWindow).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericSampleRate).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericWindow).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericSlices).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericStep).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericCaptureTime).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericDbRange).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericSmoothingInverseOctaves).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericOffset).BeginInit();
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
            numericLeftWindow.BorderStyle = BorderStyle.None;
            numericLeftWindow.Location = new Point(193, 162);
            numericLeftWindow.Maximum = new decimal(new int[] { 16384, 0, 0, 0 });
            numericLeftWindow.Name = "numericLeftWindow";
            numericLeftWindow.Size = new Size(60, 19);
            numericLeftWindow.TabIndex = 10;
            numericLeftWindow.TextAlign = HorizontalAlignment.Right;
            numericLeftWindow.Value = new decimal(new int[] { 8, 0, 0, 0 });
            // 
            // numericRightWindow
            // 
            numericRightWindow.BorderStyle = BorderStyle.None;
            numericRightWindow.Location = new Point(193, 187);
            numericRightWindow.Maximum = new decimal(new int[] { 16384, 0, 0, 0 });
            numericRightWindow.Name = "numericRightWindow";
            numericRightWindow.Size = new Size(60, 19);
            numericRightWindow.TabIndex = 11;
            numericRightWindow.TextAlign = HorizontalAlignment.Right;
            numericRightWindow.Value = new decimal(new int[] { 512, 0, 0, 0 });
            // 
            // button2
            // 
            button2.DialogResult = DialogResult.Cancel;
            button2.Location = new Point(153, 283);
            button2.Name = "button2";
            button2.Size = new Size(100, 23);
            button2.TabIndex = 13;
            button2.Text = "Cancel";
            button2.UseVisualStyleBackColor = true;
            // 
            // button1
            // 
            button1.DialogResult = DialogResult.OK;
            button1.Location = new Point(12, 283);
            button1.Name = "button1";
            button1.Size = new Size(100, 23);
            button1.TabIndex = 12;
            button1.Text = "Ok";
            button1.UseVisualStyleBackColor = true;
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
            numericSampleRate.BorderStyle = BorderStyle.None;
            numericSampleRate.Enabled = false;
            numericSampleRate.Location = new Point(193, 12);
            numericSampleRate.Maximum = new decimal(new int[] { 192000, 0, 0, 0 });
            numericSampleRate.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericSampleRate.Name = "numericSampleRate";
            numericSampleRate.ReadOnly = true;
            numericSampleRate.Size = new Size(60, 19);
            numericSampleRate.TabIndex = 16;
            numericSampleRate.TextAlign = HorizontalAlignment.Right;
            numericSampleRate.Value = new decimal(new int[] { 44100, 0, 0, 0 });
            // 
            // numericWindow
            // 
            numericWindow.BorderStyle = BorderStyle.None;
            numericWindow.Location = new Point(193, 37);
            numericWindow.Maximum = new decimal(new int[] { 32768, 0, 0, 0 });
            numericWindow.Minimum = new decimal(new int[] { 32, 0, 0, 0 });
            numericWindow.Name = "numericWindow";
            numericWindow.Size = new Size(60, 19);
            numericWindow.TabIndex = 17;
            numericWindow.TextAlign = HorizontalAlignment.Right;
            numericWindow.Value = new decimal(new int[] { 4096, 0, 0, 0 });
            numericWindow.ValueChanged += numericWindow_ValueChanged;
            // 
            // numericSlices
            // 
            numericSlices.BorderStyle = BorderStyle.None;
            numericSlices.Location = new Point(193, 62);
            numericSlices.Maximum = new decimal(new int[] { 512, 0, 0, 0 });
            numericSlices.Minimum = new decimal(new int[] { 4, 0, 0, 0 });
            numericSlices.Name = "numericSlices";
            numericSlices.Size = new Size(60, 19);
            numericSlices.TabIndex = 18;
            numericSlices.TextAlign = HorizontalAlignment.Right;
            numericSlices.Value = new decimal(new int[] { 64, 0, 0, 0 });
            numericSlices.ValueChanged += numericSlices_ValueChanged;
            // 
            // numericStep
            // 
            numericStep.BorderStyle = BorderStyle.None;
            numericStep.Location = new Point(193, 87);
            numericStep.Maximum = new decimal(new int[] { 512, 0, 0, 0 });
            numericStep.Minimum = new decimal(new int[] { 512, 0, 0, int.MinValue });
            numericStep.Name = "numericStep";
            numericStep.Size = new Size(60, 19);
            numericStep.TabIndex = 19;
            numericStep.TextAlign = HorizontalAlignment.Right;
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
            numericCaptureTime.BorderStyle = BorderStyle.None;
            numericCaptureTime.DecimalPlaces = 2;
            numericCaptureTime.Enabled = false;
            numericCaptureTime.Location = new Point(193, 137);
            numericCaptureTime.Maximum = new decimal(new int[] { 999999999, 0, 0, 0 });
            numericCaptureTime.Minimum = new decimal(new int[] { 999999999, 0, 0, int.MinValue });
            numericCaptureTime.Name = "numericCaptureTime";
            numericCaptureTime.Size = new Size(60, 19);
            numericCaptureTime.TabIndex = 22;
            numericCaptureTime.TextAlign = HorizontalAlignment.Right;
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
            numericDbRange.BorderStyle = BorderStyle.None;
            numericDbRange.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            numericDbRange.Location = new Point(193, 212);
            numericDbRange.Maximum = new decimal(new int[] { 10, 0, 0, int.MinValue });
            numericDbRange.Minimum = new decimal(new int[] { 140, 0, 0, int.MinValue });
            numericDbRange.Name = "numericDbRange";
            numericDbRange.Size = new Size(60, 19);
            numericDbRange.TabIndex = 24;
            numericDbRange.TextAlign = HorizontalAlignment.Right;
            numericDbRange.Value = new decimal(new int[] { 60, 0, 0, int.MinValue });
            // 
            // numericSmoothingInverseOctaves
            // 
            numericSmoothingInverseOctaves.BorderStyle = BorderStyle.None;
            numericSmoothingInverseOctaves.Location = new Point(214, 237);
            numericSmoothingInverseOctaves.Maximum = new decimal(new int[] { 48, 0, 0, 0 });
            numericSmoothingInverseOctaves.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericSmoothingInverseOctaves.Name = "numericSmoothingInverseOctaves";
            numericSmoothingInverseOctaves.Size = new Size(39, 19);
            numericSmoothingInverseOctaves.TabIndex = 25;
            numericSmoothingInverseOctaves.TextAlign = HorizontalAlignment.Right;
            numericSmoothingInverseOctaves.Value = new decimal(new int[] { 1, 0, 0, 0 });
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
            // label10
            // 
            label10.AutoSize = true;
            label10.ForeColor = SystemColors.ControlLight;
            label10.Location = new Point(193, 238);
            label10.Name = "label10";
            label10.Size = new Size(21, 15);
            label10.TabIndex = 27;
            label10.Text = "1 /";
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
            numericOffset.BorderStyle = BorderStyle.None;
            numericOffset.Location = new Point(193, 112);
            numericOffset.Maximum = new decimal(new int[] { 32768, 0, 0, 0 });
            numericOffset.Minimum = new decimal(new int[] { 32768, 0, 0, int.MinValue });
            numericOffset.Name = "numericOffset";
            numericOffset.Size = new Size(60, 19);
            numericOffset.TabIndex = 29;
            numericOffset.TextAlign = HorizontalAlignment.Right;
            // 
            // WaterfallOptions
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(50, 50, 50);
            ClientSize = new Size(264, 315);
            Controls.Add(numericOffset);
            Controls.Add(label11);
            Controls.Add(label10);
            Controls.Add(label9);
            Controls.Add(numericSmoothingInverseOctaves);
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
            Controls.Add(button2);
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
            ((System.ComponentModel.ISupportInitialize)numericLeftWindow).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericRightWindow).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericSampleRate).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericWindow).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericSlices).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericStep).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericCaptureTime).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericDbRange).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericSmoothingInverseOctaves).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericOffset).EndInit();
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion
        private Label label1;
        private Label label2;
        private Label label4;
        private Label label5;
        private NumericUpDown numericLeftWindow;
        private NumericUpDown numericRightWindow;
        private Button button2;
        private Button button1;
        private Label label6;
        private NumericUpDown numericSampleRate;
        private NumericUpDown numericWindow;
        private NumericUpDown numericSlices;
        private NumericUpDown numericStep;
        private Label label3;
        private Label label7;
        private NumericUpDown numericCaptureTime;
        private Label label8;
        private NumericUpDown numericDbRange;
        private NumericUpDown numericSmoothingInverseOctaves;
        private Label label9;
        private Label label10;
        private Label label11;
        private NumericUpDown numericOffset;
    }
}
