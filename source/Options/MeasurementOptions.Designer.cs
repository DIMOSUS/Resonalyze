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
            comboBoxSampleRate = new ComboBox();
            numericUpDownBits = new NumericUpDown();
            numericUpDownOctaves = new NumericUpDown();
            labelPlaybackDevice = new Label();
            comboBoxPlaybackDevice = new ComboBox();
            labelRecordingDevice = new Label();
            comboBoxRecordingDevice = new ComboBox();
            labelWaveInputChannel = new Label();
            comboBoxWaveInputChannel = new ComboBox();
            labelWaveLoopbackChannel = new Label();
            comboBoxWaveLoopbackChannel = new ComboBox();
            labelWaveLoopbackStatus = new Label();
            labelAudioBackend = new Label();
            comboBoxAudioBackend = new ComboBox();
            labelAsioDriver = new Label();
            comboBoxAsioDriver = new ComboBox();
            buttonAsioControlPanel = new Button();
            labelAsioInputChannel = new Label();
            comboBoxAsioInputChannel = new ComboBox();
            labelAsioOutputChannel = new Label();
            comboBoxAsioOutputChannel = new ComboBox();
            labelAsioLoopbackChannel = new Label();
            comboBoxAsioLoopbackChannel = new ComboBox();
            labelAsioSampleRate = new Label();
            labelAsioSampleRateStatus = new Label();
            labelAsioPlaybackLatency = new Label();
            labelAsioPlaybackLatencyValue = new Label();
            buttonAsioInputProbe = new Button();
            ((System.ComponentModel.ISupportInitialize)numericUpDownRequestedDuration).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownComputeDuration).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownBits).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownOctaves).BeginInit();
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
            comboBoxChannel.FormattingEnabled = true;
            comboBoxChannel.Location = new Point(153, 140);
            comboBoxChannel.Name = "comboBoxChannel";
            comboBoxChannel.Size = new Size(100, 23);
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
            numericUpDownRequestedDuration.BorderStyle = BorderStyle.None;
            numericUpDownRequestedDuration.Increment = new decimal(new int[] { 500, 0, 0, 0 });
            numericUpDownRequestedDuration.Location = new Point(153, 91);
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
            numericUpDownComputeDuration.Location = new Point(153, 115);
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
            button2.Location = new Point(153, 606);
            button2.Name = "button2";
            button2.Size = new Size(100, 23);
            button2.TabIndex = 13;
            button2.Text = "Cancel";
            button2.UseVisualStyleBackColor = true;
            // 
            // button1
            // 
            button1.DialogResult = DialogResult.OK;
            button1.Location = new Point(12, 606);
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
            label6.Location = new Point(12, 70);
            label6.Name = "label6";
            label6.Size = new Size(49, 15);
            label6.TabIndex = 15;
            label6.Text = "Octaves";
            // 
            // comboBoxSampleRate
            // 
            comboBoxSampleRate.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxSampleRate.FormattingEnabled = true;
            comboBoxSampleRate.Location = new Point(153, 12);
            comboBoxSampleRate.Name = "comboBoxSampleRate";
            comboBoxSampleRate.Size = new Size(100, 23);
            comboBoxSampleRate.TabIndex = 16;
            comboBoxSampleRate.SelectedIndexChanged += comboBoxSampleRate_SelectedIndexChanged;
            // 
            // numericUpDownBits
            // 
            numericUpDownBits.BorderStyle = BorderStyle.None;
            numericUpDownBits.Enabled = false;
            numericUpDownBits.Location = new Point(153, 41);
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
            numericUpDownOctaves.Location = new Point(153, 66);
            numericUpDownOctaves.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDownOctaves.Name = "numericUpDownOctaves";
            numericUpDownOctaves.ReadOnly = true;
            numericUpDownOctaves.Size = new Size(100, 19);
            numericUpDownOctaves.TabIndex = 18;
            numericUpDownOctaves.TextAlign = HorizontalAlignment.Right;
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
            comboBoxPlaybackDevice.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxPlaybackDevice.FormattingEnabled = true;
            comboBoxPlaybackDevice.Location = new Point(153, 216);
            comboBoxPlaybackDevice.Name = "comboBoxPlaybackDevice";
            comboBoxPlaybackDevice.Size = new Size(300, 23);
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
            comboBoxRecordingDevice.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxRecordingDevice.FormattingEnabled = true;
            comboBoxRecordingDevice.Location = new Point(153, 245);
            comboBoxRecordingDevice.Name = "comboBoxRecordingDevice";
            comboBoxRecordingDevice.Size = new Size(300, 23);
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
            comboBoxWaveInputChannel.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxWaveInputChannel.FormattingEnabled = true;
            comboBoxWaveInputChannel.Location = new Point(153, 274);
            comboBoxWaveInputChannel.Name = "comboBoxWaveInputChannel";
            comboBoxWaveInputChannel.Size = new Size(300, 23);
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
            comboBoxWaveLoopbackChannel.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxWaveLoopbackChannel.FormattingEnabled = true;
            comboBoxWaveLoopbackChannel.Location = new Point(153, 303);
            comboBoxWaveLoopbackChannel.Name = "comboBoxWaveLoopbackChannel";
            comboBoxWaveLoopbackChannel.Size = new Size(300, 23);
            comboBoxWaveLoopbackChannel.TabIndex = 41;
            comboBoxWaveLoopbackChannel.SelectedIndexChanged += comboBoxWaveLoopbackChannel_SelectedIndexChanged;
            // 
            // labelWaveLoopbackStatus
            // 
            labelWaveLoopbackStatus.ForeColor = SystemColors.ControlLight;
            labelWaveLoopbackStatus.Location = new Point(12, 336);
            labelWaveLoopbackStatus.Name = "labelWaveLoopbackStatus";
            labelWaveLoopbackStatus.Size = new Size(441, 32);
            labelWaveLoopbackStatus.TabIndex = 42;
            labelWaveLoopbackStatus.Text = "-";
            // 
            // labelAudioBackend
            // 
            labelAudioBackend.AutoSize = true;
            labelAudioBackend.ForeColor = SystemColors.ControlLight;
            labelAudioBackend.Location = new Point(12, 195);
            labelAudioBackend.Name = "labelAudioBackend";
            labelAudioBackend.Size = new Size(87, 15);
            labelAudioBackend.TabIndex = 23;
            labelAudioBackend.Text = "Audio backend";
            // 
            // comboBoxAudioBackend
            // 
            comboBoxAudioBackend.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxAudioBackend.FormattingEnabled = true;
            comboBoxAudioBackend.Location = new Point(153, 187);
            comboBoxAudioBackend.Name = "comboBoxAudioBackend";
            comboBoxAudioBackend.Size = new Size(100, 23);
            comboBoxAudioBackend.TabIndex = 24;
            comboBoxAudioBackend.SelectedIndexChanged += comboBoxAudioBackend_SelectedIndexChanged;
            // 
            // labelAsioDriver
            // 
            labelAsioDriver.AutoSize = true;
            labelAsioDriver.ForeColor = SystemColors.ControlLight;
            labelAsioDriver.Location = new Point(12, 369);
            labelAsioDriver.Name = "labelAsioDriver";
            labelAsioDriver.Size = new Size(66, 15);
            labelAsioDriver.TabIndex = 25;
            labelAsioDriver.Text = "ASIO driver";
            // 
            // comboBoxAsioDriver
            // 
            comboBoxAsioDriver.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxAsioDriver.FormattingEnabled = true;
            comboBoxAsioDriver.Location = new Point(153, 361);
            comboBoxAsioDriver.Name = "comboBoxAsioDriver";
            comboBoxAsioDriver.Size = new Size(300, 23);
            comboBoxAsioDriver.TabIndex = 26;
            comboBoxAsioDriver.SelectedIndexChanged += comboBoxAsioDriver_SelectedIndexChanged;
            // 
            // buttonAsioControlPanel
            // 
            buttonAsioControlPanel.Location = new Point(153, 390);
            buttonAsioControlPanel.Name = "buttonAsioControlPanel";
            buttonAsioControlPanel.Size = new Size(300, 23);
            buttonAsioControlPanel.TabIndex = 37;
            buttonAsioControlPanel.Text = "ASIO Control Panel";
            buttonAsioControlPanel.UseVisualStyleBackColor = true;
            buttonAsioControlPanel.Click += buttonAsioControlPanel_Click;
            // 
            // labelAsioInputChannel
            // 
            labelAsioInputChannel.AutoSize = true;
            labelAsioInputChannel.ForeColor = SystemColors.ControlLight;
            labelAsioInputChannel.Location = new Point(12, 427);
            labelAsioInputChannel.Name = "labelAsioInputChannel";
            labelAsioInputChannel.Size = new Size(109, 15);
            labelAsioInputChannel.TabIndex = 27;
            labelAsioInputChannel.Text = "ASIO input channel";
            // 
            // comboBoxAsioInputChannel
            // 
            comboBoxAsioInputChannel.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxAsioInputChannel.FormattingEnabled = true;
            comboBoxAsioInputChannel.Location = new Point(153, 419);
            comboBoxAsioInputChannel.Name = "comboBoxAsioInputChannel";
            comboBoxAsioInputChannel.Size = new Size(300, 23);
            comboBoxAsioInputChannel.TabIndex = 28;
            // 
            // labelAsioOutputChannel
            // 
            labelAsioOutputChannel.AutoSize = true;
            labelAsioOutputChannel.ForeColor = SystemColors.ControlLight;
            labelAsioOutputChannel.Location = new Point(12, 456);
            labelAsioOutputChannel.Name = "labelAsioOutputChannel";
            labelAsioOutputChannel.Size = new Size(122, 15);
            labelAsioOutputChannel.TabIndex = 29;
            labelAsioOutputChannel.Text = "ASIO output channels";
            // 
            // comboBoxAsioOutputChannel
            // 
            comboBoxAsioOutputChannel.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxAsioOutputChannel.FormattingEnabled = true;
            comboBoxAsioOutputChannel.Location = new Point(153, 448);
            comboBoxAsioOutputChannel.Name = "comboBoxAsioOutputChannel";
            comboBoxAsioOutputChannel.Size = new Size(300, 23);
            comboBoxAsioOutputChannel.TabIndex = 30;
            // 
            // labelAsioLoopbackChannel
            // 
            labelAsioLoopbackChannel.AutoSize = true;
            labelAsioLoopbackChannel.ForeColor = SystemColors.ControlLight;
            labelAsioLoopbackChannel.Location = new Point(12, 485);
            labelAsioLoopbackChannel.Name = "labelAsioLoopbackChannel";
            labelAsioLoopbackChannel.Size = new Size(130, 15);
            labelAsioLoopbackChannel.TabIndex = 43;
            labelAsioLoopbackChannel.Text = "ASIO loopback channel";
            // 
            // comboBoxAsioLoopbackChannel
            // 
            comboBoxAsioLoopbackChannel.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxAsioLoopbackChannel.FormattingEnabled = true;
            comboBoxAsioLoopbackChannel.Location = new Point(153, 477);
            comboBoxAsioLoopbackChannel.Name = "comboBoxAsioLoopbackChannel";
            comboBoxAsioLoopbackChannel.Size = new Size(300, 23);
            comboBoxAsioLoopbackChannel.TabIndex = 44;
            // 
            // labelAsioSampleRate
            // 
            labelAsioSampleRate.AutoSize = true;
            labelAsioSampleRate.ForeColor = SystemColors.ControlLight;
            labelAsioSampleRate.Location = new Point(12, 510);
            labelAsioSampleRate.Name = "labelAsioSampleRate";
            labelAsioSampleRate.Size = new Size(97, 15);
            labelAsioSampleRate.TabIndex = 31;
            labelAsioSampleRate.Text = "ASIO sample rate";
            // 
            // labelAsioSampleRateStatus
            // 
            labelAsioSampleRateStatus.AutoSize = true;
            labelAsioSampleRateStatus.ForeColor = SystemColors.ControlLight;
            labelAsioSampleRateStatus.Location = new Point(156, 510);
            labelAsioSampleRateStatus.Name = "labelAsioSampleRateStatus";
            labelAsioSampleRateStatus.Size = new Size(12, 15);
            labelAsioSampleRateStatus.TabIndex = 32;
            labelAsioSampleRateStatus.Text = "-";
            // 
            // labelAsioPlaybackLatency
            // 
            labelAsioPlaybackLatency.AutoSize = true;
            labelAsioPlaybackLatency.ForeColor = SystemColors.ControlLight;
            labelAsioPlaybackLatency.Location = new Point(12, 525);
            labelAsioPlaybackLatency.Name = "labelAsioPlaybackLatency";
            labelAsioPlaybackLatency.Size = new Size(95, 15);
            labelAsioPlaybackLatency.TabIndex = 35;
            labelAsioPlaybackLatency.Text = "Playback latency";
            // 
            // labelAsioPlaybackLatencyValue
            // 
            labelAsioPlaybackLatencyValue.AutoSize = true;
            labelAsioPlaybackLatencyValue.ForeColor = SystemColors.ControlLight;
            labelAsioPlaybackLatencyValue.Location = new Point(156, 525);
            labelAsioPlaybackLatencyValue.Name = "labelAsioPlaybackLatencyValue";
            labelAsioPlaybackLatencyValue.Size = new Size(12, 15);
            labelAsioPlaybackLatencyValue.TabIndex = 36;
            labelAsioPlaybackLatencyValue.Text = "-";
            // 
            // buttonAsioInputProbe
            // 
            buttonAsioInputProbe.Location = new Point(153, 558);
            buttonAsioInputProbe.Name = "buttonAsioInputProbe";
            buttonAsioInputProbe.Size = new Size(300, 23);
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
            ClientSize = new Size(465, 642);
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
        private ComboBox comboBoxSampleRate;
        private NumericUpDown numericUpDownBits;
        private NumericUpDown numericUpDownOctaves;
        private Label labelPlaybackDevice;
        private ComboBox comboBoxPlaybackDevice;
        private Label labelRecordingDevice;
        private ComboBox comboBoxRecordingDevice;
        private Label labelWaveInputChannel;
        private ComboBox comboBoxWaveInputChannel;
        private Label labelWaveLoopbackChannel;
        private ComboBox comboBoxWaveLoopbackChannel;
        private Label labelWaveLoopbackStatus;
        private Label labelAudioBackend;
        private ComboBox comboBoxAudioBackend;
        private Label labelAsioDriver;
        private ComboBox comboBoxAsioDriver;
        private Button buttonAsioControlPanel;
        private Label labelAsioInputChannel;
        private ComboBox comboBoxAsioInputChannel;
        private Label labelAsioOutputChannel;
        private ComboBox comboBoxAsioOutputChannel;
        private Label labelAsioLoopbackChannel;
        private ComboBox comboBoxAsioLoopbackChannel;
        private Label labelAsioSampleRate;
        private Label labelAsioSampleRateStatus;
        private Label labelAsioPlaybackLatency;
        private Label labelAsioPlaybackLatencyValue;
        private Button buttonAsioInputProbe;
    }
}
