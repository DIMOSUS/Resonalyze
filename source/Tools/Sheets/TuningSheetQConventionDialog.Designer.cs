namespace Resonalyze
{
    partial class TuningSheetQConventionDialog
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
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
            radioRbj = new RadioButton();
            radioSymmetric = new RadioButton();
            radioClassic = new RadioButton();
            textCheatSheet = new TextBox();
            labelHint = new Label();
            buttonExport = new Button();
            buttonCancel = new Button();
            SuspendLayout();
            //
            // labelCaption
            //
            labelCaption.AutoSize = true;
            labelCaption.ForeColor = Color.FromArgb(210, 214, 222);
            labelCaption.Location = new Point(12, 12);
            labelCaption.MaximumSize = new Size(496, 0);
            labelCaption.Name = "labelCaption";
            labelCaption.Size = new Size(496, 30);
            labelCaption.TabIndex = 0;
            labelCaption.Text = "How does the target DSP read the Q of a PEQ band? " +
                "The sheet's Q column is computed for that convention.";
            //
            // radioRbj
            //
            radioRbj.AutoSize = true;
            radioRbj.FlatStyle = FlatStyle.Flat;
            radioRbj.ForeColor = Color.White;
            radioRbj.Location = new Point(16, 54);
            radioRbj.Name = "radioRbj";
            radioRbj.Size = new Size(200, 19);
            radioRbj.TabIndex = 1;
            radioRbj.UseVisualStyleBackColor = true;
            //
            // radioSymmetric
            //
            radioSymmetric.AutoSize = true;
            radioSymmetric.FlatStyle = FlatStyle.Flat;
            radioSymmetric.ForeColor = Color.White;
            radioSymmetric.Location = new Point(16, 82);
            radioSymmetric.Name = "radioSymmetric";
            radioSymmetric.Size = new Size(200, 19);
            radioSymmetric.TabIndex = 2;
            radioSymmetric.UseVisualStyleBackColor = true;
            //
            // radioClassic
            //
            radioClassic.AutoSize = true;
            radioClassic.FlatStyle = FlatStyle.Flat;
            radioClassic.ForeColor = Color.White;
            radioClassic.Location = new Point(16, 110);
            radioClassic.Name = "radioClassic";
            radioClassic.Size = new Size(200, 19);
            radioClassic.TabIndex = 3;
            radioClassic.UseVisualStyleBackColor = true;
            //
            // textCheatSheet
            //
            textCheatSheet.BackColor = Color.FromArgb(32, 36, 46);
            textCheatSheet.BorderStyle = BorderStyle.FixedSingle;
            textCheatSheet.ForeColor = Color.FromArgb(210, 214, 222);
            textCheatSheet.Location = new Point(12, 142);
            textCheatSheet.Multiline = true;
            textCheatSheet.Name = "textCheatSheet";
            textCheatSheet.ReadOnly = true;
            textCheatSheet.ScrollBars = ScrollBars.Vertical;
            textCheatSheet.Size = new Size(496, 126);
            textCheatSheet.TabIndex = 4;
            textCheatSheet.TabStop = false;
            //
            // labelHint
            //
            labelHint.AutoSize = true;
            labelHint.ForeColor = Color.FromArgb(150, 156, 168);
            labelHint.Location = new Point(12, 278);
            labelHint.MaximumSize = new Size(496, 0);
            labelHint.Name = "labelHint";
            labelHint.Size = new Size(496, 45);
            labelHint.TabIndex = 5;
            labelHint.Text = "Only the printed Q numbers change — the filters themselves stay " +
                "as they are, and the sheet names the convention it was written for. " +
                "Processor not listed? Measure it: set one band to Fc 1 kHz and Q 4, at " +
                "+12 dB and again at −12 dB, and compare the two bandwidths.";
            //
            // buttonExport
            //
            buttonExport.BackColor = Color.FromArgb(46, 51, 67);
            buttonExport.DialogResult = DialogResult.OK;
            buttonExport.FlatStyle = FlatStyle.Popup;
            buttonExport.ForeColor = Color.White;
            buttonExport.Location = new Point(332, 332);
            buttonExport.Name = "buttonExport";
            buttonExport.Size = new Size(84, 26);
            buttonExport.TabIndex = 6;
            buttonExport.Text = "Export";
            buttonExport.UseVisualStyleBackColor = false;
            //
            // buttonCancel
            //
            buttonCancel.DialogResult = DialogResult.Cancel;
            buttonCancel.FlatStyle = FlatStyle.Popup;
            buttonCancel.ForeColor = Color.White;
            buttonCancel.Location = new Point(424, 332);
            buttonCancel.Name = "buttonCancel";
            buttonCancel.Size = new Size(84, 26);
            buttonCancel.TabIndex = 7;
            buttonCancel.Text = "Cancel";
            buttonCancel.UseVisualStyleBackColor = true;
            //
            // TuningSheetQConventionDialog
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(40, 44, 54);
            ClientSize = new Size(520, 370);
            Controls.Add(labelCaption);
            Controls.Add(radioRbj);
            Controls.Add(radioSymmetric);
            Controls.Add(radioClassic);
            Controls.Add(textCheatSheet);
            Controls.Add(labelHint);
            Controls.Add(buttonExport);
            Controls.Add(buttonCancel);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "TuningSheetQConventionDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "PEQ Q convention";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label labelCaption;
        private RadioButton radioRbj;
        private RadioButton radioSymmetric;
        private RadioButton radioClassic;
        private TextBox textCheatSheet;
        private Label labelHint;
        private Button buttonExport;
        private Button buttonCancel;
    }
}
