namespace Resonalyze
{
    partial class DspProcessorDialog
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

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            labelCaption = new Label();
            labelModel = new Label();
            comboBoxModel = new ThemedComboBox();
            labelSampleRate = new Label();
            comboBoxSampleRate = new ThemedComboBox();
            labelQConvention = new Label();
            comboBoxQConvention = new ThemedComboBox();
            labelStatus = new Label();
            checkBoxPhaseControl = new ReleaseClickCheckBox();
            checkBoxFirFilters = new ReleaseClickCheckBox();
            labelHint = new Label();
            labelNotes = new Label();
            labelNotesHint = new Label();
            textBoxNotes = new TextBox();
            buttonOk = new ReleaseClickButton();
            buttonCancel = new ReleaseClickButton();
            SuspendLayout();
            //
            // labelCaption
            //
            labelCaption.AutoSize = true;
            labelCaption.ForeColor = UiPalette.TextDefault;
            labelCaption.Location = new Point(12, 12);
            labelCaption.MaximumSize = new Size(456, 0);
            labelCaption.Name = "labelCaption";
            labelCaption.Size = new Size(456, 30);
            labelCaption.TabIndex = 0;
            labelCaption.Text = "The processor this project is designed for. Its rate is what every " +
                "simulated filter is built at — the measurements keep their own.";
            //
            // labelModel
            //
            labelModel.AutoSize = true;
            labelModel.ForeColor = UiPalette.TextSecondary;
            labelModel.Location = new Point(12, 60);
            labelModel.Name = "labelModel";
            labelModel.Size = new Size(43, 15);
            labelModel.TabIndex = 1;
            labelModel.Text = "Model:";
            //
            // comboBoxModel
            //
            comboBoxModel.BackColor = UiPalette.ControlSurface;
            comboBoxModel.ForeColor = UiPalette.TextPrimary;
            comboBoxModel.Location = new Point(148, 56);
            comboBoxModel.MaxDropDownItems = 14;
            comboBoxModel.MinimumSize = new Size(36, 19);
            comboBoxModel.Name = "comboBoxModel";
            comboBoxModel.Size = new Size(320, 23);
            comboBoxModel.TabIndex = 2;
            //
            // labelSampleRate
            //
            labelSampleRate.AutoSize = true;
            labelSampleRate.ForeColor = UiPalette.TextSecondary;
            labelSampleRate.Location = new Point(12, 94);
            labelSampleRate.Name = "labelSampleRate";
            labelSampleRate.Size = new Size(95, 15);
            labelSampleRate.TabIndex = 3;
            labelSampleRate.Text = "Processing rate:";
            //
            // comboBoxSampleRate
            //
            comboBoxSampleRate.BackColor = UiPalette.ControlSurface;
            comboBoxSampleRate.ForeColor = UiPalette.TextPrimary;
            comboBoxSampleRate.Location = new Point(148, 90);
            comboBoxSampleRate.MinimumSize = new Size(36, 19);
            comboBoxSampleRate.Name = "comboBoxSampleRate";
            comboBoxSampleRate.Size = new Size(230, 23);
            comboBoxSampleRate.TabIndex = 4;
            //
            // labelQConvention
            //
            labelQConvention.AutoSize = true;
            labelQConvention.ForeColor = UiPalette.TextSecondary;
            labelQConvention.Location = new Point(12, 128);
            labelQConvention.Name = "labelQConvention";
            labelQConvention.Size = new Size(80, 15);
            labelQConvention.TabIndex = 5;
            labelQConvention.Text = "PEQ Q reads:";
            //
            // comboBoxQConvention
            //
            comboBoxQConvention.BackColor = UiPalette.ControlSurface;
            comboBoxQConvention.ForeColor = UiPalette.TextPrimary;
            comboBoxQConvention.Location = new Point(148, 124);
            comboBoxQConvention.MinimumSize = new Size(36, 19);
            comboBoxQConvention.Name = "comboBoxQConvention";
            comboBoxQConvention.Size = new Size(320, 23);
            comboBoxQConvention.TabIndex = 6;
            //
            // checkBoxPhaseControl
            //
            checkBoxPhaseControl.AutoSize = true;
            checkBoxPhaseControl.ForeColor = UiPalette.TextPrimary;
            checkBoxPhaseControl.Location = new Point(148, 156);
            checkBoxPhaseControl.Name = "checkBoxPhaseControl";
            checkBoxPhaseControl.Size = new Size(320, 19);
            checkBoxPhaseControl.TabIndex = 7;
            checkBoxPhaseControl.Text = "Channel phase control";
            checkBoxPhaseControl.UseVisualStyleBackColor = true;
            //
            // checkBoxFirFilters
            //
            checkBoxFirFilters.AutoSize = true;
            checkBoxFirFilters.ForeColor = UiPalette.TextPrimary;
            checkBoxFirFilters.Location = new Point(148, 178);
            checkBoxFirFilters.Name = "checkBoxFirFilters";
            checkBoxFirFilters.Size = new Size(320, 19);
            checkBoxFirFilters.TabIndex = 8;
            checkBoxFirFilters.Text = "FIR filters";
            checkBoxFirFilters.UseVisualStyleBackColor = true;
            //
            // labelStatus
            //
            labelStatus.AutoSize = true;
            labelStatus.ForeColor = UiPalette.TextSecondary;
            labelStatus.Location = new Point(12, 216);
            labelStatus.MaximumSize = new Size(456, 0);
            labelStatus.Name = "labelStatus";
            labelStatus.Size = new Size(456, 60);
            labelStatus.TabIndex = 7;
            labelStatus.Text = "status";
            //
            // labelHint
            //
            labelHint.AutoSize = true;
            labelHint.ForeColor = UiPalette.TextMuted;
            labelHint.Location = new Point(12, 345);
            labelHint.MaximumSize = new Size(456, 0);
            labelHint.Name = "labelHint";
            labelHint.Size = new Size(456, 30);
            labelHint.TabIndex = 8;
            labelHint.Text = "A PEQ bank handed to the EQ Wizard carries this processor with " +
                "it, and is realized there at its rate.";
            //
            // labelNotes
            //
            labelNotes.AutoSize = true;
            labelNotes.ForeColor = UiPalette.TextSecondary;
            labelNotes.Location = new Point(12, 389);
            labelNotes.Name = "labelNotes";
            labelNotes.Size = new Size(78, 15);
            labelNotes.TabIndex = 9;
            labelNotes.Text = "Notes for AI:";
            //
            // labelNotesHint
            //
            labelNotesHint.AutoSize = true;
            labelNotesHint.ForeColor = UiPalette.TextMuted;
            labelNotesHint.Location = new Point(12, 407);
            labelNotesHint.MaximumSize = new Size(456, 0);
            labelNotesHint.Name = "labelNotesHint";
            labelNotesHint.Size = new Size(456, 45);
            labelNotesHint.TabIndex = 10;
            labelNotesHint.Text = "What an assistant cannot measure: the car and the seat, each " +
                "driver's model and where it sits, amplifier power, the DSP, and what you " +
                "want from the tune. Sent with every Copy for AI.";
            //
            // textBoxNotes
            //
            textBoxNotes.AcceptsReturn = true;
            textBoxNotes.BackColor = UiPalette.SunkenSurface;
            textBoxNotes.BorderStyle = BorderStyle.FixedSingle;
            textBoxNotes.ForeColor = UiPalette.TextDefault;
            textBoxNotes.Location = new Point(12, 459);
            textBoxNotes.MaxLength = MaximumNotesLength;
            textBoxNotes.Multiline = true;
            textBoxNotes.Name = "textBoxNotes";
            textBoxNotes.ScrollBars = ScrollBars.Vertical;
            textBoxNotes.Size = new Size(456, 100);
            textBoxNotes.TabIndex = 11;
            //
            // buttonOk
            //
            buttonOk.BackColor = UiPalette.ButtonBackground;
            buttonOk.DialogResult = DialogResult.OK;
            buttonOk.FlatStyle = FlatStyle.Popup;
            buttonOk.ForeColor = UiPalette.TextPrimary;
            buttonOk.Location = new Point(292, 573);
            buttonOk.Name = "buttonOk";
            buttonOk.Size = new Size(84, 26);
            buttonOk.TabIndex = 12;
            buttonOk.Text = "OK";
            buttonOk.UseVisualStyleBackColor = false;
            //
            // buttonCancel
            //
            buttonCancel.DialogResult = DialogResult.Cancel;
            buttonCancel.FlatStyle = FlatStyle.Popup;
            buttonCancel.ForeColor = UiPalette.TextPrimary;
            buttonCancel.Location = new Point(384, 573);
            buttonCancel.Name = "buttonCancel";
            buttonCancel.Size = new Size(84, 26);
            buttonCancel.TabIndex = 13;
            buttonCancel.Text = "Cancel";
            buttonCancel.UseVisualStyleBackColor = true;
            //
            // DspProcessorDialog
            //
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiPalette.ShellSurface;
            ClientSize = new Size(480, 611);
            Controls.Add(labelCaption);
            Controls.Add(labelModel);
            Controls.Add(comboBoxModel);
            Controls.Add(labelSampleRate);
            Controls.Add(comboBoxSampleRate);
            Controls.Add(labelQConvention);
            Controls.Add(comboBoxQConvention);
            Controls.Add(checkBoxPhaseControl);
            Controls.Add(checkBoxFirFilters);
            Controls.Add(labelStatus);
            Controls.Add(labelHint);
            Controls.Add(labelNotes);
            Controls.Add(labelNotesHint);
            Controls.Add(textBoxNotes);
            Controls.Add(buttonOk);
            Controls.Add(buttonCancel);
            Font = new Font("Segoe UI", 9F);
            ForeColor = UiPalette.TextPrimary;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "DspProcessorDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "DSP processor";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label labelCaption;
        private Label labelModel;
        private ThemedComboBox comboBoxModel;
        private Label labelSampleRate;
        private ThemedComboBox comboBoxSampleRate;
        private Label labelQConvention;
        private ThemedComboBox comboBoxQConvention;
        private Label labelStatus;
        private ReleaseClickCheckBox checkBoxPhaseControl;
        private ReleaseClickCheckBox checkBoxFirFilters;
        private Label labelHint;
        private Label labelNotes;
        private Label labelNotesHint;
        private TextBox textBoxNotes;
        private ReleaseClickButton buttonOk;
        private ReleaseClickButton buttonCancel;
    }
}
