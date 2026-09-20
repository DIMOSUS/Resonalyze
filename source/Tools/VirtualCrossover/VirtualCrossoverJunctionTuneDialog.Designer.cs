namespace Resonalyze;

partial class VirtualCrossoverJunctionTuneDialog
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
        labelJunction = new Label();
        comboBoxJunction = new ThemedComboBox();
        labelWindow = new Label();
        numericMinHz = new ThemedNumericUpDown();
        labelWindowTo = new Label();
        numericMaxHz = new ThemedNumericUpDown();
        checkBoxIndependentSlopes = new ReleaseClickCheckBox();
        labelFamilies = new Label();
        checkedListFamilies = new CheckedListBox();
        labelGoal = new Label();
        comboBoxGoalFamily = new ThemedComboBox();
        comboBoxGoalSlope = new ThemedComboBox();
        labelGoalHint = new Label();
        buttonRun = new ReleaseClickButton();
        labelStatus = new Label();
        textBoxReport = new TextBox();
        buttonApply = new ReleaseClickButton();
        buttonCancel = new ReleaseClickButton();
        ((System.ComponentModel.ISupportInitialize)numericMinHz).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numericMaxHz).BeginInit();
        SuspendLayout();
        //
        // labelJunction
        //
        labelJunction.AutoSize = true;
        labelJunction.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
        labelJunction.ForeColor = UiPalette.TextDefault;
        labelJunction.Location = new Point(12, 15);
        labelJunction.Name = "labelJunction";
        labelJunction.Size = new Size(56, 15);
        labelJunction.TabIndex = 0;
        labelJunction.Text = "Junction";
        //
        // comboBoxJunction
        //
        comboBoxJunction.BackColor = UiPalette.ControlSurface;
        comboBoxJunction.ForeColor = UiPalette.TextPrimary;
        comboBoxJunction.Location = new Point(96, 12);
        comboBoxJunction.MinimumSize = new Size(36, 21);
        comboBoxJunction.Name = "comboBoxJunction";
        comboBoxJunction.Size = new Size(150, 21);
        comboBoxJunction.TabIndex = 1;
        //
        // labelWindow
        //
        labelWindow.AutoSize = true;
        labelWindow.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
        labelWindow.ForeColor = UiPalette.TextDefault;
        labelWindow.Location = new Point(12, 46);
        labelWindow.Name = "labelWindow";
        labelWindow.Size = new Size(74, 15);
        labelWindow.TabIndex = 2;
        labelWindow.Text = "Corner from";
        //
        // numericMinHz
        //
        numericMinHz.BackColor = UiPalette.ControlSurface;
        numericMinHz.DecimalPlaces = 0;
        numericMinHz.ForeColor = UiPalette.TextPrimary;
        numericMinHz.Location = new Point(96, 43);
        numericMinHz.LogarithmicFrequencyStep = true;
        numericMinHz.Maximum = new decimal(new int[] { 24000, 0, 0, 0 });
        numericMinHz.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
        numericMinHz.MinimumSize = new Size(36, 21);
        numericMinHz.Name = "numericMinHz";
        numericMinHz.Size = new Size(70, 21);
        numericMinHz.TabIndex = 3;
        numericMinHz.TextAlign = HorizontalAlignment.Right;
        numericMinHz.ThousandsSeparator = false;
        numericMinHz.Value = new decimal(new int[] { 500, 0, 0, 0 });
        //
        // labelWindowTo
        //
        labelWindowTo.AutoSize = true;
        labelWindowTo.ForeColor = UiPalette.TextDefault;
        labelWindowTo.Location = new Point(172, 46);
        labelWindowTo.Name = "labelWindowTo";
        labelWindowTo.Size = new Size(16, 15);
        labelWindowTo.TabIndex = 4;
        labelWindowTo.Text = "to";
        //
        // numericMaxHz
        //
        numericMaxHz.BackColor = UiPalette.ControlSurface;
        numericMaxHz.DecimalPlaces = 0;
        numericMaxHz.ForeColor = UiPalette.TextPrimary;
        numericMaxHz.Location = new Point(194, 43);
        numericMaxHz.LogarithmicFrequencyStep = true;
        numericMaxHz.Maximum = new decimal(new int[] { 24000, 0, 0, 0 });
        numericMaxHz.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
        numericMaxHz.MinimumSize = new Size(36, 21);
        numericMaxHz.Name = "numericMaxHz";
        numericMaxHz.Size = new Size(70, 21);
        numericMaxHz.TabIndex = 5;
        numericMaxHz.TextAlign = HorizontalAlignment.Right;
        numericMaxHz.ThousandsSeparator = false;
        numericMaxHz.Value = new decimal(new int[] { 2000, 0, 0, 0 });
        //
        // checkBoxIndependentSlopes
        //
        checkBoxIndependentSlopes.ForeColor = UiPalette.TextPrimary;
        checkBoxIndependentSlopes.Location = new Point(276, 43);
        checkBoxIndependentSlopes.Name = "checkBoxIndependentSlopes";
        checkBoxIndependentSlopes.Size = new Size(160, 21);
        checkBoxIndependentSlopes.TabIndex = 6;
        checkBoxIndependentSlopes.Text = "Slopes free per side";
        checkBoxIndependentSlopes.UseVisualStyleBackColor = true;
        //
        // labelFamilies
        //
        labelFamilies.AutoSize = true;
        labelFamilies.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
        labelFamilies.ForeColor = UiPalette.TextDefault;
        labelFamilies.Location = new Point(12, 77);
        labelFamilies.Name = "labelFamilies";
        labelFamilies.Size = new Size(58, 15);
        labelFamilies.TabIndex = 7;
        labelFamilies.Text = "Families";
        //
        // checkedListFamilies
        //
        checkedListFamilies.BackColor = UiPalette.ControlSurface;
        checkedListFamilies.BorderStyle = BorderStyle.FixedSingle;
        checkedListFamilies.CheckOnClick = true;
        checkedListFamilies.ForeColor = UiPalette.TextPrimary;
        checkedListFamilies.Location = new Point(96, 74);
        checkedListFamilies.Name = "checkedListFamilies";
        checkedListFamilies.Size = new Size(168, 72);
        checkedListFamilies.TabIndex = 8;
        //
        // labelGoal
        //
        labelGoal.AutoSize = true;
        labelGoal.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
        labelGoal.ForeColor = UiPalette.TextDefault;
        labelGoal.Location = new Point(276, 77);
        labelGoal.Name = "labelGoal";
        labelGoal.Size = new Size(96, 15);
        labelGoal.TabIndex = 9;
        labelGoal.Text = "Acoustic goal";
        //
        // comboBoxGoalFamily
        //
        comboBoxGoalFamily.BackColor = UiPalette.ControlSurface;
        comboBoxGoalFamily.ForeColor = UiPalette.TextPrimary;
        comboBoxGoalFamily.Location = new Point(276, 97);
        comboBoxGoalFamily.MinimumSize = new Size(36, 21);
        comboBoxGoalFamily.Name = "comboBoxGoalFamily";
        comboBoxGoalFamily.Size = new Size(110, 21);
        comboBoxGoalFamily.TabIndex = 10;
        //
        // comboBoxGoalSlope
        //
        comboBoxGoalSlope.BackColor = UiPalette.ControlSurface;
        comboBoxGoalSlope.ForeColor = UiPalette.TextPrimary;
        comboBoxGoalSlope.Location = new Point(392, 97);
        comboBoxGoalSlope.MinimumSize = new Size(36, 21);
        comboBoxGoalSlope.Name = "comboBoxGoalSlope";
        comboBoxGoalSlope.Size = new Size(74, 21);
        comboBoxGoalSlope.TabIndex = 11;
        //
        // labelGoalHint
        //
        labelGoalHint.ForeColor = UiPalette.TextMuted;
        labelGoalHint.Location = new Point(276, 122);
        labelGoalHint.Name = "labelGoalHint";
        labelGoalHint.Size = new Size(190, 30);
        labelGoalHint.TabIndex = 12;
        labelGoalHint.Text = "Driver and filter together, which is steeper than the filter alone.";
        //
        // buttonRun
        //
        buttonRun.FlatStyle = FlatStyle.Popup;
        buttonRun.ForeColor = UiPalette.TextPrimary;
        buttonRun.Location = new Point(12, 158);
        buttonRun.Name = "buttonRun";
        buttonRun.Size = new Size(110, 26);
        buttonRun.TabIndex = 13;
        buttonRun.Text = "Search";
        buttonRun.UseCompatibleTextRendering = true;
        buttonRun.UseVisualStyleBackColor = true;
        //
        // labelStatus
        //
        labelStatus.AutoEllipsis = true;
        labelStatus.ForeColor = UiPalette.TextMuted;
        labelStatus.Location = new Point(130, 163);
        labelStatus.Name = "labelStatus";
        labelStatus.Size = new Size(336, 19);
        labelStatus.TabIndex = 14;
        labelStatus.Text = "Nothing searched yet.";
        //
        // textBoxReport
        //
        textBoxReport.BackColor = UiPalette.SunkenSurface;
        textBoxReport.BorderStyle = BorderStyle.FixedSingle;
        textBoxReport.Font = new Font("Consolas", 9F);
        textBoxReport.ForeColor = UiPalette.TextPrimary;
        textBoxReport.Location = new Point(12, 190);
        textBoxReport.Multiline = true;
        textBoxReport.Name = "textBoxReport";
        textBoxReport.ReadOnly = true;
        textBoxReport.ScrollBars = ScrollBars.Vertical;
        textBoxReport.Size = new Size(454, 200);
        textBoxReport.TabIndex = 15;
        textBoxReport.WordWrap = false;
        //
        // buttonApply
        //
        buttonApply.Enabled = false;
        buttonApply.FlatStyle = FlatStyle.Popup;
        buttonApply.ForeColor = UiPalette.TextPrimary;
        buttonApply.Location = new Point(296, 400);
        buttonApply.Name = "buttonApply";
        buttonApply.Size = new Size(80, 26);
        buttonApply.TabIndex = 16;
        buttonApply.Text = "Apply";
        buttonApply.UseCompatibleTextRendering = true;
        buttonApply.UseVisualStyleBackColor = true;
        //
        // buttonCancel
        //
        buttonCancel.DialogResult = DialogResult.Cancel;
        buttonCancel.FlatStyle = FlatStyle.Popup;
        buttonCancel.ForeColor = UiPalette.TextPrimary;
        buttonCancel.Location = new Point(386, 400);
        buttonCancel.Name = "buttonCancel";
        buttonCancel.Size = new Size(80, 26);
        buttonCancel.TabIndex = 17;
        buttonCancel.Text = "Close";
        buttonCancel.UseCompatibleTextRendering = true;
        buttonCancel.UseVisualStyleBackColor = true;
        //
        // VirtualCrossoverJunctionTuneDialog
        //
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiPalette.ShellSurface;
        CancelButton = buttonCancel;
        ClientSize = new Size(478, 438);
        Controls.Add(labelJunction);
        Controls.Add(comboBoxJunction);
        Controls.Add(labelWindow);
        Controls.Add(numericMinHz);
        Controls.Add(labelWindowTo);
        Controls.Add(numericMaxHz);
        Controls.Add(checkBoxIndependentSlopes);
        Controls.Add(labelFamilies);
        Controls.Add(checkedListFamilies);
        Controls.Add(labelGoal);
        Controls.Add(comboBoxGoalFamily);
        Controls.Add(comboBoxGoalSlope);
        Controls.Add(labelGoalHint);
        Controls.Add(buttonRun);
        Controls.Add(labelStatus);
        Controls.Add(textBoxReport);
        Controls.Add(buttonApply);
        Controls.Add(buttonCancel);
        Font = new Font("Segoe UI", 9F);
        ForeColor = UiPalette.TextPrimary;
        MinimizeBox = false;
        MinimumSize = new Size(494, 477);
        Name = "VirtualCrossoverJunctionTuneDialog";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Tune junction";
        ((System.ComponentModel.ISupportInitialize)numericMinHz).EndInit();
        ((System.ComponentModel.ISupportInitialize)numericMaxHz).EndInit();
        ResumeLayout(false);
    }

    private Label labelJunction;
    private ThemedComboBox comboBoxJunction;
    private Label labelWindow;
    private ThemedNumericUpDown numericMinHz;
    private Label labelWindowTo;
    private ThemedNumericUpDown numericMaxHz;
    private ReleaseClickCheckBox checkBoxIndependentSlopes;
    private Label labelFamilies;
    private CheckedListBox checkedListFamilies;
    private Label labelGoal;
    private ThemedComboBox comboBoxGoalFamily;
    private ThemedComboBox comboBoxGoalSlope;
    private Label labelGoalHint;
    private ReleaseClickButton buttonRun;
    private Label labelStatus;
    private TextBox textBoxReport;
    private ReleaseClickButton buttonApply;
    private ReleaseClickButton buttonCancel;
}
