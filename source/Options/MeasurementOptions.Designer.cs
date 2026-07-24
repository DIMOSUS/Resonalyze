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
            sweepPanel = new Panel();
            labelActualRangeCaption = new Label();
            numericUpDownRequestedDuration = new DarkNumericUpDown();
            label4 = new Label();
            numericUpDownHighFrequency = new DarkNumericUpDown();
            labelHighFrequency = new Label();
            numericUpDownLowFrequency = new DarkNumericUpDown();
            labelLowFrequency = new Label();
            numericUpDownBits = new DarkNumericUpDown();
            label2 = new Label();
            comboBoxSampleRate = new DarkComboBox();
            label1 = new Label();
            comboBoxChannel = new DarkComboBox();
            label3 = new Label();
            button1 = new Button();
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
            labelSplCalibration = new Label();
            buttonSplCalibration = new Button();
            buttonClearSplCalibration = new Button();
            waveAudioBackendPanel = new WaveAudioBackendPanel();
            asioAudioBackendPanel = new AsioAudioBackendPanel();
            sweepPanel.SuspendLayout();
            (numericUpDownRequestedDuration).BeginInit();
            (numericUpDownHighFrequency).BeginInit();
            (numericUpDownLowFrequency).BeginInit();
            (numericUpDownBits).BeginInit();
            (numericUpDownAverageRunCount).BeginInit();
            SuspendLayout();
            // 
            // sweepPanel
            // 
            sweepPanel.BackColor = Color.FromArgb(50, 55, 66);
            sweepPanel.BorderStyle = BorderStyle.FixedSingle;
            sweepPanel.Controls.Add(labelActualRangeCaption);
            sweepPanel.Controls.Add(numericUpDownRequestedDuration);
            sweepPanel.Controls.Add(label4);
            sweepPanel.Controls.Add(numericUpDownHighFrequency);
            sweepPanel.Controls.Add(labelHighFrequency);
            sweepPanel.Controls.Add(numericUpDownLowFrequency);
            sweepPanel.Controls.Add(labelLowFrequency);
            sweepPanel.Controls.Add(numericUpDownBits);
            sweepPanel.Controls.Add(label2);
            sweepPanel.Controls.Add(comboBoxSampleRate);
            sweepPanel.Controls.Add(label1);
            sweepPanel.Location = new Point(8, 6);
            sweepPanel.Name = "sweepPanel";
            sweepPanel.Size = new Size(320, 160);
            sweepPanel.TabIndex = 0;
            // 
            // labelActualRangeCaption
            // 
            labelActualRangeCaption.AutoSize = true;
            labelActualRangeCaption.ForeColor = Color.FromArgb(150, 200, 170);
            labelActualRangeCaption.Location = new Point(8, 138);
            labelActualRangeCaption.Name = "labelActualRangeCaption";
            labelActualRangeCaption.Size = new Size(74, 15);
            labelActualRangeCaption.TabIndex = 21;
            labelActualRangeCaption.Text = "Actual range";
            // 
            // numericUpDownRequestedDuration
            // 
            numericUpDownRequestedDuration.BackColor = Color.FromArgb(55, 60, 72);
            numericUpDownRequestedDuration.DecimalPlaces = 0;
            numericUpDownRequestedDuration.ForeColor = Color.White;
            numericUpDownRequestedDuration.Increment = new decimal(new int[] { 25, 0, 0, 0 });
            numericUpDownRequestedDuration.Location = new Point(145, 112);
            numericUpDownRequestedDuration.Maximum = new decimal(new int[] { 20000, 0, 0, 0 });
            numericUpDownRequestedDuration.Minimum = new decimal(new int[] { 5, 0, 0, 0 });
            numericUpDownRequestedDuration.MinimumSize = new Size(36, 19);
            numericUpDownRequestedDuration.Name = "numericUpDownRequestedDuration";
            numericUpDownRequestedDuration.Size = new Size(170, 19);
            numericUpDownRequestedDuration.TabIndex = 10;
            numericUpDownRequestedDuration.TextAlign = HorizontalAlignment.Right;
            numericUpDownRequestedDuration.ThousandsSeparator = false;
            numericUpDownRequestedDuration.Value = new decimal(new int[] { 200, 0, 0, 0 });
            numericUpDownRequestedDuration.ValueChanged += numericUpDownRequestedDuration_ValueChanged;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.ForeColor = SystemColors.ControlLight;
            label4.Location = new Point(8, 116);
            label4.Name = "label4";
            label4.Size = new Size(89, 15);
            label4.TabIndex = 7;
            label4.Text = "Per octave (ms)";
            // 
            // numericUpDownHighFrequency
            // 
            numericUpDownHighFrequency.BackColor = Color.FromArgb(55, 60, 72);
            numericUpDownHighFrequency.DecimalPlaces = 0;
            numericUpDownHighFrequency.ForeColor = Color.White;
            numericUpDownHighFrequency.Increment = new decimal(new int[] { 500, 0, 0, 0 });
            numericUpDownHighFrequency.Location = new Point(145, 87);
            numericUpDownHighFrequency.Maximum = new decimal(new int[] { 20000, 0, 0, 0 });
            numericUpDownHighFrequency.Minimum = new decimal(new int[] { 20, 0, 0, 0 });
            numericUpDownHighFrequency.MinimumSize = new Size(36, 19);
            numericUpDownHighFrequency.Name = "numericUpDownHighFrequency";
            numericUpDownHighFrequency.Size = new Size(170, 19);
            numericUpDownHighFrequency.TabIndex = 19;
            numericUpDownHighFrequency.TextAlign = HorizontalAlignment.Right;
            numericUpDownHighFrequency.ThousandsSeparator = false;
            numericUpDownHighFrequency.Value = new decimal(new int[] { 20000, 0, 0, 0 });
            numericUpDownHighFrequency.ValueChanged += numericUpDownSweepBand_ValueChanged;
            // 
            // labelHighFrequency
            // 
            labelHighFrequency.AutoSize = true;
            labelHighFrequency.ForeColor = SystemColors.ControlLight;
            labelHighFrequency.Location = new Point(8, 91);
            labelHighFrequency.Name = "labelHighFrequency";
            labelHighFrequency.Size = new Size(114, 15);
            labelHighFrequency.TabIndex = 20;
            labelHighFrequency.Text = "High frequency (Hz)";
            // 
            // numericUpDownLowFrequency
            // 
            numericUpDownLowFrequency.BackColor = Color.FromArgb(55, 60, 72);
            numericUpDownLowFrequency.DecimalPlaces = 0;
            numericUpDownLowFrequency.ForeColor = Color.White;
            numericUpDownLowFrequency.Increment = new decimal(new int[] { 5, 0, 0, 0 });
            numericUpDownLowFrequency.Location = new Point(145, 62);
            numericUpDownLowFrequency.Maximum = new decimal(new int[] { 20000, 0, 0, 0 });
            numericUpDownLowFrequency.Minimum = new decimal(new int[] { 20, 0, 0, 0 });
            numericUpDownLowFrequency.MinimumSize = new Size(36, 19);
            numericUpDownLowFrequency.Name = "numericUpDownLowFrequency";
            numericUpDownLowFrequency.Size = new Size(170, 19);
            numericUpDownLowFrequency.TabIndex = 18;
            numericUpDownLowFrequency.TextAlign = HorizontalAlignment.Right;
            numericUpDownLowFrequency.ThousandsSeparator = false;
            numericUpDownLowFrequency.Value = new decimal(new int[] { 20, 0, 0, 0 });
            numericUpDownLowFrequency.ValueChanged += numericUpDownSweepBand_ValueChanged;
            // 
            // labelLowFrequency
            // 
            labelLowFrequency.AutoSize = true;
            labelLowFrequency.ForeColor = SystemColors.ControlLight;
            labelLowFrequency.Location = new Point(8, 66);
            labelLowFrequency.Name = "labelLowFrequency";
            labelLowFrequency.Size = new Size(110, 15);
            labelLowFrequency.TabIndex = 15;
            labelLowFrequency.Text = "Low frequency (Hz)";
            // 
            // numericUpDownBits
            // 
            numericUpDownBits.BackColor = Color.FromArgb(55, 60, 72);
            numericUpDownBits.DecimalPlaces = 0;
            numericUpDownBits.Enabled = false;
            numericUpDownBits.ForeColor = Color.White;
            numericUpDownBits.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDownBits.Location = new Point(145, 37);
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
            // label2
            // 
            label2.AutoSize = true;
            label2.ForeColor = SystemColors.ControlLight;
            label2.Location = new Point(8, 41);
            label2.Name = "label2";
            label2.Size = new Size(26, 15);
            label2.TabIndex = 3;
            label2.Text = "Bits";
            // 
            // comboBoxSampleRate
            // 
            comboBoxSampleRate.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxSampleRate.ForeColor = Color.White;
            comboBoxSampleRate.Location = new Point(145, 8);
            comboBoxSampleRate.Margin = new Padding(0);
            comboBoxSampleRate.MinimumSize = new Size(36, 19);
            comboBoxSampleRate.Name = "comboBoxSampleRate";
            comboBoxSampleRate.Size = new Size(170, 23);
            comboBoxSampleRate.TabIndex = 16;
            comboBoxSampleRate.SelectedIndexChanged += comboBoxSampleRate_SelectedIndexChanged;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.ForeColor = SystemColors.ControlLight;
            label1.Location = new Point(8, 12);
            label1.Name = "label1";
            label1.Size = new Size(72, 15);
            label1.TabIndex = 1;
            label1.Text = "Sample Rate";
            // 
            // comboBoxChannel
            // 
            comboBoxChannel.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxChannel.ForeColor = Color.White;
            comboBoxChannel.Location = new Point(153, 174);
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
            label3.Location = new Point(12, 182);
            label3.Name = "label3";
            label3.Size = new Size(51, 15);
            label3.TabIndex = 5;
            label3.Text = "Channel";
            // 
            // button1
            // 
            button1.DialogResult = DialogResult.OK;
            button1.FlatStyle = FlatStyle.Popup;
            button1.ForeColor = Color.White;
            button1.Location = new Point(12, 613);
            button1.Name = "button1";
            button1.Size = new Size(311, 23);
            button1.TabIndex = 12;
            button1.Text = "Apply settings";
            button1.UseVisualStyleBackColor = true;
            // 
            // labelAudioBackend
            // 
            labelAudioBackend.AutoSize = true;
            labelAudioBackend.ForeColor = SystemColors.ControlLight;
            labelAudioBackend.Location = new Point(12, 344);
            labelAudioBackend.Name = "labelAudioBackend";
            labelAudioBackend.Size = new Size(87, 15);
            labelAudioBackend.TabIndex = 23;
            labelAudioBackend.Text = "Audio backend";
            // 
            // comboBoxAudioBackend
            // 
            comboBoxAudioBackend.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxAudioBackend.ForeColor = Color.White;
            comboBoxAudioBackend.Location = new Point(153, 336);
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
            labelAverageRunCount.Location = new Point(12, 208);
            labelAverageRunCount.Name = "labelAverageRunCount";
            labelAverageRunCount.Size = new Size(85, 15);
            labelAverageRunCount.TabIndex = 27;
            labelAverageRunCount.Text = "Measurements";
            // 
            // numericUpDownAverageRunCount
            // 
            numericUpDownAverageRunCount.BackColor = Color.FromArgb(55, 60, 72);
            numericUpDownAverageRunCount.DecimalPlaces = 0;
            numericUpDownAverageRunCount.ForeColor = Color.White;
            numericUpDownAverageRunCount.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDownAverageRunCount.Location = new Point(153, 204);
            numericUpDownAverageRunCount.Maximum = new decimal(new int[] { 64, 0, 0, 0 });
            numericUpDownAverageRunCount.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
            numericUpDownAverageRunCount.MinimumSize = new Size(36, 19);
            numericUpDownAverageRunCount.Name = "numericUpDownAverageRunCount";
            numericUpDownAverageRunCount.Size = new Size(170, 19);
            numericUpDownAverageRunCount.TabIndex = 28;
            numericUpDownAverageRunCount.TextAlign = HorizontalAlignment.Right;
            numericUpDownAverageRunCount.ThousandsSeparator = false;
            numericUpDownAverageRunCount.Value = new decimal(new int[] { 2, 0, 0, 0 });
            // 
            // checkBoxConfirmEachAverageRun
            // 
            checkBoxConfirmEachAverageRun.AutoSize = true;
            checkBoxConfirmEachAverageRun.ForeColor = SystemColors.ControlLight;
            checkBoxConfirmEachAverageRun.Location = new Point(153, 228);
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
            labelCalibration0.Location = new Point(12, 258);
            labelCalibration0.Name = "labelCalibration0";
            labelCalibration0.Size = new Size(100, 15);
            labelCalibration0.TabIndex = 30;
            labelCalibration0.Text = "Mic calibration 0°";
            // 
            // buttonCalibration0
            // 
            buttonCalibration0.FlatStyle = FlatStyle.Popup;
            buttonCalibration0.ForeColor = Color.White;
            buttonCalibration0.Location = new Point(153, 253);
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
            buttonClearCalibration0.Location = new Point(299, 253);
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
            labelCalibration90.Location = new Point(12, 283);
            labelCalibration90.Name = "labelCalibration90";
            labelCalibration90.Size = new Size(106, 15);
            labelCalibration90.TabIndex = 32;
            labelCalibration90.Text = "Mic calibration 90°";
            // 
            // buttonCalibration90
            // 
            buttonCalibration90.FlatStyle = FlatStyle.Popup;
            buttonCalibration90.ForeColor = Color.White;
            buttonCalibration90.Location = new Point(153, 278);
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
            buttonClearCalibration90.Location = new Point(299, 278);
            buttonClearCalibration90.Name = "buttonClearCalibration90";
            buttonClearCalibration90.Size = new Size(24, 23);
            buttonClearCalibration90.TabIndex = 35;
            buttonClearCalibration90.Text = "X";
            buttonClearCalibration90.UseVisualStyleBackColor = true;
            buttonClearCalibration90.Click += buttonClearCalibration90_Click;
            // 
            // labelSplCalibration
            // 
            labelSplCalibration.AutoSize = true;
            labelSplCalibration.ForeColor = SystemColors.ControlLight;
            labelSplCalibration.Location = new Point(12, 308);
            labelSplCalibration.Name = "labelSplCalibration";
            labelSplCalibration.Size = new Size(85, 15);
            labelSplCalibration.TabIndex = 36;
            labelSplCalibration.Text = "SPL calibration";
            // 
            // buttonSplCalibration
            // 
            buttonSplCalibration.FlatStyle = FlatStyle.Popup;
            buttonSplCalibration.ForeColor = Color.White;
            buttonSplCalibration.Location = new Point(153, 303);
            buttonSplCalibration.Name = "buttonSplCalibration";
            buttonSplCalibration.Size = new Size(143, 23);
            buttonSplCalibration.TabIndex = 37;
            buttonSplCalibration.Text = "Calibrate...";
            buttonSplCalibration.UseVisualStyleBackColor = true;
            buttonSplCalibration.Click += buttonSplCalibration_Click;
            // 
            // buttonClearSplCalibration
            // 
            buttonClearSplCalibration.FlatStyle = FlatStyle.Popup;
            buttonClearSplCalibration.ForeColor = Color.White;
            buttonClearSplCalibration.Location = new Point(299, 303);
            buttonClearSplCalibration.Name = "buttonClearSplCalibration";
            buttonClearSplCalibration.Size = new Size(24, 23);
            buttonClearSplCalibration.TabIndex = 38;
            buttonClearSplCalibration.Text = "X";
            buttonClearSplCalibration.UseVisualStyleBackColor = true;
            buttonClearSplCalibration.Click += buttonClearSplCalibration_Click;
            // 
            // waveAudioBackendPanel
            // 
            waveAudioBackendPanel.BackColor = Color.FromArgb(45, 50, 60);
            waveAudioBackendPanel.Location = new Point(12, 369);
            waveAudioBackendPanel.Name = "waveAudioBackendPanel";
            waveAudioBackendPanel.Size = new Size(311, 213);
            waveAudioBackendPanel.TabIndex = 25;
            // 
            // asioAudioBackendPanel
            // 
            asioAudioBackendPanel.BackColor = Color.FromArgb(45, 50, 60);
            asioAudioBackendPanel.Location = new Point(12, 369);
            asioAudioBackendPanel.Name = "asioAudioBackendPanel";
            asioAudioBackendPanel.Size = new Size(311, 213);
            asioAudioBackendPanel.TabIndex = 26;
            // 
            // MeasurementOptions
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
            ClientSize = new Size(334, 648);
            Controls.Add(asioAudioBackendPanel);
            Controls.Add(waveAudioBackendPanel);
            Controls.Add(buttonClearSplCalibration);
            Controls.Add(buttonSplCalibration);
            Controls.Add(labelSplCalibration);
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
            Controls.Add(comboBoxChannel);
            Controls.Add(label3);
            Controls.Add(button1);
            Controls.Add(sweepPanel);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "MeasurementOptions";
            ShowInTaskbar = false;
            Text = "Measurement Options";
            sweepPanel.ResumeLayout(false);
            sweepPanel.PerformLayout();
            (numericUpDownRequestedDuration).EndInit();
            (numericUpDownHighFrequency).EndInit();
            (numericUpDownLowFrequency).EndInit();
            (numericUpDownBits).EndInit();
            (numericUpDownAverageRunCount).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Panel sweepPanel;
        private Label label1;
        private Label label2;
        private DarkComboBox comboBoxChannel;
        private Label label3;
        private Label label4;
        private DarkNumericUpDown numericUpDownRequestedDuration;
        private Button button1;
        private Label labelLowFrequency;
        private DarkComboBox comboBoxSampleRate;
        private DarkNumericUpDown numericUpDownBits;
        private DarkNumericUpDown numericUpDownLowFrequency;
        private DarkNumericUpDown numericUpDownHighFrequency;
        private Label labelHighFrequency;
        private Label labelActualRangeCaption;
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
        private Label labelSplCalibration;
        private Button buttonSplCalibration;
        private Button buttonClearSplCalibration;
        private WaveAudioBackendPanel waveAudioBackendPanel;
        private AsioAudioBackendPanel asioAudioBackendPanel;
    }
}
