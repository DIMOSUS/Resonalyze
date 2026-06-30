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
            if (disposing)
            {
                warningStatusFont?.Dispose();
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
            comboBoxChannel = new DarkComboBox();
            label3 = new Label();
            label4 = new Label();
            label5 = new Label();
            numericUpDownRequestedDuration = new DarkNumericUpDown();
            numericUpDownComputeDuration = new DarkNumericUpDown();
            button1 = new Button();
            label6 = new Label();
            comboBoxSampleRate = new DarkComboBox();
            numericUpDownBits = new DarkNumericUpDown();
            numericUpDownOctaves = new DarkNumericUpDown();
            labelAudioBackend = new Label();
            comboBoxAudioBackend = new DarkComboBox();
            waveAudioBackendPanel = new WaveAudioBackendPanel();
            asioAudioBackendPanel = new AsioAudioBackendPanel();
            (numericUpDownRequestedDuration).BeginInit();
            (numericUpDownComputeDuration).BeginInit();
            (numericUpDownBits).BeginInit();
            (numericUpDownOctaves).BeginInit();
            SuspendLayout();
            //
            // label1
            //
            label1.AutoSize = true;
            label1.ForeColor = SystemColors.ControlLight;
            label1.Location = new Point(12, 20);
            label1.Name = "label1";
            label1.Size = new Size(72, 15);
            label1.TabIndex = 1;
            label1.Text = "Sample Rate";
            //
            // label2
            //
            label2.AutoSize = true;
            label2.ForeColor = SystemColors.ControlLight;
            label2.Location = new Point(12, 45);
            label2.Name = "label2";
            label2.Size = new Size(26, 15);
            label2.TabIndex = 3;
            label2.Text = "Bits";
            //
            // comboBoxChannel
            //
            comboBoxChannel.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxChannel.ForeColor = Color.White;
            comboBoxChannel.Location = new Point(153, 140);
            comboBoxChannel.Margin = new Padding(0);
            comboBoxChannel.MinimumSize = new Size(36, 19);
            comboBoxChannel.Name = "comboBoxChannel";
            comboBoxChannel.Size = new Size(170, 23);
            comboBoxChannel.TabIndex = 4;
            comboBoxChannel.SelectedIndexChanged += comboBoxChannel_SelectedIndexChanged;
            //
            // label3
            //
            label3.AutoSize = true;
            label3.ForeColor = SystemColors.ControlLight;
            label3.Location = new Point(12, 148);
            label3.Name = "label3";
            label3.Size = new Size(51, 15);
            label3.TabIndex = 5;
            label3.Text = "Channel";
            //
            // label4
            //
            label4.AutoSize = true;
            label4.ForeColor = SystemColors.ControlLight;
            label4.Location = new Point(12, 95);
            label4.Name = "label4";
            label4.Size = new Size(138, 15);
            label4.TabIndex = 7;
            label4.Text = "Requested Duration (ms)";
            //
            // label5
            //
            label5.AutoSize = true;
            label5.ForeColor = SystemColors.ControlLight;
            label5.Location = new Point(12, 119);
            label5.Name = "label5";
            label5.Size = new Size(133, 15);
            label5.TabIndex = 9;
            label5.Text = "Compute Duration (ms)";
            //
            // numericUpDownRequestedDuration
            //
            numericUpDownRequestedDuration.BackColor = Color.FromArgb(55, 60, 72);
            numericUpDownRequestedDuration.DecimalPlaces = 0;
            numericUpDownRequestedDuration.ForeColor = Color.White;
            numericUpDownRequestedDuration.Increment = new decimal(new int[] { 500, 0, 0, 0 });
            numericUpDownRequestedDuration.Location = new Point(153, 91);
            numericUpDownRequestedDuration.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            numericUpDownRequestedDuration.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDownRequestedDuration.MinimumSize = new Size(36, 19);
            numericUpDownRequestedDuration.Name = "numericUpDownRequestedDuration";
            numericUpDownRequestedDuration.Size = new Size(170, 19);
            numericUpDownRequestedDuration.TabIndex = 10;
            numericUpDownRequestedDuration.TextAlign = HorizontalAlignment.Right;
            numericUpDownRequestedDuration.ThousandsSeparator = false;
            numericUpDownRequestedDuration.Value = new decimal(new int[] { 1000, 0, 0, 0 });
            numericUpDownRequestedDuration.ValueChanged += numericUpDownRequestedDuration_ValueChanged;
            //
            // numericUpDownComputeDuration
            //
            numericUpDownComputeDuration.BackColor = Color.FromArgb(55, 60, 72);
            numericUpDownComputeDuration.DecimalPlaces = 0;
            numericUpDownComputeDuration.Enabled = false;
            numericUpDownComputeDuration.ForeColor = Color.White;
            numericUpDownComputeDuration.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDownComputeDuration.Location = new Point(153, 115);
            numericUpDownComputeDuration.Maximum = new decimal(new int[] { 1316134911, 2328, 0, 0 });
            numericUpDownComputeDuration.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericUpDownComputeDuration.MinimumSize = new Size(36, 19);
            numericUpDownComputeDuration.Name = "numericUpDownComputeDuration";
            numericUpDownComputeDuration.ReadOnly = true;
            numericUpDownComputeDuration.Size = new Size(170, 19);
            numericUpDownComputeDuration.TabIndex = 11;
            numericUpDownComputeDuration.TextAlign = HorizontalAlignment.Right;
            numericUpDownComputeDuration.ThousandsSeparator = false;
            numericUpDownComputeDuration.Value = new decimal(new int[] { 0, 0, 0, 0 });
            //
            // button1
            //
            button1.DialogResult = DialogResult.OK;
            button1.FlatStyle = FlatStyle.Popup;
            button1.ForeColor = Color.White;
            button1.Location = new Point(11, 437);
            button1.Name = "button1";
            button1.Size = new Size(311, 23);
            button1.TabIndex = 12;
            button1.Text = "Apply settings";
            button1.UseVisualStyleBackColor = true;
            //
            // label6
            //
            label6.AutoSize = true;
            label6.ForeColor = SystemColors.ControlLight;
            label6.Location = new Point(12, 70);
            label6.Name = "label6";
            label6.Size = new Size(49, 15);
            label6.TabIndex = 15;
            label6.Text = "Octaves";
            //
            // comboBoxSampleRate
            //
            comboBoxSampleRate.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxSampleRate.ForeColor = Color.White;
            comboBoxSampleRate.Location = new Point(153, 12);
            comboBoxSampleRate.Margin = new Padding(0);
            comboBoxSampleRate.MinimumSize = new Size(36, 19);
            comboBoxSampleRate.Name = "comboBoxSampleRate";
            comboBoxSampleRate.Size = new Size(170, 23);
            comboBoxSampleRate.TabIndex = 16;
            comboBoxSampleRate.SelectedIndexChanged += comboBoxSampleRate_SelectedIndexChanged;
            //
            // numericUpDownBits
            //
            numericUpDownBits.BackColor = Color.FromArgb(55, 60, 72);
            numericUpDownBits.DecimalPlaces = 0;
            numericUpDownBits.Enabled = false;
            numericUpDownBits.ForeColor = Color.White;
            numericUpDownBits.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDownBits.Location = new Point(153, 41);
            numericUpDownBits.Maximum = new decimal(new int[] { 32, 0, 0, 0 });
            numericUpDownBits.Minimum = new decimal(new int[] { 8, 0, 0, 0 });
            numericUpDownBits.MinimumSize = new Size(36, 19);
            numericUpDownBits.Name = "numericUpDownBits";
            numericUpDownBits.ReadOnly = true;
            numericUpDownBits.Size = new Size(170, 19);
            numericUpDownBits.TabIndex = 17;
            numericUpDownBits.TextAlign = HorizontalAlignment.Right;
            numericUpDownBits.ThousandsSeparator = false;
            numericUpDownBits.Value = new decimal(new int[] { 8, 0, 0, 0 });
            //
            // numericUpDownOctaves
            //
            numericUpDownOctaves.BackColor = Color.FromArgb(55, 60, 72);
            numericUpDownOctaves.DecimalPlaces = 0;
            numericUpDownOctaves.Enabled = false;
            numericUpDownOctaves.ForeColor = Color.White;
            numericUpDownOctaves.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDownOctaves.Location = new Point(153, 66);
            numericUpDownOctaves.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            numericUpDownOctaves.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDownOctaves.MinimumSize = new Size(36, 19);
            numericUpDownOctaves.Name = "numericUpDownOctaves";
            numericUpDownOctaves.ReadOnly = true;
            numericUpDownOctaves.Size = new Size(170, 19);
            numericUpDownOctaves.TabIndex = 18;
            numericUpDownOctaves.TextAlign = HorizontalAlignment.Right;
            numericUpDownOctaves.ThousandsSeparator = false;
            numericUpDownOctaves.Value = new decimal(new int[] { 1, 0, 0, 0 });
            //
            // labelAudioBackend
            //
            labelAudioBackend.AutoSize = true;
            labelAudioBackend.ForeColor = SystemColors.ControlLight;
            labelAudioBackend.Location = new Point(12, 186);
            labelAudioBackend.Name = "labelAudioBackend";
            labelAudioBackend.Size = new Size(87, 15);
            labelAudioBackend.TabIndex = 23;
            labelAudioBackend.Text = "Audio backend";
            //
            // comboBoxAudioBackend
            //
            comboBoxAudioBackend.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxAudioBackend.ForeColor = Color.White;
            comboBoxAudioBackend.Location = new Point(153, 178);
            comboBoxAudioBackend.Margin = new Padding(0);
            comboBoxAudioBackend.MinimumSize = new Size(36, 19);
            comboBoxAudioBackend.Name = "comboBoxAudioBackend";
            comboBoxAudioBackend.Size = new Size(170, 23);
            comboBoxAudioBackend.TabIndex = 24;
            comboBoxAudioBackend.SelectedIndexChanged += comboBoxAudioBackend_SelectedIndexChanged;
            //
            // waveAudioBackendPanel
            //
            waveAudioBackendPanel.Location = new Point(12, 216);
            waveAudioBackendPanel.Name = "waveAudioBackendPanel";
            waveAudioBackendPanel.Size = new Size(311, 206);
            waveAudioBackendPanel.TabIndex = 25;
            //
            // asioAudioBackendPanel
            //
            asioAudioBackendPanel.Location = new Point(12, 216);
            asioAudioBackendPanel.Name = "asioAudioBackendPanel";
            asioAudioBackendPanel.Size = new Size(311, 213);
            asioAudioBackendPanel.TabIndex = 26;
            //
            // MeasurementOptions
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
            ClientSize = new Size(334, 470);
            Controls.Add(asioAudioBackendPanel);
            Controls.Add(waveAudioBackendPanel);
            Controls.Add(comboBoxAudioBackend);
            Controls.Add(labelAudioBackend);
            Controls.Add(numericUpDownOctaves);
            Controls.Add(numericUpDownBits);
            Controls.Add(comboBoxSampleRate);
            Controls.Add(label6);
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
            (numericUpDownRequestedDuration).EndInit();
            (numericUpDownComputeDuration).EndInit();
            (numericUpDownBits).EndInit();
            (numericUpDownOctaves).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Label label1;
        private Label label2;
        private DarkComboBox comboBoxChannel;
        private Label label3;
        private Label label4;
        private Label label5;
        private DarkNumericUpDown numericUpDownRequestedDuration;
        private DarkNumericUpDown numericUpDownComputeDuration;
        private Button button1;
        private Label label6;
        private DarkComboBox comboBoxSampleRate;
        private DarkNumericUpDown numericUpDownBits;
        private DarkNumericUpDown numericUpDownOctaves;
        private Label labelAudioBackend;
        private DarkComboBox comboBoxAudioBackend;
        private WaveAudioBackendPanel waveAudioBackendPanel;
        private AsioAudioBackendPanel asioAudioBackendPanel;
    }
}
