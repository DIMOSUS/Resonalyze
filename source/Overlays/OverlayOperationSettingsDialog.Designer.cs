namespace Resonalyze
{
    partial class OverlayOperationSettingsDialog
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
            components = new System.ComponentModel.Container();
            nameLabel = new Label();
            nameTextBox = new TextBox();
            curveALabel = new Label();
            curveBLabel = new Label();
            sourceAComboBox = new DarkComboBox();
            sourceBComboBox = new DarkComboBox();
            operationLabel = new Label();
            operationComboBox = new DarkComboBox();
            colorLabel = new Label();
            colorButton = new Button();
            thicknessLabel = new Label();
            thicknessInput = new DarkNumericUpDown();
            styleLabel = new Label();
            styleComboBox = new DarkComboBox();
            blendFrequencyLabel = new Label();
            blendFrequencyInput = new DarkNumericUpDown();
            blendWidthLabel = new Label();
            blendWidthInput = new DarkComboBox();
            smoothingLabel = new Label();
            smoothingComboBox = new DarkComboBox();
            amplitudeSpaceCheckBox = new CheckBox();
            opacityLabel = new Label();
            opacityTrackBar = new TrackBar();
            opacityValueLabel = new Label();
            cancelButton = new Button();
            saveButton = new Button();
            toolTip = new ToolTip(components);
            numericTimeOffset = new DarkNumericUpDown();
            labelTimeOffset = new Label();
            checkBoxInvPhase = new CheckBox();
            panel1 = new Panel();
            (thicknessInput).BeginInit();
            (blendFrequencyInput).BeginInit();
            ((System.ComponentModel.ISupportInitialize)opacityTrackBar).BeginInit();
            (numericTimeOffset).BeginInit();
            panel1.SuspendLayout();
            SuspendLayout();
            // 
            // nameLabel
            // 
            nameLabel.AutoSize = true;
            nameLabel.ForeColor = Color.FromArgb(185, 190, 200);
            nameLabel.Location = new Point(20, 22);
            nameLabel.Name = "nameLabel";
            nameLabel.Size = new Size(39, 15);
            nameLabel.TabIndex = 0;
            nameLabel.Text = "Name";
            // 
            // nameTextBox
            // 
            nameTextBox.BackColor = Color.FromArgb(55, 58, 65);
            nameTextBox.ForeColor = Color.White;
            nameTextBox.Location = new Point(20, 42);
            nameTextBox.MaxLength = 80;
            nameTextBox.Name = "nameTextBox";
            nameTextBox.Size = new Size(400, 23);
            nameTextBox.TabIndex = 0;
            // 
            // curveALabel
            // 
            curveALabel.AutoSize = true;
            curveALabel.ForeColor = Color.FromArgb(185, 190, 200);
            curveALabel.Location = new Point(20, 82);
            curveALabel.Name = "curveALabel";
            curveALabel.Size = new Size(49, 15);
            curveALabel.TabIndex = 1;
            curveALabel.Text = "Curve A";
            // 
            // curveBLabel
            // 
            curveBLabel.AutoSize = true;
            curveBLabel.ForeColor = Color.FromArgb(185, 190, 200);
            curveBLabel.Location = new Point(3, 4);
            curveBLabel.Name = "curveBLabel";
            curveBLabel.Size = new Size(48, 15);
            curveBLabel.TabIndex = 2;
            curveBLabel.Text = "Curve B";
            // 
            // sourceAComboBox
            // 
            sourceAComboBox.BackColor = Color.FromArgb(55, 58, 65);
            sourceAComboBox.ForeColor = Color.White;
            sourceAComboBox.Location = new Point(20, 102);
            sourceAComboBox.Margin = new Padding(0);
            sourceAComboBox.MinimumSize = new Size(36, 19);
            sourceAComboBox.Name = "sourceAComboBox";
            sourceAComboBox.Size = new Size(190, 24);
            sourceAComboBox.TabIndex = 1;
            // 
            // sourceBComboBox
            // 
            sourceBComboBox.BackColor = Color.FromArgb(55, 58, 65);
            sourceBComboBox.ForeColor = Color.White;
            sourceBComboBox.Location = new Point(6, 24);
            sourceBComboBox.Margin = new Padding(0);
            sourceBComboBox.MinimumSize = new Size(36, 19);
            sourceBComboBox.Name = "sourceBComboBox";
            sourceBComboBox.Size = new Size(190, 24);
            sourceBComboBox.TabIndex = 2;
            // 
            // operationLabel
            // 
            operationLabel.AutoSize = true;
            operationLabel.ForeColor = Color.FromArgb(185, 190, 200);
            operationLabel.Location = new Point(20, 142);
            operationLabel.Name = "operationLabel";
            operationLabel.Size = new Size(60, 15);
            operationLabel.TabIndex = 3;
            operationLabel.Text = "Operation";
            // 
            // operationComboBox
            // 
            operationComboBox.BackColor = Color.FromArgb(55, 58, 65);
            operationComboBox.ForeColor = Color.White;
            operationComboBox.Location = new Point(20, 162);
            operationComboBox.Margin = new Padding(0);
            operationComboBox.MinimumSize = new Size(36, 19);
            operationComboBox.Name = "operationComboBox";
            operationComboBox.Size = new Size(190, 24);
            operationComboBox.TabIndex = 3;
            // 
            // colorLabel
            // 
            colorLabel.AutoSize = true;
            colorLabel.ForeColor = Color.FromArgb(185, 190, 200);
            colorLabel.Location = new Point(23, 240);
            colorLabel.Name = "colorLabel";
            colorLabel.Size = new Size(36, 15);
            colorLabel.TabIndex = 4;
            colorLabel.Text = "Color";
            // 
            // colorButton
            // 
            colorButton.BackColor = Color.FromArgb(62, 65, 73);
            colorButton.FlatAppearance.BorderSize = 0;
            colorButton.FlatStyle = FlatStyle.Flat;
            colorButton.ForeColor = Color.White;
            colorButton.Location = new Point(23, 260);
            colorButton.Name = "colorButton";
            colorButton.Size = new Size(122, 24);
            colorButton.TabIndex = 4;
            colorButton.UseVisualStyleBackColor = false;
            // 
            // thicknessLabel
            // 
            thicknessLabel.AutoSize = true;
            thicknessLabel.ForeColor = Color.FromArgb(185, 190, 200);
            thicknessLabel.Location = new Point(165, 240);
            thicknessLabel.Name = "thicknessLabel";
            thicknessLabel.Size = new Size(59, 15);
            thicknessLabel.TabIndex = 5;
            thicknessLabel.Text = "Thickness";
            // 
            // thicknessInput
            // 
            thicknessInput.BackColor = Color.FromArgb(55, 58, 65);
            thicknessInput.DecimalPlaces = 1;
            thicknessInput.ForeColor = Color.White;
            thicknessInput.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            thicknessInput.Location = new Point(165, 260);
            thicknessInput.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            thicknessInput.Minimum = new decimal(new int[] { 5, 0, 0, 65536 });
            thicknessInput.MinimumSize = new Size(36, 19);
            thicknessInput.Name = "thicknessInput";
            thicknessInput.Size = new Size(90, 24);
            thicknessInput.TabIndex = 5;
            thicknessInput.TextAlign = HorizontalAlignment.Right;
            thicknessInput.ThousandsSeparator = false;
            thicknessInput.Value = new decimal(new int[] { 5, 0, 0, 65536 });
            // 
            // styleLabel
            // 
            styleLabel.AutoSize = true;
            styleLabel.ForeColor = Color.FromArgb(185, 190, 200);
            styleLabel.Location = new Point(275, 240);
            styleLabel.Name = "styleLabel";
            styleLabel.Size = new Size(32, 15);
            styleLabel.TabIndex = 6;
            styleLabel.Text = "Style";
            // 
            // styleComboBox
            // 
            styleComboBox.BackColor = Color.FromArgb(55, 58, 65);
            styleComboBox.ForeColor = Color.White;
            styleComboBox.Location = new Point(275, 260);
            styleComboBox.Margin = new Padding(0);
            styleComboBox.MinimumSize = new Size(36, 19);
            styleComboBox.Name = "styleComboBox";
            styleComboBox.Size = new Size(148, 24);
            styleComboBox.TabIndex = 6;
            // 
            // blendFrequencyLabel
            // 
            blendFrequencyLabel.AutoSize = true;
            blendFrequencyLabel.ForeColor = Color.FromArgb(185, 190, 200);
            blendFrequencyLabel.Location = new Point(23, 300);
            blendFrequencyLabel.Name = "blendFrequencyLabel";
            blendFrequencyLabel.Size = new Size(93, 15);
            blendFrequencyLabel.TabIndex = 7;
            blendFrequencyLabel.Text = "Blend frequency";
            // 
            // blendFrequencyInput
            // 
            blendFrequencyInput.BackColor = Color.FromArgb(55, 58, 65);
            blendFrequencyInput.DecimalPlaces = 1;
            blendFrequencyInput.ForeColor = Color.White;
            blendFrequencyInput.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            blendFrequencyInput.Location = new Point(23, 320);
            blendFrequencyInput.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            blendFrequencyInput.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            blendFrequencyInput.MinimumSize = new Size(36, 19);
            blendFrequencyInput.Name = "blendFrequencyInput";
            blendFrequencyInput.Size = new Size(190, 24);
            blendFrequencyInput.TabIndex = 7;
            blendFrequencyInput.TextAlign = HorizontalAlignment.Right;
            blendFrequencyInput.ThousandsSeparator = true;
            blendFrequencyInput.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // blendWidthLabel
            // 
            blendWidthLabel.AutoSize = true;
            blendWidthLabel.ForeColor = Color.FromArgb(185, 190, 200);
            blendWidthLabel.Location = new Point(233, 300);
            blendWidthLabel.Name = "blendWidthLabel";
            blendWidthLabel.Size = new Size(92, 15);
            blendWidthLabel.TabIndex = 8;
            blendWidthLabel.Text = "Transition width";
            // 
            // blendWidthInput
            // 
            blendWidthInput.BackColor = Color.FromArgb(55, 58, 65);
            blendWidthInput.ForeColor = Color.White;
            blendWidthInput.Location = new Point(233, 320);
            blendWidthInput.Margin = new Padding(0);
            blendWidthInput.MinimumSize = new Size(36, 19);
            blendWidthInput.Name = "blendWidthInput";
            blendWidthInput.Size = new Size(190, 24);
            blendWidthInput.TabIndex = 8;
            // 
            // smoothingLabel
            // 
            smoothingLabel.AutoSize = true;
            smoothingLabel.ForeColor = Color.FromArgb(185, 190, 200);
            smoothingLabel.Location = new Point(23, 360);
            smoothingLabel.Name = "smoothingLabel";
            smoothingLabel.Size = new Size(66, 15);
            smoothingLabel.TabIndex = 9;
            smoothingLabel.Text = "Smoothing";
            // 
            // smoothingComboBox
            // 
            smoothingComboBox.BackColor = Color.FromArgb(55, 58, 65);
            smoothingComboBox.ForeColor = Color.White;
            smoothingComboBox.Location = new Point(23, 380);
            smoothingComboBox.Margin = new Padding(0);
            smoothingComboBox.MinimumSize = new Size(36, 19);
            smoothingComboBox.Name = "smoothingComboBox";
            smoothingComboBox.Size = new Size(400, 24);
            smoothingComboBox.TabIndex = 9;
            // 
            // amplitudeSpaceCheckBox
            // 
            amplitudeSpaceCheckBox.AutoSize = true;
            amplitudeSpaceCheckBox.Checked = true;
            amplitudeSpaceCheckBox.CheckState = CheckState.Checked;
            amplitudeSpaceCheckBox.ForeColor = Color.FromArgb(235, 237, 240);
            amplitudeSpaceCheckBox.Location = new Point(20, 200);
            amplitudeSpaceCheckBox.Name = "amplitudeSpaceCheckBox";
            amplitudeSpaceCheckBox.Size = new Size(171, 19);
            amplitudeSpaceCheckBox.TabIndex = 10;
            amplitudeSpaceCheckBox.Text = "Operate in amplitude space";
            // 
            // opacityLabel
            // 
            opacityLabel.AutoSize = true;
            opacityLabel.ForeColor = Color.FromArgb(185, 190, 200);
            opacityLabel.Location = new Point(20, 420);
            opacityLabel.Name = "opacityLabel";
            opacityLabel.Size = new Size(48, 15);
            opacityLabel.TabIndex = 11;
            opacityLabel.Text = "Opacity";
            // 
            // opacityTrackBar
            // 
            opacityTrackBar.Location = new Point(14, 440);
            opacityTrackBar.Maximum = 100;
            opacityTrackBar.Minimum = 10;
            opacityTrackBar.Name = "opacityTrackBar";
            opacityTrackBar.Size = new Size(340, 45);
            opacityTrackBar.TabIndex = 11;
            opacityTrackBar.TickFrequency = 10;
            opacityTrackBar.Value = 100;
            // 
            // opacityValueLabel
            // 
            opacityValueLabel.AutoSize = true;
            opacityValueLabel.ForeColor = Color.FromArgb(235, 237, 240);
            opacityValueLabel.Location = new Point(370, 447);
            opacityValueLabel.Name = "opacityValueLabel";
            opacityValueLabel.Size = new Size(35, 15);
            opacityValueLabel.TabIndex = 12;
            opacityValueLabel.Text = "100%";
            // 
            // cancelButton
            // 
            cancelButton.BackColor = Color.FromArgb(62, 65, 73);
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.FlatAppearance.BorderSize = 0;
            cancelButton.FlatStyle = FlatStyle.Flat;
            cancelButton.ForeColor = Color.White;
            cancelButton.Location = new Point(232, 491);
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new Size(94, 30);
            cancelButton.TabIndex = 12;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = false;
            // 
            // saveButton
            // 
            saveButton.BackColor = Color.FromArgb(64, 116, 255);
            saveButton.DialogResult = DialogResult.OK;
            saveButton.FlatAppearance.BorderSize = 0;
            saveButton.FlatStyle = FlatStyle.Flat;
            saveButton.ForeColor = Color.White;
            saveButton.Location = new Point(332, 491);
            saveButton.Name = "saveButton";
            saveButton.Size = new Size(94, 30);
            saveButton.TabIndex = 13;
            saveButton.Text = "Save";
            saveButton.UseVisualStyleBackColor = false;
            // 
            // numericTimeOffset
            // 
            numericTimeOffset.BackColor = Color.FromArgb(55, 58, 65);
            numericTimeOffset.DecimalPlaces = 3;
            numericTimeOffset.ForeColor = Color.White;
            numericTimeOffset.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            numericTimeOffset.Location = new Point(6, 84);
            numericTimeOffset.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            numericTimeOffset.Minimum = new decimal(new int[] { 10, 0, 0, int.MinValue });
            numericTimeOffset.MinimumSize = new Size(36, 19);
            numericTimeOffset.Name = "numericTimeOffset";
            numericTimeOffset.Size = new Size(94, 24);
            numericTimeOffset.TabIndex = 14;
            numericTimeOffset.TextAlign = HorizontalAlignment.Right;
            numericTimeOffset.ThousandsSeparator = false;
            numericTimeOffset.Value = new decimal(new int[] { 0, 0, 0, 0 });
            // 
            // labelTimeOffset
            // 
            labelTimeOffset.AutoSize = true;
            labelTimeOffset.ForeColor = Color.FromArgb(185, 190, 200);
            labelTimeOffset.Location = new Point(6, 64);
            labelTimeOffset.Name = "labelTimeOffset";
            labelTimeOffset.Size = new Size(69, 15);
            labelTimeOffset.TabIndex = 15;
            labelTimeOffset.Text = "Time Offset";
            // 
            // checkBoxInvPhase
            // 
            checkBoxInvPhase.AutoSize = true;
            checkBoxInvPhase.ForeColor = Color.FromArgb(235, 237, 240);
            checkBoxInvPhase.Location = new Point(117, 87);
            checkBoxInvPhase.Name = "checkBoxInvPhase";
            checkBoxInvPhase.Size = new Size(79, 19);
            checkBoxInvPhase.TabIndex = 16;
            checkBoxInvPhase.Text = "Inv. Phase";
            // 
            // panel1
            // 
            panel1.BorderStyle = BorderStyle.FixedSingle;
            panel1.Controls.Add(curveBLabel);
            panel1.Controls.Add(checkBoxInvPhase);
            panel1.Controls.Add(sourceBComboBox);
            panel1.Controls.Add(labelTimeOffset);
            panel1.Controls.Add(numericTimeOffset);
            panel1.Location = new Point(229, 77);
            panel1.Name = "panel1";
            panel1.Size = new Size(204, 116);
            panel1.TabIndex = 17;
            // 
            // OverlayOperationSettingsDialog
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(40, 42, 48);
            ClientSize = new Size(440, 534);
            Controls.Add(panel1);
            Controls.Add(nameLabel);
            Controls.Add(nameTextBox);
            Controls.Add(curveALabel);
            Controls.Add(sourceAComboBox);
            Controls.Add(operationLabel);
            Controls.Add(operationComboBox);
            Controls.Add(colorLabel);
            Controls.Add(colorButton);
            Controls.Add(thicknessLabel);
            Controls.Add(thicknessInput);
            Controls.Add(styleLabel);
            Controls.Add(styleComboBox);
            Controls.Add(blendFrequencyLabel);
            Controls.Add(blendFrequencyInput);
            Controls.Add(blendWidthLabel);
            Controls.Add(blendWidthInput);
            Controls.Add(smoothingLabel);
            Controls.Add(smoothingComboBox);
            Controls.Add(amplitudeSpaceCheckBox);
            Controls.Add(opacityLabel);
            Controls.Add(opacityTrackBar);
            Controls.Add(opacityValueLabel);
            Controls.Add(cancelButton);
            Controls.Add(saveButton);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.FromArgb(235, 237, 240);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "OverlayOperationSettingsDialog";
            Padding = new Padding(20);
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Calculated overlay settings";
            (thicknessInput).EndInit();
            (blendFrequencyInput).EndInit();
            ((System.ComponentModel.ISupportInitialize)opacityTrackBar).EndInit();
            (numericTimeOffset).EndInit();
            panel1.ResumeLayout(false);
            panel1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label nameLabel;
        private TextBox nameTextBox;
        private Label curveALabel;
        private Label curveBLabel;
        private DarkComboBox sourceAComboBox;
        private DarkComboBox sourceBComboBox;
        private Label operationLabel;
        private DarkComboBox operationComboBox;
        private Label colorLabel;
        private Button colorButton;
        private Label thicknessLabel;
        private DarkNumericUpDown thicknessInput;
        private Label styleLabel;
        private DarkComboBox styleComboBox;
        private Label blendFrequencyLabel;
        private DarkNumericUpDown blendFrequencyInput;
        private Label blendWidthLabel;
        private DarkComboBox blendWidthInput;
        private Label smoothingLabel;
        private DarkComboBox smoothingComboBox;
        private CheckBox amplitudeSpaceCheckBox;
        private Label opacityLabel;
        private TrackBar opacityTrackBar;
        private Label opacityValueLabel;
        private Button cancelButton;
        private Button saveButton;
        private ToolTip toolTip;
        private DarkNumericUpDown numericTimeOffset;
        private Label labelTimeOffset;
        private CheckBox checkBoxInvPhase;
        private Panel panel1;
    }
}
