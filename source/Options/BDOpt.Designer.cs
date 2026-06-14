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
            this.label10 = new System.Windows.Forms.Label();
            this.label9 = new System.Windows.Forms.Label();
            this.numericSmothInvOctaves = new System.Windows.Forms.NumericUpDown();
            this.numericdBRange = new System.Windows.Forms.NumericUpDown();
            this.label8 = new System.Windows.Forms.Label();
            this.numericCaptureTime = new System.Windows.Forms.NumericUpDown();
            this.label7 = new System.Windows.Forms.Label();
            this.numericWindow = new System.Windows.Forms.NumericUpDown();
            this.numericSampleRate = new System.Windows.Forms.NumericUpDown();
            this.button2 = new System.Windows.Forms.Button();
            this.button1 = new System.Windows.Forms.Button();
            this.numericRightWindow = new System.Windows.Forms.NumericUpDown();
            this.numericLeftWindow = new System.Windows.Forms.NumericUpDown();
            this.label5 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.numericOffset = new System.Windows.Forms.NumericUpDown();
            this.label11 = new System.Windows.Forms.Label();
            this.numericPeriods = new System.Windows.Forms.NumericUpDown();
            this.label3 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.numericSmothInvOctaves)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericdBRange)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericCaptureTime)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericWindow)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericSampleRate)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericRightWindow)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericLeftWindow)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericOffset)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericPeriods)).BeginInit();
            this.SuspendLayout();
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label10.Location = new System.Drawing.Point(193, 162);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(21, 15);
            this.label10.TabIndex = 44;
            this.label10.Text = "1 /";
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label9.Location = new System.Drawing.Point(12, 160);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(93, 15);
            this.label9.TabIndex = 43;
            this.label9.Text = "Smoth (octaves)";
            // 
            // numericSmothInvOctaves
            // 
            this.numericSmothInvOctaves.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericSmothInvOctaves.Location = new System.Drawing.Point(214, 161);
            this.numericSmothInvOctaves.Maximum = new decimal(new int[] {
            9,
            0,
            0,
            0});
            this.numericSmothInvOctaves.Minimum = new decimal(new int[] {
            2,
            0,
            0,
            0});
            this.numericSmothInvOctaves.Name = "numericSmothInvOctaves";
            this.numericSmothInvOctaves.Size = new System.Drawing.Size(39, 19);
            this.numericSmothInvOctaves.TabIndex = 42;
            this.numericSmothInvOctaves.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericSmothInvOctaves.Value = new decimal(new int[] {
            2,
            0,
            0,
            0});
            // 
            // numericdBRange
            // 
            this.numericdBRange.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericdBRange.Increment = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.numericdBRange.Location = new System.Drawing.Point(193, 136);
            this.numericdBRange.Maximum = new decimal(new int[] {
            10,
            0,
            0,
            -2147483648});
            this.numericdBRange.Minimum = new decimal(new int[] {
            140,
            0,
            0,
            -2147483648});
            this.numericdBRange.Name = "numericdBRange";
            this.numericdBRange.Size = new System.Drawing.Size(60, 19);
            this.numericdBRange.TabIndex = 41;
            this.numericdBRange.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericdBRange.Value = new decimal(new int[] {
            60,
            0,
            0,
            -2147483648});
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label8.Location = new System.Drawing.Point(12, 135);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(57, 15);
            this.label8.TabIndex = 40;
            this.label8.Text = "dB Range";
            // 
            // numericCaptureTime
            // 
            this.numericCaptureTime.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericCaptureTime.DecimalPlaces = 2;
            this.numericCaptureTime.Enabled = false;
            this.numericCaptureTime.Location = new System.Drawing.Point(193, 61);
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
            this.numericCaptureTime.TabIndex = 39;
            this.numericCaptureTime.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericCaptureTime.Value = new decimal(new int[] {
            4,
            0,
            0,
            0});
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label7.Location = new System.Drawing.Point(12, 60);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(105, 15);
            this.label7.TabIndex = 38;
            this.label7.Text = "Capture Time (ms)";
            // 
            // numericWindow
            // 
            this.numericWindow.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericWindow.Location = new System.Drawing.Point(193, 36);
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
            this.numericWindow.TabIndex = 37;
            this.numericWindow.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericWindow.Value = new decimal(new int[] {
            4096,
            0,
            0,
            0});
            this.numericWindow.ValueChanged += new System.EventHandler(this.numericWindow_ValueChanged);
            // 
            // numericSampleRate
            // 
            this.numericSampleRate.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericSampleRate.Enabled = false;
            this.numericSampleRate.Location = new System.Drawing.Point(193, 11);
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
            this.numericSampleRate.TabIndex = 36;
            this.numericSampleRate.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericSampleRate.Value = new decimal(new int[] {
            44100,
            0,
            0,
            0});
            // 
            // button2
            // 
            this.button2.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.button2.Location = new System.Drawing.Point(153, 256);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(100, 23);
            this.button2.TabIndex = 35;
            this.button2.Text = "Cancel";
            this.button2.UseVisualStyleBackColor = true;
            // 
            // button1
            // 
            this.button1.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.button1.Location = new System.Drawing.Point(12, 256);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(100, 23);
            this.button1.TabIndex = 34;
            this.button1.Text = "Ok";
            this.button1.UseVisualStyleBackColor = true;
            // 
            // numericRightWindow
            // 
            this.numericRightWindow.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericRightWindow.Location = new System.Drawing.Point(193, 111);
            this.numericRightWindow.Maximum = new decimal(new int[] {
            16384,
            0,
            0,
            0});
            this.numericRightWindow.Name = "numericRightWindow";
            this.numericRightWindow.Size = new System.Drawing.Size(60, 19);
            this.numericRightWindow.TabIndex = 33;
            this.numericRightWindow.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericRightWindow.Value = new decimal(new int[] {
            512,
            0,
            0,
            0});
            // 
            // numericLeftWindow
            // 
            this.numericLeftWindow.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericLeftWindow.Location = new System.Drawing.Point(193, 86);
            this.numericLeftWindow.Maximum = new decimal(new int[] {
            16384,
            0,
            0,
            0});
            this.numericLeftWindow.Name = "numericLeftWindow";
            this.numericLeftWindow.Size = new System.Drawing.Size(60, 19);
            this.numericLeftWindow.TabIndex = 32;
            this.numericLeftWindow.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericLeftWindow.Value = new decimal(new int[] {
            8,
            0,
            0,
            0});
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label5.Location = new System.Drawing.Point(12, 110);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(116, 15);
            this.label5.TabIndex = 31;
            this.label5.Text = "Tukey Window Right";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label4.Location = new System.Drawing.Point(12, 85);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(108, 15);
            this.label4.TabIndex = 30;
            this.label4.Text = "Tukey Window Left";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label2.Location = new System.Drawing.Point(12, 35);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(98, 15);
            this.label2.TabIndex = 29;
            this.label2.Text = "Window Samples";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label1.Location = new System.Drawing.Point(12, 10);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(72, 15);
            this.label1.TabIndex = 28;
            this.label1.Text = "Sample Rate";
            // 
            // numericOffset
            // 
            this.numericOffset.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericOffset.Location = new System.Drawing.Point(193, 186);
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
            this.numericOffset.TabIndex = 46;
            this.numericOffset.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // label11
            // 
            this.label11.AutoSize = true;
            this.label11.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label11.Location = new System.Drawing.Point(12, 185);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(39, 15);
            this.label11.TabIndex = 45;
            this.label11.Text = "Offset";
            // 
            // numericPeriods
            // 
            this.numericPeriods.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericPeriods.Increment = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.numericPeriods.Location = new System.Drawing.Point(192, 211);
            this.numericPeriods.Maximum = new decimal(new int[] {
            60,
            0,
            0,
            0});
            this.numericPeriods.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numericPeriods.Name = "numericPeriods";
            this.numericPeriods.Size = new System.Drawing.Size(60, 19);
            this.numericPeriods.TabIndex = 48;
            this.numericPeriods.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericPeriods.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label3.Location = new System.Drawing.Point(11, 210);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(46, 15);
            this.label3.TabIndex = 47;
            this.label3.Text = "Periods";
            // 
            // BDOpt
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(50)))), ((int)(((byte)(50)))));
            this.ClientSize = new System.Drawing.Size(264, 291);
            this.Controls.Add(this.numericPeriods);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.numericOffset);
            this.Controls.Add(this.label11);
            this.Controls.Add(this.label10);
            this.Controls.Add(this.label9);
            this.Controls.Add(this.numericSmothInvOctaves);
            this.Controls.Add(this.numericdBRange);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.numericCaptureTime);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.numericWindow);
            this.Controls.Add(this.numericSampleRate);
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
            this.Name = "BDOpt";
            this.ShowInTaskbar = false;
            this.Text = "Burst Decay Options";
            ((System.ComponentModel.ISupportInitialize)(this.numericSmothInvOctaves)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericdBRange)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericCaptureTime)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericWindow)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericSampleRate)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericRightWindow)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericLeftWindow)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericOffset)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericPeriods)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private Label label10;
        private Label label9;
        private NumericUpDown numericSmothInvOctaves;
        private NumericUpDown numericdBRange;
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
