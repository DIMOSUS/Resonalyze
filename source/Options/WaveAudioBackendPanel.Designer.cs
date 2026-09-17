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
            comboBoxPlaybackDevice = new ThemedComboBox();
            labelRecordingDevice = new Label();
            comboBoxRecordingDevice = new ThemedComboBox();
            labelWaveInputChannel = new Label();
            comboBoxWaveInputChannel = new ThemedComboBox();
            labelWaveLoopbackChannel = new Label();
            comboBoxWaveLoopbackChannel = new ThemedComboBox();
            labelWaveLoopbackStatus = new Label();
            labelDeviceSettings = new Label();
            buttonDeviceSettings = new ReleaseClickButton();
            SuspendLayout();
            //
            // labelPlaybackDevice
            //
            labelPlaybackDevice.AutoSize = true;
            labelPlaybackDevice.ForeColor = UiPalette.TextDefault;
            labelPlaybackDevice.Location = new Point(0, 8);
            labelPlaybackDevice.Name = "labelPlaybackDevice";
            labelPlaybackDevice.Size = new Size(91, 15);
            labelPlaybackDevice.TabIndex = 19;
            labelPlaybackDevice.Text = "Playback device";
            //
            // comboBoxPlaybackDevice
            //
            comboBoxPlaybackDevice.BackColor = UiPalette.ControlSurface;
            comboBoxPlaybackDevice.ForeColor = UiPalette.TextPrimary;
            comboBoxPlaybackDevice.Location = new Point(137, 0);
            comboBoxPlaybackDevice.Margin = new Padding(0);
            comboBoxPlaybackDevice.MinimumSize = new Size(36, 19);
            comboBoxPlaybackDevice.Name = "comboBoxPlaybackDevice";
            comboBoxPlaybackDevice.Size = new Size(170, 23);
            comboBoxPlaybackDevice.TabIndex = 20;
            //
            // labelRecordingDevice
            //
            labelRecordingDevice.AutoSize = true;
            labelRecordingDevice.ForeColor = UiPalette.TextDefault;
            labelRecordingDevice.Location = new Point(0, 37);
            labelRecordingDevice.Name = "labelRecordingDevice";
            labelRecordingDevice.Size = new Size(98, 15);
            labelRecordingDevice.TabIndex = 21;
            labelRecordingDevice.Text = "Recording device";
            //
            // comboBoxRecordingDevice
            //
            comboBoxRecordingDevice.BackColor = UiPalette.ControlSurface;
            comboBoxRecordingDevice.ForeColor = UiPalette.TextPrimary;
            comboBoxRecordingDevice.Location = new Point(137, 29);
            comboBoxRecordingDevice.Margin = new Padding(0);
            comboBoxRecordingDevice.MinimumSize = new Size(36, 19);
            comboBoxRecordingDevice.Name = "comboBoxRecordingDevice";
            comboBoxRecordingDevice.Size = new Size(170, 23);
            comboBoxRecordingDevice.TabIndex = 22;
            //
            // labelWaveInputChannel
            //
            labelWaveInputChannel.AutoSize = true;
            labelWaveInputChannel.ForeColor = UiPalette.TextDefault;
            labelWaveInputChannel.Location = new Point(0, 66);
            labelWaveInputChannel.Name = "labelWaveInputChannel";
            labelWaveInputChannel.Size = new Size(112, 15);
            labelWaveInputChannel.TabIndex = 38;
            labelWaveInputChannel.Text = "Wave input channel";
            //
            // comboBoxWaveInputChannel
            //
            comboBoxWaveInputChannel.BackColor = UiPalette.ControlSurface;
            comboBoxWaveInputChannel.ForeColor = UiPalette.TextPrimary;
            comboBoxWaveInputChannel.Location = new Point(137, 58);
            comboBoxWaveInputChannel.Margin = new Padding(0);
            comboBoxWaveInputChannel.MinimumSize = new Size(36, 19);
            comboBoxWaveInputChannel.Name = "comboBoxWaveInputChannel";
            comboBoxWaveInputChannel.Size = new Size(170, 23);
            comboBoxWaveInputChannel.TabIndex = 39;
            //
            // labelWaveLoopbackChannel
            //
            labelWaveLoopbackChannel.AutoSize = true;
            labelWaveLoopbackChannel.ForeColor = UiPalette.TextDefault;
            labelWaveLoopbackChannel.Location = new Point(0, 95);
            labelWaveLoopbackChannel.Name = "labelWaveLoopbackChannel";
            labelWaveLoopbackChannel.Size = new Size(133, 15);
            labelWaveLoopbackChannel.TabIndex = 40;
            labelWaveLoopbackChannel.Text = "Wave loopback channel";
            //
            // comboBoxWaveLoopbackChannel
            //
            comboBoxWaveLoopbackChannel.BackColor = UiPalette.ControlSurface;
            comboBoxWaveLoopbackChannel.ForeColor = UiPalette.TextPrimary;
            comboBoxWaveLoopbackChannel.Location = new Point(137, 87);
            comboBoxWaveLoopbackChannel.Margin = new Padding(0);
            comboBoxWaveLoopbackChannel.MinimumSize = new Size(36, 19);
            comboBoxWaveLoopbackChannel.Name = "comboBoxWaveLoopbackChannel";
            comboBoxWaveLoopbackChannel.Size = new Size(170, 23);
            comboBoxWaveLoopbackChannel.TabIndex = 41;
            //
            // labelWaveLoopbackStatus
            //
            labelWaveLoopbackStatus.ForeColor = UiPalette.TextDefault;
            labelWaveLoopbackStatus.Location = new Point(0, 117);
            labelWaveLoopbackStatus.Name = "labelWaveLoopbackStatus";
            labelWaveLoopbackStatus.Size = new Size(311, 60);
            labelWaveLoopbackStatus.TabIndex = 42;
            labelWaveLoopbackStatus.Text = "-";
            //
            // labelDeviceSettings
            //
            labelDeviceSettings.AutoSize = true;
            labelDeviceSettings.ForeColor = UiPalette.TextDefault;
            labelDeviceSettings.Location = new Point(0, 184);
            labelDeviceSettings.Name = "labelDeviceSettings";
            labelDeviceSettings.Size = new Size(91, 15);
            labelDeviceSettings.TabIndex = 43;
            labelDeviceSettings.Text = "Hardware clock";
            //
            // buttonDeviceSettings
            //
            buttonDeviceSettings.Location = new Point(137, 177);
            buttonDeviceSettings.Name = "buttonDeviceSettings";
            buttonDeviceSettings.Size = new Size(170, 29);
            buttonDeviceSettings.TabIndex = 44;
            buttonDeviceSettings.Text = "Open Device Settings";
            buttonDeviceSettings.UseVisualStyleBackColor = true;
            //
            // WaveAudioBackendPanel
            //
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiPalette.AppBackground;
            Controls.Add(buttonDeviceSettings);
            Controls.Add(labelDeviceSettings);
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
            Size = new Size(311, 213);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Label labelPlaybackDevice;
        private ThemedComboBox comboBoxPlaybackDevice;
        private Label labelRecordingDevice;
        private ThemedComboBox comboBoxRecordingDevice;
        private Label labelWaveInputChannel;
        private ThemedComboBox comboBoxWaveInputChannel;
        private Label labelWaveLoopbackChannel;
        private ThemedComboBox comboBoxWaveLoopbackChannel;
        private Label labelWaveLoopbackStatus;
        private Label labelDeviceSettings;
        private ReleaseClickButton buttonDeviceSettings;
    }
}
