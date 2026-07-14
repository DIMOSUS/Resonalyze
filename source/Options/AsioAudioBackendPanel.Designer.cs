namespace Resonalyze.Options
{
    partial class AsioAudioBackendPanel
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

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            labelAsioDriver = new Label();
            comboBoxAsioDriver = new DarkComboBox();
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
            buttonAsioControlPanel = new Button();
            SuspendLayout();
            //
            // labelAsioDriver
            //
            labelAsioDriver.AutoSize = true;
            labelAsioDriver.ForeColor = SystemColors.ControlLight;
            labelAsioDriver.Location = new Point(0, 8);
            labelAsioDriver.Name = "labelAsioDriver";
            labelAsioDriver.Size = new Size(66, 15);
            labelAsioDriver.TabIndex = 25;
            labelAsioDriver.Text = "ASIO driver";
            //
            // comboBoxAsioDriver
            //
            comboBoxAsioDriver.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxAsioDriver.ForeColor = Color.White;
            comboBoxAsioDriver.Location = new Point(141, 0);
            comboBoxAsioDriver.Margin = new Padding(0);
            comboBoxAsioDriver.MinimumSize = new Size(36, 19);
            comboBoxAsioDriver.Name = "comboBoxAsioDriver";
            comboBoxAsioDriver.Size = new Size(170, 23);
            comboBoxAsioDriver.TabIndex = 26;
            //
            // labelAsioInputChannel
            //
            labelAsioInputChannel.AutoSize = true;
            labelAsioInputChannel.ForeColor = SystemColors.ControlLight;
            labelAsioInputChannel.Location = new Point(0, 66);
            labelAsioInputChannel.Name = "labelAsioInputChannel";
            labelAsioInputChannel.Size = new Size(109, 15);
            labelAsioInputChannel.TabIndex = 29;
            labelAsioInputChannel.Text = "ASIO input channel";
            //
            // comboBoxAsioInputChannel
            //
            comboBoxAsioInputChannel.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxAsioInputChannel.ForeColor = Color.White;
            comboBoxAsioInputChannel.Location = new Point(141, 58);
            comboBoxAsioInputChannel.Margin = new Padding(0);
            comboBoxAsioInputChannel.MinimumSize = new Size(36, 19);
            comboBoxAsioInputChannel.Name = "comboBoxAsioInputChannel";
            comboBoxAsioInputChannel.Size = new Size(170, 23);
            comboBoxAsioInputChannel.TabIndex = 30;
            //
            // labelAsioOutputChannel
            //
            labelAsioOutputChannel.AutoSize = true;
            labelAsioOutputChannel.ForeColor = SystemColors.ControlLight;
            labelAsioOutputChannel.Location = new Point(-1, 37);
            labelAsioOutputChannel.Name = "labelAsioOutputChannel";
            labelAsioOutputChannel.Size = new Size(122, 15);
            labelAsioOutputChannel.TabIndex = 27;
            labelAsioOutputChannel.Text = "ASIO output channels";
            //
            // comboBoxAsioOutputChannel
            //
            comboBoxAsioOutputChannel.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxAsioOutputChannel.ForeColor = Color.White;
            comboBoxAsioOutputChannel.Location = new Point(140, 29);
            comboBoxAsioOutputChannel.Margin = new Padding(0);
            comboBoxAsioOutputChannel.MinimumSize = new Size(36, 19);
            comboBoxAsioOutputChannel.Name = "comboBoxAsioOutputChannel";
            comboBoxAsioOutputChannel.Size = new Size(170, 23);
            comboBoxAsioOutputChannel.TabIndex = 28;
            //
            // labelAsioLoopbackChannel
            //
            labelAsioLoopbackChannel.AutoSize = true;
            labelAsioLoopbackChannel.ForeColor = SystemColors.ControlLight;
            labelAsioLoopbackChannel.Location = new Point(0, 95);
            labelAsioLoopbackChannel.Name = "labelAsioLoopbackChannel";
            labelAsioLoopbackChannel.Size = new Size(130, 15);
            labelAsioLoopbackChannel.TabIndex = 43;
            labelAsioLoopbackChannel.Text = "ASIO loopback channel";
            //
            // comboBoxAsioLoopbackChannel
            //
            comboBoxAsioLoopbackChannel.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxAsioLoopbackChannel.ForeColor = Color.White;
            comboBoxAsioLoopbackChannel.Location = new Point(141, 87);
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
            labelAsioSampleRate.Location = new Point(0, 116);
            labelAsioSampleRate.Name = "labelAsioSampleRate";
            labelAsioSampleRate.Size = new Size(97, 15);
            labelAsioSampleRate.TabIndex = 31;
            labelAsioSampleRate.Text = "ASIO sample rate";
            //
            // labelAsioSampleRateStatus
            //
            labelAsioSampleRateStatus.AutoSize = true;
            labelAsioSampleRateStatus.ForeColor = SystemColors.ControlLight;
            labelAsioSampleRateStatus.Location = new Point(141, 116);
            labelAsioSampleRateStatus.Name = "labelAsioSampleRateStatus";
            labelAsioSampleRateStatus.Size = new Size(12, 15);
            labelAsioSampleRateStatus.TabIndex = 32;
            labelAsioSampleRateStatus.Text = "-";
            //
            // labelAsioPlaybackLatency
            //
            labelAsioPlaybackLatency.AutoSize = true;
            labelAsioPlaybackLatency.ForeColor = SystemColors.ControlLight;
            labelAsioPlaybackLatency.Location = new Point(0, 134);
            labelAsioPlaybackLatency.Name = "labelAsioPlaybackLatency";
            labelAsioPlaybackLatency.Size = new Size(95, 15);
            labelAsioPlaybackLatency.TabIndex = 35;
            labelAsioPlaybackLatency.Text = "Playback latency";
            //
            // labelAsioPlaybackLatencyValue
            //
            labelAsioPlaybackLatencyValue.AutoSize = true;
            labelAsioPlaybackLatencyValue.ForeColor = SystemColors.ControlLight;
            labelAsioPlaybackLatencyValue.Location = new Point(141, 134);
            labelAsioPlaybackLatencyValue.Name = "labelAsioPlaybackLatencyValue";
            labelAsioPlaybackLatencyValue.Size = new Size(12, 15);
            labelAsioPlaybackLatencyValue.TabIndex = 36;
            labelAsioPlaybackLatencyValue.Text = "-";
            //
            // buttonAsioInputProbe
            //
            buttonAsioInputProbe.FlatStyle = FlatStyle.Popup;
            buttonAsioInputProbe.ForeColor = Color.White;
            buttonAsioInputProbe.Location = new Point(141, 161);
            buttonAsioInputProbe.Name = "buttonAsioInputProbe";
            buttonAsioInputProbe.Size = new Size(170, 23);
            buttonAsioInputProbe.TabIndex = 45;
            buttonAsioInputProbe.Text = "Test ASIO Inputs";
            buttonAsioInputProbe.UseVisualStyleBackColor = true;
            //
            // buttonAsioControlPanel
            //
            buttonAsioControlPanel.FlatStyle = FlatStyle.Popup;
            buttonAsioControlPanel.ForeColor = Color.White;
            buttonAsioControlPanel.Location = new Point(141, 190);
            buttonAsioControlPanel.Name = "buttonAsioControlPanel";
            buttonAsioControlPanel.Size = new Size(170, 23);
            buttonAsioControlPanel.TabIndex = 37;
            buttonAsioControlPanel.Text = "ASIO Control Panel";
            buttonAsioControlPanel.UseVisualStyleBackColor = true;
            //
            // AsioAudioBackendPanel
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
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
            Name = "AsioAudioBackendPanel";
            Size = new Size(311, 213);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Label labelAsioDriver;
        private DarkComboBox comboBoxAsioDriver;
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
        private Button buttonAsioControlPanel;
    }
}
