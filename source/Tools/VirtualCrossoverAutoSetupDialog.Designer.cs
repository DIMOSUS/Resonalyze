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
            labelFilters = new Label();
            checkButterworth = new CheckBox();
            checkLinkwitzRiley = new CheckBox();
            checkBessel = new CheckBox();
            labelRange = new Label();
            minCrossover = new DarkNumericUpDown();
            labelDash = new Label();
            maxCrossover = new DarkNumericUpDown();
            labelHz = new Label();
            independentSlopes = new CheckBox();
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
            // labelFilters
            //
            labelFilters.AutoSize = true;
            labelFilters.ForeColor = Color.FromArgb(185, 190, 200);
            labelFilters.Location = new Point(12, 128);
            labelFilters.Name = "labelFilters";
            labelFilters.Size = new Size(42, 15);
            labelFilters.TabIndex = 10;
            labelFilters.Text = "Filters:";
            //
            // checkButterworth
            //
            checkButterworth.AutoSize = true;
            checkButterworth.Checked = true;
            checkButterworth.CheckState = CheckState.Checked;
            checkButterworth.ForeColor = Color.White;
            checkButterworth.Location = new Point(66, 126);
            checkButterworth.Name = "checkButterworth";
            checkButterworth.Size = new Size(88, 19);
            checkButterworth.TabIndex = 11;
            checkButterworth.Text = "Butterworth";
            //
            // checkLinkwitzRiley
            //
            checkLinkwitzRiley.AutoSize = true;
            checkLinkwitzRiley.Checked = true;
            checkLinkwitzRiley.CheckState = CheckState.Checked;
            checkLinkwitzRiley.ForeColor = Color.White;
            checkLinkwitzRiley.Location = new Point(168, 126);
            checkLinkwitzRiley.Name = "checkLinkwitzRiley";
            checkLinkwitzRiley.Size = new Size(104, 19);
            checkLinkwitzRiley.TabIndex = 12;
            checkLinkwitzRiley.Text = "Linkwitz-Riley";
            //
            // checkBessel
            //
            checkBessel.AutoSize = true;
            checkBessel.Checked = true;
            checkBessel.CheckState = CheckState.Checked;
            checkBessel.ForeColor = Color.White;
            checkBessel.Location = new Point(288, 126);
            checkBessel.Name = "checkBessel";
            checkBessel.Size = new Size(58, 19);
            checkBessel.TabIndex = 13;
            checkBessel.Text = "Bessel";
            //
            // labelRange
            //
            labelRange.AutoSize = true;
            labelRange.ForeColor = Color.FromArgb(185, 190, 200);
            labelRange.Location = new Point(12, 158);
            labelRange.Name = "labelRange";
            labelRange.Size = new Size(96, 15);
            labelRange.TabIndex = 14;
            labelRange.Text = "Crossover range:";
            //
            // minCrossover
            //
            minCrossover.Location = new Point(120, 154);
            minCrossover.Minimum = 20m;
            minCrossover.Maximum = 20000m;
            minCrossover.Increment = 10m;
            minCrossover.DecimalPlaces = 0;
            minCrossover.MinimumSize = new Size(36, 19);
            minCrossover.Name = "minCrossover";
            minCrossover.Size = new Size(72, 21);
            minCrossover.TabIndex = 15;
            minCrossover.Value = 20m;
            //
            // labelDash
            //
            labelDash.AutoSize = true;
            labelDash.ForeColor = Color.FromArgb(185, 190, 200);
            labelDash.Location = new Point(200, 158);
            labelDash.Name = "labelDash";
            labelDash.Size = new Size(12, 15);
            labelDash.TabIndex = 16;
            labelDash.Text = "–";
            //
            // maxCrossover
            //
            maxCrossover.Location = new Point(232, 154);
            maxCrossover.Minimum = 20m;
            maxCrossover.Maximum = 20000m;
            maxCrossover.Increment = 100m;
            maxCrossover.DecimalPlaces = 0;
            maxCrossover.MinimumSize = new Size(36, 19);
            maxCrossover.Name = "maxCrossover";
            maxCrossover.Size = new Size(72, 21);
            maxCrossover.TabIndex = 17;
            maxCrossover.Value = 20000m;
            //
            // labelHz
            //
            labelHz.AutoSize = true;
            labelHz.ForeColor = Color.FromArgb(185, 190, 200);
            labelHz.Location = new Point(312, 158);
            labelHz.Name = "labelHz";
            labelHz.Size = new Size(20, 15);
            labelHz.TabIndex = 18;
            labelHz.Text = "Hz";
            //
            // independentSlopes
            //
            independentSlopes.AutoSize = true;
            independentSlopes.ForeColor = Color.White;
            independentSlopes.Location = new Point(12, 184);
            independentSlopes.Name = "independentSlopes";
            independentSlopes.Size = new Size(172, 19);
            independentSlopes.TabIndex = 19;
            independentSlopes.Text = "Independent slopes per side";
            //
            // labelPreview
            //
            labelPreview.ForeColor = Color.FromArgb(230, 184, 0);
            labelPreview.Location = new Point(12, 214);
            labelPreview.Name = "labelPreview";
            labelPreview.Size = new Size(444, 62);
            labelPreview.TabIndex = 20;
            labelPreview.Text = "—";
            //
            // buttonApply
            //
            buttonApply.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttonApply.BackColor = Color.FromArgb(46, 51, 67);
            buttonApply.DialogResult = DialogResult.OK;
            buttonApply.FlatStyle = FlatStyle.Popup;
            buttonApply.ForeColor = Color.White;
            buttonApply.Location = new Point(282, 284);
            buttonApply.Name = "buttonApply";
            buttonApply.Size = new Size(84, 26);
            buttonApply.TabIndex = 21;
            buttonApply.Text = "Apply";
            buttonApply.UseVisualStyleBackColor = false;
            //
            // buttonCancel
            //
            buttonCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttonCancel.DialogResult = DialogResult.Cancel;
            buttonCancel.FlatStyle = FlatStyle.Popup;
            buttonCancel.ForeColor = Color.White;
            buttonCancel.Location = new Point(372, 284);
            buttonCancel.Name = "buttonCancel";
            buttonCancel.Size = new Size(84, 26);
            buttonCancel.TabIndex = 22;
            buttonCancel.Text = "Cancel";
            buttonCancel.UseVisualStyleBackColor = true;
            //
            // VirtualCrossoverAutoSetupDialog
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(40, 44, 54);
            ClientSize = new Size(468, 322);
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
            Controls.Add(labelFilters);
            Controls.Add(checkButterworth);
            Controls.Add(checkLinkwitzRiley);
            Controls.Add(checkBessel);
            Controls.Add(labelRange);
            Controls.Add(minCrossover);
            Controls.Add(labelDash);
            Controls.Add(maxCrossover);
            Controls.Add(labelHz);
            Controls.Add(independentSlopes);
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
        private Label labelFilters;
        private CheckBox checkButterworth;
        private CheckBox checkLinkwitzRiley;
        private CheckBox checkBessel;
        private Label labelRange;
        private DarkNumericUpDown minCrossover;
        private Label labelDash;
        private DarkNumericUpDown maxCrossover;
        private Label labelHz;
        private CheckBox independentSlopes;
        private Label labelPreview;
        private Button buttonApply;
        private Button buttonCancel;
    }
}
