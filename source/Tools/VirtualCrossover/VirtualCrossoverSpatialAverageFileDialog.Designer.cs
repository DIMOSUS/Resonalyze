namespace Resonalyze;

partial class VirtualCrossoverSpatialAverageFileDialog
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            toolTip?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        labelFile = new Label();
        labelWarnings = new Label();
        labelCalibration = new Label();
        comboBoxCalibration = new ThemedComboBox();
        buttonCalibrationFile = new ReleaseClickButton();
        labelCalibrationHint = new Label();
        labelHighPass = new Label();
        comboBoxHighPassKind = new ThemedComboBox();
        numericHighPassHz = new ThemedNumericUpDown();
        labelHighPassHz = new Label();
        comboBoxHighPassSlope = new ThemedComboBox();
        labelHighPassSlope = new Label();
        labelHighPassHint = new Label();
        labelAssumptions = new Label();
        buttonOk = new ReleaseClickButton();
        buttonCancel = new ReleaseClickButton();
        (numericHighPassHz).BeginInit();
        SuspendLayout();
        //
        // labelFile
        //
        labelFile.AutoEllipsis = true;
        labelFile.ForeColor = UiPalette.TextSecondary;
        labelFile.Location = new Point(12, 12);
        labelFile.Name = "labelFile";
        labelFile.Size = new Size(496, 34);
        labelFile.TabIndex = 0;
        labelFile.Text = "file";
        //
        // labelWarnings
        //
        labelWarnings.ForeColor = UiPalette.Warning;
        labelWarnings.Location = new Point(12, 50);
        labelWarnings.Name = "labelWarnings";
        labelWarnings.Size = new Size(496, 110);
        labelWarnings.TabIndex = 1;
        labelWarnings.Text = "warnings";
        //
        // labelCalibration
        //
        labelCalibration.AutoSize = true;
        labelCalibration.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
        labelCalibration.ForeColor = UiPalette.TextDefault;
        labelCalibration.Location = new Point(12, 168);
        labelCalibration.Name = "labelCalibration";
        labelCalibration.Size = new Size(215, 15);
        labelCalibration.TabIndex = 2;
        labelCalibration.Text = "Microphone calibration in the levels";
        //
        // comboBoxCalibration
        //
        comboBoxCalibration.BackColor = UiPalette.ControlSurface;
        comboBoxCalibration.ForeColor = UiPalette.TextPrimary;
        comboBoxCalibration.Location = new Point(12, 190);
        comboBoxCalibration.MinimumSize = new Size(36, 21);
        comboBoxCalibration.Name = "comboBoxCalibration";
        comboBoxCalibration.Size = new Size(380, 21);
        comboBoxCalibration.TabIndex = 3;
        //
        // buttonCalibrationFile
        //
        buttonCalibrationFile.FlatStyle = FlatStyle.Popup;
        buttonCalibrationFile.ForeColor = UiPalette.TextPrimary;
        buttonCalibrationFile.Location = new Point(398, 188);
        buttonCalibrationFile.Name = "buttonCalibrationFile";
        buttonCalibrationFile.Size = new Size(110, 25);
        buttonCalibrationFile.TabIndex = 4;
        buttonCalibrationFile.Text = "Other file...";
        buttonCalibrationFile.UseCompatibleTextRendering = true;
        buttonCalibrationFile.UseVisualStyleBackColor = true;
        //
        // labelCalibrationHint
        //
        labelCalibrationHint.ForeColor = UiPalette.TextMuted;
        labelCalibrationHint.Location = new Point(12, 218);
        labelCalibrationHint.Name = "labelCalibrationHint";
        labelCalibrationHint.Size = new Size(496, 34);
        labelCalibrationHint.TabIndex = 5;
        labelCalibrationHint.Text =
            "The file REW corrected the levels with (REW applies the input's calibration file on " +
            "export). With the right answer the panel's calibration choice can replace or remove it.";
        //
        // labelHighPass
        //
        labelHighPass.AutoSize = true;
        labelHighPass.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
        labelHighPass.ForeColor = UiPalette.TextDefault;
        labelHighPass.Location = new Point(12, 260);
        labelHighPass.Name = "labelHighPass";
        labelHighPass.Size = new Size(247, 15);
        labelHighPass.TabIndex = 6;
        labelHighPass.Text = "Protective high-pass in the measured path";
        //
        // comboBoxHighPassKind
        //
        comboBoxHighPassKind.BackColor = UiPalette.ControlSurface;
        comboBoxHighPassKind.ForeColor = UiPalette.TextPrimary;
        comboBoxHighPassKind.Location = new Point(12, 282);
        comboBoxHighPassKind.MinimumSize = new Size(36, 21);
        comboBoxHighPassKind.Name = "comboBoxHighPassKind";
        comboBoxHighPassKind.Size = new Size(150, 21);
        comboBoxHighPassKind.TabIndex = 7;
        //
        // numericHighPassHz
        //
        numericHighPassHz.BackColor = UiPalette.ControlSurface;
        numericHighPassHz.DecimalPlaces = 0;
        numericHighPassHz.ForeColor = UiPalette.TextPrimary;
        numericHighPassHz.Increment = new decimal(new int[] { 10, 0, 0, 0 });
        numericHighPassHz.Location = new Point(170, 283);
        numericHighPassHz.Maximum = new decimal(new int[] { 20000, 0, 0, 0 });
        numericHighPassHz.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
        numericHighPassHz.MinimumSize = new Size(36, 19);
        numericHighPassHz.Name = "numericHighPassHz";
        numericHighPassHz.Size = new Size(70, 19);
        numericHighPassHz.TabIndex = 8;
        numericHighPassHz.TextAlign = HorizontalAlignment.Right;
        numericHighPassHz.ThousandsSeparator = false;
        numericHighPassHz.Value = new decimal(new int[] { 2000, 0, 0, 0 });
        //
        // labelHighPassHz
        //
        labelHighPassHz.AutoSize = true;
        labelHighPassHz.ForeColor = UiPalette.TextSecondary;
        labelHighPassHz.Location = new Point(244, 285);
        labelHighPassHz.Name = "labelHighPassHz";
        labelHighPassHz.Size = new Size(21, 15);
        labelHighPassHz.TabIndex = 9;
        labelHighPassHz.Text = "Hz";
        //
        // comboBoxHighPassSlope
        //
        comboBoxHighPassSlope.BackColor = UiPalette.ControlSurface;
        comboBoxHighPassSlope.ForeColor = UiPalette.TextPrimary;
        comboBoxHighPassSlope.Location = new Point(276, 282);
        comboBoxHighPassSlope.MinimumSize = new Size(36, 21);
        comboBoxHighPassSlope.Name = "comboBoxHighPassSlope";
        comboBoxHighPassSlope.Size = new Size(64, 21);
        comboBoxHighPassSlope.TabIndex = 10;
        //
        // labelHighPassSlope
        //
        labelHighPassSlope.AutoSize = true;
        labelHighPassSlope.ForeColor = UiPalette.TextSecondary;
        labelHighPassSlope.Location = new Point(344, 285);
        labelHighPassSlope.Name = "labelHighPassSlope";
        labelHighPassSlope.Size = new Size(42, 15);
        labelHighPassSlope.TabIndex = 11;
        labelHighPassSlope.Text = "dB/oct";
        //
        // labelHighPassHint
        //
        labelHighPassHint.ForeColor = UiPalette.TextMuted;
        labelHighPassHint.Location = new Point(12, 310);
        labelHighPassHint.Name = "labelHighPassHint";
        labelHighPassHint.Size = new Size(496, 34);
        labelHighPassHint.TabIndex = 12;
        labelHighPassHint.Text = "hint";
        //
        // labelAssumptions
        //
        labelAssumptions.ForeColor = UiPalette.TextMuted;
        labelAssumptions.Location = new Point(12, 352);
        labelAssumptions.Name = "labelAssumptions";
        labelAssumptions.Size = new Size(496, 96);
        labelAssumptions.TabIndex = 13;
        labelAssumptions.Text =
            "Not asked, because nothing here could correct it — the hybrid takes it as given:" + "\r\n" +
            "•  the driver measured alone with its DSP in bypass: the chain is added on top;" + "\r\n" +
            "•  every channel's file at one input gain, without per-channel SPL alignment (REW's " +
            "Align SPL): one offset levels the whole set, and the spread warning is the only check;" + "\r\n" +
            "•  a power (RMS) average, not a dB average.";
        //
        // buttonOk
        //
        buttonOk.DialogResult = DialogResult.OK;
        buttonOk.FlatStyle = FlatStyle.Popup;
        buttonOk.ForeColor = UiPalette.TextPrimary;
        buttonOk.Location = new Point(338, 456);
        buttonOk.Name = "buttonOk";
        buttonOk.Size = new Size(80, 26);
        buttonOk.TabIndex = 14;
        buttonOk.Text = "OK";
        buttonOk.UseCompatibleTextRendering = true;
        buttonOk.UseVisualStyleBackColor = true;
        //
        // buttonCancel
        //
        buttonCancel.DialogResult = DialogResult.Cancel;
        buttonCancel.FlatStyle = FlatStyle.Popup;
        buttonCancel.ForeColor = UiPalette.TextPrimary;
        buttonCancel.Location = new Point(428, 456);
        buttonCancel.Name = "buttonCancel";
        buttonCancel.Size = new Size(80, 26);
        buttonCancel.TabIndex = 15;
        buttonCancel.Text = "Cancel";
        buttonCancel.UseCompatibleTextRendering = true;
        buttonCancel.UseVisualStyleBackColor = true;
        //
        // VirtualCrossoverSpatialAverageFileDialog
        //
        AcceptButton = buttonOk;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiPalette.ShellSurface;
        CancelButton = buttonCancel;
        ClientSize = new Size(520, 494);
        Controls.Add(labelFile);
        Controls.Add(labelWarnings);
        Controls.Add(labelCalibration);
        Controls.Add(comboBoxCalibration);
        Controls.Add(buttonCalibrationFile);
        Controls.Add(labelCalibrationHint);
        Controls.Add(labelHighPass);
        Controls.Add(comboBoxHighPassKind);
        Controls.Add(numericHighPassHz);
        Controls.Add(labelHighPassHz);
        Controls.Add(comboBoxHighPassSlope);
        Controls.Add(labelHighPassSlope);
        Controls.Add(labelHighPassHint);
        Controls.Add(labelAssumptions);
        Controls.Add(buttonOk);
        Controls.Add(buttonCancel);
        Font = new Font("Segoe UI", 9F);
        ForeColor = UiPalette.TextPrimary;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "VirtualCrossoverSpatialAverageFileDialog";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Response file";
        (numericHighPassHz).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    private Label labelFile;
    private Label labelWarnings;
    private Label labelCalibration;
    private ThemedComboBox comboBoxCalibration;
    private ReleaseClickButton buttonCalibrationFile;
    private Label labelCalibrationHint;
    private Label labelHighPass;
    private ThemedComboBox comboBoxHighPassKind;
    private ThemedNumericUpDown numericHighPassHz;
    private Label labelHighPassHz;
    private ThemedComboBox comboBoxHighPassSlope;
    private Label labelHighPassSlope;
    private Label labelHighPassHint;
    private Label labelAssumptions;
    private ReleaseClickButton buttonOk;
    private ReleaseClickButton buttonCancel;
}
