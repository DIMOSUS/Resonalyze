namespace Resonalyze
{
    partial class ApplicationSettingsDialog
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
            labelAppearance = new Label();
            radioThemeDark = new ReleaseClickRadioButton();
            radioThemeLight = new ReleaseClickRadioButton();
            labelThemeHint = new Label();
            buttonOk = new ReleaseClickButton();
            buttonCancel = new ReleaseClickButton();
            SuspendLayout();
            //
            // labelAppearance
            //
            labelAppearance.AutoSize = true;
            labelAppearance.ForeColor = UiPalette.TextAccent;
            labelAppearance.Location = new Point(12, 12);
            labelAppearance.Name = "labelAppearance";
            labelAppearance.Size = new Size(120, 15);
            labelAppearance.TabIndex = 0;
            labelAppearance.Text = "Appearance";
            //
            // radioThemeDark
            //
            radioThemeDark.AutoSize = true;
            radioThemeDark.FlatStyle = FlatStyle.Flat;
            radioThemeDark.ForeColor = UiPalette.TextPrimary;
            radioThemeDark.Location = new Point(16, 40);
            radioThemeDark.Name = "radioThemeDark";
            radioThemeDark.Size = new Size(160, 19);
            radioThemeDark.TabIndex = 1;
            radioThemeDark.Text = "Dark theme";
            radioThemeDark.UseVisualStyleBackColor = true;
            //
            // radioThemeLight
            //
            radioThemeLight.AutoSize = true;
            radioThemeLight.FlatStyle = FlatStyle.Flat;
            radioThemeLight.ForeColor = UiPalette.TextPrimary;
            radioThemeLight.Location = new Point(16, 66);
            radioThemeLight.Name = "radioThemeLight";
            radioThemeLight.Size = new Size(160, 19);
            radioThemeLight.TabIndex = 2;
            radioThemeLight.Text = "Light theme";
            radioThemeLight.UseVisualStyleBackColor = true;
            //
            // labelThemeHint
            //
            labelThemeHint.AutoSize = true;
            labelThemeHint.ForeColor = UiPalette.TextMuted;
            labelThemeHint.Location = new Point(12, 98);
            labelThemeHint.MaximumSize = new Size(356, 0);
            labelThemeHint.Name = "labelThemeHint";
            labelThemeHint.Size = new Size(356, 45);
            labelThemeHint.TabIndex = 3;
            labelThemeHint.Text = "The theme colours every panel, dialog and graph, and is applied when " +
                "Resonalyze starts. Curve colours saved in overlay and Virtual DSP files are " +
                "yours and are left alone.";
            //
            // buttonOk
            //
            buttonOk.BackColor = UiPalette.ButtonBackground;
            buttonOk.DialogResult = DialogResult.OK;
            buttonOk.FlatStyle = FlatStyle.Popup;
            buttonOk.ForeColor = UiPalette.TextPrimary;
            buttonOk.Location = new Point(192, 158);
            buttonOk.Name = "buttonOk";
            buttonOk.Size = new Size(84, 26);
            buttonOk.TabIndex = 4;
            buttonOk.Text = "OK";
            buttonOk.UseVisualStyleBackColor = false;
            //
            // buttonCancel
            //
            buttonCancel.DialogResult = DialogResult.Cancel;
            buttonCancel.FlatStyle = FlatStyle.Popup;
            buttonCancel.ForeColor = UiPalette.TextPrimary;
            buttonCancel.Location = new Point(284, 158);
            buttonCancel.Name = "buttonCancel";
            buttonCancel.Size = new Size(84, 26);
            buttonCancel.TabIndex = 5;
            buttonCancel.Text = "Cancel";
            buttonCancel.UseVisualStyleBackColor = true;
            //
            // ApplicationSettingsDialog
            //
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiPalette.ShellSurface;
            ClientSize = new Size(380, 196);
            Controls.Add(labelAppearance);
            Controls.Add(radioThemeDark);
            Controls.Add(radioThemeLight);
            Controls.Add(labelThemeHint);
            Controls.Add(buttonOk);
            Controls.Add(buttonCancel);
            Font = new Font("Segoe UI", 9F);
            ForeColor = UiPalette.TextPrimary;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "ApplicationSettingsDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Settings";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label labelAppearance;
        private ReleaseClickRadioButton radioThemeDark;
        private ReleaseClickRadioButton radioThemeLight;
        private Label labelThemeHint;
        private ReleaseClickButton buttonOk;
        private ReleaseClickButton buttonCancel;
    }
}
