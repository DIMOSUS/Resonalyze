namespace Resonalyze.Options
{
    partial class LiveSpectrumOpt
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
            labelAnalysisMode = new Label();
            radioModeTransfer = new ReleaseClickRadioButton();
            radioModeRta = new ReleaseClickRadioButton();
            radioModeMmm = new ReleaseClickRadioButton();
            comboCalibration = new ThemedComboBox();
            label2 = new Label();
            labelSignalType = new Label();
            signalTypeComboBox = new ThemedComboBox();
            label3 = new Label();
            sequenceLengthComboBox = new ThemedComboBox();
            label4 = new Label();
            overlapComboBox = new ThemedComboBox();
            label5 = new Label();
            comboSmoothingInverseOctaves = new ThemedComboBox();
            label6 = new Label();
            windowComboBox = new ThemedComboBox();
            label7 = new Label();
            averagingComboBox = new ThemedComboBox();
            label8 = new Label();
            checkPeakHold = new ReleaseClickCheckBox();
            label9 = new Label();
            checkCoherence = new ReleaseClickCheckBox();
            label10 = new Label();
            coherenceLimitComboBox = new ThemedComboBox();
            buttonResetAverage = new ReleaseClickButton();
            labelSpl = new Label();
            checkSpl = new ReleaseClickCheckBox();
            labelTilt = new Label();
            checkTilt = new ReleaseClickCheckBox();
            labelCurves = new Label();
            labelMainCurve = new Label();
            checkMainCurve = new ReleaseClickCheckBox();
            labelInputMagnitude = new Label();
            checkInputMagnitude = new ReleaseClickCheckBox();
            SuspendLayout();
            //
            // labelAnalysisMode
            //
            labelAnalysisMode.AutoSize = true;
            labelAnalysisMode.ForeColor = UiPalette.TextDefault;
            labelAnalysisMode.Location = new Point(12, 13);
            labelAnalysisMode.Name = "labelAnalysisMode";
            labelAnalysisMode.Size = new Size(38, 15);
            labelAnalysisMode.TabIndex = 74;
            labelAnalysisMode.Text = "Mode";
            //
            // radioModeTransfer
            //
            radioModeTransfer.AutoSize = true;
            radioModeTransfer.Checked = true;
            radioModeTransfer.ForeColor = UiPalette.TextDefault;
            radioModeTransfer.Location = new Point(56, 11);
            radioModeTransfer.Name = "radioModeTransfer";
            radioModeTransfer.Size = new Size(67, 19);
            radioModeTransfer.TabIndex = 75;
            radioModeTransfer.TabStop = true;
            radioModeTransfer.Text = "Transfer";
            radioModeTransfer.UseVisualStyleBackColor = true;
            //
            // radioModeRta
            //
            radioModeRta.AutoSize = true;
            radioModeRta.ForeColor = UiPalette.TextDefault;
            radioModeRta.Location = new Point(129, 11);
            radioModeRta.Name = "radioModeRta";
            radioModeRta.Size = new Size(46, 19);
            radioModeRta.TabIndex = 76;
            radioModeRta.Text = "RTA";
            radioModeRta.UseVisualStyleBackColor = true;
            //
            // radioModeMmm
            //
            radioModeMmm.AutoSize = true;
            radioModeMmm.ForeColor = UiPalette.TextDefault;
            radioModeMmm.Location = new Point(181, 11);
            radioModeMmm.Name = "radioModeMmm";
            radioModeMmm.Size = new Size(53, 19);
            radioModeMmm.TabIndex = 77;
            radioModeMmm.Text = "MMM";
            radioModeMmm.UseVisualStyleBackColor = true;
            //
            // labelSignalType
            //
            labelSignalType.AutoSize = true;
            labelSignalType.ForeColor = UiPalette.TextDefault;
            labelSignalType.Location = new Point(12, 43);
            labelSignalType.Name = "labelSignalType";
            labelSignalType.Size = new Size(64, 15);
            labelSignalType.TabIndex = 37;
            labelSignalType.Text = "Signal Type";
            //
            // signalTypeComboBox
            //
            signalTypeComboBox.BackColor = UiPalette.ControlSurface;
            signalTypeComboBox.ForeColor = UiPalette.TextPrimary;
            signalTypeComboBox.Location = new Point(132, 40);
            signalTypeComboBox.Margin = new Padding(0);
            signalTypeComboBox.MinimumSize = new Size(36, 19);
            signalTypeComboBox.Name = "signalTypeComboBox";
            signalTypeComboBox.Size = new Size(121, 23);
            signalTypeComboBox.TabIndex = 48;
            //
            // label6
            //
            label6.AutoSize = true;
            label6.ForeColor = UiPalette.TextDefault;
            label6.Location = new Point(12, 72);
            label6.Name = "label6";
            label6.Size = new Size(48, 15);
            label6.TabIndex = 55;
            label6.Text = "Window";
            //
            // windowComboBox
            //
            windowComboBox.BackColor = UiPalette.ControlSurface;
            windowComboBox.ForeColor = UiPalette.TextPrimary;
            windowComboBox.Location = new Point(132, 69);
            windowComboBox.Margin = new Padding(0);
            windowComboBox.MinimumSize = new Size(36, 19);
            windowComboBox.Name = "windowComboBox";
            windowComboBox.Size = new Size(121, 23);
            windowComboBox.TabIndex = 56;
            //
            // label3
            //
            label3.AutoSize = true;
            label3.ForeColor = UiPalette.TextDefault;
            label3.Location = new Point(12, 101);
            label3.Name = "label3";
            label3.Size = new Size(98, 15);
            label3.TabIndex = 49;
            label3.Text = "Sequence Length";
            //
            // sequenceLengthComboBox
            //
            sequenceLengthComboBox.BackColor = UiPalette.ControlSurface;
            sequenceLengthComboBox.ForeColor = UiPalette.TextPrimary;
            sequenceLengthComboBox.Location = new Point(132, 98);
            sequenceLengthComboBox.Margin = new Padding(0);
            sequenceLengthComboBox.MinimumSize = new Size(36, 19);
            sequenceLengthComboBox.Name = "sequenceLengthComboBox";
            sequenceLengthComboBox.Size = new Size(121, 23);
            sequenceLengthComboBox.TabIndex = 50;
            //
            // label4
            //
            label4.AutoSize = true;
            label4.ForeColor = UiPalette.TextDefault;
            label4.Location = new Point(12, 130);
            label4.Name = "label4";
            label4.Size = new Size(48, 15);
            label4.TabIndex = 51;
            label4.Text = "Overlap";
            //
            // overlapComboBox
            //
            overlapComboBox.BackColor = UiPalette.ControlSurface;
            overlapComboBox.ForeColor = UiPalette.TextPrimary;
            overlapComboBox.Location = new Point(132, 127);
            overlapComboBox.Margin = new Padding(0);
            overlapComboBox.MinimumSize = new Size(36, 19);
            overlapComboBox.Name = "overlapComboBox";
            overlapComboBox.Size = new Size(121, 23);
            overlapComboBox.TabIndex = 52;
            //
            // label5
            //
            label5.AutoSize = true;
            label5.ForeColor = UiPalette.TextDefault;
            label5.Location = new Point(12, 159);
            label5.Name = "label5";
            label5.Size = new Size(62, 15);
            label5.TabIndex = 53;
            label5.Text = "Smoothing";
            //
            // comboSmoothingInverseOctaves
            //
            comboSmoothingInverseOctaves.BackColor = UiPalette.ControlSurface;
            comboSmoothingInverseOctaves.ForeColor = UiPalette.TextPrimary;
            comboSmoothingInverseOctaves.Location = new Point(132, 156);
            comboSmoothingInverseOctaves.Margin = new Padding(0);
            comboSmoothingInverseOctaves.MinimumSize = new Size(36, 19);
            comboSmoothingInverseOctaves.Name = "comboSmoothingInverseOctaves";
            comboSmoothingInverseOctaves.Size = new Size(121, 23);
            comboSmoothingInverseOctaves.TabIndex = 54;
            //
            // label7
            //
            label7.AutoSize = true;
            label7.ForeColor = UiPalette.TextDefault;
            label7.Location = new Point(12, 188);
            label7.Name = "label7";
            label7.Size = new Size(60, 15);
            label7.TabIndex = 57;
            label7.Text = "Averaging";
            //
            // averagingComboBox
            //
            averagingComboBox.BackColor = UiPalette.ControlSurface;
            averagingComboBox.ForeColor = UiPalette.TextPrimary;
            averagingComboBox.Location = new Point(132, 185);
            averagingComboBox.Margin = new Padding(0);
            averagingComboBox.MinimumSize = new Size(36, 19);
            averagingComboBox.Name = "averagingComboBox";
            averagingComboBox.Size = new Size(121, 23);
            averagingComboBox.TabIndex = 58;
            //
            // label10
            //
            label10.AutoSize = true;
            label10.ForeColor = UiPalette.TextDefault;
            label10.Location = new Point(12, 217);
            label10.Name = "label10";
            label10.Size = new Size(95, 15);
            label10.TabIndex = 64;
            label10.Text = "Coherence Limit";
            //
            // coherenceLimitComboBox
            //
            coherenceLimitComboBox.BackColor = UiPalette.ControlSurface;
            coherenceLimitComboBox.ForeColor = UiPalette.TextPrimary;
            coherenceLimitComboBox.Location = new Point(132, 214);
            coherenceLimitComboBox.Margin = new Padding(0);
            coherenceLimitComboBox.MinimumSize = new Size(36, 19);
            coherenceLimitComboBox.Name = "coherenceLimitComboBox";
            coherenceLimitComboBox.Size = new Size(121, 23);
            coherenceLimitComboBox.TabIndex = 65;
            //
            // label2
            //
            label2.AutoSize = true;
            label2.ForeColor = UiPalette.TextDefault;
            label2.Location = new Point(12, 246);
            label2.Name = "label2";
            label2.Size = new Size(65, 15);
            label2.TabIndex = 46;
            label2.Text = "Calibration";
            //
            // comboCalibration
            //
            comboCalibration.BackColor = UiPalette.ControlSurface;
            comboCalibration.ForeColor = UiPalette.TextPrimary;
            comboCalibration.Location = new Point(132, 243);
            comboCalibration.Margin = new Padding(0);
            comboCalibration.MinimumSize = new Size(36, 19);
            comboCalibration.Name = "comboCalibration";
            comboCalibration.Size = new Size(121, 23);
            comboCalibration.TabIndex = 47;
            //
            // labelSpl
            //
            labelSpl.AutoSize = true;
            labelSpl.ForeColor = UiPalette.TextDefault;
            labelSpl.Location = new Point(12, 275);
            labelSpl.Name = "labelSpl";
            labelSpl.Size = new Size(43, 15);
            labelSpl.TabIndex = 77;
            labelSpl.Text = "dB SPL";
            //
            // checkSpl
            //
            checkSpl.AutoSize = true;
            checkSpl.ForeColor = UiPalette.TextDefault;
            checkSpl.Location = new Point(238, 275);
            checkSpl.Name = "checkSpl";
            checkSpl.Size = new Size(15, 14);
            checkSpl.TabIndex = 78;
            checkSpl.UseVisualStyleBackColor = true;
            //
            // labelTilt
            //
            labelTilt.AutoSize = true;
            labelTilt.ForeColor = UiPalette.TextDefault;
            labelTilt.Location = new Point(12, 298);
            labelTilt.Name = "labelTilt";
            labelTilt.Size = new Size(110, 15);
            labelTilt.TabIndex = 79;
            labelTilt.Text = "Slope compensation";
            //
            // checkTilt
            //
            checkTilt.AutoSize = true;
            checkTilt.ForeColor = UiPalette.TextDefault;
            checkTilt.Location = new Point(238, 298);
            checkTilt.Name = "checkTilt";
            checkTilt.Size = new Size(15, 14);
            checkTilt.TabIndex = 80;
            checkTilt.UseVisualStyleBackColor = true;
            //
            // labelCurves
            //
            labelCurves.AutoSize = true;
            labelCurves.ForeColor = UiPalette.TextAccent;
            labelCurves.Location = new Point(12, 321);
            labelCurves.Name = "labelCurves";
            labelCurves.Size = new Size(48, 15);
            labelCurves.TabIndex = 66;
            labelCurves.Text = "Curves:";
            //
            // labelMainCurve
            //
            labelMainCurve.AutoSize = true;
            labelMainCurve.ForeColor = UiPalette.TextDefault;
            labelMainCurve.Location = new Point(12, 344);
            labelMainCurve.Name = "labelMainCurve";
            labelMainCurve.Size = new Size(67, 15);
            labelMainCurve.TabIndex = 67;
            labelMainCurve.Text = "Main curve";
            //
            // checkMainCurve
            //
            checkMainCurve.AutoSize = true;
            checkMainCurve.ForeColor = UiPalette.TextDefault;
            checkMainCurve.Location = new Point(238, 344);
            checkMainCurve.Name = "checkMainCurve";
            checkMainCurve.Size = new Size(15, 14);
            checkMainCurve.TabIndex = 68;
            checkMainCurve.UseVisualStyleBackColor = true;
            //
            // labelInputMagnitude
            //
            labelInputMagnitude.AutoSize = true;
            labelInputMagnitude.ForeColor = UiPalette.TextDefault;
            labelInputMagnitude.Location = new Point(12, 367);
            labelInputMagnitude.Name = "labelInputMagnitude";
            labelInputMagnitude.Size = new Size(67, 15);
            labelInputMagnitude.TabIndex = 69;
            labelInputMagnitude.Text = "RTA (input)";
            //
            // checkInputMagnitude
            //
            checkInputMagnitude.AutoSize = true;
            checkInputMagnitude.ForeColor = UiPalette.TextDefault;
            checkInputMagnitude.Location = new Point(238, 367);
            checkInputMagnitude.Name = "checkInputMagnitude";
            checkInputMagnitude.Size = new Size(15, 14);
            checkInputMagnitude.TabIndex = 70;
            checkInputMagnitude.UseVisualStyleBackColor = true;
            //
            // label9
            //
            label9.AutoSize = true;
            label9.ForeColor = UiPalette.TextDefault;
            label9.Location = new Point(12, 390);
            label9.Name = "label9";
            label9.Size = new Size(64, 15);
            label9.TabIndex = 62;
            label9.Text = "Coherence";
            //
            // checkCoherence
            //
            checkCoherence.AutoSize = true;
            checkCoherence.ForeColor = UiPalette.TextDefault;
            checkCoherence.Location = new Point(238, 390);
            checkCoherence.Name = "checkCoherence";
            checkCoherence.Size = new Size(15, 14);
            checkCoherence.TabIndex = 63;
            checkCoherence.UseVisualStyleBackColor = true;
            //
            // label8
            //
            label8.AutoSize = true;
            label8.ForeColor = UiPalette.TextDefault;
            label8.Location = new Point(12, 413);
            label8.Name = "label8";
            label8.Size = new Size(60, 15);
            label8.TabIndex = 59;
            label8.Text = "Peak Hold";
            //
            // checkPeakHold
            //
            checkPeakHold.AutoSize = true;
            checkPeakHold.ForeColor = UiPalette.TextDefault;
            checkPeakHold.Location = new Point(238, 413);
            checkPeakHold.Name = "checkPeakHold";
            checkPeakHold.Size = new Size(15, 14);
            checkPeakHold.TabIndex = 60;
            checkPeakHold.UseVisualStyleBackColor = true;
            //
            // buttonResetAverage
            //
            buttonResetAverage.BackColor = UiPalette.ButtonBackground;
            buttonResetAverage.FlatStyle = FlatStyle.Popup;
            buttonResetAverage.ForeColor = UiPalette.TextPrimary;
            buttonResetAverage.Location = new Point(12, 439);
            buttonResetAverage.Name = "buttonResetAverage";
            buttonResetAverage.Size = new Size(241, 23);
            buttonResetAverage.TabIndex = 61;
            buttonResetAverage.Text = "Reset Average";
            buttonResetAverage.UseVisualStyleBackColor = false;
            //
            // LiveSpectrumOpt
            //
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiPalette.AppBackground;
            ClientSize = new Size(265, 474);
            Controls.Add(labelAnalysisMode);
            Controls.Add(radioModeTransfer);
            Controls.Add(radioModeRta);
            Controls.Add(radioModeMmm);
            Controls.Add(signalTypeComboBox);
            Controls.Add(labelSignalType);
            Controls.Add(labelSpl);
            Controls.Add(checkSpl);
            Controls.Add(labelTilt);
            Controls.Add(checkTilt);
            Controls.Add(checkInputMagnitude);
            Controls.Add(labelInputMagnitude);
            Controls.Add(checkMainCurve);
            Controls.Add(labelMainCurve);
            Controls.Add(labelCurves);
            Controls.Add(buttonResetAverage);
            Controls.Add(coherenceLimitComboBox);
            Controls.Add(label10);
            Controls.Add(checkCoherence);
            Controls.Add(label9);
            Controls.Add(checkPeakHold);
            Controls.Add(label8);
            Controls.Add(averagingComboBox);
            Controls.Add(label7);
            Controls.Add(windowComboBox);
            Controls.Add(label6);
            Controls.Add(comboSmoothingInverseOctaves);
            Controls.Add(label5);
            Controls.Add(overlapComboBox);
            Controls.Add(label4);
            Controls.Add(sequenceLengthComboBox);
            Controls.Add(label3);
            Controls.Add(comboCalibration);
            Controls.Add(label2);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "LiveSpectrumOpt";
            ShowInTaskbar = false;
            Text = "Live Spectrum Options";
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion
        private Label labelAnalysisMode;
        private ReleaseClickRadioButton radioModeTransfer;
        private ReleaseClickRadioButton radioModeRta;
        private ReleaseClickRadioButton radioModeMmm;
        private ThemedComboBox comboCalibration;
        private Label label2;
        private Label labelSignalType;
        private ThemedComboBox signalTypeComboBox;
        private Label label3;
        private ThemedComboBox sequenceLengthComboBox;
        private Label label4;
        private ThemedComboBox overlapComboBox;
        private Label label5;
        private ThemedComboBox comboSmoothingInverseOctaves;
        private Label label6;
        private ThemedComboBox windowComboBox;
        private Label label7;
        private ThemedComboBox averagingComboBox;
        private Label label8;
        private ReleaseClickCheckBox checkPeakHold;
        private Label label9;
        private ReleaseClickCheckBox checkCoherence;
        private Label label10;
        private ThemedComboBox coherenceLimitComboBox;
        private ReleaseClickButton buttonResetAverage;
        private Label labelSpl;
        private ReleaseClickCheckBox checkSpl;
        private Label labelTilt;
        private ReleaseClickCheckBox checkTilt;
        private Label labelCurves;
        private Label labelMainCurve;
        private ReleaseClickCheckBox checkMainCurve;
        private Label labelInputMagnitude;
        private ReleaseClickCheckBox checkInputMagnitude;
    }
}
