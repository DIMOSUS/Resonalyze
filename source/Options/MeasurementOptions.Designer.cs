namespace Resonalyze.Options
{
    partial class MeasurementOptions
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
            comboBoxChannel = new ComboBox();
            label3 = new Label();
            label4 = new Label();
            label5 = new Label();
            numericUpDownRequestedDuration = new NumericUpDown();
            numericUpDownComputeDuration = new NumericUpDown();
            button2 = new Button();
            button1 = new Button();
            label6 = new Label();
            numericUpDownSampleRate = new NumericUpDown();
            numericUpDownBits = new NumericUpDown();
            numericUpDownOctaves = new NumericUpDown();
            ((System.ComponentModel.ISupportInitialize)numericUpDownRequestedDuration).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownComputeDuration).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownSampleRate).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownBits).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownOctaves).BeginInit();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.ForeColor = SystemColors.ControlLight;
            label1.Location = new Point(12, 15);
            label1.Name = "label1";
            label1.Size = new Size(72, 15);
            label1.TabIndex = 1;
            label1.Text = "Sample Rate";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.ForeColor = SystemColors.ControlLight;
            label2.Location = new Point(12, 41);
            label2.Name = "label2";
            label2.Size = new Size(26, 15);
            label2.TabIndex = 3;
            label2.Text = "Bits";
            // 
            // comboBoxChannel
            // 
            comboBoxChannel.FormattingEnabled = true;
            comboBoxChannel.Location = new Point(153, 87);
            comboBoxChannel.Name = "comboBoxChannel";
            comboBoxChannel.Size = new Size(100, 23);
            comboBoxChannel.TabIndex = 4;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.ForeColor = SystemColors.ControlLight;
            label3.Location = new Point(12, 95);
            label3.Name = "label3";
            label3.Size = new Size(51, 15);
            label3.TabIndex = 5;
            label3.Text = "Channel";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.ForeColor = SystemColors.ControlLight;
            label4.Location = new Point(12, 120);
            label4.Name = "label4";
            label4.Size = new Size(138, 15);
            label4.TabIndex = 7;
            label4.Text = "Requested Duration (ms)";
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.ForeColor = SystemColors.ControlLight;
            label5.Location = new Point(12, 145);
            label5.Name = "label5";
            label5.Size = new Size(133, 15);
            label5.TabIndex = 9;
            label5.Text = "Compute Duration (ms)";
            // 
            // numericUpDownRequestedDuration
            // 
            numericUpDownRequestedDuration.BorderStyle = BorderStyle.None;
            numericUpDownRequestedDuration.Increment = new decimal(new int[] { 500, 0, 0, 0 });
            numericUpDownRequestedDuration.Location = new Point(153, 116);
            numericUpDownRequestedDuration.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            numericUpDownRequestedDuration.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDownRequestedDuration.Name = "numericUpDownRequestedDuration";
            numericUpDownRequestedDuration.Size = new Size(100, 19);
            numericUpDownRequestedDuration.TabIndex = 10;
            numericUpDownRequestedDuration.TextAlign = HorizontalAlignment.Right;
            numericUpDownRequestedDuration.Value = new decimal(new int[] { 1000, 0, 0, 0 });
            numericUpDownRequestedDuration.ValueChanged += numericUpDownRequestedDuration_ValueChanged;
            // 
            // numericUpDownComputeDuration
            // 
            numericUpDownComputeDuration.BorderStyle = BorderStyle.None;
            numericUpDownComputeDuration.Enabled = false;
            numericUpDownComputeDuration.Location = new Point(153, 141);
            numericUpDownComputeDuration.Maximum = new decimal(new int[] { 1316134911, 2328, 0, 0 });
            numericUpDownComputeDuration.Name = "numericUpDownComputeDuration";
            numericUpDownComputeDuration.ReadOnly = true;
            numericUpDownComputeDuration.Size = new Size(100, 19);
            numericUpDownComputeDuration.TabIndex = 11;
            numericUpDownComputeDuration.TextAlign = HorizontalAlignment.Right;
            // 
            // button2
            // 
            button2.DialogResult = DialogResult.Cancel;
            button2.Location = new Point(153, 197);
            button2.Name = "button2";
            button2.Size = new Size(100, 23);
            button2.TabIndex = 13;
            button2.Text = "Cancel";
            button2.UseVisualStyleBackColor = true;
            // 
            // button1
            // 
            button1.DialogResult = DialogResult.OK;
            button1.Location = new Point(12, 197);
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
            label6.Location = new Point(12, 66);
            label6.Name = "label6";
            label6.Size = new Size(49, 15);
            label6.TabIndex = 15;
            label6.Text = "Octaves";
            // 
            // numericUpDownSampleRate
            // 
            numericUpDownSampleRate.BorderStyle = BorderStyle.None;
            numericUpDownSampleRate.Enabled = false;
            numericUpDownSampleRate.Location = new Point(153, 12);
            numericUpDownSampleRate.Maximum = new decimal(new int[] { 192000, 0, 0, 0 });
            numericUpDownSampleRate.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDownSampleRate.Name = "numericUpDownSampleRate";
            numericUpDownSampleRate.ReadOnly = true;
            numericUpDownSampleRate.Size = new Size(100, 19);
            numericUpDownSampleRate.TabIndex = 16;
            numericUpDownSampleRate.TextAlign = HorizontalAlignment.Right;
            numericUpDownSampleRate.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // numericUpDownBits
            // 
            numericUpDownBits.BorderStyle = BorderStyle.None;
            numericUpDownBits.Enabled = false;
            numericUpDownBits.Location = new Point(153, 37);
            numericUpDownBits.Maximum = new decimal(new int[] { 32, 0, 0, 0 });
            numericUpDownBits.Minimum = new decimal(new int[] { 8, 0, 0, 0 });
            numericUpDownBits.Name = "numericUpDownBits";
            numericUpDownBits.ReadOnly = true;
            numericUpDownBits.Size = new Size(100, 19);
            numericUpDownBits.TabIndex = 17;
            numericUpDownBits.TextAlign = HorizontalAlignment.Right;
            numericUpDownBits.Value = new decimal(new int[] { 8, 0, 0, 0 });
            // 
            // numericUpDownOctaves
            // 
            numericUpDownOctaves.BorderStyle = BorderStyle.None;
            numericUpDownOctaves.Enabled = false;
            numericUpDownOctaves.Location = new Point(153, 62);
            numericUpDownOctaves.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDownOctaves.Name = "numericUpDownOctaves";
            numericUpDownOctaves.ReadOnly = true;
            numericUpDownOctaves.Size = new Size(100, 19);
            numericUpDownOctaves.TabIndex = 18;
            numericUpDownOctaves.TextAlign = HorizontalAlignment.Right;
            numericUpDownOctaves.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // MeasurementOptions
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(50, 50, 50);
            ClientSize = new Size(265, 231);
            Controls.Add(numericUpDownOctaves);
            Controls.Add(numericUpDownBits);
            Controls.Add(numericUpDownSampleRate);
            Controls.Add(label6);
            Controls.Add(button2);
            Controls.Add(button1);
            Controls.Add(numericUpDownComputeDuration);
            Controls.Add(numericUpDownRequestedDuration);
            Controls.Add(label5);
            Controls.Add(label4);
            Controls.Add(label3);
            Controls.Add(comboBoxChannel);
            Controls.Add(label2);
            Controls.Add(label1);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "MeasurementOptions";
            ShowInTaskbar = false;
            Text = "Measurement Options";
            ((System.ComponentModel.ISupportInitialize)numericUpDownRequestedDuration).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownComputeDuration).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownSampleRate).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownBits).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownOctaves).EndInit();
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion
        private Label label1;
        private Label label2;
        private ComboBox comboBoxChannel;
        private Label label3;
        private Label label4;
        private Label label5;
        private NumericUpDown numericUpDownRequestedDuration;
        private NumericUpDown numericUpDownComputeDuration;
        private Button button2;
        private Button button1;
        private Label label6;
        private NumericUpDown numericUpDownSampleRate;
        private NumericUpDown numericUpDownBits;
        private NumericUpDown numericUpDownOctaves;
    }
}
