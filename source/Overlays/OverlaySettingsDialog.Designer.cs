namespace Resonalyze
{
    partial class OverlaySettingsDialog
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
            titleLabel = new Label();
            nameLabel = new Label();
            nameTextBox = new TextBox();
            colorLabel = new Label();
            colorButton = new Button();
            thicknessLabel = new Label();
            thicknessInput = new DarkNumericUpDown();
            styleLabel = new Label();
            styleComboBox = new DarkComboBox();
            smoothingLabel = new Label();
            smoothingComboBox = new DarkComboBox();
            opacityLabel = new Label();
            opacityTrackBar = new TrackBar();
            opacityValueLabel = new Label();
            clearButton = new Button();
            cancelButton = new Button();
            saveButton = new Button();
            toolTip = new ToolTip(components);
            (thicknessInput).BeginInit();
            (opacityTrackBar).BeginInit();
            SuspendLayout();
            //
            // titleLabel
            //
            titleLabel.AutoSize = true;
            titleLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            titleLabel.ForeColor = Color.FromArgb(235, 237, 240);
            titleLabel.Location = new Point(20, 18);
            titleLabel.Name = "titleLabel";
            titleLabel.Text = "Overlay";
            //
            // nameLabel
            //
            nameLabel.AutoSize = true;
            nameLabel.ForeColor = Color.FromArgb(185, 190, 200);
            nameLabel.Location = new Point(20, 52);
            nameLabel.Name = "nameLabel";
            nameLabel.Text = "Name";
            //
            // nameTextBox
            //
            nameTextBox.BackColor = Color.FromArgb(55, 58, 65);
            nameTextBox.ForeColor = Color.White;
            nameTextBox.Location = new Point(20, 72);
            nameTextBox.MaxLength = 80;
            nameTextBox.Name = "nameTextBox";
            nameTextBox.Size = new Size(400, 24);
            nameTextBox.TabIndex = 0;
            //
            // colorLabel
            //
            colorLabel.AutoSize = true;
            colorLabel.ForeColor = Color.FromArgb(185, 190, 200);
            colorLabel.Location = new Point(20, 112);
            colorLabel.Name = "colorLabel";
            colorLabel.Text = "Color";
            //
            // colorButton
            //
            colorButton.BackColor = Color.FromArgb(62, 65, 73);
            colorButton.FlatStyle = FlatStyle.Flat;
            colorButton.ForeColor = Color.White;
            colorButton.Location = new Point(20, 132);
            colorButton.Name = "colorButton";
            colorButton.Size = new Size(122, 24);
            colorButton.TabIndex = 1;
            colorButton.UseVisualStyleBackColor = false;
            colorButton.FlatAppearance.BorderSize = 0;
            //
            // thicknessLabel
            //
            thicknessLabel.AutoSize = true;
            thicknessLabel.ForeColor = Color.FromArgb(185, 190, 200);
            thicknessLabel.Location = new Point(162, 112);
            thicknessLabel.Name = "thicknessLabel";
            thicknessLabel.Text = "Thickness";
            //
            // thicknessInput
            //
            thicknessInput.BackColor = Color.FromArgb(55, 58, 65);
            thicknessInput.DecimalPlaces = 1;
            thicknessInput.ForeColor = Color.White;
            thicknessInput.Increment = 0.5m;
            thicknessInput.Location = new Point(162, 132);
            thicknessInput.Maximum = 10m;
            thicknessInput.Minimum = 0.5m;
            thicknessInput.Name = "thicknessInput";
            thicknessInput.Size = new Size(90, 24);
            thicknessInput.TabIndex = 2;
            //
            // styleLabel
            //
            styleLabel.AutoSize = true;
            styleLabel.ForeColor = Color.FromArgb(185, 190, 200);
            styleLabel.Location = new Point(272, 112);
            styleLabel.Name = "styleLabel";
            styleLabel.Text = "Style";
            //
            // styleComboBox
            //
            styleComboBox.BackColor = Color.FromArgb(55, 58, 65);
            styleComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            styleComboBox.ForeColor = Color.White;
            styleComboBox.Location = new Point(272, 132);
            styleComboBox.Name = "styleComboBox";
            styleComboBox.Size = new Size(148, 24);
            styleComboBox.TabIndex = 3;
            //
            // smoothingLabel
            //
            smoothingLabel.AutoSize = true;
            smoothingLabel.ForeColor = Color.FromArgb(185, 190, 200);
            smoothingLabel.Location = new Point(20, 178);
            smoothingLabel.Name = "smoothingLabel";
            smoothingLabel.Text = "Smoothing";
            //
            // smoothingComboBox
            //
            smoothingComboBox.BackColor = Color.FromArgb(55, 58, 65);
            smoothingComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            smoothingComboBox.ForeColor = Color.White;
            smoothingComboBox.FormattingEnabled = true;
            smoothingComboBox.Location = new Point(20, 198);
            smoothingComboBox.Name = "smoothingComboBox";
            smoothingComboBox.Size = new Size(400, 24);
            smoothingComboBox.TabIndex = 4;
            //
            // opacityLabel
            //
            opacityLabel.AutoSize = true;
            opacityLabel.ForeColor = Color.FromArgb(185, 190, 200);
            opacityLabel.Location = new Point(20, 238);
            opacityLabel.Name = "opacityLabel";
            opacityLabel.Text = "Opacity";
            //
            // opacityTrackBar
            //
            opacityTrackBar.Location = new Point(14, 258);
            opacityTrackBar.Maximum = 100;
            opacityTrackBar.Minimum = 10;
            opacityTrackBar.Name = "opacityTrackBar";
            opacityTrackBar.Size = new Size(340, 40);
            opacityTrackBar.TabIndex = 5;
            opacityTrackBar.TickFrequency = 10;
            opacityTrackBar.Value = 100;
            //
            // opacityValueLabel
            //
            opacityValueLabel.AutoSize = true;
            opacityValueLabel.ForeColor = Color.FromArgb(235, 237, 240);
            opacityValueLabel.Location = new Point(370, 265);
            opacityValueLabel.Name = "opacityValueLabel";
            opacityValueLabel.Text = "100%";
            //
            // clearButton
            //
            clearButton.BackColor = Color.FromArgb(62, 65, 73);
            clearButton.DialogResult = DialogResult.OK;
            clearButton.FlatStyle = FlatStyle.Flat;
            clearButton.ForeColor = Color.White;
            clearButton.Location = new Point(20, 315);
            clearButton.Name = "clearButton";
            clearButton.Size = new Size(94, 30);
            clearButton.TabIndex = 6;
            clearButton.Text = "Clear";
            clearButton.UseVisualStyleBackColor = false;
            clearButton.FlatAppearance.BorderSize = 0;
            //
            // cancelButton
            //
            cancelButton.BackColor = Color.FromArgb(62, 65, 73);
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.FlatStyle = FlatStyle.Flat;
            cancelButton.ForeColor = Color.White;
            cancelButton.Location = new Point(226, 315);
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new Size(94, 30);
            cancelButton.TabIndex = 7;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = false;
            cancelButton.FlatAppearance.BorderSize = 0;
            //
            // saveButton
            //
            saveButton.BackColor = Color.FromArgb(64, 116, 255);
            saveButton.DialogResult = DialogResult.OK;
            saveButton.FlatStyle = FlatStyle.Flat;
            saveButton.ForeColor = Color.White;
            saveButton.Location = new Point(326, 315);
            saveButton.Name = "saveButton";
            saveButton.Size = new Size(94, 30);
            saveButton.TabIndex = 8;
            saveButton.Text = "Save";
            saveButton.UseVisualStyleBackColor = false;
            saveButton.FlatAppearance.BorderSize = 0;
            //
            // OverlaySettingsDialog
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(40, 42, 48);
            ClientSize = new Size(440, 360);
            Controls.Add(titleLabel);
            Controls.Add(nameLabel);
            Controls.Add(nameTextBox);
            Controls.Add(colorLabel);
            Controls.Add(colorButton);
            Controls.Add(thicknessLabel);
            Controls.Add(thicknessInput);
            Controls.Add(styleLabel);
            Controls.Add(styleComboBox);
            Controls.Add(smoothingLabel);
            Controls.Add(smoothingComboBox);
            Controls.Add(opacityLabel);
            Controls.Add(opacityTrackBar);
            Controls.Add(opacityValueLabel);
            Controls.Add(clearButton);
            Controls.Add(cancelButton);
            Controls.Add(saveButton);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.FromArgb(235, 237, 240);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "OverlaySettingsDialog";
            Padding = new Padding(20);
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Overlay settings";
            (thicknessInput).EndInit();
            (opacityTrackBar).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label titleLabel;
        private Label nameLabel;
        private TextBox nameTextBox;
        private Label colorLabel;
        private Button colorButton;
        private Label thicknessLabel;
        private DarkNumericUpDown thicknessInput;
        private Label styleLabel;
        private DarkComboBox styleComboBox;
        private Label smoothingLabel;
        private DarkComboBox smoothingComboBox;
        private Label opacityLabel;
        private TrackBar opacityTrackBar;
        private Label opacityValueLabel;
        private Button clearButton;
        private Button cancelButton;
        private Button saveButton;
        private ToolTip toolTip;
    }
}
