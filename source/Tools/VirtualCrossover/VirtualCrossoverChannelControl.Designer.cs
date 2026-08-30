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
            buttonSource = new ReleaseClickButton();
            buttonSpatialAverage = new ReleaseClickButton();
            labelGain = new Label();
            buttonCollapse = new ReleaseClickButton();
            numericGain = new DarkNumericUpDown();
            labelDelay = new Label();
            numericDelay = new DarkNumericUpDown();
            checkBoxInvert = new ReleaseClickCheckBox();
            checkBoxMono = new ReleaseClickCheckBox();
            comboBoxZone = new DarkComboBox();
            buttonMoveUp = new ReleaseClickButton();
            buttonMoveDown = new ReleaseClickButton();
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
            buttonPeqMenu = new ReleaseClickButton();
            labelPeqInfo = new Label();
            labelCurves = new Label();
            checkBoxShowRaw = new ReleaseClickCheckBox();
            checkBoxShowProcessed = new ReleaseClickCheckBox();
            checkBoxBypass = new ReleaseClickCheckBox();
            buttonMute = new ReleaseClickButton();
            numericHighPassRipple = new DarkNumericUpDown();
            numericLowPassRipple = new DarkNumericUpDown();
            labelTotalGain = new Label();
            (numericGain).BeginInit();
            (numericDelay).BeginInit();
            (numericHighPassHz).BeginInit();
            (numericLowPassHz).BeginInit();
            (numericHighPassRipple).BeginInit();
            (numericLowPassRipple).BeginInit();
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
            buttonSource.Size = new Size(150, 24);
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
            labelGain.Location = new Point(8, 56);
            labelGain.Name = "labelGain";
            labelGain.Size = new Size(48, 15);
            labelGain.TabIndex = 2;
            labelGain.Text = "Gain dB";
            // 
            // buttonCollapse
            // 
            buttonCollapse.FlatStyle = FlatStyle.Popup;
            buttonCollapse.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point, 204);
            buttonCollapse.ForeColor = Color.White;
            buttonCollapse.Location = new Point(8, 77);
            buttonCollapse.Name = "buttonCollapse";
            buttonCollapse.Size = new Size(48, 20);
            buttonCollapse.TabIndex = 9;
            buttonCollapse.Text = "−";
            buttonCollapse.UseCompatibleTextRendering = true;
            buttonCollapse.UseVisualStyleBackColor = true;
            // 
            // numericGain
            // 
            numericGain.BackColor = Color.FromArgb(55, 60, 72);
            numericGain.DecimalPlaces = 1;
            numericGain.ForeColor = Color.White;
            numericGain.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            numericGain.Location = new Point(70, 54);
            numericGain.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            numericGain.Minimum = new decimal(new int[] { 60, 0, 0, int.MinValue });
            numericGain.MinimumSize = new Size(36, 19);
            numericGain.Name = "numericGain";
            numericGain.Size = new Size(55, 19);
            numericGain.TabIndex = 7;
            numericGain.TextAlign = HorizontalAlignment.Right;
            numericGain.ThousandsSeparator = false;
            numericGain.Value = new decimal(new int[] { 0, 0, 0, 0 });
            // 
            // labelDelay
            // 
            labelDelay.AutoSize = true;
            labelDelay.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelDelay.ForeColor = Color.FromArgb(210, 214, 222);
            labelDelay.Location = new Point(212, 56);
            labelDelay.Name = "labelDelay";
            labelDelay.Size = new Size(37, 15);
            labelDelay.TabIndex = 4;
            labelDelay.Text = "Delay";
            // 
            // numericDelay
            // 
            numericDelay.BackColor = Color.FromArgb(55, 60, 72);
            numericDelay.DecimalPlaces = 2;
            numericDelay.ForeColor = Color.White;
            numericDelay.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            numericDelay.Location = new Point(252, 54);
            numericDelay.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            numericDelay.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericDelay.MinimumSize = new Size(36, 19);
            numericDelay.Name = "numericDelay";
            numericDelay.Size = new Size(66, 19);
            numericDelay.TabIndex = 8;
            numericDelay.TextAlign = HorizontalAlignment.Right;
            numericDelay.ThousandsSeparator = false;
            numericDelay.Value = new decimal(new int[] { 0, 0, 0, 0 });
            // 
            // comboBoxZone
            // 
            comboBoxZone.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxZone.ForeColor = Color.White;
            comboBoxZone.Location = new Point(70, 77);
            comboBoxZone.MinimumSize = new Size(36, 19);
            comboBoxZone.Name = "comboBoxZone";
            comboBoxZone.Size = new Size(68, 19);
            comboBoxZone.TabIndex = 10;
            // 
            // checkBoxInvert
            // 
            checkBoxInvert.AutoSize = true;
            checkBoxInvert.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            checkBoxInvert.ForeColor = Color.FromArgb(210, 214, 222);
            checkBoxInvert.Location = new Point(208, 77);
            checkBoxInvert.Name = "checkBoxInvert";
            checkBoxInvert.Size = new Size(57, 19);
            checkBoxInvert.TabIndex = 12;
            checkBoxInvert.Text = "Invert";
            checkBoxInvert.UseVisualStyleBackColor = true;
            // 
            // checkBoxMono
            // 
            checkBoxMono.AutoSize = true;
            checkBoxMono.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            checkBoxMono.ForeColor = Color.FromArgb(210, 214, 222);
            checkBoxMono.Location = new Point(144, 77);
            checkBoxMono.Name = "checkBoxMono";
            checkBoxMono.Size = new Size(58, 19);
            checkBoxMono.TabIndex = 11;
            checkBoxMono.Text = "Mono";
            checkBoxMono.UseVisualStyleBackColor = true;
            //
            // buttonMoveUp
            //
            buttonMoveUp.FlatStyle = FlatStyle.Popup;
            buttonMoveUp.ForeColor = Color.White;
            buttonMoveUp.Location = new Point(268, 77);
            buttonMoveUp.Name = "buttonMoveUp";
            buttonMoveUp.Size = new Size(23, 20);
            buttonMoveUp.TabIndex = 23;
            buttonMoveUp.Text = "▲";
            buttonMoveUp.UseCompatibleTextRendering = true;
            buttonMoveUp.UseVisualStyleBackColor = true;
            //
            // buttonMoveDown
            //
            buttonMoveDown.FlatStyle = FlatStyle.Popup;
            buttonMoveDown.ForeColor = Color.White;
            buttonMoveDown.Location = new Point(293, 77);
            buttonMoveDown.Name = "buttonMoveDown";
            buttonMoveDown.Size = new Size(23, 20);
            buttonMoveDown.TabIndex = 24;
            buttonMoveDown.Text = "▼";
            buttonMoveDown.UseCompatibleTextRendering = true;
            buttonMoveDown.UseVisualStyleBackColor = true;
            //
            // labelCrossover
            //
            labelCrossover.AutoSize = true;
            labelCrossover.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelCrossover.ForeColor = Color.FromArgb(210, 214, 222);
            labelCrossover.Location = new Point(8, 105);
            labelCrossover.Name = "labelCrossover";
            labelCrossover.Size = new Size(58, 15);
            labelCrossover.TabIndex = 8;
            labelCrossover.Text = "Crossover";
            // 
            // comboBoxCrossoverKind
            // 
            comboBoxCrossoverKind.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxCrossoverKind.ForeColor = Color.White;
            comboBoxCrossoverKind.Location = new Point(70, 103);
            comboBoxCrossoverKind.MinimumSize = new Size(36, 19);
            comboBoxCrossoverKind.Name = "comboBoxCrossoverKind";
            comboBoxCrossoverKind.Size = new Size(100, 19);
            comboBoxCrossoverKind.TabIndex = 13;
            // 
            // labelMeasuredPolarity
            // 
            labelMeasuredPolarity.AutoSize = true;
            labelMeasuredPolarity.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelMeasuredPolarity.ForeColor = Color.FromArgb(170, 176, 190);
            labelMeasuredPolarity.Location = new Point(186, 105);
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
            labelHighPass.Location = new Point(8, 131);
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
            numericHighPassHz.Location = new Point(70, 129);
            numericHighPassHz.LogarithmicFrequencyStep = true;
            numericHighPassHz.Maximum = new decimal(new int[] { 24000, 0, 0, 0 });
            numericHighPassHz.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            numericHighPassHz.MinimumSize = new Size(36, 19);
            numericHighPassHz.Name = "numericHighPassHz";
            numericHighPassHz.Size = new Size(60, 19);
            numericHighPassHz.TabIndex = 14;
            numericHighPassHz.TextAlign = HorizontalAlignment.Right;
            numericHighPassHz.ThousandsSeparator = false;
            numericHighPassHz.Value = new decimal(new int[] { 2000, 0, 0, 0 });
            // 
            // comboBoxHighPassFamily
            // 
            comboBoxHighPassFamily.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxHighPassFamily.ForeColor = Color.White;
            comboBoxHighPassFamily.Location = new Point(134, 129);
            comboBoxHighPassFamily.MinimumSize = new Size(36, 19);
            comboBoxHighPassFamily.Name = "comboBoxHighPassFamily";
            comboBoxHighPassFamily.Size = new Size(74, 19);
            comboBoxHighPassFamily.TabIndex = 15;
            // 
            // comboBoxHighPassSlope
            // 
            comboBoxHighPassSlope.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxHighPassSlope.ForeColor = Color.White;
            comboBoxHighPassSlope.Location = new Point(211, 129);
            comboBoxHighPassSlope.MinimumSize = new Size(36, 19);
            comboBoxHighPassSlope.Name = "comboBoxHighPassSlope";
            comboBoxHighPassSlope.Size = new Size(54, 19);
            comboBoxHighPassSlope.TabIndex = 16;
            // 
            // labelLowPass
            // 
            labelLowPass.AutoSize = true;
            labelLowPass.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelLowPass.ForeColor = Color.FromArgb(210, 214, 222);
            labelLowPass.Location = new Point(8, 157);
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
            numericLowPassHz.Location = new Point(70, 155);
            numericLowPassHz.LogarithmicFrequencyStep = true;
            numericLowPassHz.Maximum = new decimal(new int[] { 24000, 0, 0, 0 });
            numericLowPassHz.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            numericLowPassHz.MinimumSize = new Size(36, 19);
            numericLowPassHz.Name = "numericLowPassHz";
            numericLowPassHz.Size = new Size(60, 19);
            numericLowPassHz.TabIndex = 18;
            numericLowPassHz.TextAlign = HorizontalAlignment.Right;
            numericLowPassHz.ThousandsSeparator = false;
            numericLowPassHz.Value = new decimal(new int[] { 2000, 0, 0, 0 });
            // 
            // comboBoxLowPassFamily
            // 
            comboBoxLowPassFamily.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxLowPassFamily.ForeColor = Color.White;
            comboBoxLowPassFamily.Location = new Point(134, 155);
            comboBoxLowPassFamily.MinimumSize = new Size(36, 19);
            comboBoxLowPassFamily.Name = "comboBoxLowPassFamily";
            comboBoxLowPassFamily.Size = new Size(74, 19);
            comboBoxLowPassFamily.TabIndex = 19;
            // 
            // comboBoxLowPassSlope
            // 
            comboBoxLowPassSlope.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxLowPassSlope.ForeColor = Color.White;
            comboBoxLowPassSlope.Location = new Point(211, 155);
            comboBoxLowPassSlope.MinimumSize = new Size(36, 19);
            comboBoxLowPassSlope.Name = "comboBoxLowPassSlope";
            comboBoxLowPassSlope.Size = new Size(54, 19);
            comboBoxLowPassSlope.TabIndex = 20;
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
            // buttonPeqMenu
            // 
            buttonPeqMenu.FlatStyle = FlatStyle.Popup;
            buttonPeqMenu.ForeColor = Color.White;
            buttonPeqMenu.Location = new Point(70, 181);
            buttonPeqMenu.Name = "buttonPeqMenu";
            buttonPeqMenu.Size = new Size(80, 19);
            buttonPeqMenu.TabIndex = 22;
            buttonPeqMenu.Text = "Load / Edit…";
            buttonPeqMenu.UseCompatibleTextRendering = true;
            buttonPeqMenu.UseVisualStyleBackColor = true;
            // 
            // labelPeqInfo
            // 
            labelPeqInfo.AutoEllipsis = true;
            labelPeqInfo.ForeColor = Color.FromArgb(170, 176, 190);
            labelPeqInfo.Location = new Point(152, 183);
            labelPeqInfo.Name = "labelPeqInfo";
            labelPeqInfo.Size = new Size(167, 15);
            labelPeqInfo.TabIndex = 21;
            labelPeqInfo.Text = "No PEQ";
            // 
            // labelCurves
            // 
            labelCurves.AutoSize = true;
            labelCurves.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelCurves.ForeColor = Color.FromArgb(210, 214, 222);
            labelCurves.Location = new Point(8, 35);
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
            checkBoxShowRaw.Location = new Point(70, 33);
            checkBoxShowRaw.Name = "checkBoxShowRaw";
            checkBoxShowRaw.Size = new Size(48, 19);
            checkBoxShowRaw.TabIndex = 4;
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
            checkBoxShowProcessed.Location = new Point(132, 33);
            checkBoxShowProcessed.Name = "checkBoxShowProcessed";
            checkBoxShowProcessed.Size = new Size(79, 19);
            checkBoxShowProcessed.TabIndex = 5;
            checkBoxShowProcessed.Text = "Processed";
            checkBoxShowProcessed.UseVisualStyleBackColor = true;
            // 
            // checkBoxBypass
            // 
            checkBoxBypass.AutoSize = true;
            checkBoxBypass.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            checkBoxBypass.ForeColor = Color.FromArgb(210, 214, 222);
            checkBoxBypass.Location = new Point(220, 33);
            checkBoxBypass.Name = "checkBoxBypass";
            checkBoxBypass.Size = new Size(62, 19);
            checkBoxBypass.TabIndex = 6;
            checkBoxBypass.Text = "Bypass";
            checkBoxBypass.UseVisualStyleBackColor = true;
            // 
            // buttonSpatialAverage
            // 
            buttonSpatialAverage.BackColor = Color.FromArgb(46, 51, 67);
            buttonSpatialAverage.FlatStyle = FlatStyle.Popup;
            buttonSpatialAverage.ForeColor = Color.White;
            buttonSpatialAverage.Location = new Point(224, 4);
            buttonSpatialAverage.Name = "buttonSpatialAverage";
            buttonSpatialAverage.Size = new Size(60, 24);
            buttonSpatialAverage.TabIndex = 2;
            buttonSpatialAverage.Text = "MMM";
            buttonSpatialAverage.UseCompatibleTextRendering = true;
            buttonSpatialAverage.UseVisualStyleBackColor = false;
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
            buttonMute.TabIndex = 3;
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
            numericHighPassRipple.Location = new Point(268, 129);
            numericHighPassRipple.Maximum = new decimal(new int[] { 30, 0, 0, 65536 });
            numericHighPassRipple.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            numericHighPassRipple.MinimumSize = new Size(36, 19);
            numericHighPassRipple.Name = "numericHighPassRipple";
            numericHighPassRipple.Size = new Size(50, 19);
            numericHighPassRipple.TabIndex = 17;
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
            numericLowPassRipple.Location = new Point(269, 155);
            numericLowPassRipple.Maximum = new decimal(new int[] { 30, 0, 0, 65536 });
            numericLowPassRipple.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            numericLowPassRipple.MinimumSize = new Size(36, 19);
            numericLowPassRipple.Name = "numericLowPassRipple";
            numericLowPassRipple.Size = new Size(50, 19);
            numericLowPassRipple.TabIndex = 21;
            numericLowPassRipple.TextAlign = HorizontalAlignment.Right;
            numericLowPassRipple.ThousandsSeparator = false;
            numericLowPassRipple.Value = new decimal(new int[] { 5, 0, 0, 65536 });
            //
            // labelTotalGain
            // 
            labelTotalGain.AutoSize = true;
            labelTotalGain.ForeColor = Color.FromArgb(170, 176, 190);
            labelTotalGain.Location = new Point(131, 56);
            labelTotalGain.Name = "labelTotalGain";
            labelTotalGain.Size = new Size(47, 15);
            labelTotalGain.TabIndex = 37;
            labelTotalGain.Text = "All +0.0";
            // 
            // VirtualCrossoverChannelControl
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(46, 51, 62);
            Controls.Add(labelTotalGain);
            Controls.Add(numericLowPassRipple);
            Controls.Add(numericHighPassRipple);
            Controls.Add(buttonMute);
            Controls.Add(labelChannel);
            Controls.Add(buttonSource);
            Controls.Add(buttonSpatialAverage);
            Controls.Add(labelGain);
            Controls.Add(buttonCollapse);
            Controls.Add(numericGain);
            Controls.Add(labelDelay);
            Controls.Add(numericDelay);
            Controls.Add(checkBoxInvert);
            Controls.Add(checkBoxMono);
            Controls.Add(comboBoxZone);
            Controls.Add(buttonMoveUp);
            Controls.Add(buttonMoveDown);
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
            Controls.Add(buttonPeqMenu);
            Controls.Add(labelPeqInfo);
            Controls.Add(labelCurves);
            Controls.Add(checkBoxShowRaw);
            Controls.Add(checkBoxShowProcessed);
            Controls.Add(checkBoxBypass);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            MaximumSize = new Size(324, 206);
            MinimumSize = new Size(324, 206);
            Name = "VirtualCrossoverChannelControl";
            Size = new Size(322, 204);
            (numericGain).EndInit();
            (numericDelay).EndInit();
            (numericHighPassHz).EndInit();
            (numericLowPassHz).EndInit();
            (numericHighPassRipple).EndInit();
            (numericLowPassRipple).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label labelChannel;
        private ReleaseClickButton buttonSource;
        private ReleaseClickButton buttonSpatialAverage;
        private Label labelGain;
        private ReleaseClickButton buttonCollapse;
        private DarkNumericUpDown numericGain;
        private Label labelDelay;
        private DarkNumericUpDown numericDelay;
        private ReleaseClickCheckBox checkBoxInvert;
        private ReleaseClickCheckBox checkBoxMono;
        private DarkComboBox comboBoxZone;
        private ReleaseClickButton buttonMoveUp;
        private ReleaseClickButton buttonMoveDown;
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
        private ReleaseClickButton buttonPeqMenu;
        private Label labelPeqInfo;
        private Label labelCurves;
        private ReleaseClickCheckBox checkBoxShowRaw;
        private ReleaseClickCheckBox checkBoxShowProcessed;
        private ReleaseClickCheckBox checkBoxBypass;
        private ReleaseClickButton buttonMute;
        private DarkNumericUpDown numericHighPassRipple;
        private DarkNumericUpDown numericLowPassRipple;
        private Label labelTotalGain;
    }
}
