namespace Resonalyze.Options
{
    partial class GDOpt
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
            labelGateOffset = new Label();
            buttonFit = new Button();
            numericGateOffset = new DarkNumericUpDown();
            label9 = new Label();
            labelMinFrequency = new Label();
            comboSmoothingInverseOctaves = new DarkComboBox();
            numericRightWindow = new DarkNumericUpDown();
            numericLeftWindow = new DarkNumericUpDown();
            label5 = new Label();
            label4 = new Label();
            numericWindow = new DarkNumericUpDown();
            label1 = new Label();
            irPlotView = new OxyPlot.WindowsForms.PlotView();
            (numericGateOffset).BeginInit();
            (numericRightWindow).BeginInit();
            (numericLeftWindow).BeginInit();
            (numericWindow).BeginInit();
            SuspendLayout();
            // 
            // labelGateOffset
            // 
            labelGateOffset.AutoSize = true;
            labelGateOffset.ForeColor = SystemColors.ControlLight;
            labelGateOffset.Location = new Point(12, 14);
            labelGateOffset.Name = "labelGateOffset";
            labelGateOffset.Size = new Size(91, 15);
            labelGateOffset.TabIndex = 60;
            labelGateOffset.Text = "Gate offset (ms)";
            // 
            // buttonFit
            // 
            buttonFit.BackColor = Color.FromArgb(55, 60, 72);
            buttonFit.FlatStyle = FlatStyle.Flat;
            buttonFit.ForeColor = Color.White;
            buttonFit.Location = new Point(115, 11);
            buttonFit.Name = "buttonFit";
            buttonFit.Size = new Size(33, 21);
            buttonFit.TabIndex = 61;
            buttonFit.Text = "Fit";
            buttonFit.UseCompatibleTextRendering = true;
            buttonFit.UseVisualStyleBackColor = false;
            // 
            // numericGateOffset
            // 
            numericGateOffset.BackColor = Color.FromArgb(55, 60, 72);
            numericGateOffset.DecimalPlaces = 3;
            numericGateOffset.ForeColor = Color.White;
            numericGateOffset.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            numericGateOffset.Location = new Point(153, 12);
            numericGateOffset.Maximum = new decimal(new int[] { 2000, 0, 0, 0 });
            numericGateOffset.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericGateOffset.MinimumSize = new Size(36, 19);
            numericGateOffset.Name = "numericGateOffset";
            numericGateOffset.Size = new Size(100, 19);
            numericGateOffset.TabIndex = 62;
            numericGateOffset.TextAlign = HorizontalAlignment.Right;
            numericGateOffset.ThousandsSeparator = false;
            numericGateOffset.Value = new decimal(new int[] { 0, 0, 0, 0 });
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.ForeColor = SystemColors.ControlLight;
            label9.Location = new Point(12, 136);
            label9.Name = "label9";
            label9.Size = new Size(117, 15);
            label9.TabIndex = 40;
            label9.Text = "Smoothing (octaves)";
            // 
            // labelMinFrequency
            // 
            labelMinFrequency.AutoSize = true;
            labelMinFrequency.ForeColor = Color.FromArgb(150, 170, 205);
            labelMinFrequency.Location = new Point(12, 111);
            labelMinFrequency.Name = "labelMinFrequency";
            labelMinFrequency.Size = new Size(120, 15);
            labelMinFrequency.TabIndex = 45;
            labelMinFrequency.Text = "Reliable from ≈ — Hz";
            // 
            // comboSmoothingInverseOctaves
            // 
            comboSmoothingInverseOctaves.BackColor = Color.FromArgb(55, 60, 72);
            comboSmoothingInverseOctaves.ForeColor = Color.White;
            comboSmoothingInverseOctaves.Location = new Point(153, 135);
            comboSmoothingInverseOctaves.Margin = new Padding(0);
            comboSmoothingInverseOctaves.MinimumSize = new Size(36, 19);
            comboSmoothingInverseOctaves.Name = "comboSmoothingInverseOctaves";
            comboSmoothingInverseOctaves.Size = new Size(100, 23);
            comboSmoothingInverseOctaves.TabIndex = 39;
            // 
            // numericRightWindow
            // 
            numericRightWindow.BackColor = Color.FromArgb(55, 60, 72);
            numericRightWindow.DecimalPlaces = 2;
            numericRightWindow.ForeColor = Color.White;
            numericRightWindow.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            numericRightWindow.Location = new Point(153, 86);
            numericRightWindow.Maximum = new decimal(new int[] { 680, 0, 0, 0 });
            numericRightWindow.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericRightWindow.MinimumSize = new Size(36, 19);
            numericRightWindow.Name = "numericRightWindow";
            numericRightWindow.Size = new Size(100, 19);
            numericRightWindow.TabIndex = 38;
            numericRightWindow.TextAlign = HorizontalAlignment.Right;
            numericRightWindow.ThousandsSeparator = false;
            numericRightWindow.Value = new decimal(new int[] { 300, 0, 0, 131072 });
            // 
            // numericLeftWindow
            // 
            numericLeftWindow.BackColor = Color.FromArgb(55, 60, 72);
            numericLeftWindow.DecimalPlaces = 2;
            numericLeftWindow.ForeColor = Color.White;
            numericLeftWindow.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            numericLeftWindow.Location = new Point(153, 61);
            numericLeftWindow.Maximum = new decimal(new int[] { 680, 0, 0, 0 });
            numericLeftWindow.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericLeftWindow.MinimumSize = new Size(36, 19);
            numericLeftWindow.Name = "numericLeftWindow";
            numericLeftWindow.Size = new Size(100, 19);
            numericLeftWindow.TabIndex = 37;
            numericLeftWindow.TextAlign = HorizontalAlignment.Right;
            numericLeftWindow.ThousandsSeparator = false;
            numericLeftWindow.Value = new decimal(new int[] { 50, 0, 0, 131072 });
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.ForeColor = SystemColors.ControlLight;
            label5.Location = new Point(12, 85);
            label5.Name = "label5";
            label5.Size = new Size(97, 15);
            label5.TabIndex = 36;
            label5.Text = "Right Tukey (ms)";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.ForeColor = SystemColors.ControlLight;
            label4.Location = new Point(12, 60);
            label4.Name = "label4";
            label4.Size = new Size(89, 15);
            label4.TabIndex = 35;
            label4.Text = "Left Tukey (ms)";
            // 
            // numericWindow
            // 
            numericWindow.BackColor = Color.FromArgb(55, 60, 72);
            numericWindow.DecimalPlaces = 2;
            numericWindow.ForeColor = Color.White;
            numericWindow.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            numericWindow.Location = new Point(153, 37);
            numericWindow.Maximum = new decimal(new int[] { 680, 0, 0, 0 });
            numericWindow.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericWindow.MinimumSize = new Size(36, 19);
            numericWindow.Name = "numericWindow";
            numericWindow.Size = new Size(100, 19);
            numericWindow.TabIndex = 34;
            numericWindow.TextAlign = HorizontalAlignment.Right;
            numericWindow.ThousandsSeparator = false;
            numericWindow.Value = new decimal(new int[] { 1000, 0, 0, 131072 });
            numericWindow.ValueChanged += numericWindow_ValueChanged;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.ForeColor = SystemColors.ControlLight;
            label1.Location = new Point(12, 39);
            label1.Name = "label1";
            label1.Size = new Size(73, 15);
            label1.TabIndex = 33;
            label1.Text = "Plateau (ms)";
            // 
            // irPlotView
            // 
            irPlotView.BackColor = Color.FromArgb(32, 36, 46);
            irPlotView.Location = new Point(12, 167);
            irPlotView.Name = "irPlotView";
            irPlotView.PanCursor = Cursors.Hand;
            irPlotView.Size = new Size(241, 300);
            irPlotView.TabIndex = 44;
            irPlotView.Text = "plotView1";
            irPlotView.ZoomHorizontalCursor = Cursors.SizeWE;
            irPlotView.ZoomRectangleCursor = Cursors.SizeNWSE;
            irPlotView.ZoomVerticalCursor = Cursors.SizeNS;
            // 
            // GDOpt
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
            ClientSize = new Size(265, 474);
            Controls.Add(irPlotView);
            Controls.Add(numericGateOffset);
            Controls.Add(buttonFit);
            Controls.Add(labelGateOffset);
            Controls.Add(labelMinFrequency);
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
            Name = "GDOpt";
            ShowInTaskbar = false;
            Text = "Group Delay Options";
            (numericGateOffset).EndInit();
            (numericRightWindow).EndInit();
            (numericLeftWindow).EndInit();
            (numericWindow).EndInit();
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion

        private Label labelGateOffset;
        private Button buttonFit;
        private DarkNumericUpDown numericGateOffset;
        private Label label9;
        private Label labelMinFrequency;
        private DarkComboBox comboSmoothingInverseOctaves;
        private DarkNumericUpDown numericRightWindow;
        private DarkNumericUpDown numericLeftWindow;
        private Label label5;
        private Label label4;
        private DarkNumericUpDown numericWindow;
        private Label label1;
        private OxyPlot.WindowsForms.PlotView irPlotView;
    }
}
