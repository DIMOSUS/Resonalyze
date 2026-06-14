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
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.comboBoxChanel = new System.Windows.Forms.ComboBox();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.numericUpDownDesireDuration = new System.Windows.Forms.NumericUpDown();
            this.numericUpDownComputeDuration = new System.Windows.Forms.NumericUpDown();
            this.button2 = new System.Windows.Forms.Button();
            this.button1 = new System.Windows.Forms.Button();
            this.label6 = new System.Windows.Forms.Label();
            this.numericUpDownSampleRate = new System.Windows.Forms.NumericUpDown();
            this.numericUpDownBits = new System.Windows.Forms.NumericUpDown();
            this.numericUpDownOctaves = new System.Windows.Forms.NumericUpDown();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownDesireDuration)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownComputeDuration)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownSampleRate)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownBits)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownOctaves)).BeginInit();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label1.Location = new System.Drawing.Point(12, 15);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(72, 15);
            this.label1.TabIndex = 1;
            this.label1.Text = "Sample Rate";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label2.Location = new System.Drawing.Point(12, 41);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(26, 15);
            this.label2.TabIndex = 3;
            this.label2.Text = "Bits";
            // 
            // comboBoxChanel
            // 
            this.comboBoxChanel.FormattingEnabled = true;
            this.comboBoxChanel.Location = new System.Drawing.Point(153, 87);
            this.comboBoxChanel.Name = "comboBoxChanel";
            this.comboBoxChanel.Size = new System.Drawing.Size(100, 23);
            this.comboBoxChanel.TabIndex = 4;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label3.Location = new System.Drawing.Point(12, 95);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(44, 15);
            this.label3.TabIndex = 5;
            this.label3.Text = "Chanel";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label4.Location = new System.Drawing.Point(12, 120);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(115, 15);
            this.label4.TabIndex = 7;
            this.label4.Text = "Desire Duration (ms)";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label5.Location = new System.Drawing.Point(12, 145);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(133, 15);
            this.label5.TabIndex = 9;
            this.label5.Text = "Compute Duration (ms)";
            // 
            // numericUpDownDesireDuration
            // 
            this.numericUpDownDesireDuration.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericUpDownDesireDuration.Increment = new decimal(new int[] {
            500,
            0,
            0,
            0});
            this.numericUpDownDesireDuration.Location = new System.Drawing.Point(153, 116);
            this.numericUpDownDesireDuration.Maximum = new decimal(new int[] {
            100000,
            0,
            0,
            0});
            this.numericUpDownDesireDuration.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numericUpDownDesireDuration.Name = "numericUpDownDesireDuration";
            this.numericUpDownDesireDuration.Size = new System.Drawing.Size(100, 19);
            this.numericUpDownDesireDuration.TabIndex = 10;
            this.numericUpDownDesireDuration.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericUpDownDesireDuration.Value = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.numericUpDownDesireDuration.ValueChanged += new System.EventHandler(this.numericUpDownDesireDuration_ValueChanged);
            // 
            // numericUpDownComputeDuration
            // 
            this.numericUpDownComputeDuration.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericUpDownComputeDuration.Enabled = false;
            this.numericUpDownComputeDuration.Location = new System.Drawing.Point(153, 141);
            this.numericUpDownComputeDuration.Maximum = new decimal(new int[] {
            1316134911,
            2328,
            0,
            0});
            this.numericUpDownComputeDuration.Name = "numericUpDownComputeDuration";
            this.numericUpDownComputeDuration.ReadOnly = true;
            this.numericUpDownComputeDuration.Size = new System.Drawing.Size(100, 19);
            this.numericUpDownComputeDuration.TabIndex = 11;
            this.numericUpDownComputeDuration.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // button2
            // 
            this.button2.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.button2.Location = new System.Drawing.Point(153, 197);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(100, 23);
            this.button2.TabIndex = 13;
            this.button2.Text = "Cancel";
            this.button2.UseVisualStyleBackColor = true;
            // 
            // button1
            // 
            this.button1.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.button1.Location = new System.Drawing.Point(12, 197);
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
            this.label6.Location = new System.Drawing.Point(12, 66);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(49, 15);
            this.label6.TabIndex = 15;
            this.label6.Text = "Octaves";
            // 
            // numericUpDownSampleRate
            // 
            this.numericUpDownSampleRate.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericUpDownSampleRate.Enabled = false;
            this.numericUpDownSampleRate.Location = new System.Drawing.Point(153, 12);
            this.numericUpDownSampleRate.Maximum = new decimal(new int[] {
            192000,
            0,
            0,
            0});
            this.numericUpDownSampleRate.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numericUpDownSampleRate.Name = "numericUpDownSampleRate";
            this.numericUpDownSampleRate.ReadOnly = true;
            this.numericUpDownSampleRate.Size = new System.Drawing.Size(100, 19);
            this.numericUpDownSampleRate.TabIndex = 16;
            this.numericUpDownSampleRate.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericUpDownSampleRate.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            // 
            // numericUpDownBits
            // 
            this.numericUpDownBits.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericUpDownBits.Enabled = false;
            this.numericUpDownBits.Location = new System.Drawing.Point(153, 37);
            this.numericUpDownBits.Maximum = new decimal(new int[] {
            32,
            0,
            0,
            0});
            this.numericUpDownBits.Minimum = new decimal(new int[] {
            8,
            0,
            0,
            0});
            this.numericUpDownBits.Name = "numericUpDownBits";
            this.numericUpDownBits.ReadOnly = true;
            this.numericUpDownBits.Size = new System.Drawing.Size(100, 19);
            this.numericUpDownBits.TabIndex = 17;
            this.numericUpDownBits.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericUpDownBits.Value = new decimal(new int[] {
            8,
            0,
            0,
            0});
            // 
            // numericUpDownOctaves
            // 
            this.numericUpDownOctaves.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericUpDownOctaves.Enabled = false;
            this.numericUpDownOctaves.Location = new System.Drawing.Point(153, 62);
            this.numericUpDownOctaves.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numericUpDownOctaves.Name = "numericUpDownOctaves";
            this.numericUpDownOctaves.ReadOnly = true;
            this.numericUpDownOctaves.Size = new System.Drawing.Size(100, 19);
            this.numericUpDownOctaves.TabIndex = 18;
            this.numericUpDownOctaves.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericUpDownOctaves.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            // 
            // MeasurementOptions
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(50)))), ((int)(((byte)(50)))));
            this.ClientSize = new System.Drawing.Size(265, 231);
            this.Controls.Add(this.numericUpDownOctaves);
            this.Controls.Add(this.numericUpDownBits);
            this.Controls.Add(this.numericUpDownSampleRate);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.numericUpDownComputeDuration);
            this.Controls.Add(this.numericUpDownDesireDuration);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.comboBoxChanel);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "MeasurementOptions";
            this.ShowInTaskbar = false;
            this.Text = "Measurement Options";
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownDesireDuration)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownComputeDuration)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownSampleRate)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownBits)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownOctaves)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private Label label1;
        private Label label2;
        private ComboBox comboBoxChanel;
        private Label label3;
        private Label label4;
        private Label label5;
        private NumericUpDown numericUpDownDesireDuration;
        private NumericUpDown numericUpDownComputeDuration;
        private Button button2;
        private Button button1;
        private Label label6;
        private NumericUpDown numericUpDownSampleRate;
        private NumericUpDown numericUpDownBits;
        private NumericUpDown numericUpDownOctaves;
    }
}
