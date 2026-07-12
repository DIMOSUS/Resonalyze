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
            labelSubElevation = new Label();
            subElevation = new DarkNumericUpDown();
            labelSubElevationUnit = new Label();
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
            independentSlopes.Checked = true;
            independentSlopes.CheckState = CheckState.Checked;
            independentSlopes.ForeColor = Color.White;
            independentSlopes.Location = new Point(12, 184);
            independentSlopes.Name = "independentSlopes";
            independentSlopes.Size = new Size(172, 19);
            independentSlopes.TabIndex = 19;
            independentSlopes.Text = "Independent slopes per side";
            //
            // labelSubElevation
            //
            labelSubElevation.AutoSize = true;
            labelSubElevation.ForeColor = Color.FromArgb(185, 190, 200);
            labelSubElevation.Location = new Point(12, 214);
            labelSubElevation.Name = "labelSubElevation";
            labelSubElevation.Size = new Size(160, 15);
            labelSubElevation.TabIndex = 20;
            labelSubElevation.Text = "Sub level over mid/treble:";
            //
            // subElevation
            //
            subElevation.Location = new Point(196, 210);
            subElevation.Minimum = 0m;
            subElevation.Maximum = 60m;
            subElevation.Increment = 1m;
            subElevation.DecimalPlaces = 1;
            subElevation.MinimumSize = new Size(36, 19);
            subElevation.Name = "subElevation";
            subElevation.Size = new Size(72, 21);
            subElevation.TabIndex = 21;
            subElevation.Value = 0m;
            //
            // labelSubElevationUnit
            //
            labelSubElevationUnit.AutoSize = true;
            labelSubElevationUnit.ForeColor = Color.FromArgb(185, 190, 200);
            labelSubElevationUnit.Location = new Point(276, 214);
            labelSubElevationUnit.Name = "labelSubElevationUnit";
            labelSubElevationUnit.Size = new Size(20, 15);
            labelSubElevationUnit.TabIndex = 22;
            labelSubElevationUnit.Text = "dB";
            //
            // labelPreview
            //
            labelPreview.ForeColor = Color.FromArgb(230, 184, 0);
            labelPreview.Location = new Point(12, 240);
            labelPreview.Name = "labelPreview";
            labelPreview.Size = new Size(444, 62);
            labelPreview.TabIndex = 23;
            labelPreview.Text = "—";
            //
            // buttonApply
            //
            buttonApply.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttonApply.BackColor = Color.FromArgb(46, 51, 67);
            buttonApply.DialogResult = DialogResult.OK;
            buttonApply.FlatStyle = FlatStyle.Popup;
            buttonApply.ForeColor = Color.White;
            buttonApply.Location = new Point(282, 310);
            buttonApply.Name = "buttonApply";
            buttonApply.Size = new Size(84, 26);
            buttonApply.TabIndex = 24;
            buttonApply.Text = "Apply";
            buttonApply.UseVisualStyleBackColor = false;
            //
            // buttonCancel
            //
            buttonCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttonCancel.DialogResult = DialogResult.Cancel;
            buttonCancel.FlatStyle = FlatStyle.Popup;
            buttonCancel.ForeColor = Color.White;
            buttonCancel.Location = new Point(372, 310);
            buttonCancel.Name = "buttonCancel";
            buttonCancel.Size = new Size(84, 26);
            buttonCancel.TabIndex = 25;
            buttonCancel.Text = "Cancel";
            buttonCancel.UseVisualStyleBackColor = true;
            //
            // VirtualCrossoverAutoSetupDialog
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(40, 44, 54);
            ClientSize = new Size(468, 348);
            Controls.Add(labelHeader);
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
            Controls.Add(labelSubElevation);
            Controls.Add(subElevation);
            Controls.Add(labelSubElevationUnit);
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
        private Label labelSubElevation;
        private DarkNumericUpDown subElevation;
        private Label labelSubElevationUnit;
        private Label labelPreview;
        private Button buttonApply;
        private Button buttonCancel;
    }
}
