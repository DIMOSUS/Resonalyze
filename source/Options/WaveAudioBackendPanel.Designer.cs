namespace Resonalyze.Options
{
    partial class WaveAudioBackendPanel
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
            labelPlaybackDevice = new Label();
            comboBoxPlaybackDevice = new DarkComboBox();
            labelRecordingDevice = new Label();
            comboBoxRecordingDevice = new DarkComboBox();
            labelWaveInputChannel = new Label();
            comboBoxWaveInputChannel = new DarkComboBox();
            labelWaveLoopbackChannel = new Label();
            comboBoxWaveLoopbackChannel = new DarkComboBox();
            labelWaveLoopbackStatus = new Label();
            SuspendLayout();
            //
            // labelPlaybackDevice
            //
            labelPlaybackDevice.AutoSize = true;
            labelPlaybackDevice.ForeColor = SystemColors.ControlLight;
            labelPlaybackDevice.Location = new Point(0, 8);
            labelPlaybackDevice.Name = "labelPlaybackDevice";
            labelPlaybackDevice.Size = new Size(91, 15);
            labelPlaybackDevice.TabIndex = 19;
            labelPlaybackDevice.Text = "Playback device";
            //
            // comboBoxPlaybackDevice
            //
            comboBoxPlaybackDevice.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxPlaybackDevice.ForeColor = Color.White;
            comboBoxPlaybackDevice.Location = new Point(141, 0);
            comboBoxPlaybackDevice.Margin = new Padding(0);
            comboBoxPlaybackDevice.MinimumSize = new Size(36, 19);
            comboBoxPlaybackDevice.Name = "comboBoxPlaybackDevice";
            comboBoxPlaybackDevice.Size = new Size(170, 23);
            comboBoxPlaybackDevice.TabIndex = 20;
            //
            // labelRecordingDevice
            //
            labelRecordingDevice.AutoSize = true;
            labelRecordingDevice.ForeColor = SystemColors.ControlLight;
            labelRecordingDevice.Location = new Point(0, 37);
            labelRecordingDevice.Name = "labelRecordingDevice";
            labelRecordingDevice.Size = new Size(98, 15);
            labelRecordingDevice.TabIndex = 21;
            labelRecordingDevice.Text = "Recording device";
            //
            // comboBoxRecordingDevice
            //
            comboBoxRecordingDevice.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxRecordingDevice.ForeColor = Color.White;
            comboBoxRecordingDevice.Location = new Point(141, 29);
            comboBoxRecordingDevice.Margin = new Padding(0);
            comboBoxRecordingDevice.MinimumSize = new Size(36, 19);
            comboBoxRecordingDevice.Name = "comboBoxRecordingDevice";
            comboBoxRecordingDevice.Size = new Size(170, 23);
            comboBoxRecordingDevice.TabIndex = 22;
            //
            // labelWaveInputChannel
            //
            labelWaveInputChannel.AutoSize = true;
            labelWaveInputChannel.ForeColor = SystemColors.ControlLight;
            labelWaveInputChannel.Location = new Point(0, 66);
            labelWaveInputChannel.Name = "labelWaveInputChannel";
            labelWaveInputChannel.Size = new Size(112, 15);
            labelWaveInputChannel.TabIndex = 38;
            labelWaveInputChannel.Text = "Wave input channel";
            //
            // comboBoxWaveInputChannel
            //
            comboBoxWaveInputChannel.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxWaveInputChannel.ForeColor = Color.White;
            comboBoxWaveInputChannel.Location = new Point(141, 58);
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
            labelWaveLoopbackChannel.Location = new Point(0, 95);
            labelWaveLoopbackChannel.Name = "labelWaveLoopbackChannel";
            labelWaveLoopbackChannel.Size = new Size(133, 15);
            labelWaveLoopbackChannel.TabIndex = 40;
            labelWaveLoopbackChannel.Text = "Wave loopback channel";
            //
            // comboBoxWaveLoopbackChannel
            //
            comboBoxWaveLoopbackChannel.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxWaveLoopbackChannel.ForeColor = Color.White;
            comboBoxWaveLoopbackChannel.Location = new Point(141, 87);
            comboBoxWaveLoopbackChannel.Margin = new Padding(0);
            comboBoxWaveLoopbackChannel.MinimumSize = new Size(36, 19);
            comboBoxWaveLoopbackChannel.Name = "comboBoxWaveLoopbackChannel";
            comboBoxWaveLoopbackChannel.Size = new Size(170, 23);
            comboBoxWaveLoopbackChannel.TabIndex = 41;
            //
            // labelWaveLoopbackStatus
            //
            labelWaveLoopbackStatus.ForeColor = SystemColors.ControlLight;
            labelWaveLoopbackStatus.Location = new Point(0, 117);
            labelWaveLoopbackStatus.Name = "labelWaveLoopbackStatus";
            labelWaveLoopbackStatus.Size = new Size(311, 60);
            labelWaveLoopbackStatus.TabIndex = 42;
            labelWaveLoopbackStatus.Text = "-";
            //
            // WaveAudioBackendPanel
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
            Controls.Add(labelWaveLoopbackStatus);
            Controls.Add(comboBoxWaveLoopbackChannel);
            Controls.Add(labelWaveLoopbackChannel);
            Controls.Add(comboBoxWaveInputChannel);
            Controls.Add(labelWaveInputChannel);
            Controls.Add(comboBoxRecordingDevice);
            Controls.Add(labelRecordingDevice);
            Controls.Add(comboBoxPlaybackDevice);
            Controls.Add(labelPlaybackDevice);
            Name = "WaveAudioBackendPanel";
            Size = new Size(311, 177);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Label labelPlaybackDevice;
        private DarkComboBox comboBoxPlaybackDevice;
        private Label labelRecordingDevice;
        private DarkComboBox comboBoxRecordingDevice;
        private Label labelWaveInputChannel;
        private DarkComboBox comboBoxWaveInputChannel;
        private Label labelWaveLoopbackChannel;
        private DarkComboBox comboBoxWaveLoopbackChannel;
        private Label labelWaveLoopbackStatus;
    }
}
