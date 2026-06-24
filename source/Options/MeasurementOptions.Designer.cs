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
            labelPlaybackDevice = new Label();
            comboBoxPlaybackDevice = new DarkComboBox();
            labelRecordingDevice = new Label();
            comboBoxRecordingDevice = new DarkComboBox();
            labelWaveInputChannel = new Label();
            comboBoxWaveInputChannel = new DarkComboBox();
            labelWaveLoopbackChannel = new Label();
            comboBoxWaveLoopbackChannel = new DarkComboBox();
            labelWaveLoopbackStatus = new Label();
            labelAudioBackend = new Label();
            comboBoxAudioBackend = new DarkComboBox();
            labelAsioDriver = new Label();
            comboBoxAsioDriver = new DarkComboBox();
            buttonAsioControlPanel = new Button();
            labelAsioInputChannel = new Label();
            comboBoxAsioInputChannel = new DarkComboBox();
            labelAsioOutputChannel = new Label();
            comboBoxAsioOutputChannel = new DarkComboBox();
            labelAsioLoopbackChannel = new Label();
            comboBoxAsioLoopbackChannel = new DarkComboBox();
            labelAsioSampleRate = new Label();
            labelAsioSampleRateStatus = new Label();
            labelAsioPlaybackLatency = new Label();
            labelAsioPlaybackLatencyValue = new Label();
            buttonAsioInputProbe = new Button();
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
            button1.Location = new Point(12, 608);
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
            // labelPlaybackDevice
            // 
            labelPlaybackDevice.AutoSize = true;
            labelPlaybackDevice.ForeColor = SystemColors.ControlLight;
            labelPlaybackDevice.Location = new Point(12, 224);
            labelPlaybackDevice.Name = "labelPlaybackDevice";
            labelPlaybackDevice.Size = new Size(91, 15);
            labelPlaybackDevice.TabIndex = 19;
            labelPlaybackDevice.Text = "Playback device";
            // 
            // comboBoxPlaybackDevice
            // 
            comboBoxPlaybackDevice.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxPlaybackDevice.ForeColor = Color.White;
            comboBoxPlaybackDevice.Location = new Point(153, 216);
            comboBoxPlaybackDevice.Margin = new Padding(0);
            comboBoxPlaybackDevice.MinimumSize = new Size(36, 19);
            comboBoxPlaybackDevice.Name = "comboBoxPlaybackDevice";
            comboBoxPlaybackDevice.Size = new Size(170, 23);
            comboBoxPlaybackDevice.TabIndex = 20;
            comboBoxPlaybackDevice.SelectedIndexChanged += comboBoxPlaybackDevice_SelectedIndexChanged;
            // 
            // labelRecordingDevice
            // 
            labelRecordingDevice.AutoSize = true;
            labelRecordingDevice.ForeColor = SystemColors.ControlLight;
            labelRecordingDevice.Location = new Point(12, 253);
            labelRecordingDevice.Name = "labelRecordingDevice";
            labelRecordingDevice.Size = new Size(98, 15);
            labelRecordingDevice.TabIndex = 21;
            labelRecordingDevice.Text = "Recording device";
            // 
            // comboBoxRecordingDevice
            // 
            comboBoxRecordingDevice.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxRecordingDevice.ForeColor = Color.White;
            comboBoxRecordingDevice.Location = new Point(153, 245);
            comboBoxRecordingDevice.Margin = new Padding(0);
            comboBoxRecordingDevice.MinimumSize = new Size(36, 19);
            comboBoxRecordingDevice.Name = "comboBoxRecordingDevice";
            comboBoxRecordingDevice.Size = new Size(170, 23);
            comboBoxRecordingDevice.TabIndex = 22;
            comboBoxRecordingDevice.SelectedIndexChanged += comboBoxRecordingDevice_SelectedIndexChanged;
            // 
            // labelWaveInputChannel
            // 
            labelWaveInputChannel.AutoSize = true;
            labelWaveInputChannel.ForeColor = SystemColors.ControlLight;
            labelWaveInputChannel.Location = new Point(12, 282);
            labelWaveInputChannel.Name = "labelWaveInputChannel";
            labelWaveInputChannel.Size = new Size(112, 15);
            labelWaveInputChannel.TabIndex = 38;
            labelWaveInputChannel.Text = "Wave input channel";
            // 
            // comboBoxWaveInputChannel
            // 
            comboBoxWaveInputChannel.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxWaveInputChannel.ForeColor = Color.White;
            comboBoxWaveInputChannel.Location = new Point(153, 274);
            comboBoxWaveInputChannel.Margin = new Padding(0);
            comboBoxWaveInputChannel.MinimumSize = new Size(36, 19);
            comboBoxWaveInputChannel.Name = "comboBoxWaveInputChannel";
            comboBoxWaveInputChannel.Size = new Size(170, 23);
            comboBoxWaveInputChannel.TabIndex = 39;
            // 
            // labelWaveLoopbackChannel
            // 
            labelWaveLoopbackChannel.AutoSize = true;
            labelWaveLoopbackChannel.ForeColor = SystemColors.ControlLight;
            labelWaveLoopbackChannel.Location = new Point(12, 311);
            labelWaveLoopbackChannel.Name = "labelWaveLoopbackChannel";
            labelWaveLoopbackChannel.Size = new Size(133, 15);
            labelWaveLoopbackChannel.TabIndex = 40;
            labelWaveLoopbackChannel.Text = "Wave loopback channel";
            // 
            // comboBoxWaveLoopbackChannel
            // 
            comboBoxWaveLoopbackChannel.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxWaveLoopbackChannel.ForeColor = Color.White;
            comboBoxWaveLoopbackChannel.Location = new Point(153, 303);
            comboBoxWaveLoopbackChannel.Margin = new Padding(0);
            comboBoxWaveLoopbackChannel.MinimumSize = new Size(36, 19);
            comboBoxWaveLoopbackChannel.Name = "comboBoxWaveLoopbackChannel";
            comboBoxWaveLoopbackChannel.Size = new Size(170, 23);
            comboBoxWaveLoopbackChannel.TabIndex = 41;
            comboBoxWaveLoopbackChannel.SelectedIndexChanged += comboBoxWaveLoopbackChannel_SelectedIndexChanged;
            // 
            // labelWaveLoopbackStatus
            // 
            labelWaveLoopbackStatus.ForeColor = SystemColors.ControlLight;
            labelWaveLoopbackStatus.Location = new Point(12, 336);
            labelWaveLoopbackStatus.Name = "labelWaveLoopbackStatus";
            labelWaveLoopbackStatus.Size = new Size(311, 32);
            labelWaveLoopbackStatus.TabIndex = 42;
            labelWaveLoopbackStatus.Text = "-";
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
            // labelAsioDriver
            // 
            labelAsioDriver.AutoSize = true;
            labelAsioDriver.ForeColor = SystemColors.ControlLight;
            labelAsioDriver.Location = new Point(12, 388);
            labelAsioDriver.Name = "labelAsioDriver";
            labelAsioDriver.Size = new Size(66, 15);
            labelAsioDriver.TabIndex = 25;
            labelAsioDriver.Text = "ASIO driver";
            // 
            // comboBoxAsioDriver
            // 
            comboBoxAsioDriver.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxAsioDriver.ForeColor = Color.White;
            comboBoxAsioDriver.Location = new Point(153, 380);
            comboBoxAsioDriver.Margin = new Padding(0);
            comboBoxAsioDriver.MinimumSize = new Size(36, 19);
            comboBoxAsioDriver.Name = "comboBoxAsioDriver";
            comboBoxAsioDriver.Size = new Size(170, 23);
            comboBoxAsioDriver.TabIndex = 26;
            comboBoxAsioDriver.SelectedIndexChanged += comboBoxAsioDriver_SelectedIndexChanged;
            // 
            // buttonAsioControlPanel
            // 
            buttonAsioControlPanel.FlatStyle = FlatStyle.Popup;
            buttonAsioControlPanel.ForeColor = Color.White;
            buttonAsioControlPanel.Location = new Point(153, 570);
            buttonAsioControlPanel.Name = "buttonAsioControlPanel";
            buttonAsioControlPanel.Size = new Size(170, 23);
            buttonAsioControlPanel.TabIndex = 37;
            buttonAsioControlPanel.Text = "ASIO Control Panel";
            buttonAsioControlPanel.UseVisualStyleBackColor = true;
            buttonAsioControlPanel.Click += buttonAsioControlPanel_Click;
            // 
            // labelAsioInputChannel
            // 
            labelAsioInputChannel.AutoSize = true;
            labelAsioInputChannel.ForeColor = SystemColors.ControlLight;
            labelAsioInputChannel.Location = new Point(12, 417);
            labelAsioInputChannel.Name = "labelAsioInputChannel";
            labelAsioInputChannel.Size = new Size(109, 15);
            labelAsioInputChannel.TabIndex = 27;
            labelAsioInputChannel.Text = "ASIO input channel";
            // 
            // comboBoxAsioInputChannel
            // 
            comboBoxAsioInputChannel.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxAsioInputChannel.ForeColor = Color.White;
            comboBoxAsioInputChannel.Location = new Point(153, 409);
            comboBoxAsioInputChannel.Margin = new Padding(0);
            comboBoxAsioInputChannel.MinimumSize = new Size(36, 19);
            comboBoxAsioInputChannel.Name = "comboBoxAsioInputChannel";
            comboBoxAsioInputChannel.Size = new Size(170, 23);
            comboBoxAsioInputChannel.TabIndex = 28;
            // 
            // labelAsioOutputChannel
            // 
            labelAsioOutputChannel.AutoSize = true;
            labelAsioOutputChannel.ForeColor = SystemColors.ControlLight;
            labelAsioOutputChannel.Location = new Point(12, 446);
            labelAsioOutputChannel.Name = "labelAsioOutputChannel";
            labelAsioOutputChannel.Size = new Size(122, 15);
            labelAsioOutputChannel.TabIndex = 29;
            labelAsioOutputChannel.Text = "ASIO output channels";
            // 
            // comboBoxAsioOutputChannel
            // 
            comboBoxAsioOutputChannel.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxAsioOutputChannel.ForeColor = Color.White;
            comboBoxAsioOutputChannel.Location = new Point(153, 438);
            comboBoxAsioOutputChannel.Margin = new Padding(0);
            comboBoxAsioOutputChannel.MinimumSize = new Size(36, 19);
            comboBoxAsioOutputChannel.Name = "comboBoxAsioOutputChannel";
            comboBoxAsioOutputChannel.Size = new Size(170, 23);
            comboBoxAsioOutputChannel.TabIndex = 30;
            // 
            // labelAsioLoopbackChannel
            // 
            labelAsioLoopbackChannel.AutoSize = true;
            labelAsioLoopbackChannel.ForeColor = SystemColors.ControlLight;
            labelAsioLoopbackChannel.Location = new Point(12, 475);
            labelAsioLoopbackChannel.Name = "labelAsioLoopbackChannel";
            labelAsioLoopbackChannel.Size = new Size(130, 15);
            labelAsioLoopbackChannel.TabIndex = 43;
            labelAsioLoopbackChannel.Text = "ASIO loopback channel";
            // 
            // comboBoxAsioLoopbackChannel
            // 
            comboBoxAsioLoopbackChannel.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxAsioLoopbackChannel.ForeColor = Color.White;
            comboBoxAsioLoopbackChannel.Location = new Point(153, 467);
            comboBoxAsioLoopbackChannel.Margin = new Padding(0);
            comboBoxAsioLoopbackChannel.MinimumSize = new Size(36, 19);
            comboBoxAsioLoopbackChannel.Name = "comboBoxAsioLoopbackChannel";
            comboBoxAsioLoopbackChannel.Size = new Size(170, 23);
            comboBoxAsioLoopbackChannel.TabIndex = 44;
            // 
            // labelAsioSampleRate
            // 
            labelAsioSampleRate.AutoSize = true;
            labelAsioSampleRate.ForeColor = SystemColors.ControlLight;
            labelAsioSampleRate.Location = new Point(12, 496);
            labelAsioSampleRate.Name = "labelAsioSampleRate";
            labelAsioSampleRate.Size = new Size(97, 15);
            labelAsioSampleRate.TabIndex = 31;
            labelAsioSampleRate.Text = "ASIO sample rate";
            // 
            // labelAsioSampleRateStatus
            // 
            labelAsioSampleRateStatus.AutoSize = true;
            labelAsioSampleRateStatus.ForeColor = SystemColors.ControlLight;
            labelAsioSampleRateStatus.Location = new Point(153, 496);
            labelAsioSampleRateStatus.Name = "labelAsioSampleRateStatus";
            labelAsioSampleRateStatus.Size = new Size(12, 15);
            labelAsioSampleRateStatus.TabIndex = 32;
            labelAsioSampleRateStatus.Text = "-";
            // 
            // labelAsioPlaybackLatency
            // 
            labelAsioPlaybackLatency.AutoSize = true;
            labelAsioPlaybackLatency.ForeColor = SystemColors.ControlLight;
            labelAsioPlaybackLatency.Location = new Point(12, 514);
            labelAsioPlaybackLatency.Name = "labelAsioPlaybackLatency";
            labelAsioPlaybackLatency.Size = new Size(95, 15);
            labelAsioPlaybackLatency.TabIndex = 35;
            labelAsioPlaybackLatency.Text = "Playback latency";
            // 
            // labelAsioPlaybackLatencyValue
            // 
            labelAsioPlaybackLatencyValue.AutoSize = true;
            labelAsioPlaybackLatencyValue.ForeColor = SystemColors.ControlLight;
            labelAsioPlaybackLatencyValue.Location = new Point(153, 514);
            labelAsioPlaybackLatencyValue.Name = "labelAsioPlaybackLatencyValue";
            labelAsioPlaybackLatencyValue.Size = new Size(12, 15);
            labelAsioPlaybackLatencyValue.TabIndex = 36;
            labelAsioPlaybackLatencyValue.Text = "-";
            // 
            // buttonAsioInputProbe
            // 
            buttonAsioInputProbe.FlatStyle = FlatStyle.Popup;
            buttonAsioInputProbe.ForeColor = Color.White;
            buttonAsioInputProbe.Location = new Point(153, 541);
            buttonAsioInputProbe.Name = "buttonAsioInputProbe";
            buttonAsioInputProbe.Size = new Size(170, 23);
            buttonAsioInputProbe.TabIndex = 45;
            buttonAsioInputProbe.Text = "Test ASIO Inputs";
            buttonAsioInputProbe.UseVisualStyleBackColor = true;
            buttonAsioInputProbe.Click += buttonAsioInputProbe_Click;
            // 
            // MeasurementOptions
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
            ClientSize = new Size(336, 642);
            Controls.Add(buttonAsioInputProbe);
            Controls.Add(comboBoxAsioLoopbackChannel);
            Controls.Add(labelAsioLoopbackChannel);
            Controls.Add(buttonAsioControlPanel);
            Controls.Add(labelAsioPlaybackLatencyValue);
            Controls.Add(labelAsioPlaybackLatency);
            Controls.Add(labelAsioSampleRateStatus);
            Controls.Add(labelAsioSampleRate);
            Controls.Add(comboBoxAsioOutputChannel);
            Controls.Add(labelAsioOutputChannel);
            Controls.Add(comboBoxAsioInputChannel);
            Controls.Add(labelAsioInputChannel);
            Controls.Add(comboBoxAsioDriver);
            Controls.Add(labelAsioDriver);
            Controls.Add(labelWaveLoopbackStatus);
            Controls.Add(comboBoxWaveLoopbackChannel);
            Controls.Add(labelWaveLoopbackChannel);
            Controls.Add(comboBoxWaveInputChannel);
            Controls.Add(labelWaveInputChannel);
            Controls.Add(comboBoxAudioBackend);
            Controls.Add(labelAudioBackend);
            Controls.Add(comboBoxRecordingDevice);
            Controls.Add(labelRecordingDevice);
            Controls.Add(comboBoxPlaybackDevice);
            Controls.Add(labelPlaybackDevice);
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
        private Label labelPlaybackDevice;
        private DarkComboBox comboBoxPlaybackDevice;
        private Label labelRecordingDevice;
        private DarkComboBox comboBoxRecordingDevice;
        private Label labelWaveInputChannel;
        private DarkComboBox comboBoxWaveInputChannel;
        private Label labelWaveLoopbackChannel;
        private DarkComboBox comboBoxWaveLoopbackChannel;
        private Label labelWaveLoopbackStatus;
        private Label labelAudioBackend;
        private DarkComboBox comboBoxAudioBackend;
        private Label labelAsioDriver;
        private DarkComboBox comboBoxAsioDriver;
        private Button buttonAsioControlPanel;
        private Label labelAsioInputChannel;
        private DarkComboBox comboBoxAsioInputChannel;
        private Label labelAsioOutputChannel;
        private DarkComboBox comboBoxAsioOutputChannel;
        private Label labelAsioLoopbackChannel;
        private DarkComboBox comboBoxAsioLoopbackChannel;
        private Label labelAsioSampleRate;
        private Label labelAsioSampleRateStatus;
        private Label labelAsioPlaybackLatency;
        private Label labelAsioPlaybackLatencyValue;
        private Button buttonAsioInputProbe;
    }
}
