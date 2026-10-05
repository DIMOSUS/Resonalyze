namespace Resonalyze;

partial class VirtualCrossoverOverlayExportDialog
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        labelIntro = new Label();
        labelCurve = new Label();
        comboBoxCurve = new ThemedComboBox();
        labelSlot = new Label();
        comboBoxSlot = new ThemedComboBox();
        labelReplaces = new Label();
        buttonSave = new ReleaseClickButton();
        buttonCancel = new ReleaseClickButton();
        SuspendLayout();
        //
        // labelIntro
        //
        labelIntro.ForeColor = UiPalette.TextSecondary;
        labelIntro.Location = new Point(12, 12);
        labelIntro.Name = "labelIntro";
        labelIntro.Size = new Size(456, 32);
        labelIntro.TabIndex = 0;
        labelIntro.Text =
            "Saves a curve of the upper plot, as drawn and with its smoothing, as a Captured overlay in " +
            "Frequency Response.";
        //
        // labelCurve
        //
        labelCurve.AutoSize = true;
        labelCurve.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
        labelCurve.ForeColor = UiPalette.TextDefault;
        labelCurve.Location = new Point(12, 56);
        labelCurve.Name = "labelCurve";
        labelCurve.Size = new Size(39, 15);
        labelCurve.TabIndex = 1;
        labelCurve.Text = "Curve";
        //
        // comboBoxCurve
        //
        comboBoxCurve.BackColor = UiPalette.ControlSurface;
        comboBoxCurve.DropDownWidth = 396;
        comboBoxCurve.ForeColor = UiPalette.TextPrimary;
        comboBoxCurve.Location = new Point(72, 53);
        comboBoxCurve.MaxDropDownItems = 16;
        comboBoxCurve.MinimumSize = new Size(36, 21);
        comboBoxCurve.Name = "comboBoxCurve";
        comboBoxCurve.Size = new Size(396, 21);
        comboBoxCurve.TabIndex = 2;
        //
        // labelSlot
        //
        labelSlot.AutoSize = true;
        labelSlot.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
        labelSlot.ForeColor = UiPalette.TextDefault;
        labelSlot.Location = new Point(12, 86);
        labelSlot.Name = "labelSlot";
        labelSlot.Size = new Size(28, 15);
        labelSlot.TabIndex = 3;
        labelSlot.Text = "Slot";
        //
        // comboBoxSlot
        //
        comboBoxSlot.BackColor = UiPalette.ControlSurface;
        comboBoxSlot.DropDownWidth = 396;
        comboBoxSlot.ForeColor = UiPalette.TextPrimary;
        comboBoxSlot.Location = new Point(72, 83);
        comboBoxSlot.MaxDropDownItems = 12;
        comboBoxSlot.MinimumSize = new Size(36, 21);
        comboBoxSlot.Name = "comboBoxSlot";
        comboBoxSlot.Size = new Size(396, 21);
        comboBoxSlot.TabIndex = 4;
        //
        // labelReplaces
        //
        labelReplaces.AutoEllipsis = true;
        labelReplaces.ForeColor = UiPalette.Warning;
        labelReplaces.Location = new Point(72, 110);
        labelReplaces.Name = "labelReplaces";
        labelReplaces.Size = new Size(396, 19);
        labelReplaces.TabIndex = 5;
        //
        // buttonSave
        //
        buttonSave.DialogResult = DialogResult.OK;
        buttonSave.FlatStyle = FlatStyle.Popup;
        buttonSave.ForeColor = UiPalette.TextPrimary;
        buttonSave.Location = new Point(298, 138);
        buttonSave.Name = "buttonSave";
        buttonSave.Size = new Size(80, 26);
        buttonSave.TabIndex = 6;
        buttonSave.Text = "Save";
        buttonSave.UseCompatibleTextRendering = true;
        buttonSave.UseVisualStyleBackColor = true;
        //
        // buttonCancel
        //
        buttonCancel.DialogResult = DialogResult.Cancel;
        buttonCancel.FlatStyle = FlatStyle.Popup;
        buttonCancel.ForeColor = UiPalette.TextPrimary;
        buttonCancel.Location = new Point(388, 138);
        buttonCancel.Name = "buttonCancel";
        buttonCancel.Size = new Size(80, 26);
        buttonCancel.TabIndex = 7;
        buttonCancel.Text = "Cancel";
        buttonCancel.UseCompatibleTextRendering = true;
        buttonCancel.UseVisualStyleBackColor = true;
        //
        // VirtualCrossoverOverlayExportDialog
        //
        AcceptButton = buttonSave;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiPalette.ShellSurface;
        CancelButton = buttonCancel;
        ClientSize = new Size(480, 176);
        Controls.Add(labelIntro);
        Controls.Add(labelCurve);
        Controls.Add(comboBoxCurve);
        Controls.Add(labelSlot);
        Controls.Add(comboBoxSlot);
        Controls.Add(labelReplaces);
        Controls.Add(buttonSave);
        Controls.Add(buttonCancel);
        Font = new Font("Segoe UI", 9F);
        ForeColor = UiPalette.TextPrimary;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "VirtualCrossoverOverlayExportDialog";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Capture to overlay";
        ResumeLayout(false);
        PerformLayout();
    }

    private Label labelIntro;
    private Label labelCurve;
    private ThemedComboBox comboBoxCurve;
    private Label labelSlot;
    private ThemedComboBox comboBoxSlot;
    private Label labelReplaces;
    private ReleaseClickButton buttonSave;
    private ReleaseClickButton buttonCancel;
}
