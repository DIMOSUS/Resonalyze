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
            buttonSaveSweepFile = new Button();
            labelActualRangeCaption = new Label();
            numericUpDownRequestedDuration = new DarkNumericUpDown();
            label4 = new Label();
            numericUpDownHighFrequency = new DarkNumericUpDown();
            labelHighFrequency = new Label();
            numericUpDownLowFrequency = new DarkNumericUpDown();
            labelLowFrequency = new Label();
            labelProtectiveHighPass = new Label();
            labelProtectiveHighPassParameters = new Label();
            comboBoxProtectiveHighPassKind = new DarkComboBox();
            numericUpDownProtectiveHighPassFrequency = new DarkNumericUpDown();
            comboBoxProtectiveHighPassSlope = new DarkComboBox();
            numericUpDownBits = new DarkNumericUpDown();
            label2 = new Label();
            comboBoxSampleRate = new DarkComboBox();
            label1 = new Label();
            comboBoxChannel = new DarkComboBox();
            label3 = new Label();
            audioBackendPanel = new Panel();
            button1 = new Button();
            labelAudioBackend = new Label();
            comboBoxAudioBackend = new DarkComboBox();
            labelAverageRunCount = new Label();
            numericUpDownAverageRunCount = new DarkNumericUpDown();
            checkBoxConfirmEachAverageRun = new CheckBox();
            labelCalibration0 = new Label();
            buttonCalibration0 = new Button();
            buttonClearCalibration0 = new Button();
            labelCalibrationExtra = new Label();
            buttonCalibrationExtra = new Button();
            labelSplCalibration = new Label();
            buttonSplCalibration = new Button();
            buttonClearSplCalibration = new Button();
            waveAudioBackendPanel = new WaveAudioBackendPanel();
            asioAudioBackendPanel = new AsioAudioBackendPanel();
            sweepPanel.SuspendLayout();
            audioBackendPanel.SuspendLayout();
            (numericUpDownRequestedDuration).BeginInit();
            (numericUpDownHighFrequency).BeginInit();
            (numericUpDownLowFrequency).BeginInit();
            (numericUpDownProtectiveHighPassFrequency).BeginInit();
            (numericUpDownBits).BeginInit();
            (numericUpDownAverageRunCount).BeginInit();
            SuspendLayout();
            // 
            // sweepPanel
            // 
            sweepPanel.BackColor = Color.FromArgb(50, 55, 66);
            sweepPanel.BorderStyle = BorderStyle.FixedSingle;
            sweepPanel.Controls.Add(buttonSaveSweepFile);
            sweepPanel.Controls.Add(comboBoxProtectiveHighPassSlope);
            sweepPanel.Controls.Add(numericUpDownProtectiveHighPassFrequency);
            sweepPanel.Controls.Add(comboBoxProtectiveHighPassKind);
            sweepPanel.Controls.Add(labelProtectiveHighPassParameters);
            sweepPanel.Controls.Add(labelProtectiveHighPass);
            sweepPanel.Controls.Add(labelActualRangeCaption);
            sweepPanel.Controls.Add(numericUpDownRequestedDuration);
            sweepPanel.Controls.Add(label4);
            sweepPanel.Controls.Add(numericUpDownHighFrequency);
            sweepPanel.Controls.Add(labelHighFrequency);
            sweepPanel.Controls.Add(numericUpDownLowFrequency);
            sweepPanel.Controls.Add(labelLowFrequency);
            sweepPanel.Location = new Point(8, 6);
            sweepPanel.Name = "sweepPanel";
            sweepPanel.Size = new Size(322, 184);
            sweepPanel.TabIndex = 0;
            //
            // buttonSaveSweepFile
            //
            buttonSaveSweepFile.FlatStyle = FlatStyle.Popup;
            buttonSaveSweepFile.ForeColor = Color.White;
            buttonSaveSweepFile.Location = new Point(8, 153);
            buttonSaveSweepFile.Name = "buttonSaveSweepFile";
            buttonSaveSweepFile.Size = new Size(307, 23);
            buttonSaveSweepFile.TabIndex = 22;
            buttonSaveSweepFile.Text = "Save sweep as WAV...";
            buttonSaveSweepFile.UseVisualStyleBackColor = true;
            buttonSaveSweepFile.Click += buttonSaveSweepFile_Click;
            //
            // labelActualRangeCaption
            //
            labelActualRangeCaption.AutoSize = true;
            labelActualRangeCaption.ForeColor = Color.FromArgb(150, 200, 170);
            labelActualRangeCaption.Location = new Point(8, 84);
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
            numericUpDownRequestedDuration.Location = new Point(145, 58);
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
            label4.Location = new Point(8, 62);
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
            numericUpDownHighFrequency.Location = new Point(145, 33);
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
            labelHighFrequency.Location = new Point(8, 37);
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
            numericUpDownLowFrequency.Location = new Point(145, 8);
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
            labelLowFrequency.Location = new Point(8, 12);
            labelLowFrequency.Name = "labelLowFrequency";
            labelLowFrequency.Size = new Size(110, 15);
            labelLowFrequency.TabIndex = 15;
            labelLowFrequency.Text = "Low frequency (Hz)";
            //
            // labelProtectiveHighPass
            //
            labelProtectiveHighPass.AutoSize = true;
            labelProtectiveHighPass.ForeColor = SystemColors.ControlLight;
            labelProtectiveHighPass.Location = new Point(8, 108);
            labelProtectiveHighPass.Name = "labelProtectiveHighPass";
            labelProtectiveHighPass.Size = new Size(83, 15);
            labelProtectiveHighPass.TabIndex = 23;
            labelProtectiveHighPass.Text = "Protective HPF";
            //
            // labelProtectiveHighPassParameters
            //
            labelProtectiveHighPassParameters.AutoSize = true;
            labelProtectiveHighPassParameters.ForeColor = SystemColors.ControlLight;
            labelProtectiveHighPassParameters.Location = new Point(8, 133);
            labelProtectiveHighPassParameters.Name = "labelProtectiveHighPassParameters";
            labelProtectiveHighPassParameters.Size = new Size(104, 15);
            labelProtectiveHighPassParameters.TabIndex = 24;
            labelProtectiveHighPassParameters.Text = "Corner (Hz) / slope";
            //
            // comboBoxProtectiveHighPassKind
            //
            comboBoxProtectiveHighPassKind.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxProtectiveHighPassKind.ForeColor = Color.White;
            comboBoxProtectiveHighPassKind.Location = new Point(145, 103);
            comboBoxProtectiveHighPassKind.Margin = new Padding(0);
            comboBoxProtectiveHighPassKind.MinimumSize = new Size(36, 19);
            comboBoxProtectiveHighPassKind.Name = "comboBoxProtectiveHighPassKind";
            comboBoxProtectiveHighPassKind.Size = new Size(170, 23);
            comboBoxProtectiveHighPassKind.TabIndex = 25;
            //
            // numericUpDownProtectiveHighPassFrequency
            //
            numericUpDownProtectiveHighPassFrequency.BackColor = Color.FromArgb(55, 60, 72);
            numericUpDownProtectiveHighPassFrequency.DecimalPlaces = 0;
            numericUpDownProtectiveHighPassFrequency.ForeColor = Color.White;
            numericUpDownProtectiveHighPassFrequency.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            numericUpDownProtectiveHighPassFrequency.Location = new Point(145, 130);
            numericUpDownProtectiveHighPassFrequency.Maximum = new decimal(new int[] { 20000, 0, 0, 0 });
            numericUpDownProtectiveHighPassFrequency.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            numericUpDownProtectiveHighPassFrequency.MinimumSize = new Size(36, 19);
            numericUpDownProtectiveHighPassFrequency.Name = "numericUpDownProtectiveHighPassFrequency";
            numericUpDownProtectiveHighPassFrequency.Size = new Size(70, 19);
            numericUpDownProtectiveHighPassFrequency.TabIndex = 26;
            numericUpDownProtectiveHighPassFrequency.TextAlign = HorizontalAlignment.Right;
            numericUpDownProtectiveHighPassFrequency.ThousandsSeparator = false;
            numericUpDownProtectiveHighPassFrequency.Value = new decimal(new int[] { 2000, 0, 0, 0 });
            //
            // comboBoxProtectiveHighPassSlope
            //
            comboBoxProtectiveHighPassSlope.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxProtectiveHighPassSlope.ForeColor = Color.White;
            comboBoxProtectiveHighPassSlope.Location = new Point(220, 128);
            comboBoxProtectiveHighPassSlope.Margin = new Padding(0);
            comboBoxProtectiveHighPassSlope.MinimumSize = new Size(36, 19);
            comboBoxProtectiveHighPassSlope.Name = "comboBoxProtectiveHighPassSlope";
            comboBoxProtectiveHighPassSlope.Size = new Size(95, 23);
            comboBoxProtectiveHighPassSlope.TabIndex = 27;
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
            comboBoxChannel.Location = new Point(154, 198);
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
            label3.Location = new Point(17, 206);
            label3.Name = "label3";
            label3.Size = new Size(51, 15);
            label3.TabIndex = 5;
            label3.Text = "Channel";
            //
            // audioBackendPanel
            //
            audioBackendPanel.BackColor = Color.FromArgb(50, 55, 66);
            audioBackendPanel.BorderStyle = BorderStyle.FixedSingle;
            audioBackendPanel.Controls.Add(button1);
            audioBackendPanel.Controls.Add(asioAudioBackendPanel);
            audioBackendPanel.Controls.Add(waveAudioBackendPanel);
            audioBackendPanel.Controls.Add(comboBoxAudioBackend);
            audioBackendPanel.Controls.Add(labelAudioBackend);
            audioBackendPanel.Controls.Add(numericUpDownBits);
            audioBackendPanel.Controls.Add(label2);
            audioBackendPanel.Controls.Add(comboBoxSampleRate);
            audioBackendPanel.Controls.Add(label1);
            audioBackendPanel.Location = new Point(8, 354);
            audioBackendPanel.Name = "audioBackendPanel";
            audioBackendPanel.Size = new Size(322, 368);
            audioBackendPanel.TabIndex = 39;
            //
            // button1
            //
            button1.DialogResult = DialogResult.OK;
            button1.FlatStyle = FlatStyle.Popup;
            button1.ForeColor = Color.White;
            button1.Location = new Point(8, 339);
            button1.Name = "button1";
            button1.Size = new Size(307, 23);
            button1.TabIndex = 12;
            button1.Text = "Apply settings";
            button1.UseVisualStyleBackColor = true;
            //
            // labelAudioBackend
            //
            labelAudioBackend.AutoSize = true;
            labelAudioBackend.ForeColor = SystemColors.ControlLight;
            labelAudioBackend.Location = new Point(8, 70);
            labelAudioBackend.Name = "labelAudioBackend";
            labelAudioBackend.Size = new Size(87, 15);
            labelAudioBackend.TabIndex = 23;
            labelAudioBackend.Text = "Audio backend";
            //
            // comboBoxAudioBackend
            //
            comboBoxAudioBackend.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxAudioBackend.ForeColor = Color.White;
            comboBoxAudioBackend.Location = new Point(145, 62);
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
            labelAverageRunCount.Location = new Point(17, 232);
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
            numericUpDownAverageRunCount.Location = new Point(154, 228);
            numericUpDownAverageRunCount.Maximum = new decimal(new int[] { 64, 0, 0, 0 });
            numericUpDownAverageRunCount.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDownAverageRunCount.MinimumSize = new Size(36, 19);
            numericUpDownAverageRunCount.Name = "numericUpDownAverageRunCount";
            numericUpDownAverageRunCount.Size = new Size(170, 19);
            numericUpDownAverageRunCount.TabIndex = 28;
            numericUpDownAverageRunCount.TextAlign = HorizontalAlignment.Right;
            numericUpDownAverageRunCount.ThousandsSeparator = false;
            numericUpDownAverageRunCount.Value = new decimal(new int[] { 2, 0, 0, 0 });
            numericUpDownAverageRunCount.ValueChanged += averagingSetting_Changed;
            //
            // checkBoxConfirmEachAverageRun
            // 
            checkBoxConfirmEachAverageRun.AutoSize = true;
            checkBoxConfirmEachAverageRun.ForeColor = SystemColors.ControlLight;
            checkBoxConfirmEachAverageRun.Location = new Point(154, 252);
            checkBoxConfirmEachAverageRun.Name = "checkBoxConfirmEachAverageRun";
            checkBoxConfirmEachAverageRun.Size = new Size(119, 19);
            checkBoxConfirmEachAverageRun.TabIndex = 29;
            checkBoxConfirmEachAverageRun.Text = "Confirm each run";
            checkBoxConfirmEachAverageRun.UseVisualStyleBackColor = true;
            checkBoxConfirmEachAverageRun.CheckedChanged += averagingSetting_Changed;
            // 
            // labelCalibration0
            // 
            labelCalibration0.AutoSize = true;
            labelCalibration0.ForeColor = SystemColors.ControlLight;
            labelCalibration0.Location = new Point(17, 282);
            labelCalibration0.Name = "labelCalibration0";
            labelCalibration0.Size = new Size(100, 15);
            labelCalibration0.TabIndex = 30;
            labelCalibration0.Text = "Mic calibration 0°";
            // 
            // buttonCalibration0
            // 
            buttonCalibration0.FlatStyle = FlatStyle.Popup;
            buttonCalibration0.ForeColor = Color.White;
            buttonCalibration0.Location = new Point(154, 277);
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
            buttonClearCalibration0.Location = new Point(300, 277);
            buttonClearCalibration0.Name = "buttonClearCalibration0";
            buttonClearCalibration0.Size = new Size(24, 23);
            buttonClearCalibration0.TabIndex = 34;
            buttonClearCalibration0.Text = "X";
            buttonClearCalibration0.UseVisualStyleBackColor = true;
            buttonClearCalibration0.Click += buttonClearCalibration0_Click;
            // 
            // labelCalibrationExtra
            //
            labelCalibrationExtra.AutoSize = true;
            labelCalibrationExtra.ForeColor = SystemColors.ControlLight;
            labelCalibrationExtra.Location = new Point(17, 307);
            labelCalibrationExtra.Name = "labelCalibrationExtra";
            labelCalibrationExtra.Size = new Size(106, 15);
            labelCalibrationExtra.TabIndex = 32;
            labelCalibrationExtra.Text = "More calibrations";
            //
            // buttonCalibrationExtra
            //
            buttonCalibrationExtra.FlatStyle = FlatStyle.Popup;
            buttonCalibrationExtra.ForeColor = Color.White;
            buttonCalibrationExtra.Location = new Point(154, 302);
            buttonCalibrationExtra.Name = "buttonCalibrationExtra";
            buttonCalibrationExtra.Size = new Size(170, 23);
            buttonCalibrationExtra.TabIndex = 33;
            buttonCalibrationExtra.Text = "Manage...";
            buttonCalibrationExtra.UseVisualStyleBackColor = true;
            buttonCalibrationExtra.Click += buttonCalibrationExtra_Click;
            // 
            // labelSplCalibration
            // 
            labelSplCalibration.AutoSize = true;
            labelSplCalibration.ForeColor = SystemColors.ControlLight;
            labelSplCalibration.Location = new Point(17, 332);
            labelSplCalibration.Name = "labelSplCalibration";
            labelSplCalibration.Size = new Size(85, 15);
            labelSplCalibration.TabIndex = 36;
            labelSplCalibration.Text = "SPL calibration";
            // 
            // buttonSplCalibration
            // 
            buttonSplCalibration.FlatStyle = FlatStyle.Popup;
            buttonSplCalibration.ForeColor = Color.White;
            buttonSplCalibration.Location = new Point(154, 327);
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
            buttonClearSplCalibration.Location = new Point(300, 327);
            buttonClearSplCalibration.Name = "buttonClearSplCalibration";
            buttonClearSplCalibration.Size = new Size(24, 23);
            buttonClearSplCalibration.TabIndex = 38;
            buttonClearSplCalibration.Text = "X";
            buttonClearSplCalibration.UseVisualStyleBackColor = true;
            buttonClearSplCalibration.Click += buttonClearSplCalibration_Click;
            // 
            // waveAudioBackendPanel
            //
            waveAudioBackendPanel.BackColor = Color.FromArgb(50, 55, 66);
            waveAudioBackendPanel.Location = new Point(8, 95);
            waveAudioBackendPanel.Name = "waveAudioBackendPanel";
            waveAudioBackendPanel.Size = new Size(311, 213);
            waveAudioBackendPanel.TabIndex = 25;
            //
            // asioAudioBackendPanel
            //
            asioAudioBackendPanel.BackColor = Color.FromArgb(50, 55, 66);
            asioAudioBackendPanel.Location = new Point(8, 95);
            asioAudioBackendPanel.Name = "asioAudioBackendPanel";
            asioAudioBackendPanel.Size = new Size(311, 213);
            asioAudioBackendPanel.TabIndex = 26;
            // 
            // MeasurementOptions
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
            ClientSize = new Size(334, 728);
            Controls.Add(audioBackendPanel);
            Controls.Add(buttonClearSplCalibration);
            Controls.Add(buttonSplCalibration);
            Controls.Add(labelSplCalibration);
            Controls.Add(buttonCalibrationExtra);
            Controls.Add(labelCalibrationExtra);
            Controls.Add(buttonClearCalibration0);
            Controls.Add(buttonCalibration0);
            Controls.Add(labelCalibration0);
            Controls.Add(checkBoxConfirmEachAverageRun);
            Controls.Add(numericUpDownAverageRunCount);
            Controls.Add(labelAverageRunCount);
            Controls.Add(comboBoxChannel);
            Controls.Add(label3);
            Controls.Add(sweepPanel);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "MeasurementOptions";
            ShowInTaskbar = false;
            Text = "Measurement Options";
            sweepPanel.ResumeLayout(false);
            sweepPanel.PerformLayout();
            audioBackendPanel.ResumeLayout(false);
            audioBackendPanel.PerformLayout();
            (numericUpDownRequestedDuration).EndInit();
            (numericUpDownHighFrequency).EndInit();
            (numericUpDownLowFrequency).EndInit();
            (numericUpDownProtectiveHighPassFrequency).EndInit();
            (numericUpDownBits).EndInit();
            (numericUpDownAverageRunCount).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Panel sweepPanel;
        private Panel audioBackendPanel;
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
        private Button buttonSaveSweepFile;
        private Label labelProtectiveHighPass;
        private Label labelProtectiveHighPassParameters;
        private DarkComboBox comboBoxProtectiveHighPassKind;
        private DarkNumericUpDown numericUpDownProtectiveHighPassFrequency;
        private DarkComboBox comboBoxProtectiveHighPassSlope;
        private Label labelAudioBackend;
        private DarkComboBox comboBoxAudioBackend;
        private Label labelAverageRunCount;
        private DarkNumericUpDown numericUpDownAverageRunCount;
        private CheckBox checkBoxConfirmEachAverageRun;
        private Label labelCalibration0;
        private Button buttonCalibration0;
        private Button buttonClearCalibration0;
        private Label labelCalibrationExtra;
        private Button buttonCalibrationExtra;
        private Label labelSplCalibration;
        private Button buttonSplCalibration;
        private Button buttonClearSplCalibration;
        private WaveAudioBackendPanel waveAudioBackendPanel;
        private AsioAudioBackendPanel asioAudioBackendPanel;
    }
}
