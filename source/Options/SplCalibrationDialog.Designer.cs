namespace Resonalyze.Options
{
    partial class SplCalibrationDialog
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
            labelInstruction = new Label();
            labelReference = new Label();
            comboBoxReference = new DarkComboBox();
            labelGainWarning = new Label();
            buttonStart = new Button();
            progressBar = new ProgressBar();
            labelStatus = new Label();
            buttonSave = new Button();
            buttonCancel = new Button();
            SuspendLayout();
            //
            // labelInstruction
            //
            labelInstruction.ForeColor = SystemColors.ControlLight;
            labelInstruction.Location = new Point(16, 16);
            labelInstruction.Name = "labelInstruction";
            labelInstruction.Size = new Size(398, 36);
            labelInstruction.TabIndex = 0;
            labelInstruction.Text = "Fit the acoustic calibrator over the microphone capsule and select the reference level it is set to.";
            //
            // labelReference
            //
            labelReference.AutoSize = true;
            labelReference.ForeColor = SystemColors.ControlLight;
            labelReference.Location = new Point(16, 64);
            labelReference.Name = "labelReference";
            labelReference.Size = new Size(89, 15);
            labelReference.TabIndex = 1;
            labelReference.Text = "Reference level";
            //
            // comboBoxReference
            //
            comboBoxReference.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxReference.ForeColor = Color.White;
            comboBoxReference.Location = new Point(140, 60);
            comboBoxReference.Margin = new Padding(0);
            comboBoxReference.MinimumSize = new Size(36, 19);
            comboBoxReference.Name = "comboBoxReference";
            comboBoxReference.Size = new Size(130, 23);
            comboBoxReference.TabIndex = 2;
            //
            // labelGainWarning
            //
            labelGainWarning.ForeColor = Color.Gold;
            labelGainWarning.Location = new Point(16, 92);
            labelGainWarning.Name = "labelGainWarning";
            labelGainWarning.Size = new Size(398, 48);
            labelGainWarning.TabIndex = 3;
            labelGainWarning.Text = "⚠ Calibrate at the input gain you measure with, then leave the preamp untouched. A moved gain knob silently invalidates the SPL scale — software cannot detect it.";
            //
            // buttonStart
            //
            buttonStart.FlatStyle = FlatStyle.Popup;
            buttonStart.ForeColor = Color.White;
            buttonStart.Location = new Point(16, 150);
            buttonStart.Name = "buttonStart";
            buttonStart.Size = new Size(398, 28);
            buttonStart.TabIndex = 4;
            buttonStart.Text = "Start calibration";
            buttonStart.UseVisualStyleBackColor = true;
            buttonStart.Click += buttonStart_Click;
            //
            // progressBar
            //
            progressBar.Location = new Point(16, 186);
            progressBar.Name = "progressBar";
            progressBar.Size = new Size(398, 8);
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.TabIndex = 5;
            progressBar.Visible = false;
            //
            // labelStatus
            //
            labelStatus.ForeColor = SystemColors.ControlLight;
            labelStatus.Location = new Point(16, 204);
            labelStatus.Name = "labelStatus";
            labelStatus.Size = new Size(398, 64);
            labelStatus.TabIndex = 6;
            labelStatus.Text = "Idle.";
            //
            // buttonSave
            //
            buttonSave.DialogResult = DialogResult.OK;
            buttonSave.Enabled = false;
            buttonSave.FlatStyle = FlatStyle.Popup;
            buttonSave.ForeColor = Color.White;
            buttonSave.Location = new Point(204, 282);
            buttonSave.Name = "buttonSave";
            buttonSave.Size = new Size(130, 30);
            buttonSave.TabIndex = 7;
            buttonSave.Text = "Save calibration";
            buttonSave.UseVisualStyleBackColor = true;
            //
            // buttonCancel
            //
            buttonCancel.DialogResult = DialogResult.Cancel;
            buttonCancel.FlatStyle = FlatStyle.Popup;
            buttonCancel.ForeColor = Color.White;
            buttonCancel.Location = new Point(338, 282);
            buttonCancel.Name = "buttonCancel";
            buttonCancel.Size = new Size(76, 30);
            buttonCancel.TabIndex = 8;
            buttonCancel.Text = "Cancel";
            buttonCancel.UseVisualStyleBackColor = true;
            //
            // SplCalibrationDialog
            //
            AcceptButton = buttonSave;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
            CancelButton = buttonCancel;
            ClientSize = new Size(430, 328);
            Controls.Add(labelInstruction);
            Controls.Add(labelReference);
            Controls.Add(comboBoxReference);
            Controls.Add(labelGainWarning);
            Controls.Add(buttonStart);
            Controls.Add(progressBar);
            Controls.Add(labelStatus);
            Controls.Add(buttonSave);
            Controls.Add(buttonCancel);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "SplCalibrationDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "SPL calibration";
            FormClosing += SplCalibrationDialog_FormClosing;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label labelInstruction;
        private Label labelReference;
        private DarkComboBox comboBoxReference;
        private Label labelGainWarning;
        private Button buttonStart;
        private ProgressBar progressBar;
        private Label labelStatus;
        private Button buttonSave;
        private Button buttonCancel;
    }
}
