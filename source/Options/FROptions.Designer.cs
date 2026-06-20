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
            button2 = new Button();
            button1 = new Button();
            label1 = new Label();
            numericWindow = new NumericUpDown();
            numericRightWindow = new NumericUpDown();
            numericLeftWindow = new NumericUpDown();
            label5 = new Label();
            label4 = new Label();
            label10 = new Label();
            label9 = new Label();
            numericSmoothingInverseOctaves = new NumericUpDown();
            checkUseCalibration = new CheckBox();
            label2 = new Label();
            ((System.ComponentModel.ISupportInitialize)numericWindow).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericRightWindow).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericLeftWindow).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericSmoothingInverseOctaves).BeginInit();
            SuspendLayout();
            // 
            // button2
            // 
            button2.DialogResult = DialogResult.Cancel;
            button2.Location = new Point(153, 237);
            button2.Name = "button2";
            button2.Size = new Size(100, 23);
            button2.TabIndex = 15;
            button2.Text = "Cancel";
            button2.UseVisualStyleBackColor = true;
            // 
            // button1
            // 
            button1.DialogResult = DialogResult.OK;
            button1.Location = new Point(12, 237);
            button1.Name = "button1";
            button1.Size = new Size(100, 23);
            button1.TabIndex = 14;
            button1.Text = "Ok";
            button1.UseVisualStyleBackColor = true;
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
            numericWindow.BorderStyle = BorderStyle.None;
            numericWindow.Location = new Point(193, 12);
            numericWindow.Maximum = new decimal(new int[] { 32768, 0, 0, 0 });
            numericWindow.Minimum = new decimal(new int[] { 4, 0, 0, 0 });
            numericWindow.Name = "numericWindow";
            numericWindow.Size = new Size(60, 19);
            numericWindow.TabIndex = 17;
            numericWindow.TextAlign = HorizontalAlignment.Right;
            numericWindow.Value = new decimal(new int[] { 8192, 0, 0, 0 });
            numericWindow.ValueChanged += numericWindow_ValueChanged;
            // 
            // numericRightWindow
            // 
            numericRightWindow.BorderStyle = BorderStyle.None;
            numericRightWindow.Location = new Point(193, 61);
            numericRightWindow.Maximum = new decimal(new int[] { 16384, 0, 0, 0 });
            numericRightWindow.Name = "numericRightWindow";
            numericRightWindow.Size = new Size(60, 19);
            numericRightWindow.TabIndex = 21;
            numericRightWindow.TextAlign = HorizontalAlignment.Right;
            numericRightWindow.Value = new decimal(new int[] { 256, 0, 0, 0 });
            // 
            // numericLeftWindow
            // 
            numericLeftWindow.BorderStyle = BorderStyle.None;
            numericLeftWindow.Location = new Point(193, 36);
            numericLeftWindow.Maximum = new decimal(new int[] { 16384, 0, 0, 0 });
            numericLeftWindow.Name = "numericLeftWindow";
            numericLeftWindow.Size = new Size(60, 19);
            numericLeftWindow.TabIndex = 20;
            numericLeftWindow.TextAlign = HorizontalAlignment.Right;
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
            // label10
            // 
            label10.AutoSize = true;
            label10.ForeColor = SystemColors.ControlLight;
            label10.Location = new Point(193, 87);
            label10.Name = "label10";
            label10.Size = new Size(21, 15);
            label10.TabIndex = 30;
            label10.Text = "1 /";
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
            // numericSmoothingInverseOctaves
            // 
            numericSmoothingInverseOctaves.BorderStyle = BorderStyle.None;
            numericSmoothingInverseOctaves.Location = new Point(214, 86);
            numericSmoothingInverseOctaves.Maximum = new decimal(new int[] { 48, 0, 0, 0 });
            numericSmoothingInverseOctaves.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericSmoothingInverseOctaves.Name = "numericSmoothingInverseOctaves";
            numericSmoothingInverseOctaves.Size = new Size(39, 19);
            numericSmoothingInverseOctaves.TabIndex = 28;
            numericSmoothingInverseOctaves.TextAlign = HorizontalAlignment.Right;
            numericSmoothingInverseOctaves.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // checkUseCalibration
            // 
            checkUseCalibration.AutoSize = true;
            checkUseCalibration.ForeColor = SystemColors.ControlLight;
            checkUseCalibration.Location = new Point(238, 111);
            checkUseCalibration.Name = "checkUseCalibration";
            checkUseCalibration.Size = new Size(15, 14);
            checkUseCalibration.TabIndex = 47;
            checkUseCalibration.UseVisualStyleBackColor = true;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.ForeColor = SystemColors.ControlLight;
            label2.Location = new Point(12, 110);
            label2.Name = "label2";
            label2.Size = new Size(87, 15);
            label2.TabIndex = 46;
            label2.Text = "Use Calibration";
            // 
            // FROptions
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
            ClientSize = new Size(265, 272);
            Controls.Add(checkUseCalibration);
            Controls.Add(label2);
            Controls.Add(label10);
            Controls.Add(label9);
            Controls.Add(numericSmoothingInverseOctaves);
            Controls.Add(numericRightWindow);
            Controls.Add(numericLeftWindow);
            Controls.Add(label5);
            Controls.Add(label4);
            Controls.Add(numericWindow);
            Controls.Add(label1);
            Controls.Add(button2);
            Controls.Add(button1);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "FROptions";
            ShowInTaskbar = false;
            Text = "Frequency Response Options";
            ((System.ComponentModel.ISupportInitialize)numericWindow).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericRightWindow).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericLeftWindow).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericSmoothingInverseOctaves).EndInit();
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion

        private Button button2;
        private Button button1;
        private Label label1;
        private NumericUpDown numericWindow;
        private NumericUpDown numericRightWindow;
        private NumericUpDown numericLeftWindow;
        private Label label5;
        private Label label4;
        private Label label10;
        private Label label9;
        private NumericUpDown numericSmoothingInverseOctaves;
        private CheckBox checkUseCalibration;
        private Label label2;
    }
}
