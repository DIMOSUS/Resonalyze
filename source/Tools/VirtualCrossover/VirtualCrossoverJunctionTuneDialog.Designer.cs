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
        labelSlopes = new Label();
        comboBoxMinSlope = new ThemedComboBox();
        labelSlopeTo = new Label();
        comboBoxMaxSlope = new ThemedComboBox();
        labelFamilies = new Label();
        checkButterworth = new ReleaseClickCheckBox();
        checkLinkwitzRiley = new ReleaseClickCheckBox();
        checkBessel = new ReleaseClickCheckBox();
        labelTuneFor = new Label();
        radioSummation = new ReleaseClickRadioButton();
        radioAcoustic = new ReleaseClickRadioButton();
        checkBoxSplitCorners = new ReleaseClickCheckBox();
        comboBoxGoalFamily = new ThemedComboBox();
        comboBoxGoalSlope = new ThemedComboBox();
        labelSumBudget = new Label();
        numericSumBudget = new ThemedNumericUpDown();
        labelSumBudgetUnit = new Label();
        labelGoalHint = new Label();
        buttonRun = new ReleaseClickButton();
        labelStatus = new Label();
        textBoxReport = new StatusRichTextBox();
        buttonApply = new ReleaseClickButton();
        buttonCancel = new ReleaseClickButton();
        ((System.ComponentModel.ISupportInitialize)numericMinHz).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numericMaxHz).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numericSumBudget).BeginInit();
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
        comboBoxJunction.Size = new Size(200, 21);
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
        checkBoxIndependentSlopes.Checked = true;
        checkBoxIndependentSlopes.CheckState = CheckState.Checked;
        checkBoxIndependentSlopes.ForeColor = UiPalette.TextPrimary;
        checkBoxIndependentSlopes.Location = new Point(300, 43);
        checkBoxIndependentSlopes.Name = "checkBoxIndependentSlopes";
        checkBoxIndependentSlopes.Size = new Size(160, 21);
        checkBoxIndependentSlopes.TabIndex = 6;
        checkBoxIndependentSlopes.Text = "Slopes free per side";
        checkBoxIndependentSlopes.UseVisualStyleBackColor = true;
        //
        // labelSlopes
        //
        labelSlopes.AutoSize = true;
        labelSlopes.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
        labelSlopes.ForeColor = UiPalette.TextDefault;
        labelSlopes.Location = new Point(480, 98);
        labelSlopes.Name = "labelSlopes";
        labelSlopes.Size = new Size(44, 15);
        labelSlopes.TabIndex = 7;
        labelSlopes.Text = "slopes";
        //
        // comboBoxMinSlope
        //
        comboBoxMinSlope.BackColor = UiPalette.ControlSurface;
        comboBoxMinSlope.ForeColor = UiPalette.TextPrimary;
        comboBoxMinSlope.Location = new Point(532, 95);
        comboBoxMinSlope.MinimumSize = new Size(36, 21);
        comboBoxMinSlope.Name = "comboBoxMinSlope";
        comboBoxMinSlope.Size = new Size(70, 21);
        comboBoxMinSlope.TabIndex = 8;
        //
        // labelSlopeTo
        //
        labelSlopeTo.AutoSize = true;
        labelSlopeTo.ForeColor = UiPalette.TextDefault;
        labelSlopeTo.Location = new Point(608, 98);
        labelSlopeTo.Name = "labelSlopeTo";
        labelSlopeTo.Size = new Size(16, 15);
        labelSlopeTo.TabIndex = 9;
        labelSlopeTo.Text = "to";
        //
        // comboBoxMaxSlope
        //
        comboBoxMaxSlope.BackColor = UiPalette.ControlSurface;
        comboBoxMaxSlope.ForeColor = UiPalette.TextPrimary;
        comboBoxMaxSlope.Location = new Point(630, 95);
        comboBoxMaxSlope.MinimumSize = new Size(36, 21);
        comboBoxMaxSlope.Name = "comboBoxMaxSlope";
        comboBoxMaxSlope.Size = new Size(70, 21);
        comboBoxMaxSlope.TabIndex = 10;
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
        // checkButterworth
        //
        checkButterworth.AutoSize = true;
        checkButterworth.ForeColor = UiPalette.TextPrimary;
        checkButterworth.Location = new Point(96, 74);
        checkButterworth.Name = "checkButterworth";
        checkButterworth.Size = new Size(92, 19);
        checkButterworth.TabIndex = 8;
        checkButterworth.Text = "Butterworth";
        checkButterworth.UseVisualStyleBackColor = true;
        //
        // checkLinkwitzRiley
        //
        checkLinkwitzRiley.AutoSize = true;
        checkLinkwitzRiley.ForeColor = UiPalette.TextPrimary;
        checkLinkwitzRiley.Location = new Point(96, 97);
        checkLinkwitzRiley.Name = "checkLinkwitzRiley";
        checkLinkwitzRiley.Size = new Size(104, 19);
        checkLinkwitzRiley.TabIndex = 9;
        checkLinkwitzRiley.Text = "Linkwitz-Riley";
        checkLinkwitzRiley.UseVisualStyleBackColor = true;
        //
        // checkBessel
        //
        checkBessel.AutoSize = true;
        checkBessel.ForeColor = UiPalette.TextPrimary;
        checkBessel.Location = new Point(96, 120);
        checkBessel.Name = "checkBessel";
        checkBessel.Size = new Size(58, 19);
        checkBessel.TabIndex = 10;
        checkBessel.Text = "Bessel";
        checkBessel.UseVisualStyleBackColor = true;
        //
        // labelTuneFor
        //
        labelTuneFor.AutoSize = true;
        labelTuneFor.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
        labelTuneFor.ForeColor = UiPalette.TextDefault;
        labelTuneFor.Location = new Point(300, 74);
        labelTuneFor.Name = "labelTuneFor";
        labelTuneFor.Size = new Size(64, 15);
        labelTuneFor.TabIndex = 9;
        labelTuneFor.Text = "Tune for";
        //
        // radioSummation
        //
        radioSummation.AutoSize = true;
        radioSummation.Checked = true;
        radioSummation.ForeColor = UiPalette.TextPrimary;
        radioSummation.Location = new Point(300, 95);
        radioSummation.Name = "radioSummation";
        radioSummation.Size = new Size(150, 19);
        radioSummation.TabIndex = 10;
        radioSummation.TabStop = true;
        radioSummation.Text = "the best summation";
        radioSummation.UseVisualStyleBackColor = true;
        //
        // radioAcoustic
        //
        radioAcoustic.AutoSize = true;
        radioAcoustic.ForeColor = UiPalette.TextPrimary;
        radioAcoustic.Location = new Point(300, 118);
        radioAcoustic.Name = "radioAcoustic";
        radioAcoustic.Size = new Size(170, 19);
        radioAcoustic.TabIndex = 11;
        radioAcoustic.Text = "this acoustic crossover";
        radioAcoustic.UseVisualStyleBackColor = true;
        //
        // checkBoxSplitCorners
        //
        checkBoxSplitCorners.Checked = true;
        checkBoxSplitCorners.CheckState = CheckState.Checked;
        checkBoxSplitCorners.ForeColor = UiPalette.TextPrimary;
        checkBoxSplitCorners.Location = new Point(470, 43);
        checkBoxSplitCorners.Name = "checkBoxSplitCorners";
        checkBoxSplitCorners.Size = new Size(190, 21);
        checkBoxSplitCorners.TabIndex = 7;
        checkBoxSplitCorners.Text = "Corners free per side";
        checkBoxSplitCorners.UseVisualStyleBackColor = true;
        //
        // comboBoxGoalFamily
        //
        comboBoxGoalFamily.BackColor = UiPalette.ControlSurface;
        comboBoxGoalFamily.ForeColor = UiPalette.TextPrimary;
        comboBoxGoalFamily.Location = new Point(480, 116);
        comboBoxGoalFamily.MinimumSize = new Size(36, 21);
        comboBoxGoalFamily.Name = "comboBoxGoalFamily";
        comboBoxGoalFamily.Size = new Size(130, 21);
        comboBoxGoalFamily.TabIndex = 10;
        //
        // comboBoxGoalSlope
        //
        comboBoxGoalSlope.BackColor = UiPalette.ControlSurface;
        comboBoxGoalSlope.ForeColor = UiPalette.TextPrimary;
        comboBoxGoalSlope.Location = new Point(616, 116);
        comboBoxGoalSlope.MinimumSize = new Size(36, 21);
        comboBoxGoalSlope.Name = "comboBoxGoalSlope";
        comboBoxGoalSlope.Size = new Size(74, 21);
        comboBoxGoalSlope.TabIndex = 11;
        //
        // labelSumBudget
        //
        labelSumBudget.AutoSize = true;
        labelSumBudget.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
        labelSumBudget.ForeColor = UiPalette.TextDefault;
        labelSumBudget.Location = new Point(700, 119);
        labelSumBudget.Name = "labelSumBudget";
        labelSumBudget.Size = new Size(46, 15);
        labelSumBudget.TabIndex = 18;
        labelSumBudget.Text = "budget";
        //
        // numericSumBudget
        //
        numericSumBudget.BackColor = UiPalette.ControlSurface;
        numericSumBudget.DecimalPlaces = 1;
        numericSumBudget.ForeColor = UiPalette.TextPrimary;
        numericSumBudget.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
        numericSumBudget.Location = new Point(752, 116);
        numericSumBudget.Maximum = new decimal(new int[] { 3, 0, 0, 0 });
        numericSumBudget.MinimumSize = new Size(36, 21);
        numericSumBudget.Name = "numericSumBudget";
        numericSumBudget.Size = new Size(52, 21);
        numericSumBudget.TabIndex = 19;
        numericSumBudget.TextAlign = HorizontalAlignment.Right;
        numericSumBudget.Value = new decimal(new int[] { 1, 0, 0, 0 });
        //
        // labelSumBudgetUnit
        //
        labelSumBudgetUnit.AutoSize = true;
        labelSumBudgetUnit.ForeColor = UiPalette.TextDefault;
        labelSumBudgetUnit.Location = new Point(808, 119);
        labelSumBudgetUnit.Name = "labelSumBudgetUnit";
        labelSumBudgetUnit.Size = new Size(58, 15);
        labelSumBudgetUnit.TabIndex = 20;
        labelSumBudgetUnit.Text = "dB of sum";
        //
        // labelGoalHint
        //
        labelGoalHint.ForeColor = UiPalette.TextMuted;
        labelGoalHint.Location = new Point(300, 143);
        labelGoalHint.Name = "labelGoalHint";
        labelGoalHint.Size = new Size(570, 15);
        labelGoalHint.TabIndex = 12;
        labelGoalHint.Text = "Driver and filter together, which is steeper than the filter alone.";
        //
        // buttonRun
        //
        buttonRun.FlatStyle = FlatStyle.Popup;
        buttonRun.ForeColor = UiPalette.TextPrimary;
        buttonRun.Location = new Point(12, 170);
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
        labelStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        labelStatus.Location = new Point(130, 175);
        labelStatus.Name = "labelStatus";
        labelStatus.Size = new Size(742, 19);
        labelStatus.TabIndex = 14;
        labelStatus.Text = "Nothing searched yet.";
        //
        // textBoxReport
        //
        textBoxReport.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        textBoxReport.BackColor = UiPalette.SunkenSurface;
        textBoxReport.BorderStyle = BorderStyle.FixedSingle;
        textBoxReport.Font = new Font("Consolas", 9F);
        textBoxReport.ForeColor = UiPalette.TextPrimary;
        textBoxReport.Location = new Point(12, 202);
        textBoxReport.Name = "textBoxReport";
        textBoxReport.ReadOnly = true;
        textBoxReport.ScrollBars = RichTextBoxScrollBars.Both;
        textBoxReport.Size = new Size(860, 250);
        textBoxReport.TabIndex = 15;
        textBoxReport.WordWrap = false;
        //
        // buttonApply
        //
        buttonApply.Enabled = false;
        buttonApply.FlatStyle = FlatStyle.Popup;
        buttonApply.ForeColor = UiPalette.TextPrimary;
        buttonApply.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        buttonApply.Location = new Point(692, 464);
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
        buttonCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        buttonCancel.Location = new Point(792, 464);
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
        ClientSize = new Size(884, 502);
        Controls.Add(labelJunction);
        Controls.Add(comboBoxJunction);
        Controls.Add(labelWindow);
        Controls.Add(numericMinHz);
        Controls.Add(labelWindowTo);
        Controls.Add(numericMaxHz);
        Controls.Add(checkBoxIndependentSlopes);
        Controls.Add(labelSlopes);
        Controls.Add(comboBoxMinSlope);
        Controls.Add(labelSlopeTo);
        Controls.Add(comboBoxMaxSlope);
        Controls.Add(labelFamilies);
        Controls.Add(checkButterworth);
        Controls.Add(checkLinkwitzRiley);
        Controls.Add(checkBessel);
        Controls.Add(labelTuneFor);
        Controls.Add(radioSummation);
        Controls.Add(radioAcoustic);
        Controls.Add(checkBoxSplitCorners);
        Controls.Add(comboBoxGoalFamily);
        Controls.Add(comboBoxGoalSlope);
        Controls.Add(labelSumBudget);
        Controls.Add(numericSumBudget);
        Controls.Add(labelSumBudgetUnit);
        Controls.Add(labelGoalHint);
        Controls.Add(buttonRun);
        Controls.Add(labelStatus);
        Controls.Add(textBoxReport);
        Controls.Add(buttonApply);
        Controls.Add(buttonCancel);
        Font = new Font("Segoe UI", 9F);
        ForeColor = UiPalette.TextPrimary;
        MinimizeBox = false;
        MinimumSize = new Size(900, 432);
        Name = "VirtualCrossoverJunctionTuneDialog";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Tune junction";
        ((System.ComponentModel.ISupportInitialize)numericMinHz).EndInit();
        ((System.ComponentModel.ISupportInitialize)numericMaxHz).EndInit();
        ((System.ComponentModel.ISupportInitialize)numericSumBudget).EndInit();
        ResumeLayout(false);
    }

    private Label labelJunction;
    private ThemedComboBox comboBoxJunction;
    private Label labelWindow;
    private ThemedNumericUpDown numericMinHz;
    private Label labelWindowTo;
    private ThemedNumericUpDown numericMaxHz;
    private ReleaseClickCheckBox checkBoxIndependentSlopes;
    private Label labelSlopes;
    private ThemedComboBox comboBoxMinSlope;
    private Label labelSlopeTo;
    private ThemedComboBox comboBoxMaxSlope;
    private Label labelFamilies;
    private ReleaseClickCheckBox checkButterworth;
    private ReleaseClickCheckBox checkLinkwitzRiley;
    private ReleaseClickCheckBox checkBessel;
    private Label labelTuneFor;
    private ReleaseClickRadioButton radioSummation;
    private ReleaseClickRadioButton radioAcoustic;
    private ReleaseClickCheckBox checkBoxSplitCorners;
    private ThemedComboBox comboBoxGoalFamily;
    private ThemedComboBox comboBoxGoalSlope;
    private Label labelSumBudget;
    private ThemedNumericUpDown numericSumBudget;
    private Label labelSumBudgetUnit;
    private Label labelGoalHint;
    private ReleaseClickButton buttonRun;
    private Label labelStatus;
    private StatusRichTextBox textBoxReport;
    private ReleaseClickButton buttonApply;
    private ReleaseClickButton buttonCancel;
}
