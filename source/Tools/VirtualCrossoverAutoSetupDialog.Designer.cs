namespace Resonalyze
{
    partial class VirtualCrossoverAutoSetupDialog
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
                toolTip.Dispose();
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
            labelHeader = new Label();
            labelName1 = new Label();
            labelBand1 = new Label();
            comboType1 = new DarkComboBox();
            labelName2 = new Label();
            labelBand2 = new Label();
            comboType2 = new DarkComboBox();
            labelName3 = new Label();
            labelBand3 = new Label();
            comboType3 = new DarkComboBox();
            labelPreview = new Label();
            buttonApply = new Button();
            buttonCancel = new Button();
            SuspendLayout();
            //
            // labelHeader
            //
            labelHeader.AutoSize = true;
            labelHeader.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelHeader.ForeColor = Color.FromArgb(210, 214, 222);
            labelHeader.Location = new Point(12, 12);
            labelHeader.Name = "labelHeader";
            labelHeader.Size = new Size(320, 15);
            labelHeader.TabIndex = 0;
            labelHeader.Text = "Confirm the detected driver types (usable band shown):";
            //
            // labelName1
            //
            labelName1.AutoEllipsis = true;
            labelName1.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelName1.Location = new Point(12, 42);
            labelName1.Name = "labelName1";
            labelName1.Size = new Size(190, 15);
            labelName1.TabIndex = 1;
            labelName1.Text = "A";
            //
            // labelBand1
            //
            labelBand1.AutoSize = true;
            labelBand1.ForeColor = Color.FromArgb(170, 176, 190);
            labelBand1.Location = new Point(210, 42);
            labelBand1.Name = "labelBand1";
            labelBand1.Size = new Size(90, 15);
            labelBand1.TabIndex = 2;
            labelBand1.Text = "—";
            //
            // comboType1
            //
            comboType1.BackColor = Color.FromArgb(55, 60, 72);
            comboType1.ForeColor = Color.White;
            comboType1.Location = new Point(346, 40);
            comboType1.MinimumSize = new Size(36, 19);
            comboType1.Name = "comboType1";
            comboType1.Size = new Size(110, 19);
            comboType1.TabIndex = 3;
            //
            // labelName2
            //
            labelName2.AutoEllipsis = true;
            labelName2.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelName2.Location = new Point(12, 70);
            labelName2.Name = "labelName2";
            labelName2.Size = new Size(190, 15);
            labelName2.TabIndex = 4;
            labelName2.Text = "B";
            //
            // labelBand2
            //
            labelBand2.AutoSize = true;
            labelBand2.ForeColor = Color.FromArgb(170, 176, 190);
            labelBand2.Location = new Point(210, 70);
            labelBand2.Name = "labelBand2";
            labelBand2.Size = new Size(90, 15);
            labelBand2.TabIndex = 5;
            labelBand2.Text = "—";
            //
            // comboType2
            //
            comboType2.BackColor = Color.FromArgb(55, 60, 72);
            comboType2.ForeColor = Color.White;
            comboType2.Location = new Point(346, 68);
            comboType2.MinimumSize = new Size(36, 19);
            comboType2.Name = "comboType2";
            comboType2.Size = new Size(110, 19);
            comboType2.TabIndex = 6;
            //
            // labelName3
            //
            labelName3.AutoEllipsis = true;
            labelName3.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelName3.Location = new Point(12, 98);
            labelName3.Name = "labelName3";
            labelName3.Size = new Size(190, 15);
            labelName3.TabIndex = 7;
            labelName3.Text = "C";
            //
            // labelBand3
            //
            labelBand3.AutoSize = true;
            labelBand3.ForeColor = Color.FromArgb(170, 176, 190);
            labelBand3.Location = new Point(210, 98);
            labelBand3.Name = "labelBand3";
            labelBand3.Size = new Size(90, 15);
            labelBand3.TabIndex = 8;
            labelBand3.Text = "—";
            //
            // comboType3
            //
            comboType3.BackColor = Color.FromArgb(55, 60, 72);
            comboType3.ForeColor = Color.White;
            comboType3.Location = new Point(346, 96);
            comboType3.MinimumSize = new Size(36, 19);
            comboType3.Name = "comboType3";
            comboType3.Size = new Size(110, 19);
            comboType3.TabIndex = 9;
            //
            // labelPreview
            //
            labelPreview.ForeColor = Color.FromArgb(230, 184, 0);
            labelPreview.Location = new Point(12, 130);
            labelPreview.Name = "labelPreview";
            labelPreview.Size = new Size(444, 60);
            labelPreview.TabIndex = 10;
            labelPreview.Text = "—";
            //
            // buttonApply
            //
            buttonApply.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttonApply.BackColor = Color.FromArgb(46, 51, 67);
            buttonApply.DialogResult = DialogResult.OK;
            buttonApply.FlatStyle = FlatStyle.Popup;
            buttonApply.ForeColor = Color.White;
            buttonApply.Location = new Point(282, 200);
            buttonApply.Name = "buttonApply";
            buttonApply.Size = new Size(84, 26);
            buttonApply.TabIndex = 11;
            buttonApply.Text = "Apply";
            buttonApply.UseVisualStyleBackColor = false;
            //
            // buttonCancel
            //
            buttonCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttonCancel.DialogResult = DialogResult.Cancel;
            buttonCancel.FlatStyle = FlatStyle.Popup;
            buttonCancel.ForeColor = Color.White;
            buttonCancel.Location = new Point(372, 200);
            buttonCancel.Name = "buttonCancel";
            buttonCancel.Size = new Size(84, 26);
            buttonCancel.TabIndex = 12;
            buttonCancel.Text = "Cancel";
            buttonCancel.UseVisualStyleBackColor = true;
            //
            // VirtualCrossoverAutoSetupDialog
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(40, 44, 54);
            ClientSize = new Size(468, 238);
            Controls.Add(labelHeader);
            Controls.Add(labelName1);
            Controls.Add(labelBand1);
            Controls.Add(comboType1);
            Controls.Add(labelName2);
            Controls.Add(labelBand2);
            Controls.Add(comboType2);
            Controls.Add(labelName3);
            Controls.Add(labelBand3);
            Controls.Add(comboType3);
            Controls.Add(labelPreview);
            Controls.Add(buttonApply);
            Controls.Add(buttonCancel);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "VirtualCrossoverAutoSetupDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Crossover auto setup";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label labelHeader;
        private Label labelName1;
        private Label labelBand1;
        private DarkComboBox comboType1;
        private Label labelName2;
        private Label labelBand2;
        private DarkComboBox comboType2;
        private Label labelName3;
        private Label labelBand3;
        private DarkComboBox comboType3;
        private Label labelPreview;
        private Button buttonApply;
        private Button buttonCancel;
    }
}
