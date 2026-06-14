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
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.numericLeftWindow = new System.Windows.Forms.NumericUpDown();
            this.numericRightWindow = new System.Windows.Forms.NumericUpDown();
            this.button2 = new System.Windows.Forms.Button();
            this.button1 = new System.Windows.Forms.Button();
            this.label6 = new System.Windows.Forms.Label();
            this.numericSampleRate = new System.Windows.Forms.NumericUpDown();
            this.numericWindow = new System.Windows.Forms.NumericUpDown();
            this.numericSlices = new System.Windows.Forms.NumericUpDown();
            this.numericStep = new System.Windows.Forms.NumericUpDown();
            this.label3 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.numericCaptureTime = new System.Windows.Forms.NumericUpDown();
            this.label8 = new System.Windows.Forms.Label();
            this.numericDbRange = new System.Windows.Forms.NumericUpDown();
            this.numericSmoothingInverseOctaves = new System.Windows.Forms.NumericUpDown();
            this.label9 = new System.Windows.Forms.Label();
            this.label10 = new System.Windows.Forms.Label();
            this.label11 = new System.Windows.Forms.Label();
            this.numericOffset = new System.Windows.Forms.NumericUpDown();
            ((System.ComponentModel.ISupportInitialize)(this.numericLeftWindow)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericRightWindow)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericSampleRate)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericWindow)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericSlices)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericStep)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericCaptureTime)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericDbRange)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericSmoothingInverseOctaves)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericOffset)).BeginInit();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label1.Location = new System.Drawing.Point(12, 11);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(72, 15);
            this.label1.TabIndex = 1;
            this.label1.Text = "Sample Rate";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label2.Location = new System.Drawing.Point(12, 36);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(98, 15);
            this.label2.TabIndex = 3;
            this.label2.Text = "Window Samples";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label4.Location = new System.Drawing.Point(12, 161);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(108, 15);
            this.label4.TabIndex = 7;
            this.label4.Text = "Tukey Window Left";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label5.Location = new System.Drawing.Point(12, 186);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(116, 15);
            this.label5.TabIndex = 9;
            this.label5.Text = "Tukey Window Right";
            // 
            // numericLeftWindow
            // 
            this.numericLeftWindow.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericLeftWindow.Location = new System.Drawing.Point(193, 162);
            this.numericLeftWindow.Maximum = new decimal(new int[] {
            16384,
            0,
            0,
            0});
            this.numericLeftWindow.Name = "numericLeftWindow";
            this.numericLeftWindow.Size = new System.Drawing.Size(60, 19);
            this.numericLeftWindow.TabIndex = 10;
            this.numericLeftWindow.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericLeftWindow.Value = new decimal(new int[] {
            8,
            0,
            0,
            0});
            // 
            // numericRightWindow
            // 
            this.numericRightWindow.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericRightWindow.Location = new System.Drawing.Point(193, 187);
            this.numericRightWindow.Maximum = new decimal(new int[] {
            16384,
            0,
            0,
            0});
            this.numericRightWindow.Name = "numericRightWindow";
            this.numericRightWindow.Size = new System.Drawing.Size(60, 19);
            this.numericRightWindow.TabIndex = 11;
            this.numericRightWindow.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericRightWindow.Value = new decimal(new int[] {
            512,
            0,
            0,
            0});
            // 
            // button2
            // 
            this.button2.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.button2.Location = new System.Drawing.Point(153, 283);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(100, 23);
            this.button2.TabIndex = 13;
            this.button2.Text = "Cancel";
            this.button2.UseVisualStyleBackColor = true;
            // 
            // button1
            // 
            this.button1.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.button1.Location = new System.Drawing.Point(12, 283);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(100, 23);
            this.button1.TabIndex = 12;
            this.button1.Text = "Ok";
            this.button1.UseVisualStyleBackColor = true;
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label6.Location = new System.Drawing.Point(12, 61);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(36, 15);
            this.label6.TabIndex = 15;
            this.label6.Text = "Slices";
            // 
            // numericSampleRate
            // 
            this.numericSampleRate.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericSampleRate.Enabled = false;
            this.numericSampleRate.Location = new System.Drawing.Point(193, 12);
            this.numericSampleRate.Maximum = new decimal(new int[] {
            192000,
            0,
            0,
            0});
            this.numericSampleRate.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numericSampleRate.Name = "numericSampleRate";
            this.numericSampleRate.ReadOnly = true;
            this.numericSampleRate.Size = new System.Drawing.Size(60, 19);
            this.numericSampleRate.TabIndex = 16;
            this.numericSampleRate.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericSampleRate.Value = new decimal(new int[] {
            44100,
            0,
            0,
            0});
            // 
            // numericWindow
            // 
            this.numericWindow.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericWindow.Location = new System.Drawing.Point(193, 37);
            this.numericWindow.Maximum = new decimal(new int[] {
            32768,
            0,
            0,
            0});
            this.numericWindow.Minimum = new decimal(new int[] {
            32,
            0,
            0,
            0});
            this.numericWindow.Name = "numericWindow";
            this.numericWindow.Size = new System.Drawing.Size(60, 19);
            this.numericWindow.TabIndex = 17;
            this.numericWindow.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericWindow.Value = new decimal(new int[] {
            4096,
            0,
            0,
            0});
            this.numericWindow.ValueChanged += new System.EventHandler(this.numericWindow_ValueChanged);
            // 
            // numericSlices
            // 
            this.numericSlices.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericSlices.Location = new System.Drawing.Point(193, 62);
            this.numericSlices.Maximum = new decimal(new int[] {
            512,
            0,
            0,
            0});
            this.numericSlices.Minimum = new decimal(new int[] {
            4,
            0,
            0,
            0});
            this.numericSlices.Name = "numericSlices";
            this.numericSlices.Size = new System.Drawing.Size(60, 19);
            this.numericSlices.TabIndex = 18;
            this.numericSlices.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericSlices.Value = new decimal(new int[] {
            64,
            0,
            0,
            0});
            this.numericSlices.ValueChanged += new System.EventHandler(this.numericSlices_ValueChanged);
            // 
            // numericStep
            // 
            this.numericStep.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericStep.Location = new System.Drawing.Point(193, 87);
            this.numericStep.Maximum = new decimal(new int[] {
            512,
            0,
            0,
            0});
            this.numericStep.Minimum = new decimal(new int[] {
            512,
            0,
            0,
            -2147483648});
            this.numericStep.Name = "numericStep";
            this.numericStep.Size = new System.Drawing.Size(60, 19);
            this.numericStep.TabIndex = 19;
            this.numericStep.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericStep.Value = new decimal(new int[] {
            4,
            0,
            0,
            0});
            this.numericStep.ValueChanged += new System.EventHandler(this.numericStep_ValueChanged);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label3.Location = new System.Drawing.Point(12, 86);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(30, 15);
            this.label3.TabIndex = 20;
            this.label3.Text = "Step";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label7.Location = new System.Drawing.Point(12, 136);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(105, 15);
            this.label7.TabIndex = 21;
            this.label7.Text = "Capture Time (ms)";
            // 
            // numericCaptureTime
            // 
            this.numericCaptureTime.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericCaptureTime.DecimalPlaces = 2;
            this.numericCaptureTime.Enabled = false;
            this.numericCaptureTime.Location = new System.Drawing.Point(193, 137);
            this.numericCaptureTime.Maximum = new decimal(new int[] {
            999999999,
            0,
            0,
            0});
            this.numericCaptureTime.Minimum = new decimal(new int[] {
            999999999,
            0,
            0,
            -2147483648});
            this.numericCaptureTime.Name = "numericCaptureTime";
            this.numericCaptureTime.Size = new System.Drawing.Size(60, 19);
            this.numericCaptureTime.TabIndex = 22;
            this.numericCaptureTime.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericCaptureTime.Value = new decimal(new int[] {
            4,
            0,
            0,
            0});
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label8.Location = new System.Drawing.Point(12, 211);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(57, 15);
            this.label8.TabIndex = 23;
            this.label8.Text = "dB Range";
            // 
            // numericDbRange
            // 
            this.numericDbRange.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericDbRange.Increment = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.numericDbRange.Location = new System.Drawing.Point(193, 212);
            this.numericDbRange.Maximum = new decimal(new int[] {
            10,
            0,
            0,
            -2147483648});
            this.numericDbRange.Minimum = new decimal(new int[] {
            140,
            0,
            0,
            -2147483648});
            this.numericDbRange.Name = "numericDbRange";
            this.numericDbRange.Size = new System.Drawing.Size(60, 19);
            this.numericDbRange.TabIndex = 24;
            this.numericDbRange.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericDbRange.Value = new decimal(new int[] {
            60,
            0,
            0,
            -2147483648});
            // 
            // numericSmoothingInverseOctaves
            // 
            this.numericSmoothingInverseOctaves.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericSmoothingInverseOctaves.Location = new System.Drawing.Point(214, 237);
            this.numericSmoothingInverseOctaves.Maximum = new decimal(new int[] {
            48,
            0,
            0,
            0});
            this.numericSmoothingInverseOctaves.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numericSmoothingInverseOctaves.Name = "numericSmoothingInverseOctaves";
            this.numericSmoothingInverseOctaves.Size = new System.Drawing.Size(39, 19);
            this.numericSmoothingInverseOctaves.TabIndex = 25;
            this.numericSmoothingInverseOctaves.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericSmoothingInverseOctaves.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label9.Location = new System.Drawing.Point(12, 236);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(93, 15);
            this.label9.TabIndex = 26;
            this.label9.Text = "Smoothing (octaves)";
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label10.Location = new System.Drawing.Point(193, 238);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(21, 15);
            this.label10.TabIndex = 27;
            this.label10.Text = "1 /";
            // 
            // label11
            // 
            this.label11.AutoSize = true;
            this.label11.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label11.Location = new System.Drawing.Point(12, 111);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(39, 15);
            this.label11.TabIndex = 28;
            this.label11.Text = "Offset";
            // 
            // numericOffset
            // 
            this.numericOffset.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericOffset.Location = new System.Drawing.Point(193, 112);
            this.numericOffset.Maximum = new decimal(new int[] {
            32768,
            0,
            0,
            0});
            this.numericOffset.Minimum = new decimal(new int[] {
            32768,
            0,
            0,
            -2147483648});
            this.numericOffset.Name = "numericOffset";
            this.numericOffset.Size = new System.Drawing.Size(60, 19);
            this.numericOffset.TabIndex = 29;
            this.numericOffset.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // WaterfallOptions
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(50)))), ((int)(((byte)(50)))));
            this.ClientSize = new System.Drawing.Size(264, 315);
            this.Controls.Add(this.numericOffset);
            this.Controls.Add(this.label11);
            this.Controls.Add(this.label10);
            this.Controls.Add(this.label9);
            this.Controls.Add(this.numericSmoothingInverseOctaves);
            this.Controls.Add(this.numericDbRange);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.numericCaptureTime);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.numericStep);
            this.Controls.Add(this.numericSlices);
            this.Controls.Add(this.numericWindow);
            this.Controls.Add(this.numericSampleRate);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.numericRightWindow);
            this.Controls.Add(this.numericLeftWindow);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "WaterfallOptions";
            this.ShowInTaskbar = false;
            this.Text = "Waterfall Options";
            ((System.ComponentModel.ISupportInitialize)(this.numericLeftWindow)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericRightWindow)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericSampleRate)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericWindow)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericSlices)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericStep)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericCaptureTime)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericDbRange)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericSmoothingInverseOctaves)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericOffset)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

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
