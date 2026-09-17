namespace Resonalyze.Options
{
    partial class AngleCalibrationDialog
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            labelName = new Label();
            textBoxName = new TextBox();
            labelBase = new Label();
            comboBoxBase = new ThemedComboBox();
            labelAngle = new Label();
            numericAngle = new ThemedNumericUpDown();
            labelAngleUnit = new Label();
            labelDiameter = new Label();
            numericDiameter = new ThemedNumericUpDown();
            labelDiameterUnit = new Label();
            labelGrid = new Label();
            comboBoxGrid = new ThemedComboBox();
            labelReference = new Label();
            comboBoxReference = new ThemedComboBox();
            plotViewPreview = new OxyPlot.WindowsForms.PlotView();
            labelSummary = new Label();
            buttonOk = new ReleaseClickButton();
            buttonCancel = new ReleaseClickButton();
            SuspendLayout();
            //
            // labelName
            //
            labelName.AutoSize = true;
            labelName.ForeColor = UiPalette.TextDefault;
            labelName.Location = new Point(16, 20);
            labelName.Name = "labelName";
            labelName.Size = new Size(39, 15);
            labelName.TabIndex = 0;
            labelName.Text = "Name";
            //
            // textBoxName
            //
            textBoxName.BackColor = UiPalette.ControlSurface;
            textBoxName.BorderStyle = BorderStyle.FixedSingle;
            textBoxName.ForeColor = UiPalette.TextPrimary;
            textBoxName.Location = new Point(150, 16);
            textBoxName.Name = "textBoxName";
            textBoxName.Size = new Size(290, 23);
            textBoxName.TabIndex = 1;
            //
            // labelBase
            //
            labelBase.AutoSize = true;
            labelBase.ForeColor = UiPalette.TextDefault;
            labelBase.Location = new Point(16, 52);
            labelBase.Name = "labelBase";
            labelBase.Size = new Size(88, 15);
            labelBase.TabIndex = 2;
            labelBase.Text = "Derived from";
            //
            // comboBoxBase
            //
            comboBoxBase.BackColor = UiPalette.ControlSurface;
            comboBoxBase.ForeColor = UiPalette.TextPrimary;
            comboBoxBase.Location = new Point(150, 48);
            comboBoxBase.Margin = new Padding(0);
            comboBoxBase.MinimumSize = new Size(36, 19);
            comboBoxBase.Name = "comboBoxBase";
            comboBoxBase.Size = new Size(290, 23);
            comboBoxBase.TabIndex = 3;
            //
            // labelAngle
            //
            labelAngle.AutoSize = true;
            labelAngle.ForeColor = UiPalette.TextDefault;
            labelAngle.Location = new Point(16, 84);
            labelAngle.Name = "labelAngle";
            labelAngle.Size = new Size(115, 15);
            labelAngle.TabIndex = 4;
            labelAngle.Text = "Angle of incidence";
            //
            // numericAngle
            //
            numericAngle.BackColor = UiPalette.ControlSurface;
            numericAngle.DecimalPlaces = 1;
            numericAngle.ForeColor = UiPalette.TextPrimary;
            numericAngle.Increment = new decimal(new int[] { 5, 0, 0, 0 });
            numericAngle.Location = new Point(150, 80);
            numericAngle.Maximum = new decimal(new int[] { 90, 0, 0, 0 });
            numericAngle.MinimumSize = new Size(36, 19);
            numericAngle.Name = "numericAngle";
            numericAngle.Size = new Size(90, 23);
            numericAngle.TabIndex = 5;
            numericAngle.TextAlign = HorizontalAlignment.Right;
            //
            // labelAngleUnit
            //
            labelAngleUnit.AutoSize = true;
            labelAngleUnit.ForeColor = UiPalette.TextDefault;
            labelAngleUnit.Location = new Point(248, 84);
            labelAngleUnit.Name = "labelAngleUnit";
            labelAngleUnit.Size = new Size(160, 15);
            labelAngleUnit.TabIndex = 6;
            labelAngleUnit.Text = "degrees off axis (0 = on axis)";
            //
            // labelDiameter
            //
            labelDiameter.AutoSize = true;
            labelDiameter.ForeColor = UiPalette.TextDefault;
            labelDiameter.Location = new Point(16, 116);
            labelDiameter.Name = "labelDiameter";
            labelDiameter.Size = new Size(97, 15);
            labelDiameter.TabIndex = 7;
            labelDiameter.Text = "Front diameter";
            //
            // numericDiameter
            //
            numericDiameter.BackColor = UiPalette.ControlSurface;
            numericDiameter.DecimalPlaces = 2;
            numericDiameter.ForeColor = UiPalette.TextPrimary;
            numericDiameter.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            numericDiameter.Location = new Point(150, 112);
            numericDiameter.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            numericDiameter.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericDiameter.MinimumSize = new Size(36, 19);
            numericDiameter.Name = "numericDiameter";
            numericDiameter.Size = new Size(90, 23);
            numericDiameter.TabIndex = 8;
            numericDiameter.TextAlign = HorizontalAlignment.Right;
            numericDiameter.Value = new decimal(new int[] { 127, 0, 0, 65536 });
            //
            // labelDiameterUnit
            //
            labelDiameterUnit.AutoSize = true;
            labelDiameterUnit.ForeColor = UiPalette.TextDefault;
            labelDiameterUnit.Location = new Point(248, 116);
            labelDiameterUnit.Name = "labelDiameterUnit";
            labelDiameterUnit.Size = new Size(180, 15);
            labelDiameterUnit.TabIndex = 9;
            labelDiameterUnit.Text = "mm, across the front";
            //
            // labelGrid
            //
            labelGrid.AutoSize = true;
            labelGrid.ForeColor = UiPalette.TextDefault;
            labelGrid.Location = new Point(16, 148);
            labelGrid.Name = "labelGrid";
            labelGrid.Size = new Size(92, 15);
            labelGrid.TabIndex = 10;
            labelGrid.Text = "Protection grid";
            //
            // comboBoxGrid
            //
            comboBoxGrid.BackColor = UiPalette.ControlSurface;
            comboBoxGrid.ForeColor = UiPalette.TextPrimary;
            comboBoxGrid.Location = new Point(150, 144);
            comboBoxGrid.Margin = new Padding(0);
            comboBoxGrid.MinimumSize = new Size(36, 19);
            comboBoxGrid.Name = "comboBoxGrid";
            comboBoxGrid.Size = new Size(290, 23);
            comboBoxGrid.TabIndex = 11;
            //
            // labelReference
            //
            labelReference.AutoSize = true;
            labelReference.ForeColor = UiPalette.TextDefault;
            labelReference.Location = new Point(16, 180);
            labelReference.Name = "labelReference";
            labelReference.Size = new Size(60, 15);
            labelReference.TabIndex = 12;
            labelReference.Text = "Model";
            //
            // comboBoxReference
            //
            comboBoxReference.BackColor = UiPalette.ControlSurface;
            comboBoxReference.ForeColor = UiPalette.TextPrimary;
            comboBoxReference.Location = new Point(150, 176);
            comboBoxReference.Margin = new Padding(0);
            comboBoxReference.MinimumSize = new Size(36, 19);
            comboBoxReference.Name = "comboBoxReference";
            comboBoxReference.Size = new Size(290, 23);
            comboBoxReference.TabIndex = 13;
            //
            // plotViewPreview
            //
            plotViewPreview.BackColor = UiPalette.GraphSurfaceMuted;
            plotViewPreview.Location = new Point(16, 210);
            plotViewPreview.Name = "plotViewPreview";
            plotViewPreview.PanCursor = Cursors.Hand;
            plotViewPreview.Size = new Size(424, 210);
            plotViewPreview.TabIndex = 14;
            plotViewPreview.Text = "plotViewPreview";
            plotViewPreview.ZoomHorizontalCursor = Cursors.SizeWE;
            plotViewPreview.ZoomRectangleCursor = Cursors.SizeNWSE;
            plotViewPreview.ZoomVerticalCursor = Cursors.SizeNS;
            //
            // labelSummary
            //
            labelSummary.ForeColor = UiPalette.TextDefault;
            labelSummary.Location = new Point(16, 428);
            labelSummary.Name = "labelSummary";
            labelSummary.Size = new Size(424, 72);
            labelSummary.TabIndex = 15;
            labelSummary.Text = "Estimated.";
            //
            // buttonOk
            //
            buttonOk.DialogResult = DialogResult.OK;
            buttonOk.FlatStyle = FlatStyle.Popup;
            buttonOk.ForeColor = UiPalette.TextPrimary;
            buttonOk.Location = new Point(268, 508);
            buttonOk.Name = "buttonOk";
            buttonOk.Size = new Size(84, 28);
            buttonOk.TabIndex = 16;
            buttonOk.Text = "OK";
            buttonOk.UseVisualStyleBackColor = true;
            //
            // buttonCancel
            //
            buttonCancel.DialogResult = DialogResult.Cancel;
            buttonCancel.FlatStyle = FlatStyle.Popup;
            buttonCancel.ForeColor = UiPalette.TextPrimary;
            buttonCancel.Location = new Point(356, 508);
            buttonCancel.Name = "buttonCancel";
            buttonCancel.Size = new Size(84, 28);
            buttonCancel.TabIndex = 17;
            buttonCancel.Text = "Cancel";
            buttonCancel.UseVisualStyleBackColor = true;
            //
            // AngleCalibrationDialog
            //
            AcceptButton = buttonOk;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiPalette.AppBackground;
            CancelButton = buttonCancel;
            ClientSize = new Size(456, 548);
            Controls.Add(labelName);
            Controls.Add(textBoxName);
            Controls.Add(labelBase);
            Controls.Add(comboBoxBase);
            Controls.Add(labelAngle);
            Controls.Add(numericAngle);
            Controls.Add(labelAngleUnit);
            Controls.Add(labelDiameter);
            Controls.Add(numericDiameter);
            Controls.Add(labelDiameterUnit);
            Controls.Add(labelGrid);
            Controls.Add(comboBoxGrid);
            Controls.Add(labelReference);
            Controls.Add(comboBoxReference);
            Controls.Add(plotViewPreview);
            Controls.Add(labelSummary);
            Controls.Add(buttonOk);
            Controls.Add(buttonCancel);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "AngleCalibrationDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Angle calibration";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label labelName;
        private TextBox textBoxName;
        private Label labelBase;
        private ThemedComboBox comboBoxBase;
        private Label labelAngle;
        private ThemedNumericUpDown numericAngle;
        private Label labelAngleUnit;
        private Label labelDiameter;
        private ThemedNumericUpDown numericDiameter;
        private Label labelDiameterUnit;
        private Label labelGrid;
        private ThemedComboBox comboBoxGrid;
        private Label labelReference;
        private ThemedComboBox comboBoxReference;
        private OxyPlot.WindowsForms.PlotView plotViewPreview;
        private Label labelSummary;
        private ReleaseClickButton buttonOk;
        private ReleaseClickButton buttonCancel;
    }
}
