namespace Resonalyze;

partial class VirtualCrossoverAcousticGoalDialog
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
        labelIntro = new Label();
        labelHighPass = new Label();
        comboBoxHighPassFamily = new ThemedComboBox();
        comboBoxHighPassSlope = new ThemedComboBox();
        labelHighPassElectrical = new Label();
        labelLowPass = new Label();
        comboBoxLowPassFamily = new ThemedComboBox();
        comboBoxLowPassSlope = new ThemedComboBox();
        labelLowPassElectrical = new Label();
        labelNote = new Label();
        buttonClear = new ReleaseClickButton();
        buttonOk = new ReleaseClickButton();
        buttonCancel = new ReleaseClickButton();
        SuspendLayout();
        //
        // labelIntro
        //
        labelIntro.ForeColor = UiPalette.TextSecondary;
        labelIntro.Location = new Point(12, 12);
        labelIntro.Name = "labelIntro";
        labelIntro.Size = new Size(420, 47);
        labelIntro.TabIndex = 0;
        labelIntro.Text =
            "What driver and filter should add up to at this channel's edges. The corner " +
            "always follows the electrical filter, so only the family and slope are stated " +
            "here. Auto Tune then aims at this instead of the filter itself.";
        //
        // labelHighPass
        //
        labelHighPass.AutoSize = true;
        labelHighPass.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
        labelHighPass.ForeColor = UiPalette.TextDefault;
        labelHighPass.Location = new Point(12, 70);
        labelHighPass.Name = "labelHighPass";
        labelHighPass.Size = new Size(64, 15);
        labelHighPass.TabIndex = 1;
        labelHighPass.Text = "High-pass";
        //
        // comboBoxHighPassFamily
        //
        comboBoxHighPassFamily.BackColor = UiPalette.ControlSurface;
        comboBoxHighPassFamily.ForeColor = UiPalette.TextPrimary;
        comboBoxHighPassFamily.Location = new Point(96, 67);
        comboBoxHighPassFamily.MinimumSize = new Size(36, 21);
        comboBoxHighPassFamily.Name = "comboBoxHighPassFamily";
        comboBoxHighPassFamily.Size = new Size(120, 21);
        comboBoxHighPassFamily.TabIndex = 2;
        //
        // comboBoxHighPassSlope
        //
        comboBoxHighPassSlope.BackColor = UiPalette.ControlSurface;
        comboBoxHighPassSlope.ForeColor = UiPalette.TextPrimary;
        comboBoxHighPassSlope.Location = new Point(222, 67);
        comboBoxHighPassSlope.MinimumSize = new Size(36, 21);
        comboBoxHighPassSlope.Name = "comboBoxHighPassSlope";
        comboBoxHighPassSlope.Size = new Size(74, 21);
        comboBoxHighPassSlope.TabIndex = 3;
        //
        // labelHighPassElectrical
        //
        labelHighPassElectrical.AutoEllipsis = true;
        labelHighPassElectrical.ForeColor = UiPalette.TextMuted;
        labelHighPassElectrical.Location = new Point(302, 70);
        labelHighPassElectrical.Name = "labelHighPassElectrical";
        labelHighPassElectrical.Size = new Size(130, 19);
        labelHighPassElectrical.TabIndex = 4;
        labelHighPassElectrical.Text = "no high-pass";
        //
        // labelLowPass
        //
        labelLowPass.AutoSize = true;
        labelLowPass.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
        labelLowPass.ForeColor = UiPalette.TextDefault;
        labelLowPass.Location = new Point(12, 98);
        labelLowPass.Name = "labelLowPass";
        labelLowPass.Size = new Size(60, 15);
        labelLowPass.TabIndex = 5;
        labelLowPass.Text = "Low-pass";
        //
        // comboBoxLowPassFamily
        //
        comboBoxLowPassFamily.BackColor = UiPalette.ControlSurface;
        comboBoxLowPassFamily.ForeColor = UiPalette.TextPrimary;
        comboBoxLowPassFamily.Location = new Point(96, 95);
        comboBoxLowPassFamily.MinimumSize = new Size(36, 21);
        comboBoxLowPassFamily.Name = "comboBoxLowPassFamily";
        comboBoxLowPassFamily.Size = new Size(120, 21);
        comboBoxLowPassFamily.TabIndex = 6;
        //
        // comboBoxLowPassSlope
        //
        comboBoxLowPassSlope.BackColor = UiPalette.ControlSurface;
        comboBoxLowPassSlope.ForeColor = UiPalette.TextPrimary;
        comboBoxLowPassSlope.Location = new Point(222, 95);
        comboBoxLowPassSlope.MinimumSize = new Size(36, 21);
        comboBoxLowPassSlope.Name = "comboBoxLowPassSlope";
        comboBoxLowPassSlope.Size = new Size(74, 21);
        comboBoxLowPassSlope.TabIndex = 7;
        //
        // labelLowPassElectrical
        //
        labelLowPassElectrical.AutoEllipsis = true;
        labelLowPassElectrical.ForeColor = UiPalette.TextMuted;
        labelLowPassElectrical.Location = new Point(302, 98);
        labelLowPassElectrical.Name = "labelLowPassElectrical";
        labelLowPassElectrical.Size = new Size(130, 19);
        labelLowPassElectrical.TabIndex = 8;
        labelLowPassElectrical.Text = "no low-pass";
        //
        // labelNote
        //
        labelNote.ForeColor = UiPalette.TextMuted;
        labelNote.Location = new Point(12, 126);
        labelNote.Name = "labelNote";
        labelNote.Size = new Size(420, 32);
        labelNote.TabIndex = 9;
        labelNote.Text = "note";
        //
        // buttonClear
        //
        buttonClear.FlatStyle = FlatStyle.Popup;
        buttonClear.ForeColor = UiPalette.TextPrimary;
        buttonClear.Location = new Point(12, 166);
        buttonClear.Name = "buttonClear";
        buttonClear.Size = new Size(110, 26);
        buttonClear.TabIndex = 10;
        buttonClear.Text = "State nothing";
        buttonClear.UseCompatibleTextRendering = true;
        buttonClear.UseVisualStyleBackColor = true;
        //
        // buttonOk
        //
        buttonOk.DialogResult = DialogResult.OK;
        buttonOk.FlatStyle = FlatStyle.Popup;
        buttonOk.ForeColor = UiPalette.TextPrimary;
        buttonOk.Location = new Point(262, 166);
        buttonOk.Name = "buttonOk";
        buttonOk.Size = new Size(80, 26);
        buttonOk.TabIndex = 11;
        buttonOk.Text = "OK";
        buttonOk.UseCompatibleTextRendering = true;
        buttonOk.UseVisualStyleBackColor = true;
        //
        // buttonCancel
        //
        buttonCancel.DialogResult = DialogResult.Cancel;
        buttonCancel.FlatStyle = FlatStyle.Popup;
        buttonCancel.ForeColor = UiPalette.TextPrimary;
        buttonCancel.Location = new Point(352, 166);
        buttonCancel.Name = "buttonCancel";
        buttonCancel.Size = new Size(80, 26);
        buttonCancel.TabIndex = 12;
        buttonCancel.Text = "Cancel";
        buttonCancel.UseCompatibleTextRendering = true;
        buttonCancel.UseVisualStyleBackColor = true;
        //
        // VirtualCrossoverAcousticGoalDialog
        //
        AcceptButton = buttonOk;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiPalette.ShellSurface;
        CancelButton = buttonCancel;
        ClientSize = new Size(444, 204);
        Controls.Add(labelIntro);
        Controls.Add(labelHighPass);
        Controls.Add(comboBoxHighPassFamily);
        Controls.Add(comboBoxHighPassSlope);
        Controls.Add(labelHighPassElectrical);
        Controls.Add(labelLowPass);
        Controls.Add(comboBoxLowPassFamily);
        Controls.Add(comboBoxLowPassSlope);
        Controls.Add(labelLowPassElectrical);
        Controls.Add(labelNote);
        Controls.Add(buttonClear);
        Controls.Add(buttonOk);
        Controls.Add(buttonCancel);
        Font = new Font("Segoe UI", 9F);
        ForeColor = UiPalette.TextPrimary;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "VirtualCrossoverAcousticGoalDialog";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Acoustic crossover goal";
        ResumeLayout(false);
    }

    private Label labelIntro;
    private Label labelHighPass;
    private ThemedComboBox comboBoxHighPassFamily;
    private ThemedComboBox comboBoxHighPassSlope;
    private Label labelHighPassElectrical;
    private Label labelLowPass;
    private ThemedComboBox comboBoxLowPassFamily;
    private ThemedComboBox comboBoxLowPassSlope;
    private Label labelLowPassElectrical;
    private Label labelNote;
    private ReleaseClickButton buttonClear;
    private ReleaseClickButton buttonOk;
    private ReleaseClickButton buttonCancel;
}
