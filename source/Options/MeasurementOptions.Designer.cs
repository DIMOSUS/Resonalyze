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
            labelAverageRunCount = new Label();
            numericUpDownAverageRunCount = new DarkNumericUpDown();
            checkBoxConfirmEachAverageRun = new CheckBox();
            labelCalibration0 = new Label();
            buttonCalibration0 = new Button();
            buttonClearCalibration0 = new Button();
            labelCalibration90 = new Label();
            buttonCalibration90 = new Button();
            buttonClearCalibration90 = new Button();
            waveAudioBackendPanel = new WaveAudioBackendPanel();
            asioAudioBackendPanel = new AsioAudioBackendPanel();
            (numericUpDownRequestedDuration).BeginInit();
            (numericUpDownComputeDuration).BeginInit();
            (numericUpDownBits).BeginInit();
            (numericUpDownOctaves).BeginInit();
            (numericUpDownAverageRunCount).BeginInit();
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
            button1.Location = new Point(11, 541);
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
            labelAudioBackend.Location = new Point(12, 274);
            labelAudioBackend.Name = "labelAudioBackend";
            labelAudioBackend.Size = new Size(87, 15);
            labelAudioBackend.TabIndex = 23;
            labelAudioBackend.Text = "Audio backend";
            //
            // comboBoxAudioBackend
            //
            comboBoxAudioBackend.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxAudioBackend.ForeColor = Color.White;
            comboBoxAudioBackend.Location = new Point(153, 266);
            comboBoxAudioBackend.Margin = new Padding(0);
            comboBoxAudioBackend.MinimumSize = new Size(36, 19);
            comboBoxAudioBackend.Name = "comboBoxAudioBackend";
            comboBoxAudioBackend.Size = new Size(170, 23);
            comboBoxAudioBackend.TabIndex = 24;
            comboBoxAudioBackend.SelectedIndexChanged += comboBoxAudioBackend_SelectedIndexChanged;
            //
            // labelAverageRunCount
            //
            labelAverageRunCount.AutoSize = true;
            labelAverageRunCount.ForeColor = SystemColors.ControlLight;
            labelAverageRunCount.Location = new Point(12, 174);
            labelAverageRunCount.Name = "labelAverageRunCount";
            labelAverageRunCount.Size = new Size(84, 15);
            labelAverageRunCount.TabIndex = 27;
            labelAverageRunCount.Text = "Measurements";
            //
            // numericUpDownAverageRunCount
            //
            numericUpDownAverageRunCount.BackColor = Color.FromArgb(55, 60, 72);
            numericUpDownAverageRunCount.DecimalPlaces = 0;
            numericUpDownAverageRunCount.ForeColor = Color.White;
            numericUpDownAverageRunCount.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDownAverageRunCount.Location = new Point(153, 170);
            numericUpDownAverageRunCount.Maximum = new decimal(new int[] { 64, 0, 0, 0 });
            numericUpDownAverageRunCount.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDownAverageRunCount.MinimumSize = new Size(36, 19);
            numericUpDownAverageRunCount.Name = "numericUpDownAverageRunCount";
            numericUpDownAverageRunCount.Size = new Size(170, 19);
            numericUpDownAverageRunCount.TabIndex = 28;
            numericUpDownAverageRunCount.TextAlign = HorizontalAlignment.Right;
            numericUpDownAverageRunCount.ThousandsSeparator = false;
            numericUpDownAverageRunCount.Value = new decimal(new int[] { 1, 0, 0, 0 });
            //
            // checkBoxConfirmEachAverageRun
            //
            checkBoxConfirmEachAverageRun.AutoSize = true;
            checkBoxConfirmEachAverageRun.ForeColor = SystemColors.ControlLight;
            checkBoxConfirmEachAverageRun.Location = new Point(153, 194);
            checkBoxConfirmEachAverageRun.Name = "checkBoxConfirmEachAverageRun";
            checkBoxConfirmEachAverageRun.Size = new Size(119, 19);
            checkBoxConfirmEachAverageRun.TabIndex = 29;
            checkBoxConfirmEachAverageRun.Text = "Confirm each run";
            checkBoxConfirmEachAverageRun.UseVisualStyleBackColor = true;
            //
            // labelCalibration0
            //
            labelCalibration0.AutoSize = true;
            labelCalibration0.ForeColor = SystemColors.ControlLight;
            labelCalibration0.Location = new Point(12, 224);
            labelCalibration0.Name = "labelCalibration0";
            labelCalibration0.Size = new Size(99, 15);
            labelCalibration0.TabIndex = 30;
            labelCalibration0.Text = "Mic calibration 0°";
            //
            // buttonCalibration0
            //
            buttonCalibration0.FlatStyle = FlatStyle.Popup;
            buttonCalibration0.ForeColor = Color.White;
            buttonCalibration0.Location = new Point(153, 219);
            buttonCalibration0.Name = "buttonCalibration0";
            buttonCalibration0.Size = new Size(143, 23);
            buttonCalibration0.TabIndex = 31;
            buttonCalibration0.Text = "Select file...";
            buttonCalibration0.UseVisualStyleBackColor = true;
            buttonCalibration0.Click += buttonCalibration0_Click;
            //
            // buttonClearCalibration0
            //
            buttonClearCalibration0.FlatStyle = FlatStyle.Popup;
            buttonClearCalibration0.ForeColor = Color.White;
            buttonClearCalibration0.Location = new Point(299, 219);
            buttonClearCalibration0.Name = "buttonClearCalibration0";
            buttonClearCalibration0.Size = new Size(24, 23);
            buttonClearCalibration0.TabIndex = 34;
            buttonClearCalibration0.Text = "X";
            buttonClearCalibration0.UseVisualStyleBackColor = true;
            buttonClearCalibration0.Click += buttonClearCalibration0_Click;
            //
            // labelCalibration90
            //
            labelCalibration90.AutoSize = true;
            labelCalibration90.ForeColor = SystemColors.ControlLight;
            labelCalibration90.Location = new Point(12, 249);
            labelCalibration90.Name = "labelCalibration90";
            labelCalibration90.Size = new Size(105, 15);
            labelCalibration90.TabIndex = 32;
            labelCalibration90.Text = "Mic calibration 90°";
            //
            // buttonCalibration90
            //
            buttonCalibration90.FlatStyle = FlatStyle.Popup;
            buttonCalibration90.ForeColor = Color.White;
            buttonCalibration90.Location = new Point(153, 244);
            buttonCalibration90.Name = "buttonCalibration90";
            buttonCalibration90.Size = new Size(143, 23);
            buttonCalibration90.TabIndex = 33;
            buttonCalibration90.Text = "Select file...";
            buttonCalibration90.UseVisualStyleBackColor = true;
            buttonCalibration90.Click += buttonCalibration90_Click;
            //
            // buttonClearCalibration90
            //
            buttonClearCalibration90.FlatStyle = FlatStyle.Popup;
            buttonClearCalibration90.ForeColor = Color.White;
            buttonClearCalibration90.Location = new Point(299, 244);
            buttonClearCalibration90.Name = "buttonClearCalibration90";
            buttonClearCalibration90.Size = new Size(24, 23);
            buttonClearCalibration90.TabIndex = 35;
            buttonClearCalibration90.Text = "X";
            buttonClearCalibration90.UseVisualStyleBackColor = true;
            buttonClearCalibration90.Click += buttonClearCalibration90_Click;
            //
            // waveAudioBackendPanel
            //
            waveAudioBackendPanel.Location = new Point(12, 304);
            waveAudioBackendPanel.Name = "waveAudioBackendPanel";
            waveAudioBackendPanel.Size = new Size(311, 206);
            waveAudioBackendPanel.TabIndex = 25;
            //
            // asioAudioBackendPanel
            //
            asioAudioBackendPanel.Location = new Point(12, 304);
            asioAudioBackendPanel.Name = "asioAudioBackendPanel";
            asioAudioBackendPanel.Size = new Size(311, 213);
            asioAudioBackendPanel.TabIndex = 26;
            //
            // MeasurementOptions
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
            ClientSize = new Size(334, 574);
            Controls.Add(asioAudioBackendPanel);
            Controls.Add(waveAudioBackendPanel);
            Controls.Add(buttonClearCalibration90);
            Controls.Add(buttonCalibration90);
            Controls.Add(labelCalibration90);
            Controls.Add(buttonClearCalibration0);
            Controls.Add(buttonCalibration0);
            Controls.Add(labelCalibration0);
            Controls.Add(checkBoxConfirmEachAverageRun);
            Controls.Add(numericUpDownAverageRunCount);
            Controls.Add(labelAverageRunCount);
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
            (numericUpDownAverageRunCount).EndInit();
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
        private Label labelAverageRunCount;
        private DarkNumericUpDown numericUpDownAverageRunCount;
        private CheckBox checkBoxConfirmEachAverageRun;
        private Label labelCalibration0;
        private Button buttonCalibration0;
        private Button buttonClearCalibration0;
        private Label labelCalibration90;
        private Button buttonCalibration90;
        private Button buttonClearCalibration90;
        private WaveAudioBackendPanel waveAudioBackendPanel;
        private AsioAudioBackendPanel asioAudioBackendPanel;
    }
}
