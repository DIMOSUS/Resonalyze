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
            checkBoxMono = new CheckBox();
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
            numericHighPassRipple = new DarkNumericUpDown();
            numericLowPassRipple = new DarkNumericUpDown();
            labelAllpass = new Label();
            comboAllPassType = new DarkComboBox();
            numericAllPassFreq = new DarkNumericUpDown();
            numericAllPassQ = new DarkNumericUpDown();
            labelAllpassBand = new Label();
            (numericGain).BeginInit();
            (numericDelay).BeginInit();
            (numericHighPassHz).BeginInit();
            (numericLowPassHz).BeginInit();
            (numericHighPassRipple).BeginInit();
            (numericLowPassRipple).BeginInit();
            (numericAllPassFreq).BeginInit();
            (numericAllPassQ).BeginInit();
            SuspendLayout();
            // 
            // labelChannel
            // 
            labelChannel.AutoSize = true;
            labelChannel.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point, 204);
            labelChannel.ForeColor = Color.FromArgb(210, 214, 222);
            labelChannel.Location = new Point(8, 8);
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
            buttonSource.Location = new Point(70, 4);
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
            labelGain.Location = new Point(8, 35);
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
            numericGain.Location = new Point(70, 33);
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
            labelDelay.Location = new Point(152, 35);
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
            numericDelay.Location = new Point(220, 33);
            numericDelay.Maximum = new decimal(new int[] { 30, 0, 0, 0 });
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
            labelDelayMm.Location = new Point(220, 54);
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
            checkBoxInvert.Location = new Point(70, 56);
            checkBoxInvert.Name = "checkBoxInvert";
            checkBoxInvert.Size = new Size(57, 19);
            checkBoxInvert.TabIndex = 7;
            checkBoxInvert.Text = "Invert";
            checkBoxInvert.UseVisualStyleBackColor = true;
            // 
            // checkBoxMono
            // 
            checkBoxMono.AutoSize = true;
            checkBoxMono.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            checkBoxMono.ForeColor = Color.FromArgb(210, 214, 222);
            checkBoxMono.Location = new Point(142, 56);
            checkBoxMono.Name = "checkBoxMono";
            checkBoxMono.Size = new Size(58, 19);
            checkBoxMono.TabIndex = 27;
            checkBoxMono.Text = "Mono";
            checkBoxMono.UseVisualStyleBackColor = true;
            // 
            // labelCrossover
            // 
            labelCrossover.AutoSize = true;
            labelCrossover.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelCrossover.ForeColor = Color.FromArgb(210, 214, 222);
            labelCrossover.Location = new Point(8, 79);
            labelCrossover.Name = "labelCrossover";
            labelCrossover.Size = new Size(58, 15);
            labelCrossover.TabIndex = 8;
            labelCrossover.Text = "Crossover";
            // 
            // comboBoxCrossoverKind
            // 
            comboBoxCrossoverKind.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxCrossoverKind.ForeColor = Color.White;
            comboBoxCrossoverKind.Location = new Point(70, 77);
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
            labelMeasuredPolarity.Location = new Point(186, 79);
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
            labelHighPass.Location = new Point(8, 105);
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
            numericHighPassHz.Location = new Point(70, 103);
            numericHighPassHz.Maximum = new decimal(new int[] { 24000, 0, 0, 0 });
            numericHighPassHz.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            numericHighPassHz.MinimumSize = new Size(36, 19);
            numericHighPassHz.Name = "numericHighPassHz";
            numericHighPassHz.Size = new Size(60, 19);
            numericHighPassHz.TabIndex = 11;
            numericHighPassHz.TextAlign = HorizontalAlignment.Right;
            numericHighPassHz.ThousandsSeparator = false;
            numericHighPassHz.Value = new decimal(new int[] { 2000, 0, 0, 0 });
            // 
            // comboBoxHighPassFamily
            // 
            comboBoxHighPassFamily.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxHighPassFamily.ForeColor = Color.White;
            comboBoxHighPassFamily.Location = new Point(134, 103);
            comboBoxHighPassFamily.MinimumSize = new Size(36, 19);
            comboBoxHighPassFamily.Name = "comboBoxHighPassFamily";
            comboBoxHighPassFamily.Size = new Size(74, 19);
            comboBoxHighPassFamily.TabIndex = 12;
            // 
            // comboBoxHighPassSlope
            // 
            comboBoxHighPassSlope.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxHighPassSlope.ForeColor = Color.White;
            comboBoxHighPassSlope.Location = new Point(211, 103);
            comboBoxHighPassSlope.MinimumSize = new Size(36, 19);
            comboBoxHighPassSlope.Name = "comboBoxHighPassSlope";
            comboBoxHighPassSlope.Size = new Size(54, 19);
            comboBoxHighPassSlope.TabIndex = 13;
            // 
            // labelLowPass
            // 
            labelLowPass.AutoSize = true;
            labelLowPass.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelLowPass.ForeColor = Color.FromArgb(210, 214, 222);
            labelLowPass.Location = new Point(8, 131);
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
            numericLowPassHz.Location = new Point(70, 129);
            numericLowPassHz.Maximum = new decimal(new int[] { 24000, 0, 0, 0 });
            numericLowPassHz.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            numericLowPassHz.MinimumSize = new Size(36, 19);
            numericLowPassHz.Name = "numericLowPassHz";
            numericLowPassHz.Size = new Size(60, 19);
            numericLowPassHz.TabIndex = 15;
            numericLowPassHz.TextAlign = HorizontalAlignment.Right;
            numericLowPassHz.ThousandsSeparator = false;
            numericLowPassHz.Value = new decimal(new int[] { 2000, 0, 0, 0 });
            // 
            // comboBoxLowPassFamily
            // 
            comboBoxLowPassFamily.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxLowPassFamily.ForeColor = Color.White;
            comboBoxLowPassFamily.Location = new Point(134, 129);
            comboBoxLowPassFamily.MinimumSize = new Size(36, 19);
            comboBoxLowPassFamily.Name = "comboBoxLowPassFamily";
            comboBoxLowPassFamily.Size = new Size(74, 19);
            comboBoxLowPassFamily.TabIndex = 16;
            // 
            // comboBoxLowPassSlope
            // 
            comboBoxLowPassSlope.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxLowPassSlope.ForeColor = Color.White;
            comboBoxLowPassSlope.Location = new Point(211, 129);
            comboBoxLowPassSlope.MinimumSize = new Size(36, 19);
            comboBoxLowPassSlope.Name = "comboBoxLowPassSlope";
            comboBoxLowPassSlope.Size = new Size(54, 19);
            comboBoxLowPassSlope.TabIndex = 17;
            // 
            // labelPeq
            // 
            labelPeq.AutoSize = true;
            labelPeq.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelPeq.ForeColor = Color.FromArgb(210, 214, 222);
            labelPeq.Location = new Point(8, 183);
            labelPeq.Name = "labelPeq";
            labelPeq.Size = new Size(29, 15);
            labelPeq.TabIndex = 18;
            labelPeq.Text = "PEQ";
            // 
            // buttonPeqLoad
            // 
            buttonPeqLoad.FlatStyle = FlatStyle.Popup;
            buttonPeqLoad.ForeColor = Color.White;
            buttonPeqLoad.Location = new Point(70, 181);
            buttonPeqLoad.Name = "buttonPeqLoad";
            buttonPeqLoad.Size = new Size(66, 19);
            buttonPeqLoad.TabIndex = 19;
            buttonPeqLoad.Text = "Load...";
            buttonPeqLoad.UseCompatibleTextRendering = true;
            buttonPeqLoad.UseVisualStyleBackColor = true;
            // 
            // buttonPeqClear
            // 
            buttonPeqClear.FlatStyle = FlatStyle.Popup;
            buttonPeqClear.ForeColor = Color.White;
            buttonPeqClear.Location = new Point(142, 181);
            buttonPeqClear.Name = "buttonPeqClear";
            buttonPeqClear.Size = new Size(56, 19);
            buttonPeqClear.TabIndex = 20;
            buttonPeqClear.Text = "Clear";
            buttonPeqClear.UseCompatibleTextRendering = true;
            buttonPeqClear.UseVisualStyleBackColor = true;
            // 
            // labelPeqInfo
            // 
            labelPeqInfo.AutoSize = true;
            labelPeqInfo.ForeColor = Color.FromArgb(170, 176, 190);
            labelPeqInfo.Location = new Point(204, 183);
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
            labelCurves.Location = new Point(8, 209);
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
            checkBoxShowRaw.Location = new Point(70, 207);
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
            checkBoxShowProcessed.Location = new Point(132, 207);
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
            checkBoxBypass.Location = new Point(220, 207);
            checkBoxBypass.Name = "checkBoxBypass";
            checkBoxBypass.Size = new Size(62, 19);
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
            buttonMute.Location = new Point(288, 4);
            buttonMute.Name = "buttonMute";
            buttonMute.Size = new Size(30, 24);
            buttonMute.TabIndex = 26;
            buttonMute.Text = "🔈";
            buttonMute.UseCompatibleTextRendering = true;
            buttonMute.UseVisualStyleBackColor = false;
            // 
            // numericHighPassRipple
            // 
            numericHighPassRipple.BackColor = Color.FromArgb(55, 60, 72);
            numericHighPassRipple.DecimalPlaces = 1;
            numericHighPassRipple.ForeColor = Color.White;
            numericHighPassRipple.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            numericHighPassRipple.Location = new Point(268, 103);
            numericHighPassRipple.Maximum = new decimal(new int[] { 30, 0, 0, 65536 });
            numericHighPassRipple.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            numericHighPassRipple.MinimumSize = new Size(36, 19);
            numericHighPassRipple.Name = "numericHighPassRipple";
            numericHighPassRipple.Size = new Size(50, 19);
            numericHighPassRipple.TabIndex = 28;
            numericHighPassRipple.TextAlign = HorizontalAlignment.Right;
            numericHighPassRipple.ThousandsSeparator = false;
            numericHighPassRipple.Value = new decimal(new int[] { 5, 0, 0, 65536 });
            // 
            // numericLowPassRipple
            // 
            numericLowPassRipple.BackColor = Color.FromArgb(55, 60, 72);
            numericLowPassRipple.DecimalPlaces = 1;
            numericLowPassRipple.ForeColor = Color.White;
            numericLowPassRipple.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            numericLowPassRipple.Location = new Point(269, 129);
            numericLowPassRipple.Maximum = new decimal(new int[] { 30, 0, 0, 65536 });
            numericLowPassRipple.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            numericLowPassRipple.MinimumSize = new Size(36, 19);
            numericLowPassRipple.Name = "numericLowPassRipple";
            numericLowPassRipple.Size = new Size(50, 19);
            numericLowPassRipple.TabIndex = 29;
            numericLowPassRipple.TextAlign = HorizontalAlignment.Right;
            numericLowPassRipple.ThousandsSeparator = false;
            numericLowPassRipple.Value = new decimal(new int[] { 5, 0, 0, 65536 });
            // 
            // labelAllpass
            // 
            labelAllpass.AutoSize = true;
            labelAllpass.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelAllpass.ForeColor = Color.FromArgb(210, 214, 222);
            labelAllpass.Location = new Point(8, 157);
            labelAllpass.Name = "labelAllpass";
            labelAllpass.Size = new Size(49, 15);
            labelAllpass.TabIndex = 30;
            labelAllpass.Text = "All-pass";
            // 
            // comboAllPassType
            // 
            comboAllPassType.BackColor = Color.FromArgb(55, 60, 72);
            comboAllPassType.ForeColor = Color.White;
            comboAllPassType.Location = new Point(70, 155);
            comboAllPassType.MinimumSize = new Size(36, 19);
            comboAllPassType.Name = "comboAllPassType";
            comboAllPassType.Size = new Size(60, 19);
            comboAllPassType.TabIndex = 32;
            // 
            // numericAllPassFreq
            // 
            numericAllPassFreq.BackColor = Color.FromArgb(55, 60, 72);
            numericAllPassFreq.DecimalPlaces = 0;
            numericAllPassFreq.ForeColor = Color.White;
            numericAllPassFreq.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            numericAllPassFreq.Location = new Point(134, 155);
            numericAllPassFreq.Maximum = new decimal(new int[] { 24000, 0, 0, 0 });
            numericAllPassFreq.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            numericAllPassFreq.MinimumSize = new Size(36, 19);
            numericAllPassFreq.Name = "numericAllPassFreq";
            numericAllPassFreq.Size = new Size(60, 19);
            numericAllPassFreq.TabIndex = 34;
            numericAllPassFreq.TextAlign = HorizontalAlignment.Right;
            numericAllPassFreq.ThousandsSeparator = false;
            numericAllPassFreq.Value = new decimal(new int[] { 2000, 0, 0, 0 });
            // 
            // numericAllPassQ
            // 
            numericAllPassQ.BackColor = Color.FromArgb(55, 60, 72);
            numericAllPassQ.DecimalPlaces = 1;
            numericAllPassQ.ForeColor = Color.White;
            numericAllPassQ.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            numericAllPassQ.Location = new Point(198, 155);
            numericAllPassQ.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            numericAllPassQ.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            numericAllPassQ.MinimumSize = new Size(36, 19);
            numericAllPassQ.Name = "numericAllPassQ";
            numericAllPassQ.Size = new Size(50, 19);
            numericAllPassQ.TabIndex = 35;
            numericAllPassQ.TextAlign = HorizontalAlignment.Right;
            numericAllPassQ.ThousandsSeparator = false;
            numericAllPassQ.Value = new decimal(new int[] { 10, 0, 0, 65536 });
            // 
            // labelAllpassBand
            // 
            labelAllpassBand.AutoSize = true;
            labelAllpassBand.ForeColor = Color.FromArgb(170, 176, 190);
            labelAllpassBand.Location = new Point(254, 157);
            labelAllpassBand.Name = "labelAllpassBand";
            labelAllpassBand.Size = new Size(49, 15);
            labelAllpassBand.TabIndex = 36;
            labelAllpassBand.Text = "= 0.00 ms";
            // 
            // VirtualCrossoverChannelControl
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(46, 51, 62);
            BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(labelAllpassBand);
            Controls.Add(numericAllPassQ);
            Controls.Add(numericAllPassFreq);
            Controls.Add(comboAllPassType);
            Controls.Add(labelAllpass);
            Controls.Add(numericLowPassRipple);
            Controls.Add(numericHighPassRipple);
            Controls.Add(buttonMute);
            Controls.Add(labelChannel);
            Controls.Add(buttonSource);
            Controls.Add(labelGain);
            Controls.Add(numericGain);
            Controls.Add(labelDelay);
            Controls.Add(numericDelay);
            Controls.Add(labelDelayMm);
            Controls.Add(checkBoxInvert);
            Controls.Add(checkBoxMono);
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
            MaximumSize = new Size(324, 232);
            MinimumSize = new Size(324, 232);
            Name = "VirtualCrossoverChannelControl";
            Size = new Size(322, 230);
            (numericGain).EndInit();
            (numericDelay).EndInit();
            (numericHighPassHz).EndInit();
            (numericLowPassHz).EndInit();
            (numericHighPassRipple).EndInit();
            (numericLowPassRipple).EndInit();
            (numericAllPassFreq).EndInit();
            (numericAllPassQ).EndInit();
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
        private CheckBox checkBoxMono;
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
        private DarkNumericUpDown numericHighPassRipple;
        private DarkNumericUpDown numericLowPassRipple;
        private Label labelAllpass;
        private DarkComboBox comboAllPassType;
        private DarkNumericUpDown numericAllPassFreq;
        private DarkNumericUpDown numericAllPassQ;
        private Label labelAllpassBand;
    }
}
