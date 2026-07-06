namespace Resonalyze
{
    partial class VirtualCrossoverChannelControl
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
            labelChannel = new Label();
            buttonSource = new Button();
            labelGain = new Label();
            numericGain = new DarkNumericUpDown();
            labelDelay = new Label();
            numericDelay = new DarkNumericUpDown();
            labelDelayMm = new Label();
            checkBoxInvert = new CheckBox();
            labelCrossover = new Label();
            comboBoxCrossoverKind = new DarkComboBox();
            labelMeasuredPolarity = new Label();
            labelHighPass = new Label();
            numericHighPassHz = new DarkNumericUpDown();
            comboBoxHighPassFamily = new DarkComboBox();
            comboBoxHighPassSlope = new DarkComboBox();
            labelLowPass = new Label();
            numericLowPassHz = new DarkNumericUpDown();
            comboBoxLowPassFamily = new DarkComboBox();
            comboBoxLowPassSlope = new DarkComboBox();
            labelPeq = new Label();
            buttonPeqLoad = new Button();
            buttonPeqClear = new Button();
            labelPeqInfo = new Label();
            labelCurves = new Label();
            checkBoxShowRaw = new CheckBox();
            checkBoxShowProcessed = new CheckBox();
            checkBoxBypass = new CheckBox();
            buttonMute = new Button();
            (numericGain).BeginInit();
            (numericDelay).BeginInit();
            (numericHighPassHz).BeginInit();
            (numericLowPassHz).BeginInit();
            SuspendLayout();
            // 
            // labelChannel
            // 
            labelChannel.AutoSize = true;
            labelChannel.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point, 204);
            labelChannel.ForeColor = Color.FromArgb(210, 214, 222);
            labelChannel.Location = new Point(8, 10);
            labelChannel.Name = "labelChannel";
            labelChannel.Size = new Size(61, 15);
            labelChannel.TabIndex = 0;
            labelChannel.Text = "Channel A";
            // 
            // buttonSource
            // 
            buttonSource.BackColor = Color.FromArgb(46, 51, 67);
            buttonSource.FlatStyle = FlatStyle.Popup;
            buttonSource.ForeColor = Color.White;
            buttonSource.Location = new Point(78, 6);
            buttonSource.Name = "buttonSource";
            buttonSource.Size = new Size(214, 24);
            buttonSource.TabIndex = 1;
            buttonSource.Text = "Source...";
            buttonSource.TextAlign = ContentAlignment.MiddleLeft;
            buttonSource.UseVisualStyleBackColor = false;
            // 
            // labelGain
            // 
            labelGain.AutoSize = true;
            labelGain.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelGain.ForeColor = Color.FromArgb(210, 214, 222);
            labelGain.Location = new Point(8, 40);
            labelGain.Name = "labelGain";
            labelGain.Size = new Size(48, 15);
            labelGain.TabIndex = 2;
            labelGain.Text = "Gain dB";
            // 
            // numericGain
            // 
            numericGain.BackColor = Color.FromArgb(55, 60, 72);
            numericGain.DecimalPlaces = 1;
            numericGain.ForeColor = Color.White;
            numericGain.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            numericGain.Location = new Point(78, 38);
            numericGain.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            numericGain.Minimum = new decimal(new int[] { 60, 0, 0, int.MinValue });
            numericGain.MinimumSize = new Size(36, 19);
            numericGain.Name = "numericGain";
            numericGain.Size = new Size(66, 19);
            numericGain.TabIndex = 3;
            numericGain.TextAlign = HorizontalAlignment.Right;
            numericGain.ThousandsSeparator = false;
            numericGain.Value = new decimal(new int[] { 0, 0, 0, 0 });
            // 
            // labelDelay
            // 
            labelDelay.AutoSize = true;
            labelDelay.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelDelay.ForeColor = Color.FromArgb(210, 214, 222);
            labelDelay.Location = new Point(160, 40);
            labelDelay.Name = "labelDelay";
            labelDelay.Size = new Size(56, 15);
            labelDelay.TabIndex = 4;
            labelDelay.Text = "Delay ms";
            // 
            // numericDelay
            // 
            numericDelay.BackColor = Color.FromArgb(55, 60, 72);
            numericDelay.DecimalPlaces = 2;
            numericDelay.ForeColor = Color.White;
            numericDelay.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            numericDelay.Location = new Point(228, 38);
            numericDelay.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            numericDelay.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericDelay.MinimumSize = new Size(36, 19);
            numericDelay.Name = "numericDelay";
            numericDelay.Size = new Size(66, 19);
            numericDelay.TabIndex = 5;
            numericDelay.TextAlign = HorizontalAlignment.Right;
            numericDelay.ThousandsSeparator = false;
            numericDelay.Value = new decimal(new int[] { 0, 0, 0, 0 });
            // 
            // labelDelayMm
            // 
            labelDelayMm.AutoSize = true;
            labelDelayMm.ForeColor = Color.FromArgb(170, 176, 190);
            labelDelayMm.Location = new Point(228, 60);
            labelDelayMm.Name = "labelDelayMm";
            labelDelayMm.Size = new Size(49, 15);
            labelDelayMm.TabIndex = 6;
            labelDelayMm.Text = "= 0 mm";
            // 
            // checkBoxInvert
            // 
            checkBoxInvert.AutoSize = true;
            checkBoxInvert.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            checkBoxInvert.ForeColor = Color.FromArgb(210, 214, 222);
            checkBoxInvert.Location = new Point(78, 62);
            checkBoxInvert.Name = "checkBoxInvert";
            checkBoxInvert.Size = new Size(100, 19);
            checkBoxInvert.TabIndex = 7;
            checkBoxInvert.Text = "Invert polarity";
            checkBoxInvert.UseVisualStyleBackColor = true;
            // 
            // labelCrossover
            // 
            labelCrossover.AutoSize = true;
            labelCrossover.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelCrossover.ForeColor = Color.FromArgb(210, 214, 222);
            labelCrossover.Location = new Point(8, 90);
            labelCrossover.Name = "labelCrossover";
            labelCrossover.Size = new Size(58, 15);
            labelCrossover.TabIndex = 8;
            labelCrossover.Text = "Crossover";
            // 
            // comboBoxCrossoverKind
            // 
            comboBoxCrossoverKind.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxCrossoverKind.ForeColor = Color.White;
            comboBoxCrossoverKind.Location = new Point(78, 88);
            comboBoxCrossoverKind.MinimumSize = new Size(36, 19);
            comboBoxCrossoverKind.Name = "comboBoxCrossoverKind";
            comboBoxCrossoverKind.Size = new Size(100, 19);
            comboBoxCrossoverKind.TabIndex = 9;
            // 
            // labelMeasuredPolarity
            // 
            labelMeasuredPolarity.AutoSize = true;
            labelMeasuredPolarity.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelMeasuredPolarity.ForeColor = Color.FromArgb(170, 176, 190);
            labelMeasuredPolarity.Location = new Point(194, 90);
            labelMeasuredPolarity.Name = "labelMeasuredPolarity";
            labelMeasuredPolarity.Size = new Size(75, 15);
            labelMeasuredPolarity.TabIndex = 25;
            labelMeasuredPolarity.Text = "IR: Unknown";
            // 
            // labelHighPass
            // 
            labelHighPass.AutoSize = true;
            labelHighPass.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelHighPass.ForeColor = Color.FromArgb(210, 214, 222);
            labelHighPass.Location = new Point(8, 116);
            labelHighPass.Name = "labelHighPass";
            labelHighPass.Size = new Size(41, 15);
            labelHighPass.TabIndex = 10;
            labelHighPass.Text = "HP Hz";
            // 
            // numericHighPassHz
            // 
            numericHighPassHz.BackColor = Color.FromArgb(55, 60, 72);
            numericHighPassHz.DecimalPlaces = 0;
            numericHighPassHz.ForeColor = Color.White;
            numericHighPassHz.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            numericHighPassHz.Location = new Point(78, 114);
            numericHighPassHz.Maximum = new decimal(new int[] { 24000, 0, 0, 0 });
            numericHighPassHz.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            numericHighPassHz.MinimumSize = new Size(36, 19);
            numericHighPassHz.Name = "numericHighPassHz";
            numericHighPassHz.Size = new Size(66, 19);
            numericHighPassHz.TabIndex = 11;
            numericHighPassHz.TextAlign = HorizontalAlignment.Right;
            numericHighPassHz.ThousandsSeparator = false;
            numericHighPassHz.Value = new decimal(new int[] { 2000, 0, 0, 0 });
            // 
            // comboBoxHighPassFamily
            // 
            comboBoxHighPassFamily.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxHighPassFamily.ForeColor = Color.White;
            comboBoxHighPassFamily.Location = new Point(150, 114);
            comboBoxHighPassFamily.MinimumSize = new Size(36, 19);
            comboBoxHighPassFamily.Name = "comboBoxHighPassFamily";
            comboBoxHighPassFamily.Size = new Size(90, 19);
            comboBoxHighPassFamily.TabIndex = 12;
            // 
            // comboBoxHighPassSlope
            // 
            comboBoxHighPassSlope.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxHighPassSlope.ForeColor = Color.White;
            comboBoxHighPassSlope.Location = new Point(242, 114);
            comboBoxHighPassSlope.MinimumSize = new Size(36, 19);
            comboBoxHighPassSlope.Name = "comboBoxHighPassSlope";
            comboBoxHighPassSlope.Size = new Size(76, 19);
            comboBoxHighPassSlope.TabIndex = 13;
            // 
            // labelLowPass
            // 
            labelLowPass.AutoSize = true;
            labelLowPass.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelLowPass.ForeColor = Color.FromArgb(210, 214, 222);
            labelLowPass.Location = new Point(8, 142);
            labelLowPass.Name = "labelLowPass";
            labelLowPass.Size = new Size(38, 15);
            labelLowPass.TabIndex = 14;
            labelLowPass.Text = "LP Hz";
            // 
            // numericLowPassHz
            // 
            numericLowPassHz.BackColor = Color.FromArgb(55, 60, 72);
            numericLowPassHz.DecimalPlaces = 0;
            numericLowPassHz.ForeColor = Color.White;
            numericLowPassHz.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            numericLowPassHz.Location = new Point(78, 140);
            numericLowPassHz.Maximum = new decimal(new int[] { 24000, 0, 0, 0 });
            numericLowPassHz.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            numericLowPassHz.MinimumSize = new Size(36, 19);
            numericLowPassHz.Name = "numericLowPassHz";
            numericLowPassHz.Size = new Size(66, 19);
            numericLowPassHz.TabIndex = 15;
            numericLowPassHz.TextAlign = HorizontalAlignment.Right;
            numericLowPassHz.ThousandsSeparator = false;
            numericLowPassHz.Value = new decimal(new int[] { 2000, 0, 0, 0 });
            // 
            // comboBoxLowPassFamily
            // 
            comboBoxLowPassFamily.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxLowPassFamily.ForeColor = Color.White;
            comboBoxLowPassFamily.Location = new Point(150, 140);
            comboBoxLowPassFamily.MinimumSize = new Size(36, 19);
            comboBoxLowPassFamily.Name = "comboBoxLowPassFamily";
            comboBoxLowPassFamily.Size = new Size(90, 19);
            comboBoxLowPassFamily.TabIndex = 16;
            // 
            // comboBoxLowPassSlope
            // 
            comboBoxLowPassSlope.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxLowPassSlope.ForeColor = Color.White;
            comboBoxLowPassSlope.Location = new Point(242, 140);
            comboBoxLowPassSlope.MinimumSize = new Size(36, 19);
            comboBoxLowPassSlope.Name = "comboBoxLowPassSlope";
            comboBoxLowPassSlope.Size = new Size(76, 19);
            comboBoxLowPassSlope.TabIndex = 17;
            // 
            // labelPeq
            // 
            labelPeq.AutoSize = true;
            labelPeq.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelPeq.ForeColor = Color.FromArgb(210, 214, 222);
            labelPeq.Location = new Point(8, 172);
            labelPeq.Name = "labelPeq";
            labelPeq.Size = new Size(29, 15);
            labelPeq.TabIndex = 18;
            labelPeq.Text = "PEQ";
            // 
            // buttonPeqLoad
            // 
            buttonPeqLoad.FlatStyle = FlatStyle.Popup;
            buttonPeqLoad.ForeColor = Color.White;
            buttonPeqLoad.Location = new Point(78, 168);
            buttonPeqLoad.Name = "buttonPeqLoad";
            buttonPeqLoad.Size = new Size(66, 23);
            buttonPeqLoad.TabIndex = 19;
            buttonPeqLoad.Text = "Load...";
            buttonPeqLoad.UseVisualStyleBackColor = true;
            // 
            // buttonPeqClear
            // 
            buttonPeqClear.FlatStyle = FlatStyle.Popup;
            buttonPeqClear.ForeColor = Color.White;
            buttonPeqClear.Location = new Point(150, 168);
            buttonPeqClear.Name = "buttonPeqClear";
            buttonPeqClear.Size = new Size(56, 23);
            buttonPeqClear.TabIndex = 20;
            buttonPeqClear.Text = "Clear";
            buttonPeqClear.UseVisualStyleBackColor = true;
            // 
            // labelPeqInfo
            // 
            labelPeqInfo.AutoSize = true;
            labelPeqInfo.ForeColor = Color.FromArgb(170, 176, 190);
            labelPeqInfo.Location = new Point(212, 172);
            labelPeqInfo.Name = "labelPeqInfo";
            labelPeqInfo.Size = new Size(48, 15);
            labelPeqInfo.TabIndex = 21;
            labelPeqInfo.Text = "No PEQ";
            // 
            // labelCurves
            // 
            labelCurves.AutoSize = true;
            labelCurves.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelCurves.ForeColor = Color.FromArgb(210, 214, 222);
            labelCurves.Location = new Point(8, 200);
            labelCurves.Name = "labelCurves";
            labelCurves.Size = new Size(42, 15);
            labelCurves.TabIndex = 22;
            labelCurves.Text = "Curves";
            // 
            // checkBoxShowRaw
            // 
            checkBoxShowRaw.AutoSize = true;
            checkBoxShowRaw.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            checkBoxShowRaw.ForeColor = Color.FromArgb(210, 214, 222);
            checkBoxShowRaw.Location = new Point(78, 198);
            checkBoxShowRaw.Name = "checkBoxShowRaw";
            checkBoxShowRaw.Size = new Size(48, 19);
            checkBoxShowRaw.TabIndex = 23;
            checkBoxShowRaw.Text = "Raw";
            checkBoxShowRaw.UseVisualStyleBackColor = true;
            // 
            // checkBoxShowProcessed
            // 
            checkBoxShowProcessed.AutoSize = true;
            checkBoxShowProcessed.Checked = true;
            checkBoxShowProcessed.CheckState = CheckState.Checked;
            checkBoxShowProcessed.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            checkBoxShowProcessed.ForeColor = Color.FromArgb(210, 214, 222);
            checkBoxShowProcessed.Location = new Point(140, 198);
            checkBoxShowProcessed.Name = "checkBoxShowProcessed";
            checkBoxShowProcessed.Size = new Size(79, 19);
            checkBoxShowProcessed.TabIndex = 24;
            checkBoxShowProcessed.Text = "Processed";
            checkBoxShowProcessed.UseVisualStyleBackColor = true;
            //
            // checkBoxBypass
            //
            checkBoxBypass.AutoSize = true;
            checkBoxBypass.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            checkBoxBypass.ForeColor = Color.FromArgb(210, 214, 222);
            checkBoxBypass.Location = new Point(228, 198);
            checkBoxBypass.Name = "checkBoxBypass";
            checkBoxBypass.Size = new Size(66, 19);
            checkBoxBypass.TabIndex = 25;
            checkBoxBypass.Text = "Bypass";
            checkBoxBypass.UseVisualStyleBackColor = true;
            //
            // buttonMute
            //
            buttonMute.BackColor = Color.FromArgb(46, 51, 67);
            buttonMute.FlatStyle = FlatStyle.Popup;
            buttonMute.Font = new Font("Segoe UI Emoji", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            buttonMute.ForeColor = Color.White;
            buttonMute.Location = new Point(296, 6);
            buttonMute.Name = "buttonMute";
            buttonMute.Size = new Size(30, 24);
            buttonMute.TabIndex = 26;
            buttonMute.Text = "🔈";
            buttonMute.UseCompatibleTextRendering = true;
            buttonMute.UseVisualStyleBackColor = false;
            // 
            // VirtualCrossoverChannelControl
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(46, 51, 62);
            BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(buttonMute);
            Controls.Add(labelChannel);
            Controls.Add(buttonSource);
            Controls.Add(labelGain);
            Controls.Add(numericGain);
            Controls.Add(labelDelay);
            Controls.Add(numericDelay);
            Controls.Add(labelDelayMm);
            Controls.Add(checkBoxInvert);
            Controls.Add(labelCrossover);
            Controls.Add(comboBoxCrossoverKind);
            Controls.Add(labelMeasuredPolarity);
            Controls.Add(labelHighPass);
            Controls.Add(numericHighPassHz);
            Controls.Add(comboBoxHighPassFamily);
            Controls.Add(comboBoxHighPassSlope);
            Controls.Add(labelLowPass);
            Controls.Add(numericLowPassHz);
            Controls.Add(comboBoxLowPassFamily);
            Controls.Add(comboBoxLowPassSlope);
            Controls.Add(labelPeq);
            Controls.Add(buttonPeqLoad);
            Controls.Add(buttonPeqClear);
            Controls.Add(labelPeqInfo);
            Controls.Add(labelCurves);
            Controls.Add(checkBoxShowRaw);
            Controls.Add(checkBoxShowProcessed);
            Controls.Add(checkBoxBypass);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            MaximumSize = new Size(324, 226);
            MinimumSize = new Size(324, 226);
            Name = "VirtualCrossoverChannelControl";
            Size = new Size(324, 226);
            (numericGain).EndInit();
            (numericDelay).EndInit();
            (numericHighPassHz).EndInit();
            (numericLowPassHz).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label labelChannel;
        private Button buttonSource;
        private Label labelGain;
        private DarkNumericUpDown numericGain;
        private Label labelDelay;
        private DarkNumericUpDown numericDelay;
        private Label labelDelayMm;
        private CheckBox checkBoxInvert;
        private Label labelCrossover;
        private DarkComboBox comboBoxCrossoverKind;
        private Label labelMeasuredPolarity;
        private Label labelHighPass;
        private DarkNumericUpDown numericHighPassHz;
        private DarkComboBox comboBoxHighPassFamily;
        private DarkComboBox comboBoxHighPassSlope;
        private Label labelLowPass;
        private DarkNumericUpDown numericLowPassHz;
        private DarkComboBox comboBoxLowPassFamily;
        private DarkComboBox comboBoxLowPassSlope;
        private Label labelPeq;
        private Button buttonPeqLoad;
        private Button buttonPeqClear;
        private Label labelPeqInfo;
        private Label labelCurves;
        private CheckBox checkBoxShowRaw;
        private CheckBox checkBoxShowProcessed;
        private CheckBox checkBoxBypass;
        private Button buttonMute;
    }
}
