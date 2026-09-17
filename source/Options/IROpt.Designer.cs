namespace Resonalyze.Options
{
    partial class IROpt
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
            numericLength = new ThemedNumericUpDown();
            label1 = new Label();
            labelBandWidth = new Label();
            comboBandWidth = new ThemedComboBox();
            labelBandCenter = new Label();
            comboBandCenter = new ThemedComboBox();
            labelAmplitudeScale = new Label();
            comboAmplitudeScale = new ThemedComboBox();
            labelTimeUnit = new Label();
            comboTimeUnit = new ThemedComboBox();
            labelTimeOrigin = new Label();
            comboTimeOrigin = new ThemedComboBox();
            labelEnvelopeSmoothing = new Label();
            numericEnvelopeSmoothing = new ThemedNumericUpDown();
            labelInvert = new Label();
            checkInvert = new ReleaseClickCheckBox();
            labelNormalizeStep = new Label();
            checkNormalizeStep = new ReleaseClickCheckBox();
            labelCurves = new Label();
            checkBoxShowImpulse = new ReleaseClickCheckBox();
            checkBoxShowEnvelope = new ReleaseClickCheckBox();
            checkBoxShowStep = new ReleaseClickCheckBox();
            (numericLength).BeginInit();
            (numericEnvelopeSmoothing).BeginInit();
            SuspendLayout();
            //
            // numericLength
            //
            numericLength.BackColor = UiPalette.ControlSurface;
            numericLength.DecimalPlaces = 0;
            numericLength.ForeColor = UiPalette.TextPrimary;
            numericLength.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericLength.Location = new Point(193, 12);
            numericLength.Maximum = new decimal(new int[] { 32768, 0, 0, 0 });
            numericLength.Minimum = new decimal(new int[] { 4, 0, 0, 0 });
            numericLength.MinimumSize = new Size(36, 19);
            numericLength.Name = "numericLength";
            numericLength.Size = new Size(60, 19);
            numericLength.TabIndex = 38;
            numericLength.TextAlign = HorizontalAlignment.Right;
            numericLength.ThousandsSeparator = false;
            numericLength.Value = new decimal(new int[] { 8192, 0, 0, 0 });
            //
            // label1
            //
            label1.AutoSize = true;
            label1.ForeColor = UiPalette.TextDefault;
            label1.Location = new Point(12, 14);
            label1.Name = "label1";
            label1.Size = new Size(98, 15);
            label1.TabIndex = 37;
            label1.Text = "Length (samples)";
            //
            // labelBandWidth
            //
            labelBandWidth.AutoSize = true;
            labelBandWidth.ForeColor = UiPalette.TextDefault;
            labelBandWidth.Location = new Point(12, 41);
            labelBandWidth.Name = "labelBandWidth";
            labelBandWidth.Size = new Size(70, 15);
            labelBandWidth.TabIndex = 55;
            labelBandWidth.Text = "Band filter";
            //
            // comboBandWidth
            //
            comboBandWidth.BackColor = UiPalette.ControlSurface;
            comboBandWidth.ForeColor = UiPalette.TextPrimary;
            comboBandWidth.Location = new Point(153, 38);
            comboBandWidth.Margin = new Padding(0);
            comboBandWidth.MinimumSize = new Size(36, 19);
            comboBandWidth.Name = "comboBandWidth";
            comboBandWidth.Size = new Size(100, 23);
            comboBandWidth.TabIndex = 56;
            //
            // labelBandCenter
            //
            labelBandCenter.AutoSize = true;
            labelBandCenter.ForeColor = UiPalette.TextDefault;
            labelBandCenter.Location = new Point(12, 68);
            labelBandCenter.Name = "labelBandCenter";
            labelBandCenter.Size = new Size(80, 15);
            labelBandCenter.TabIndex = 57;
            labelBandCenter.Text = "Band centre";
            //
            // comboBandCenter
            //
            comboBandCenter.BackColor = UiPalette.ControlSurface;
            comboBandCenter.ForeColor = UiPalette.TextPrimary;
            comboBandCenter.Location = new Point(153, 65);
            comboBandCenter.Margin = new Padding(0);
            comboBandCenter.MinimumSize = new Size(36, 19);
            comboBandCenter.Name = "comboBandCenter";
            comboBandCenter.Size = new Size(100, 23);
            comboBandCenter.TabIndex = 58;
            //
            // labelAmplitudeScale
            //
            labelAmplitudeScale.AutoSize = true;
            labelAmplitudeScale.ForeColor = UiPalette.TextDefault;
            labelAmplitudeScale.Location = new Point(12, 95);
            labelAmplitudeScale.Name = "labelAmplitudeScale";
            labelAmplitudeScale.Size = new Size(90, 15);
            labelAmplitudeScale.TabIndex = 39;
            labelAmplitudeScale.Text = "Amplitude scale";
            //
            // comboAmplitudeScale
            //
            comboAmplitudeScale.BackColor = UiPalette.ControlSurface;
            comboAmplitudeScale.ForeColor = UiPalette.TextPrimary;
            comboAmplitudeScale.Location = new Point(153, 92);
            comboAmplitudeScale.Margin = new Padding(0);
            comboAmplitudeScale.MinimumSize = new Size(36, 19);
            comboAmplitudeScale.Name = "comboAmplitudeScale";
            comboAmplitudeScale.Size = new Size(100, 23);
            comboAmplitudeScale.TabIndex = 40;
            //
            // labelTimeUnit
            //
            labelTimeUnit.AutoSize = true;
            labelTimeUnit.ForeColor = UiPalette.TextDefault;
            labelTimeUnit.Location = new Point(12, 122);
            labelTimeUnit.Name = "labelTimeUnit";
            labelTimeUnit.Size = new Size(57, 15);
            labelTimeUnit.TabIndex = 41;
            labelTimeUnit.Text = "Time axis";
            //
            // comboTimeUnit
            //
            comboTimeUnit.BackColor = UiPalette.ControlSurface;
            comboTimeUnit.ForeColor = UiPalette.TextPrimary;
            comboTimeUnit.Location = new Point(153, 119);
            comboTimeUnit.Margin = new Padding(0);
            comboTimeUnit.MinimumSize = new Size(36, 19);
            comboTimeUnit.Name = "comboTimeUnit";
            comboTimeUnit.Size = new Size(100, 23);
            comboTimeUnit.TabIndex = 42;
            //
            // labelTimeOrigin
            //
            labelTimeOrigin.AutoSize = true;
            labelTimeOrigin.ForeColor = UiPalette.TextDefault;
            labelTimeOrigin.Location = new Point(12, 149);
            labelTimeOrigin.Name = "labelTimeOrigin";
            labelTimeOrigin.Size = new Size(58, 15);
            labelTimeOrigin.TabIndex = 43;
            labelTimeOrigin.Text = "Time zero";
            //
            // comboTimeOrigin
            //
            comboTimeOrigin.BackColor = UiPalette.ControlSurface;
            comboTimeOrigin.ForeColor = UiPalette.TextPrimary;
            comboTimeOrigin.Location = new Point(153, 146);
            comboTimeOrigin.Margin = new Padding(0);
            comboTimeOrigin.MinimumSize = new Size(36, 19);
            comboTimeOrigin.Name = "comboTimeOrigin";
            comboTimeOrigin.Size = new Size(100, 23);
            comboTimeOrigin.TabIndex = 44;
            //
            // labelEnvelopeSmoothing
            //
            labelEnvelopeSmoothing.AutoSize = true;
            labelEnvelopeSmoothing.ForeColor = UiPalette.TextDefault;
            labelEnvelopeSmoothing.Location = new Point(12, 176);
            labelEnvelopeSmoothing.Name = "labelEnvelopeSmoothing";
            labelEnvelopeSmoothing.Size = new Size(120, 15);
            labelEnvelopeSmoothing.TabIndex = 45;
            labelEnvelopeSmoothing.Text = "ETC smoothing (ms)";
            //
            // numericEnvelopeSmoothing
            //
            numericEnvelopeSmoothing.BackColor = UiPalette.ControlSurface;
            numericEnvelopeSmoothing.DecimalPlaces = 2;
            numericEnvelopeSmoothing.ForeColor = UiPalette.TextPrimary;
            numericEnvelopeSmoothing.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            numericEnvelopeSmoothing.Location = new Point(193, 174);
            numericEnvelopeSmoothing.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            numericEnvelopeSmoothing.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericEnvelopeSmoothing.MinimumSize = new Size(36, 19);
            numericEnvelopeSmoothing.Name = "numericEnvelopeSmoothing";
            numericEnvelopeSmoothing.Size = new Size(60, 19);
            numericEnvelopeSmoothing.TabIndex = 46;
            numericEnvelopeSmoothing.TextAlign = HorizontalAlignment.Right;
            numericEnvelopeSmoothing.Value = new decimal(new int[] { 0, 0, 0, 0 });
            //
            // labelInvert
            //
            labelInvert.AutoSize = true;
            labelInvert.ForeColor = UiPalette.TextDefault;
            labelInvert.Location = new Point(12, 203);
            labelInvert.Name = "labelInvert";
            labelInvert.Size = new Size(88, 15);
            labelInvert.TabIndex = 47;
            labelInvert.Text = "Invert polarity";
            //
            // checkInvert
            //
            checkInvert.AutoSize = true;
            checkInvert.ForeColor = UiPalette.TextDefault;
            checkInvert.Location = new Point(238, 204);
            checkInvert.Name = "checkInvert";
            checkInvert.Size = new Size(15, 14);
            checkInvert.TabIndex = 48;
            checkInvert.UseVisualStyleBackColor = true;
            //
            // labelNormalizeStep
            //
            labelNormalizeStep.AutoSize = true;
            labelNormalizeStep.ForeColor = UiPalette.TextDefault;
            labelNormalizeStep.Location = new Point(12, 230);
            labelNormalizeStep.Name = "labelNormalizeStep";
            labelNormalizeStep.Size = new Size(140, 15);
            labelNormalizeStep.TabIndex = 49;
            labelNormalizeStep.Text = "Step against IR peak";
            //
            // checkNormalizeStep
            //
            checkNormalizeStep.AutoSize = true;
            checkNormalizeStep.ForeColor = UiPalette.TextDefault;
            checkNormalizeStep.Location = new Point(238, 231);
            checkNormalizeStep.Name = "checkNormalizeStep";
            checkNormalizeStep.Size = new Size(15, 14);
            checkNormalizeStep.TabIndex = 50;
            checkNormalizeStep.UseVisualStyleBackColor = true;
            //
            // labelCurves
            //
            labelCurves.AutoSize = true;
            labelCurves.ForeColor = UiPalette.TextAccent;
            labelCurves.Location = new Point(12, 259);
            labelCurves.Name = "labelCurves";
            labelCurves.Size = new Size(48, 15);
            labelCurves.TabIndex = 51;
            labelCurves.Text = "Curves:";
            //
            // checkBoxShowImpulse
            //
            checkBoxShowImpulse.AutoSize = true;
            checkBoxShowImpulse.ForeColor = UiPalette.TextDefault;
            checkBoxShowImpulse.Location = new Point(12, 281);
            checkBoxShowImpulse.Name = "checkBoxShowImpulse";
            checkBoxShowImpulse.Size = new Size(149, 19);
            checkBoxShowImpulse.TabIndex = 52;
            checkBoxShowImpulse.Text = "Show impulse response";
            checkBoxShowImpulse.UseVisualStyleBackColor = true;
            //
            // checkBoxShowEnvelope
            //
            checkBoxShowEnvelope.AutoSize = true;
            checkBoxShowEnvelope.ForeColor = UiPalette.TextDefault;
            checkBoxShowEnvelope.Location = new Point(12, 306);
            checkBoxShowEnvelope.Name = "checkBoxShowEnvelope";
            checkBoxShowEnvelope.Size = new Size(149, 19);
            checkBoxShowEnvelope.TabIndex = 53;
            checkBoxShowEnvelope.Text = "Show envelope (ETC)";
            checkBoxShowEnvelope.UseVisualStyleBackColor = true;
            //
            // checkBoxShowStep
            //
            checkBoxShowStep.AutoSize = true;
            checkBoxShowStep.ForeColor = UiPalette.TextDefault;
            checkBoxShowStep.Location = new Point(12, 331);
            checkBoxShowStep.Name = "checkBoxShowStep";
            checkBoxShowStep.Size = new Size(149, 19);
            checkBoxShowStep.TabIndex = 54;
            checkBoxShowStep.Text = "Show step response";
            checkBoxShowStep.UseVisualStyleBackColor = true;
            //
            // IROpt
            //
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiPalette.AppBackground;
            ClientSize = new Size(265, 362);
            Controls.Add(checkBoxShowStep);
            Controls.Add(checkBoxShowEnvelope);
            Controls.Add(checkBoxShowImpulse);
            Controls.Add(labelCurves);
            Controls.Add(checkNormalizeStep);
            Controls.Add(labelNormalizeStep);
            Controls.Add(checkInvert);
            Controls.Add(labelInvert);
            Controls.Add(numericEnvelopeSmoothing);
            Controls.Add(labelEnvelopeSmoothing);
            Controls.Add(comboTimeOrigin);
            Controls.Add(labelTimeOrigin);
            Controls.Add(comboTimeUnit);
            Controls.Add(labelTimeUnit);
            Controls.Add(comboBandCenter);
            Controls.Add(labelBandCenter);
            Controls.Add(comboBandWidth);
            Controls.Add(labelBandWidth);
            Controls.Add(comboAmplitudeScale);
            Controls.Add(labelAmplitudeScale);
            Controls.Add(numericLength);
            Controls.Add(label1);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "IROpt";
            ShowInTaskbar = false;
            Text = "Impulse Response Options";
            (numericLength).EndInit();
            (numericEnvelopeSmoothing).EndInit();
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion

        private ThemedNumericUpDown numericLength;
        private Label label1;
        private Label labelBandWidth;
        private ThemedComboBox comboBandWidth;
        private Label labelBandCenter;
        private ThemedComboBox comboBandCenter;
        private Label labelAmplitudeScale;
        private ThemedComboBox comboAmplitudeScale;
        private Label labelTimeUnit;
        private ThemedComboBox comboTimeUnit;
        private Label labelTimeOrigin;
        private ThemedComboBox comboTimeOrigin;
        private Label labelEnvelopeSmoothing;
        private ThemedNumericUpDown numericEnvelopeSmoothing;
        private Label labelInvert;
        private ReleaseClickCheckBox checkInvert;
        private Label labelNormalizeStep;
        private ReleaseClickCheckBox checkNormalizeStep;
        private Label labelCurves;
        private ReleaseClickCheckBox checkBoxShowImpulse;
        private ReleaseClickCheckBox checkBoxShowEnvelope;
        private ReleaseClickCheckBox checkBoxShowStep;
    }
}
