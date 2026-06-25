namespace Resonalyze.Options
{
    partial class IROpt
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
            numericLength = new DarkNumericUpDown();
            label1 = new Label();
            button1 = new Button();
            checkLogarithmic = new CheckBox();
            label2 = new Label();
            (numericLength).BeginInit();
            SuspendLayout();
            // 
            // numericLength
            // 
            numericLength.BackColor = Color.FromArgb(55, 60, 72);
            numericLength.DecimalPlaces = 0;
            numericLength.ForeColor = Color.White;
            numericLength.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericLength.Location = new Point(193, 12);
            numericLength.Maximum = new decimal(new int[] { 32768, 0, 0, 0 });
            numericLength.Minimum = new decimal(new int[] { 4, 0, 0, 0 });
            numericLength.MinimumSize = new Size(36, 19);
            numericLength.Name = "numericLength";
            numericLength.Size = new Size(60, 19);
            numericLength.TabIndex = 38;
            numericLength.TextAlign = HorizontalAlignment.Right;
            numericLength.ThousandsSeparator = false;
            numericLength.Value = new decimal(new int[] { 8192, 0, 0, 0 });
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.ForeColor = SystemColors.ControlLight;
            label1.Location = new Point(12, 14);
            label1.Name = "label1";
            label1.Size = new Size(98, 15);
            label1.TabIndex = 37;
            label1.Text = "Length (samples)";
            // 
            // button1
            // 
            button1.BackColor = Color.FromArgb(50, 55, 80);
            button1.DialogResult = DialogResult.OK;
            button1.FlatStyle = FlatStyle.Popup;
            button1.ForeColor = Color.White;
            button1.Location = new Point(12, 68);
            button1.Name = "button1";
            button1.Size = new Size(241, 23);
            button1.TabIndex = 35;
            button1.Text = "Apply settings";
            button1.UseVisualStyleBackColor = false;
            // 
            // checkLogarithmic
            // 
            checkLogarithmic.AutoSize = true;
            checkLogarithmic.ForeColor = SystemColors.ControlLight;
            checkLogarithmic.Location = new Point(238, 39);
            checkLogarithmic.Name = "checkLogarithmic";
            checkLogarithmic.Size = new Size(15, 14);
            checkLogarithmic.TabIndex = 47;
            checkLogarithmic.UseVisualStyleBackColor = true;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.ForeColor = SystemColors.ControlLight;
            label2.Location = new Point(12, 38);
            label2.Name = "label2";
            label2.Size = new Size(101, 15);
            label2.TabIndex = 46;
            label2.Text = "Logarithmic Sacle";
            // 
            // IROpt
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
            ClientSize = new Size(265, 102);
            Controls.Add(checkLogarithmic);
            Controls.Add(label2);
            Controls.Add(numericLength);
            Controls.Add(label1);
            Controls.Add(button1);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "IROpt";
            ShowInTaskbar = false;
            Text = "Impulse Response Options";
            (numericLength).EndInit();
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion

        private DarkNumericUpDown numericLength;
        private Label label1;
        private Button button1;
        private CheckBox checkLogarithmic;
        private Label label2;
    }
}
