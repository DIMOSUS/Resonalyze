namespace Resonalyze
{
    partial class VirtualCrossoverGateDialog
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
            labelGateOffset = new Label();
            numericGateOffset = new DarkNumericUpDown();
            buttonFit = new Button();
            labelLeft = new Label();
            numericLeft = new DarkNumericUpDown();
            labelPlateau = new Label();
            numericPlateau = new DarkNumericUpDown();
            labelRight = new Label();
            numericRight = new DarkNumericUpDown();
            labelMinFrequency = new Label();
            labelTau = new Label();
            numericTau = new DarkNumericUpDown();
            buttonTauSlope = new Button();
            buttonTauPeak = new Button();
            labelWindowMode = new Label();
            comboWindowMode = new DarkComboBox();
            labelFdwCycles = new Label();
            comboFdwCycles = new DarkComboBox();
            labelDetrendMode = new Label();
            comboDetrendMode = new DarkComboBox();
            labelAutoDetrend = new Label();
            irPlotView = new OxyPlot.WindowsForms.PlotView();
            buttonSave = new Button();
            buttonCancel = new Button();
            (numericGateOffset).BeginInit();
            (numericLeft).BeginInit();
            (numericPlateau).BeginInit();
            (numericRight).BeginInit();
            (numericTau).BeginInit();
            SuspendLayout();
            // 
            // labelGateOffset
            // 
            labelGateOffset.AutoSize = true;
            labelGateOffset.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelGateOffset.ForeColor = Color.FromArgb(210, 214, 222);
            labelGateOffset.Location = new Point(12, 14);
            labelGateOffset.Name = "labelGateOffset";
            labelGateOffset.Size = new Size(83, 15);
            labelGateOffset.TabIndex = 0;
            labelGateOffset.Text = "Gate offset ms";
            // 
            // numericGateOffset
            // 
            numericGateOffset.BackColor = Color.FromArgb(55, 60, 72);
            numericGateOffset.DecimalPlaces = 2;
            numericGateOffset.ForeColor = Color.White;
            numericGateOffset.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            numericGateOffset.Location = new Point(112, 12);
            numericGateOffset.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            numericGateOffset.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericGateOffset.MinimumSize = new Size(36, 19);
            numericGateOffset.Name = "numericGateOffset";
            numericGateOffset.Size = new Size(80, 19);
            numericGateOffset.TabIndex = 1;
            numericGateOffset.TextAlign = HorizontalAlignment.Right;
            numericGateOffset.ThousandsSeparator = false;
            numericGateOffset.Value = new decimal(new int[] { 0, 0, 0, 0 });
            // 
            // buttonFit
            // 
            buttonFit.FlatStyle = FlatStyle.Popup;
            buttonFit.ForeColor = Color.White;
            buttonFit.Location = new Point(198, 10);
            buttonFit.Name = "buttonFit";
            buttonFit.Size = new Size(40, 23);
            buttonFit.TabIndex = 2;
            buttonFit.Text = "Fit";
            buttonFit.UseVisualStyleBackColor = true;
            // 
            // labelLeft
            // 
            labelLeft.AutoSize = true;
            labelLeft.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelLeft.ForeColor = Color.FromArgb(210, 214, 222);
            labelLeft.Location = new Point(262, 14);
            labelLeft.Name = "labelLeft";
            labelLeft.Size = new Size(46, 15);
            labelLeft.TabIndex = 3;
            labelLeft.Text = "Left ms";
            // 
            // numericLeft
            // 
            numericLeft.BackColor = Color.FromArgb(55, 60, 72);
            numericLeft.DecimalPlaces = 2;
            numericLeft.ForeColor = Color.White;
            numericLeft.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            numericLeft.Location = new Point(322, 12);
            numericLeft.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            numericLeft.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericLeft.MinimumSize = new Size(36, 19);
            numericLeft.Name = "numericLeft";
            numericLeft.Size = new Size(66, 19);
            numericLeft.TabIndex = 4;
            numericLeft.TextAlign = HorizontalAlignment.Right;
            numericLeft.ThousandsSeparator = false;
            numericLeft.Value = new decimal(new int[] { 5, 0, 0, 65536 });
            // 
            // labelPlateau
            // 
            labelPlateau.AutoSize = true;
            labelPlateau.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelPlateau.ForeColor = Color.FromArgb(210, 214, 222);
            labelPlateau.Location = new Point(12, 44);
            labelPlateau.Name = "labelPlateau";
            labelPlateau.Size = new Size(65, 15);
            labelPlateau.TabIndex = 5;
            labelPlateau.Text = "Plateau ms";
            // 
            // numericPlateau
            // 
            numericPlateau.BackColor = Color.FromArgb(55, 60, 72);
            numericPlateau.DecimalPlaces = 2;
            numericPlateau.ForeColor = Color.White;
            numericPlateau.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            numericPlateau.Location = new Point(112, 42);
            numericPlateau.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            numericPlateau.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericPlateau.MinimumSize = new Size(36, 19);
            numericPlateau.Name = "numericPlateau";
            numericPlateau.Size = new Size(80, 19);
            numericPlateau.TabIndex = 6;
            numericPlateau.TextAlign = HorizontalAlignment.Right;
            numericPlateau.ThousandsSeparator = false;
            numericPlateau.Value = new decimal(new int[] { 4, 0, 0, 0 });
            // 
            // labelRight
            // 
            labelRight.AutoSize = true;
            labelRight.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelRight.ForeColor = Color.FromArgb(210, 214, 222);
            labelRight.Location = new Point(262, 44);
            labelRight.Name = "labelRight";
            labelRight.Size = new Size(54, 15);
            labelRight.TabIndex = 7;
            labelRight.Text = "Right ms";
            // 
            // numericRight
            // 
            numericRight.BackColor = Color.FromArgb(55, 60, 72);
            numericRight.DecimalPlaces = 2;
            numericRight.ForeColor = Color.White;
            numericRight.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            numericRight.Location = new Point(322, 42);
            numericRight.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            numericRight.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericRight.MinimumSize = new Size(36, 19);
            numericRight.Name = "numericRight";
            numericRight.Size = new Size(66, 19);
            numericRight.TabIndex = 8;
            numericRight.TextAlign = HorizontalAlignment.Right;
            numericRight.ThousandsSeparator = false;
            numericRight.Value = new decimal(new int[] { 15, 0, 0, 65536 });
            // 
            // labelMinFrequency
            // 
            labelMinFrequency.AutoSize = true;
            labelMinFrequency.ForeColor = Color.FromArgb(230, 184, 0);
            labelMinFrequency.Location = new Point(414, 44);
            labelMinFrequency.Name = "labelMinFrequency";
            labelMinFrequency.Size = new Size(120, 15);
            labelMinFrequency.TabIndex = 9;
            labelMinFrequency.Text = "Reliable from ≈ — Hz";
            //
            // labelTau
            //
            labelTau.AutoSize = true;
            labelTau.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelTau.ForeColor = Color.FromArgb(210, 214, 222);
            labelTau.Location = new Point(12, 74);
            labelTau.Name = "labelTau";
            labelTau.Size = new Size(34, 15);
            labelTau.TabIndex = 13;
            labelTau.Text = "τ ms";
            //
            // numericTau
            //
            numericTau.BackColor = Color.FromArgb(55, 60, 72);
            numericTau.DecimalPlaces = 2;
            numericTau.ForeColor = Color.White;
            numericTau.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            numericTau.Location = new Point(112, 72);
            numericTau.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            numericTau.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            numericTau.MinimumSize = new Size(36, 19);
            numericTau.Name = "numericTau";
            numericTau.Size = new Size(80, 19);
            numericTau.TabIndex = 14;
            numericTau.TextAlign = HorizontalAlignment.Right;
            numericTau.ThousandsSeparator = false;
            numericTau.Value = new decimal(new int[] { 0, 0, 0, 0 });
            //
            // buttonTauSlope
            //
            buttonTauSlope.FlatStyle = FlatStyle.Popup;
            buttonTauSlope.ForeColor = Color.White;
            buttonTauSlope.Location = new Point(198, 70);
            buttonTauSlope.Name = "buttonTauSlope";
            buttonTauSlope.Size = new Size(56, 23);
            buttonTauSlope.TabIndex = 15;
            buttonTauSlope.Text = "Slope";
            buttonTauSlope.UseVisualStyleBackColor = true;
            //
            // buttonTauPeak
            //
            buttonTauPeak.FlatStyle = FlatStyle.Popup;
            buttonTauPeak.ForeColor = Color.White;
            buttonTauPeak.Location = new Point(260, 70);
            buttonTauPeak.Name = "buttonTauPeak";
            buttonTauPeak.Size = new Size(56, 23);
            buttonTauPeak.TabIndex = 16;
            buttonTauPeak.Text = "Peak";
            buttonTauPeak.UseVisualStyleBackColor = true;
            //
            // labelWindowMode
            //
            labelWindowMode.AutoSize = true;
            labelWindowMode.ForeColor = Color.FromArgb(210, 214, 222);
            labelWindowMode.Location = new Point(12, 104);
            labelWindowMode.Name = "labelWindowMode";
            labelWindowMode.Size = new Size(52, 15);
            labelWindowMode.TabIndex = 17;
            labelWindowMode.Text = "Window";
            //
            // comboWindowMode
            //
            comboWindowMode.BackColor = Color.FromArgb(55, 60, 72);
            comboWindowMode.DropDownStyle = ComboBoxStyle.DropDownList;
            comboWindowMode.ForeColor = Color.White;
            comboWindowMode.Items.AddRange(new object[] { "Fixed", "FDW" });
            comboWindowMode.Location = new Point(112, 102);
            comboWindowMode.MinimumSize = new Size(36, 19);
            comboWindowMode.Name = "comboWindowMode";
            comboWindowMode.Size = new Size(126, 19);
            comboWindowMode.TabIndex = 18;
            //
            // labelFdwCycles
            //
            labelFdwCycles.AutoSize = true;
            labelFdwCycles.ForeColor = Color.FromArgb(210, 214, 222);
            labelFdwCycles.Location = new Point(262, 104);
            labelFdwCycles.Name = "labelFdwCycles";
            labelFdwCycles.Size = new Size(67, 15);
            labelFdwCycles.TabIndex = 19;
            labelFdwCycles.Text = "FDW cycles";
            //
            // comboFdwCycles
            //
            comboFdwCycles.BackColor = Color.FromArgb(55, 60, 72);
            comboFdwCycles.DropDownStyle = ComboBoxStyle.DropDownList;
            comboFdwCycles.ForeColor = Color.White;
            comboFdwCycles.Items.AddRange(new object[] { 4, 6, 8 });
            comboFdwCycles.Location = new Point(342, 102);
            comboFdwCycles.MinimumSize = new Size(36, 19);
            comboFdwCycles.Name = "comboFdwCycles";
            comboFdwCycles.Size = new Size(74, 19);
            comboFdwCycles.TabIndex = 20;
            //
            // labelDetrendMode
            //
            labelDetrendMode.AutoSize = true;
            labelDetrendMode.ForeColor = Color.FromArgb(210, 214, 222);
            labelDetrendMode.Location = new Point(12, 134);
            labelDetrendMode.Name = "labelDetrendMode";
            labelDetrendMode.Size = new Size(49, 15);
            labelDetrendMode.TabIndex = 21;
            labelDetrendMode.Text = "Detrend";
            //
            // comboDetrendMode
            //
            comboDetrendMode.BackColor = Color.FromArgb(55, 60, 72);
            comboDetrendMode.DropDownStyle = ComboBoxStyle.DropDownList;
            comboDetrendMode.ForeColor = Color.White;
            comboDetrendMode.Items.AddRange(new object[] { "Off", "Auto", "Manual" });
            comboDetrendMode.Location = new Point(112, 132);
            comboDetrendMode.MinimumSize = new Size(36, 19);
            comboDetrendMode.Name = "comboDetrendMode";
            comboDetrendMode.Size = new Size(126, 19);
            comboDetrendMode.TabIndex = 22;
            //
            // labelAutoDetrend
            //
            labelAutoDetrend.AutoSize = true;
            labelAutoDetrend.ForeColor = Color.FromArgb(210, 214, 222);
            labelAutoDetrend.Location = new Point(262, 134);
            labelAutoDetrend.Name = "labelAutoDetrend";
            labelAutoDetrend.Size = new Size(0, 15);
            labelAutoDetrend.TabIndex = 23;
            //
            // irPlotView
            //
            irPlotView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            irPlotView.BackColor = Color.FromArgb(32, 36, 46);
            irPlotView.Location = new Point(12, 164);
            irPlotView.Name = "irPlotView";
            irPlotView.PanCursor = Cursors.Hand;
            irPlotView.Size = new Size(596, 300);
            irPlotView.TabIndex = 10;
            irPlotView.ZoomHorizontalCursor = Cursors.SizeWE;
            irPlotView.ZoomRectangleCursor = Cursors.SizeNWSE;
            irPlotView.ZoomVerticalCursor = Cursors.SizeNS;
            // 
            // buttonSave
            // 
            buttonSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttonSave.BackColor = Color.FromArgb(46, 51, 67);
            buttonSave.DialogResult = DialogResult.OK;
            buttonSave.FlatStyle = FlatStyle.Popup;
            buttonSave.ForeColor = Color.White;
            buttonSave.Location = new Point(434, 474);
            buttonSave.Name = "buttonSave";
            buttonSave.Size = new Size(84, 26);
            buttonSave.TabIndex = 11;
            buttonSave.Text = "Save";
            buttonSave.UseVisualStyleBackColor = false;
            // 
            // buttonCancel
            // 
            buttonCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttonCancel.DialogResult = DialogResult.Cancel;
            buttonCancel.FlatStyle = FlatStyle.Popup;
            buttonCancel.ForeColor = Color.White;
            buttonCancel.Location = new Point(524, 474);
            buttonCancel.Name = "buttonCancel";
            buttonCancel.Size = new Size(84, 26);
            buttonCancel.TabIndex = 12;
            buttonCancel.Text = "Cancel";
            buttonCancel.UseVisualStyleBackColor = true;
            // 
            // VirtualCrossoverGateDialog
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(40, 44, 54);
            ClientSize = new Size(620, 512);
            Controls.Add(labelGateOffset);
            Controls.Add(numericGateOffset);
            Controls.Add(buttonFit);
            Controls.Add(labelLeft);
            Controls.Add(numericLeft);
            Controls.Add(labelPlateau);
            Controls.Add(numericPlateau);
            Controls.Add(labelRight);
            Controls.Add(numericRight);
            Controls.Add(labelMinFrequency);
            Controls.Add(labelTau);
            Controls.Add(numericTau);
            Controls.Add(buttonTauSlope);
            Controls.Add(buttonTauPeak);
            Controls.Add(labelWindowMode);
            Controls.Add(comboWindowMode);
            Controls.Add(labelFdwCycles);
            Controls.Add(comboFdwCycles);
            Controls.Add(labelDetrendMode);
            Controls.Add(comboDetrendMode);
            Controls.Add(labelAutoDetrend);
            Controls.Add(irPlotView);
            Controls.Add(buttonSave);
            Controls.Add(buttonCancel);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "VirtualCrossoverGateDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Phase gate";
            (numericGateOffset).EndInit();
            (numericLeft).EndInit();
            (numericPlateau).EndInit();
            (numericRight).EndInit();
            (numericTau).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label labelGateOffset;
        private DarkNumericUpDown numericGateOffset;
        private Button buttonFit;
        private Label labelLeft;
        private DarkNumericUpDown numericLeft;
        private Label labelPlateau;
        private DarkNumericUpDown numericPlateau;
        private Label labelRight;
        private DarkNumericUpDown numericRight;
        private Label labelMinFrequency;
        private Label labelTau;
        private DarkNumericUpDown numericTau;
        private Button buttonTauSlope;
        private Button buttonTauPeak;
        private Label labelWindowMode;
        private DarkComboBox comboWindowMode;
        private Label labelFdwCycles;
        private DarkComboBox comboFdwCycles;
        private Label labelDetrendMode;
        private DarkComboBox comboDetrendMode;
        private Label labelAutoDetrend;
        private OxyPlot.WindowsForms.PlotView irPlotView;
        private Button buttonSave;
        private Button buttonCancel;
    }
}
