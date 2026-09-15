namespace Resonalyze.Ui.Dialogs;

partial class RewImportDialog
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            closing.Dispose();
        }

        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        labelInstruction = new Label();
        labelAddress = new Label();
        textAddress = new TextBox();
        buttonRefresh = new ReleaseClickButton();
        labelStatus = new Label();
        measurementGridView = new DataGridView();
        ColumnName = new DataGridViewTextBoxColumn();
        ColumnDate = new DataGridViewTextBoxColumn();
        ColumnRate = new DataGridViewTextBoxColumn();
        ColumnPeak = new DataGridViewTextBoxColumn();
        labelOffset = new Label();
        numericOffset = new DarkNumericUpDown();
        checkOffsetUnknown = new ReleaseClickCheckBox();
        labelLevel = new Label();
        numericLevel = new DarkNumericUpDown();
        labelOffsetHelp = new Label();
        labelSelection = new Label();
        labelProblem = new Label();
        buttonImport = new ReleaseClickButton();
        buttonCancel = new ReleaseClickButton();
        ((System.ComponentModel.ISupportInitialize)measurementGridView).BeginInit();
        (numericOffset).BeginInit();
        (numericLevel).BeginInit();
        SuspendLayout();
        //
        // labelInstruction
        //
        labelInstruction.ForeColor = Color.FromArgb(210, 214, 222);
        labelInstruction.Location = new Point(16, 14);
        labelInstruction.Name = "labelInstruction";
        labelInstruction.Size = new Size(608, 36);
        labelInstruction.TabIndex = 0;
        labelInstruction.Text = "Choose a measurement REW holds. Only one taken against a loopback timing reference can be placed on this session's time base. Its impulse response arrives unnormalised and unwindowed.";
        //
        // labelAddress
        //
        labelAddress.AutoSize = true;
        labelAddress.ForeColor = Color.FromArgb(210, 214, 222);
        labelAddress.Location = new Point(16, 62);
        labelAddress.Name = "labelAddress";
        labelAddress.Size = new Size(72, 15);
        labelAddress.TabIndex = 1;
        labelAddress.Text = "REW address";
        //
        // textAddress
        //
        textAddress.BackColor = Color.FromArgb(55, 58, 65);
        textAddress.BorderStyle = BorderStyle.FixedSingle;
        textAddress.ForeColor = Color.White;
        textAddress.Location = new Point(104, 59);
        textAddress.Name = "textAddress";
        textAddress.Size = new Size(406, 23);
        textAddress.TabIndex = 2;
        //
        // buttonRefresh
        //
        buttonRefresh.BackColor = Color.FromArgb(50, 55, 80);
        buttonRefresh.FlatStyle = FlatStyle.Popup;
        buttonRefresh.ForeColor = Color.White;
        buttonRefresh.Location = new Point(520, 58);
        buttonRefresh.Name = "buttonRefresh";
        buttonRefresh.Size = new Size(104, 25);
        buttonRefresh.TabIndex = 3;
        buttonRefresh.Text = "Refresh";
        buttonRefresh.UseVisualStyleBackColor = false;
        //
        // labelStatus
        //
        labelStatus.ForeColor = Color.FromArgb(185, 190, 200);
        labelStatus.Location = new Point(104, 86);
        labelStatus.Name = "labelStatus";
        labelStatus.Size = new Size(520, 18);
        labelStatus.TabIndex = 4;
        //
        // measurementGridView
        //
        measurementGridView.AllowUserToAddRows = false;
        measurementGridView.AllowUserToDeleteRows = false;
        measurementGridView.AllowUserToResizeRows = false;
        measurementGridView.BackgroundColor = Color.FromArgb(40, 42, 48);
        measurementGridView.BorderStyle = BorderStyle.None;
        measurementGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        measurementGridView.Columns.AddRange(new DataGridViewColumn[] { ColumnName, ColumnDate, ColumnRate, ColumnPeak });
        measurementGridView.Location = new Point(16, 110);
        measurementGridView.Margin = new Padding(0);
        measurementGridView.MultiSelect = false;
        measurementGridView.Name = "measurementGridView";
        measurementGridView.ReadOnly = true;
        measurementGridView.RowHeadersVisible = false;
        measurementGridView.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        measurementGridView.Size = new Size(608, 150);
        measurementGridView.TabIndex = 5;
        //
        // ColumnName
        //
        ColumnName.HeaderText = "Name";
        ColumnName.Name = "ColumnName";
        ColumnName.ReadOnly = true;
        ColumnName.Width = 272;
        //
        // ColumnDate
        //
        ColumnDate.HeaderText = "Date";
        ColumnDate.Name = "ColumnDate";
        ColumnDate.ReadOnly = true;
        ColumnDate.Width = 150;
        //
        // ColumnRate
        //
        ColumnRate.HeaderText = "Rate, Hz";
        ColumnRate.Name = "ColumnRate";
        ColumnRate.ReadOnly = true;
        ColumnRate.Width = 76;
        //
        // ColumnPeak
        //
        ColumnPeak.HeaderText = "Peak, ms";
        ColumnPeak.Name = "ColumnPeak";
        ColumnPeak.ReadOnly = true;
        ColumnPeak.Width = 88;
        //
        // labelOffset
        //
        labelOffset.AutoSize = true;
        labelOffset.ForeColor = Color.FromArgb(210, 214, 222);
        labelOffset.Location = new Point(16, 277);
        labelOffset.Name = "labelOffset";
        labelOffset.Size = new Size(147, 15);
        labelOffset.TabIndex = 6;
        labelOffset.Text = "REW timing offset, ms";
        //
        // numericOffset
        //
        numericOffset.BackColor = Color.FromArgb(55, 60, 72);
        numericOffset.DecimalPlaces = 4;
        numericOffset.ForeColor = Color.White;
        numericOffset.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
        numericOffset.Location = new Point(176, 274);
        numericOffset.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
        numericOffset.Minimum = new decimal(new int[] { 1000, 0, 0, int.MinValue });
        numericOffset.MinimumSize = new Size(36, 19);
        numericOffset.Name = "numericOffset";
        numericOffset.Size = new Size(96, 23);
        numericOffset.TabIndex = 7;
        numericOffset.TextAlign = HorizontalAlignment.Right;
        numericOffset.ThousandsSeparator = false;
        numericOffset.Value = new decimal(new int[] { 0, 0, 0, 0 });
        //
        // checkOffsetUnknown
        //
        checkOffsetUnknown.AutoSize = true;
        checkOffsetUnknown.ForeColor = Color.FromArgb(210, 214, 222);
        checkOffsetUnknown.Location = new Point(288, 276);
        checkOffsetUnknown.Name = "checkOffsetUnknown";
        checkOffsetUnknown.Size = new Size(92, 19);
        checkOffsetUnknown.TabIndex = 8;
        checkOffsetUnknown.Text = "I don't know";
        checkOffsetUnknown.UseVisualStyleBackColor = false;
        //
        // labelLevel
        //
        labelLevel.AutoSize = true;
        labelLevel.ForeColor = Color.FromArgb(210, 214, 222);
        labelLevel.Location = new Point(16, 307);
        labelLevel.Name = "labelLevel";
        labelLevel.Size = new Size(126, 15);
        labelLevel.TabIndex = 14;
        labelLevel.Text = "REW sweep level, dBFS";
        //
        // numericLevel
        //
        numericLevel.BackColor = Color.FromArgb(55, 60, 72);
        numericLevel.DecimalPlaces = 1;
        numericLevel.ForeColor = Color.White;
        numericLevel.Increment = new decimal(new int[] { 1, 0, 0, 0 });
        numericLevel.Location = new Point(176, 304);
        numericLevel.Maximum = new decimal(new int[] { 0, 0, 0, 0 });
        numericLevel.Minimum = new decimal(new int[] { 100, 0, 0, int.MinValue });
        numericLevel.MinimumSize = new Size(36, 19);
        numericLevel.Name = "numericLevel";
        numericLevel.Size = new Size(96, 23);
        numericLevel.TabIndex = 9;
        numericLevel.TextAlign = HorizontalAlignment.Right;
        numericLevel.ThousandsSeparator = false;
        numericLevel.Value = new decimal(new int[] { 12, 0, 0, int.MinValue });
        //
        // labelOffsetHelp
        //
        labelOffsetHelp.ForeColor = Color.FromArgb(185, 190, 200);
        labelOffsetHelp.Location = new Point(16, 334);
        labelOffsetHelp.Name = "labelOffsetHelp";
        labelOffsetHelp.Size = new Size(608, 70);
        labelOffsetHelp.TabIndex = 9;
        labelOffsetHelp.Text = "Timing offset: filled in where REW records it; most measurements use none. I don't know imports the shape without claiming its position. Sweep level: REW scales a response to digital full scale, this program to the loopback, so the loopback's level is taken back out. It starts at REW's current setting; an analog loopback's gain is not in it.";
        //
        // labelSelection
        //
        labelSelection.ForeColor = Color.FromArgb(185, 190, 200);
        labelSelection.Location = new Point(16, 406);
        labelSelection.Name = "labelSelection";
        labelSelection.Size = new Size(608, 52);
        labelSelection.TabIndex = 10;
        //
        // labelProblem
        //
        labelProblem.ForeColor = Color.FromArgb(255, 190, 80);
        labelProblem.Location = new Point(16, 460);
        labelProblem.Name = "labelProblem";
        labelProblem.Size = new Size(608, 68);
        labelProblem.TabIndex = 11;
        //
        // buttonImport
        //
        buttonImport.BackColor = Color.FromArgb(50, 55, 80);
        buttonImport.FlatStyle = FlatStyle.Popup;
        buttonImport.ForeColor = Color.White;
        buttonImport.Location = new Point(430, 534);
        buttonImport.Name = "buttonImport";
        buttonImport.Size = new Size(94, 30);
        buttonImport.TabIndex = 12;
        buttonImport.Text = "Import";
        buttonImport.UseVisualStyleBackColor = false;
        //
        // buttonCancel
        //
        buttonCancel.BackColor = Color.FromArgb(50, 55, 80);
        buttonCancel.DialogResult = DialogResult.Cancel;
        buttonCancel.FlatStyle = FlatStyle.Popup;
        buttonCancel.ForeColor = Color.White;
        buttonCancel.Location = new Point(530, 534);
        buttonCancel.Name = "buttonCancel";
        buttonCancel.Size = new Size(94, 30);
        buttonCancel.TabIndex = 13;
        buttonCancel.Text = "Cancel";
        buttonCancel.UseVisualStyleBackColor = false;
        //
        // RewImportDialog
        //
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(40, 42, 48);
        CancelButton = buttonCancel;
        ClientSize = new Size(640, 576);
        Controls.Add(labelInstruction);
        Controls.Add(labelAddress);
        Controls.Add(textAddress);
        Controls.Add(buttonRefresh);
        Controls.Add(labelStatus);
        Controls.Add(measurementGridView);
        Controls.Add(labelOffset);
        Controls.Add(numericOffset);
        Controls.Add(checkOffsetUnknown);
        Controls.Add(labelLevel);
        Controls.Add(numericLevel);
        Controls.Add(labelOffsetHelp);
        Controls.Add(labelSelection);
        Controls.Add(labelProblem);
        Controls.Add(buttonImport);
        Controls.Add(buttonCancel);
        Font = new Font("Segoe UI", 9F);
        ForeColor = Color.White;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "RewImportDialog";
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Import from REW";
        ((System.ComponentModel.ISupportInitialize)measurementGridView).EndInit();
        (numericOffset).EndInit();
        (numericLevel).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Label labelInstruction;
    private Label labelAddress;
    private TextBox textAddress;
    private ReleaseClickButton buttonRefresh;
    private Label labelStatus;
    private DataGridView measurementGridView;
    private DataGridViewTextBoxColumn ColumnName;
    private DataGridViewTextBoxColumn ColumnDate;
    private DataGridViewTextBoxColumn ColumnRate;
    private DataGridViewTextBoxColumn ColumnPeak;
    private Label labelOffset;
    private DarkNumericUpDown numericOffset;
    private ReleaseClickCheckBox checkOffsetUnknown;
    private Label labelLevel;
    private DarkNumericUpDown numericLevel;
    private Label labelOffsetHelp;
    private Label labelSelection;
    private Label labelProblem;
    private ReleaseClickButton buttonImport;
    private ReleaseClickButton buttonCancel;
}
