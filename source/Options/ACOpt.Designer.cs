namespace Resonalyze.Options
{
    partial class ACOpt
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
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            labelCurves = new Label();
            checkBoxShowAutocorrelation = new CheckBox();
            SuspendLayout();
            //
            // labelCurves
            //
            labelCurves.AutoSize = true;
            labelCurves.ForeColor = Color.FromArgb(150, 170, 205);
            labelCurves.Location = new Point(12, 14);
            labelCurves.Name = "labelCurves";
            labelCurves.Size = new Size(48, 15);
            labelCurves.TabIndex = 0;
            labelCurves.Text = "Curves:";
            //
            // checkBoxShowAutocorrelation
            //
            checkBoxShowAutocorrelation.AutoSize = true;
            checkBoxShowAutocorrelation.ForeColor = SystemColors.ControlLight;
            checkBoxShowAutocorrelation.Location = new Point(12, 36);
            checkBoxShowAutocorrelation.Name = "checkBoxShowAutocorrelation";
            checkBoxShowAutocorrelation.Size = new Size(149, 19);
            checkBoxShowAutocorrelation.TabIndex = 1;
            checkBoxShowAutocorrelation.Text = "Show autocorrelation";
            checkBoxShowAutocorrelation.UseVisualStyleBackColor = true;
            //
            // ACOpt
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
            ClientSize = new Size(265, 66);
            Controls.Add(checkBoxShowAutocorrelation);
            Controls.Add(labelCurves);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "ACOpt";
            ShowInTaskbar = false;
            Text = "Autocorrelation Options";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label labelCurves;
        private CheckBox checkBoxShowAutocorrelation;
    }
}
