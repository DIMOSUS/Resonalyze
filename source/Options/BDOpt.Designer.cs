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
            label10 = new Label();
            label9 = new Label();
            numericSmoothingInverseOctaves = new NumericUpDown();
            numericDbRange = new NumericUpDown();
            label8 = new Label();
            numericCaptureTime = new NumericUpDown();
            label7 = new Label();
            numericWindow = new NumericUpDown();
            numericSampleRate = new NumericUpDown();
            button2 = new Button();
            button1 = new Button();
            numericRightWindow = new NumericUpDown();
            numericLeftWindow = new NumericUpDown();
            label5 = new Label();
            label4 = new Label();
            label2 = new Label();
            label1 = new Label();
            numericOffset = new NumericUpDown();
            label11 = new Label();
            numericPeriods = new NumericUpDown();
            label3 = new Label();
            ((System.ComponentModel.ISupportInitialize)numericSmoothingInverseOctaves).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericDbRange).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericCaptureTime).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericWindow).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericSampleRate).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericRightWindow).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericLeftWindow).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericOffset).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericPeriods).BeginInit();
            SuspendLayout();
            // 
            // label10
            // 
            label10.AutoSize = true;
            label10.ForeColor = SystemColors.ControlLight;
            label10.Location = new Point(193, 162);
            label10.Name = "label10";
            label10.Size = new Size(21, 15);
            label10.TabIndex = 44;
            label10.Text = "1 /";
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.ForeColor = SystemColors.ControlLight;
            label9.Location = new Point(12, 160);
            label9.Name = "label9";
            label9.Size = new Size(117, 15);
            label9.TabIndex = 43;
            label9.Text = "Smoothing (octaves)";
            // 
            // numericSmoothingInverseOctaves
            // 
            numericSmoothingInverseOctaves.BorderStyle = BorderStyle.None;
            numericSmoothingInverseOctaves.Location = new Point(214, 161);
            numericSmoothingInverseOctaves.Maximum = new decimal(new int[] { 9, 0, 0, 0 });
            numericSmoothingInverseOctaves.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
            numericSmoothingInverseOctaves.Name = "numericSmoothingInverseOctaves";
            numericSmoothingInverseOctaves.Size = new Size(39, 19);
            numericSmoothingInverseOctaves.TabIndex = 42;
            numericSmoothingInverseOctaves.TextAlign = HorizontalAlignment.Right;
            numericSmoothingInverseOctaves.Value = new decimal(new int[] { 2, 0, 0, 0 });
            // 
            // numericDbRange
            // 
            numericDbRange.BorderStyle = BorderStyle.None;
            numericDbRange.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            numericDbRange.Location = new Point(193, 136);
            numericDbRange.Maximum = new decimal(new int[] { 10, 0, 0, int.MinValue });
            numericDbRange.Minimum = new decimal(new int[] { 140, 0, 0, int.MinValue });
            numericDbRange.Name = "numericDbRange";
            numericDbRange.Size = new Size(60, 19);
            numericDbRange.TabIndex = 41;
            numericDbRange.TextAlign = HorizontalAlignment.Right;
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
            numericCaptureTime.BorderStyle = BorderStyle.None;
            numericCaptureTime.DecimalPlaces = 2;
            numericCaptureTime.Enabled = false;
            numericCaptureTime.Location = new Point(193, 61);
            numericCaptureTime.Maximum = new decimal(new int[] { 999999999, 0, 0, 0 });
            numericCaptureTime.Minimum = new decimal(new int[] { 999999999, 0, 0, int.MinValue });
            numericCaptureTime.Name = "numericCaptureTime";
            numericCaptureTime.Size = new Size(60, 19);
            numericCaptureTime.TabIndex = 39;
            numericCaptureTime.TextAlign = HorizontalAlignment.Right;
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
            numericWindow.BorderStyle = BorderStyle.None;
            numericWindow.Location = new Point(193, 36);
            numericWindow.Maximum = new decimal(new int[] { 32768, 0, 0, 0 });
            numericWindow.Minimum = new decimal(new int[] { 32, 0, 0, 0 });
            numericWindow.Name = "numericWindow";
            numericWindow.Size = new Size(60, 19);
            numericWindow.TabIndex = 37;
            numericWindow.TextAlign = HorizontalAlignment.Right;
            numericWindow.Value = new decimal(new int[] { 4096, 0, 0, 0 });
            numericWindow.ValueChanged += numericWindow_ValueChanged;
            // 
            // numericSampleRate
            // 
            numericSampleRate.BorderStyle = BorderStyle.None;
            numericSampleRate.Enabled = false;
            numericSampleRate.Location = new Point(193, 11);
            numericSampleRate.Maximum = new decimal(new int[] { 192000, 0, 0, 0 });
            numericSampleRate.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericSampleRate.Name = "numericSampleRate";
            numericSampleRate.ReadOnly = true;
            numericSampleRate.Size = new Size(60, 19);
            numericSampleRate.TabIndex = 36;
            numericSampleRate.TextAlign = HorizontalAlignment.Right;
            numericSampleRate.Value = new decimal(new int[] { 44100, 0, 0, 0 });
            // 
            // button2
            // 
            button2.DialogResult = DialogResult.Cancel;
            button2.Location = new Point(153, 256);
            button2.Name = "button2";
            button2.Size = new Size(100, 23);
            button2.TabIndex = 35;
            button2.Text = "Cancel";
            button2.UseVisualStyleBackColor = true;
            // 
            // button1
            // 
            button1.DialogResult = DialogResult.OK;
            button1.Location = new Point(12, 256);
            button1.Name = "button1";
            button1.Size = new Size(100, 23);
            button1.TabIndex = 34;
            button1.Text = "Ok";
            button1.UseVisualStyleBackColor = true;
            // 
            // numericRightWindow
            // 
            numericRightWindow.BorderStyle = BorderStyle.None;
            numericRightWindow.Location = new Point(193, 111);
            numericRightWindow.Maximum = new decimal(new int[] { 16384, 0, 0, 0 });
            numericRightWindow.Name = "numericRightWindow";
            numericRightWindow.Size = new Size(60, 19);
            numericRightWindow.TabIndex = 33;
            numericRightWindow.TextAlign = HorizontalAlignment.Right;
            numericRightWindow.Value = new decimal(new int[] { 512, 0, 0, 0 });
            // 
            // numericLeftWindow
            // 
            numericLeftWindow.BorderStyle = BorderStyle.None;
            numericLeftWindow.Location = new Point(193, 86);
            numericLeftWindow.Maximum = new decimal(new int[] { 16384, 0, 0, 0 });
            numericLeftWindow.Name = "numericLeftWindow";
            numericLeftWindow.Size = new Size(60, 19);
            numericLeftWindow.TabIndex = 32;
            numericLeftWindow.TextAlign = HorizontalAlignment.Right;
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
            numericOffset.BorderStyle = BorderStyle.None;
            numericOffset.Location = new Point(193, 186);
            numericOffset.Maximum = new decimal(new int[] { 32768, 0, 0, 0 });
            numericOffset.Minimum = new decimal(new int[] { 32768, 0, 0, int.MinValue });
            numericOffset.Name = "numericOffset";
            numericOffset.Size = new Size(60, 19);
            numericOffset.TabIndex = 46;
            numericOffset.TextAlign = HorizontalAlignment.Right;
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
            numericPeriods.BorderStyle = BorderStyle.None;
            numericPeriods.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            numericPeriods.Location = new Point(192, 211);
            numericPeriods.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            numericPeriods.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericPeriods.Name = "numericPeriods";
            numericPeriods.Size = new Size(60, 19);
            numericPeriods.TabIndex = 48;
            numericPeriods.TextAlign = HorizontalAlignment.Right;
            numericPeriods.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.ForeColor = SystemColors.ControlLight;
            label3.Location = new Point(11, 210);
            label3.Name = "label3";
            label3.Size = new Size(46, 15);
            label3.TabIndex = 47;
            label3.Text = "Periods";
            // 
            // BDOpt
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
            ClientSize = new Size(264, 291);
            Controls.Add(numericPeriods);
            Controls.Add(label3);
            Controls.Add(numericOffset);
            Controls.Add(label11);
            Controls.Add(label10);
            Controls.Add(label9);
            Controls.Add(numericSmoothingInverseOctaves);
            Controls.Add(numericDbRange);
            Controls.Add(label8);
            Controls.Add(numericCaptureTime);
            Controls.Add(label7);
            Controls.Add(numericWindow);
            Controls.Add(numericSampleRate);
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
            Name = "BDOpt";
            ShowInTaskbar = false;
            Text = "Burst Decay Options";
            ((System.ComponentModel.ISupportInitialize)numericSmoothingInverseOctaves).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericDbRange).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericCaptureTime).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericWindow).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericSampleRate).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericRightWindow).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericLeftWindow).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericOffset).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericPeriods).EndInit();
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion

        private Label label10;
        private Label label9;
        private NumericUpDown numericSmoothingInverseOctaves;
        private NumericUpDown numericDbRange;
        private Label label8;
        private NumericUpDown numericCaptureTime;
        private Label label7;
        private NumericUpDown numericWindow;
        private NumericUpDown numericSampleRate;
        private Button button2;
        private Button button1;
        private NumericUpDown numericRightWindow;
        private NumericUpDown numericLeftWindow;
        private Label label5;
        private Label label4;
        private Label label2;
        private Label label1;
        private NumericUpDown numericOffset;
        private Label label11;
        private NumericUpDown numericPeriods;
        private Label label3;
    }
}
