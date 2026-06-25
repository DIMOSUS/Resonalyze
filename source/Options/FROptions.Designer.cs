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
            button1 = new Button();
            label1 = new Label();
            numericWindow = new DarkNumericUpDown();
            numericRightWindow = new DarkNumericUpDown();
            numericLeftWindow = new DarkNumericUpDown();
            label5 = new Label();
            label4 = new Label();
            label9 = new Label();
            comboSmoothingInverseOctaves = new DarkComboBox();
            checkUseCalibration = new CheckBox();
            label2 = new Label();
            irPlotView = new OxyPlot.WindowsForms.PlotView();
            (numericWindow).BeginInit();
            (numericRightWindow).BeginInit();
            (numericLeftWindow).BeginInit();
            SuspendLayout();
            // 
            // button1
            // 
            button1.BackColor = Color.FromArgb(50, 55, 80);
            button1.DialogResult = DialogResult.OK;
            button1.FlatStyle = FlatStyle.Popup;
            button1.ForeColor = Color.White;
            button1.Location = new Point(12, 437);
            button1.Name = "button1";
            button1.Size = new Size(241, 23);
            button1.TabIndex = 14;
            button1.Text = "Apply settings";
            button1.UseVisualStyleBackColor = false;
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
            // irPlotView
            // 
            irPlotView.BackColor = Color.FromArgb(32, 36, 46);
            irPlotView.Location = new Point(12, 131);
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
            ClientSize = new Size(265, 471);
            Controls.Add(irPlotView);
            Controls.Add(checkUseCalibration);
            Controls.Add(label2);
            Controls.Add(label9);
            Controls.Add(comboSmoothingInverseOctaves);
            Controls.Add(numericRightWindow);
            Controls.Add(numericLeftWindow);
            Controls.Add(label5);
            Controls.Add(label4);
            Controls.Add(numericWindow);
            Controls.Add(label1);
            Controls.Add(button1);
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
        private Button button1;
        private Label label1;
        private DarkNumericUpDown numericWindow;
        private DarkNumericUpDown numericRightWindow;
        private DarkNumericUpDown numericLeftWindow;
        private Label label5;
        private Label label4;
        private Label label9;
        private DarkComboBox comboSmoothingInverseOctaves;
        private CheckBox checkUseCalibration;
        private Label label2;
        private OxyPlot.WindowsForms.PlotView irPlotView;
    }
}
