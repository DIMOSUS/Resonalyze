namespace Resonalyze
{
    partial class SignalGeneratorPanel
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
            if (disposing)
            {
                components?.Dispose();
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
            titleLabel = new Label();
            labelSignalType = new Label();
            comboBoxSignalType = new DarkComboBox();
            labelFrequency = new Label();
            numericFrequency = new DarkNumericUpDown();
            labelDuration = new Label();
            numericDuration = new DarkNumericUpDown();
            labelLevel = new Label();
            numericLevel = new DarkNumericUpDown();
            labelAudioSettingsTitle = new Label();
            labelAudioSettings = new Label();
            buttonPlay = new Button();
            buttonStop = new Button();
            labelStatusTitle = new Label();
            labelStatus = new Label();
            ((System.ComponentModel.ISupportInitialize)numericFrequency).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericDuration).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericLevel).BeginInit();
            SuspendLayout();
            // 
            // titleLabel
            // 
            titleLabel.AutoSize = true;
            titleLabel.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold, GraphicsUnit.Point, 204);
            titleLabel.ForeColor = Color.FromArgb(210, 214, 222);
            titleLabel.Location = new Point(18, 18);
            titleLabel.Name = "titleLabel";
            titleLabel.Size = new Size(132, 21);
            titleLabel.TabIndex = 0;
            titleLabel.Text = "Signal Generator";
            // 
            // labelSignalType
            // 
            labelSignalType.AutoSize = true;
            labelSignalType.ForeColor = Color.FromArgb(210, 214, 222);
            labelSignalType.Location = new Point(18, 64);
            labelSignalType.Name = "labelSignalType";
            labelSignalType.Size = new Size(65, 15);
            labelSignalType.TabIndex = 1;
            labelSignalType.Text = "Signal type";
            // 
            // comboBoxSignalType
            // 
            comboBoxSignalType.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxSignalType.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxSignalType.ForeColor = Color.White;
            comboBoxSignalType.FormattingEnabled = true;
            comboBoxSignalType.Location = new Point(150, 60);
            comboBoxSignalType.MinimumSize = new Size(36, 19);
            comboBoxSignalType.Name = "comboBoxSignalType";
            comboBoxSignalType.Size = new Size(210, 23);
            comboBoxSignalType.TabIndex = 2;
            // 
            // labelFrequency
            // 
            labelFrequency.AutoSize = true;
            labelFrequency.ForeColor = Color.FromArgb(210, 214, 222);
            labelFrequency.Location = new Point(18, 94);
            labelFrequency.Name = "labelFrequency";
            labelFrequency.Size = new Size(83, 15);
            labelFrequency.TabIndex = 3;
            labelFrequency.Text = "Frequency, Hz";
            // 
            // numericFrequency
            // 
            numericFrequency.BackColor = Color.FromArgb(55, 60, 72);
            numericFrequency.DecimalPlaces = 1;
            numericFrequency.ForeColor = Color.White;
            numericFrequency.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            numericFrequency.Location = new Point(150, 90);
            numericFrequency.Maximum = new decimal(new int[] { 96000, 0, 0, 0 });
            numericFrequency.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericFrequency.MinimumSize = new Size(36, 19);
            numericFrequency.Name = "numericFrequency";
            numericFrequency.Size = new Size(110, 23);
            numericFrequency.TabIndex = 4;
            numericFrequency.TextAlign = HorizontalAlignment.Right;
            numericFrequency.Value = new decimal(new int[] { 1000, 0, 0, 0 });
            // 
            // labelDuration
            // 
            labelDuration.AutoSize = true;
            labelDuration.ForeColor = Color.FromArgb(210, 214, 222);
            labelDuration.Location = new Point(18, 124);
            labelDuration.Name = "labelDuration";
            labelDuration.Size = new Size(65, 15);
            labelDuration.TabIndex = 5;
            labelDuration.Text = "Duration, s";
            // 
            // numericDuration
            // 
            numericDuration.BackColor = Color.FromArgb(55, 60, 72);
            numericDuration.ForeColor = Color.White;
            numericDuration.Location = new Point(150, 120);
            numericDuration.Maximum = new decimal(new int[] { 600, 0, 0, 0 });
            numericDuration.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericDuration.MinimumSize = new Size(36, 19);
            numericDuration.Name = "numericDuration";
            numericDuration.Size = new Size(110, 23);
            numericDuration.TabIndex = 6;
            numericDuration.TextAlign = HorizontalAlignment.Right;
            numericDuration.Value = new decimal(new int[] { 10, 0, 0, 0 });
            // 
            // labelLevel
            // 
            labelLevel.AutoSize = true;
            labelLevel.ForeColor = Color.FromArgb(210, 214, 222);
            labelLevel.Location = new Point(18, 154);
            labelLevel.Name = "labelLevel";
            labelLevel.Size = new Size(47, 15);
            labelLevel.TabIndex = 7;
            labelLevel.Text = "Level, %";
            // 
            // numericLevel
            // 
            numericLevel.BackColor = Color.FromArgb(55, 60, 72);
            numericLevel.ForeColor = Color.White;
            numericLevel.Location = new Point(150, 150);
            numericLevel.MinimumSize = new Size(36, 19);
            numericLevel.Name = "numericLevel";
            numericLevel.Size = new Size(110, 23);
            numericLevel.TabIndex = 8;
            numericLevel.TextAlign = HorizontalAlignment.Right;
            numericLevel.Value = new decimal(new int[] { 50, 0, 0, 0 });
            // 
            // labelAudioSettingsTitle
            // 
            labelAudioSettingsTitle.AutoSize = true;
            labelAudioSettingsTitle.ForeColor = Color.FromArgb(210, 214, 222);
            labelAudioSettingsTitle.Location = new Point(18, 190);
            labelAudioSettingsTitle.Name = "labelAudioSettingsTitle";
            labelAudioSettingsTitle.Size = new Size(83, 15);
            labelAudioSettingsTitle.TabIndex = 9;
            labelAudioSettingsTitle.Text = "Audio settings";
            // 
            // labelAudioSettings
            // 
            labelAudioSettings.AutoEllipsis = true;
            labelAudioSettings.ForeColor = Color.FromArgb(190, 220, 255);
            labelAudioSettings.Location = new Point(150, 190);
            labelAudioSettings.Name = "labelAudioSettings";
            labelAudioSettings.Size = new Size(520, 20);
            labelAudioSettings.TabIndex = 10;
            labelAudioSettings.Text = "-";
            // 
            // buttonPlay
            // 
            buttonPlay.BackColor = Color.FromArgb(50, 55, 80);
            buttonPlay.FlatStyle = FlatStyle.Popup;
            buttonPlay.ForeColor = Color.White;
            buttonPlay.Location = new Point(150, 226);
            buttonPlay.Name = "buttonPlay";
            buttonPlay.Size = new Size(100, 25);
            buttonPlay.TabIndex = 11;
            buttonPlay.Text = "Play";
            buttonPlay.UseVisualStyleBackColor = false;
            // 
            // buttonStop
            // 
            buttonStop.BackColor = Color.FromArgb(50, 55, 80);
            buttonStop.Enabled = false;
            buttonStop.FlatStyle = FlatStyle.Popup;
            buttonStop.ForeColor = Color.White;
            buttonStop.Location = new Point(260, 226);
            buttonStop.Name = "buttonStop";
            buttonStop.Size = new Size(100, 25);
            buttonStop.TabIndex = 12;
            buttonStop.Text = "Stop";
            buttonStop.UseVisualStyleBackColor = false;
            // 
            // labelStatusTitle
            // 
            labelStatusTitle.AutoSize = true;
            labelStatusTitle.ForeColor = Color.FromArgb(210, 214, 222);
            labelStatusTitle.Location = new Point(18, 264);
            labelStatusTitle.Name = "labelStatusTitle";
            labelStatusTitle.Size = new Size(39, 15);
            labelStatusTitle.TabIndex = 13;
            labelStatusTitle.Text = "Status";
            // 
            // labelStatus
            // 
            labelStatus.AutoSize = true;
            labelStatus.ForeColor = Color.FromArgb(140, 255, 160);
            labelStatus.Location = new Point(150, 264);
            labelStatus.Name = "labelStatus";
            labelStatus.Size = new Size(39, 15);
            labelStatus.TabIndex = 14;
            labelStatus.Text = "Ready";
            // 
            // SignalGeneratorPanel
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            AutoScroll = true;
            BackColor = Color.FromArgb(40, 44, 54);
            BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(labelStatus);
            Controls.Add(labelStatusTitle);
            Controls.Add(buttonStop);
            Controls.Add(buttonPlay);
            Controls.Add(labelAudioSettings);
            Controls.Add(labelAudioSettingsTitle);
            Controls.Add(numericLevel);
            Controls.Add(labelLevel);
            Controls.Add(numericDuration);
            Controls.Add(labelDuration);
            Controls.Add(numericFrequency);
            Controls.Add(labelFrequency);
            Controls.Add(comboBoxSignalType);
            Controls.Add(labelSignalType);
            Controls.Add(titleLabel);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            Name = "SignalGeneratorPanel";
            Size = new Size(1182, 706);
            ((System.ComponentModel.ISupportInitialize)numericFrequency).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericDuration).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericLevel).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label titleLabel;
        private Label labelSignalType;
        private DarkComboBox comboBoxSignalType;
        private Label labelFrequency;
        private DarkNumericUpDown numericFrequency;
        private Label labelDuration;
        private DarkNumericUpDown numericDuration;
        private Label labelLevel;
        private DarkNumericUpDown numericLevel;
        private Label labelAudioSettingsTitle;
        private Label labelAudioSettings;
        private Button buttonPlay;
        private Button buttonStop;
        private Label labelStatusTitle;
        private Label labelStatus;
    }
}
