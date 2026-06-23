namespace Resonalyze.Options
{
    partial class PROpt
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
            numericRightWindow = new NumericUpDown();
            numericLeftWindow = new NumericUpDown();
            label5 = new Label();
            label4 = new Label();
            numericWindow = new NumericUpDown();
            label1 = new Label();
            button1 = new Button();
            numericOffset = new NumericUpDown();
            label11 = new Label();
            checkBoxUnwrap = new CheckBox();
            label2 = new Label();
            irPlotView = new OxyPlot.WindowsForms.PlotView();
            ((System.ComponentModel.ISupportInitialize)numericSmoothingInverseOctaves).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericRightWindow).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericLeftWindow).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericWindow).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericOffset).BeginInit();
            SuspendLayout();
            //
            // label10
            //
            label10.AutoSize = true;
            label10.ForeColor = SystemColors.ControlLight;
            label10.Location = new Point(193, 87);
            label10.Name = "label10";
            label10.Size = new Size(21, 15);
            label10.TabIndex = 41;
            label10.Text = "1 /";
            //
            // label9
            //
            label9.AutoSize = true;
            label9.ForeColor = SystemColors.ControlLight;
            label9.Location = new Point(12, 85);
            label9.Name = "label9";
            label9.Size = new Size(117, 15);
            label9.TabIndex = 40;
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
            numericSmoothingInverseOctaves.TabIndex = 39;
            numericSmoothingInverseOctaves.TextAlign = HorizontalAlignment.Right;
            numericSmoothingInverseOctaves.Value = new decimal(new int[] { 1, 0, 0, 0 });
            //
            // numericRightWindow
            //
            numericRightWindow.BorderStyle = BorderStyle.None;
            numericRightWindow.Location = new Point(193, 61);
            numericRightWindow.Maximum = new decimal(new int[] { 16384, 0, 0, 0 });
            numericRightWindow.Name = "numericRightWindow";
            numericRightWindow.Size = new Size(60, 19);
            numericRightWindow.TabIndex = 38;
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
            numericLeftWindow.TabIndex = 37;
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
            label5.TabIndex = 36;
            label5.Text = "Tukey Window Right";
            //
            // label4
            //
            label4.AutoSize = true;
            label4.ForeColor = SystemColors.ControlLight;
            label4.Location = new Point(12, 35);
            label4.Name = "label4";
            label4.Size = new Size(109, 15);
            label4.TabIndex = 35;
            label4.Text = "Tukey Window Left";
            //
            // numericWindow
            //
            numericWindow.BorderStyle = BorderStyle.None;
            numericWindow.Location = new Point(193, 12);
            numericWindow.Maximum = new decimal(new int[] { 32768, 0, 0, 0 });
            numericWindow.Minimum = new decimal(new int[] { 4, 0, 0, 0 });
            numericWindow.Name = "numericWindow";
            numericWindow.Size = new Size(60, 19);
            numericWindow.TabIndex = 34;
            numericWindow.TextAlign = HorizontalAlignment.Right;
            numericWindow.Value = new decimal(new int[] { 8192, 0, 0, 0 });
            numericWindow.ValueChanged += numericWindow_ValueChanged;
            //
            // label1
            //
            label1.AutoSize = true;
            label1.ForeColor = SystemColors.ControlLight;
            label1.Location = new Point(12, 14);
            label1.Name = "label1";
            label1.Size = new Size(51, 15);
            label1.TabIndex = 33;
            label1.Text = "Window";
            //
            // button1
            //
            button1.BackColor = Color.FromArgb(50, 55, 80);
            button1.DialogResult = DialogResult.OK;
            button1.FlatStyle = FlatStyle.Popup;
            button1.ForeColor = Color.White;
            button1.Location = new Point(12, 462);
            button1.Name = "button1";
            button1.Size = new Size(241, 23);
            button1.TabIndex = 31;
            button1.Text = "Apply settings";
            button1.UseVisualStyleBackColor = false;
            //
            // numericOffset
            //
            numericOffset.BorderStyle = BorderStyle.None;
            numericOffset.Location = new Point(193, 111);
            numericOffset.Maximum = new decimal(new int[] { 32768, 0, 0, 0 });
            numericOffset.Minimum = new decimal(new int[] { 32768, 0, 0, int.MinValue });
            numericOffset.Name = "numericOffset";
            numericOffset.Size = new Size(60, 19);
            numericOffset.TabIndex = 43;
            numericOffset.TextAlign = HorizontalAlignment.Right;
            //
            // label11
            //
            label11.AutoSize = true;
            label11.ForeColor = SystemColors.ControlLight;
            label11.Location = new Point(12, 110);
            label11.Name = "label11";
            label11.Size = new Size(39, 15);
            label11.TabIndex = 42;
            label11.Text = "Offset";
            //
            // checkBoxUnwrap
            //
            checkBoxUnwrap.AutoSize = true;
            checkBoxUnwrap.ForeColor = SystemColors.ControlLight;
            checkBoxUnwrap.Location = new Point(238, 136);
            checkBoxUnwrap.Name = "checkBoxUnwrap";
            checkBoxUnwrap.Size = new Size(15, 14);
            checkBoxUnwrap.TabIndex = 45;
            checkBoxUnwrap.UseVisualStyleBackColor = true;
            //
            // label2
            //
            label2.AutoSize = true;
            label2.ForeColor = SystemColors.ControlLight;
            label2.Location = new Point(12, 135);
            label2.Name = "label2";
            label2.Size = new Size(48, 15);
            label2.TabIndex = 44;
            label2.Text = "Unwrap";
            //
            // irPlotView
            //
            irPlotView.Location = new Point(12, 156);
            irPlotView.Name = "irPlotView";
            irPlotView.PanCursor = Cursors.Hand;
            irPlotView.Size = new Size(241, 300);
            irPlotView.TabIndex = 50;
            irPlotView.Text = "plotView1";
            irPlotView.ZoomHorizontalCursor = Cursors.SizeWE;
            irPlotView.ZoomRectangleCursor = Cursors.SizeNWSE;
            irPlotView.ZoomVerticalCursor = Cursors.SizeNS;
            //
            // PROpt
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
            ClientSize = new Size(265, 497);
            Controls.Add(irPlotView);
            Controls.Add(checkBoxUnwrap);
            Controls.Add(label2);
            Controls.Add(numericOffset);
            Controls.Add(label11);
            Controls.Add(label10);
            Controls.Add(label9);
            Controls.Add(numericSmoothingInverseOctaves);
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
            Name = "PROpt";
            ShowInTaskbar = false;
            Text = "Phase Response Options";
            ((System.ComponentModel.ISupportInitialize)numericSmoothingInverseOctaves).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericRightWindow).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericLeftWindow).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericWindow).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericOffset).EndInit();
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion

        private Label label10;
        private Label label9;
        private NumericUpDown numericSmoothingInverseOctaves;
        private NumericUpDown numericRightWindow;
        private NumericUpDown numericLeftWindow;
        private Label label5;
        private Label label4;
        private NumericUpDown numericWindow;
        private Label label1;
        private Button button1;
        private NumericUpDown numericOffset;
        private Label label11;
        private CheckBox checkBoxUnwrap;
        private Label label2;
        private OxyPlot.WindowsForms.PlotView irPlotView;
    }
}
