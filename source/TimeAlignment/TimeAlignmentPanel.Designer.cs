namespace Resonalyze
{
    partial class TimeAlignmentPanel
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
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(TimeAlignmentPanel));
            helpLabel = new Label();
            sourceSummaryLabel = new Label();
            bandpassCheckBox = new CheckBox();
            bandpassCenterLabel = new Label();
            bandpassCenterNumeric = new DarkNumericUpDown();
            bandpassPassOctavesLabel = new Label();
            bandpassPassOctavesNumeric = new DarkNumericUpDown();
            bandpassFadeOctavesLabel = new Label();
            bandpassFadeOctavesNumeric = new DarkNumericUpDown();
            bandpassPlotView = new OxyPlot.WindowsForms.PlotView();
            envelopePlotView = new OxyPlot.WindowsForms.PlotView();
            statusTextBox = new StatusRichTextBox();
            compareLabel = new Label();
            (bandpassCenterNumeric).BeginInit();
            (bandpassPassOctavesNumeric).BeginInit();
            (bandpassFadeOctavesNumeric).BeginInit();
            SuspendLayout();
            // 
            // helpLabel
            // 
            helpLabel.ForeColor = Color.FromArgb(205, 210, 220);
            helpLabel.Location = new Point(18, 64);
            helpLabel.Name = "helpLabel";
            helpLabel.Size = new Size(500, 166);
            helpLabel.TabIndex = 2;
            helpLabel.Text = resources.GetString("helpLabel.Text");
            // 
            // sourceSummaryLabel
            // 
            sourceSummaryLabel.Font = new Font("Segoe UI Semibold", 11.25F, FontStyle.Bold, GraphicsUnit.Point, 204);
            sourceSummaryLabel.ForeColor = Color.FromArgb(210, 214, 222);
            sourceSummaryLabel.Location = new Point(18, 18);
            sourceSummaryLabel.Name = "sourceSummaryLabel";
            sourceSummaryLabel.Size = new Size(500, 20);
            sourceSummaryLabel.TabIndex = 3;
            sourceSummaryLabel.Text = "Source: waiting for an impulse response.";
            // 
            // bandpassCheckBox
            // 
            bandpassCheckBox.AutoSize = true;
            bandpassCheckBox.BackColor = Color.Transparent;
            bandpassCheckBox.ForeColor = Color.FromArgb(210, 214, 222);
            bandpassCheckBox.Location = new Point(18, 242);
            bandpassCheckBox.Name = "bandpassCheckBox";
            bandpassCheckBox.Size = new Size(143, 19);
            bandpassCheckBox.TabIndex = 4;
            bandpassCheckBox.Text = "Use bandpass window";
            bandpassCheckBox.UseVisualStyleBackColor = false;
            // 
            // bandpassCenterLabel
            // 
            bandpassCenterLabel.AutoSize = true;
            bandpassCenterLabel.ForeColor = Color.FromArgb(210, 214, 222);
            bandpassCenterLabel.Location = new Point(18, 276);
            bandpassCenterLabel.Name = "bandpassCenterLabel";
            bandpassCenterLabel.Size = new Size(118, 15);
            bandpassCenterLabel.TabIndex = 5;
            bandpassCenterLabel.Text = "Center frequency, Hz";
            // 
            // bandpassCenterNumeric
            // 
            bandpassCenterNumeric.BackColor = Color.FromArgb(55, 60, 72);
            bandpassCenterNumeric.DecimalPlaces = 0;
            bandpassCenterNumeric.ForeColor = Color.White;
            bandpassCenterNumeric.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            bandpassCenterNumeric.Location = new Point(180, 272);
            bandpassCenterNumeric.Maximum = new decimal(new int[] { 20000, 0, 0, 0 });
            bandpassCenterNumeric.Minimum = new decimal(new int[] { 20, 0, 0, 0 });
            bandpassCenterNumeric.MinimumSize = new Size(36, 19);
            bandpassCenterNumeric.Name = "bandpassCenterNumeric";
            bandpassCenterNumeric.Size = new Size(120, 23);
            bandpassCenterNumeric.TabIndex = 6;
            bandpassCenterNumeric.TextAlign = HorizontalAlignment.Right;
            bandpassCenterNumeric.ThousandsSeparator = false;
            bandpassCenterNumeric.Value = new decimal(new int[] { 1000, 0, 0, 0 });
            // 
            // bandpassPassOctavesLabel
            // 
            bandpassPassOctavesLabel.AutoSize = true;
            bandpassPassOctavesLabel.ForeColor = Color.FromArgb(210, 214, 222);
            bandpassPassOctavesLabel.Location = new Point(18, 310);
            bandpassPassOctavesLabel.Name = "bandpassPassOctavesLabel";
            bandpassPassOctavesLabel.Size = new Size(86, 15);
            bandpassPassOctavesLabel.TabIndex = 7;
            bandpassPassOctavesLabel.Text = "Pass width, oct";
            // 
            // bandpassPassOctavesNumeric
            // 
            bandpassPassOctavesNumeric.BackColor = Color.FromArgb(55, 60, 72);
            bandpassPassOctavesNumeric.DecimalPlaces = 1;
            bandpassPassOctavesNumeric.ForeColor = Color.White;
            bandpassPassOctavesNumeric.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            bandpassPassOctavesNumeric.Location = new Point(180, 306);
            bandpassPassOctavesNumeric.Maximum = new decimal(new int[] { 8, 0, 0, 0 });
            bandpassPassOctavesNumeric.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            bandpassPassOctavesNumeric.MinimumSize = new Size(36, 19);
            bandpassPassOctavesNumeric.Name = "bandpassPassOctavesNumeric";
            bandpassPassOctavesNumeric.Size = new Size(120, 23);
            bandpassPassOctavesNumeric.TabIndex = 8;
            bandpassPassOctavesNumeric.TextAlign = HorizontalAlignment.Right;
            bandpassPassOctavesNumeric.ThousandsSeparator = false;
            bandpassPassOctavesNumeric.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // bandpassFadeOctavesLabel
            // 
            bandpassFadeOctavesLabel.AutoSize = true;
            bandpassFadeOctavesLabel.ForeColor = Color.FromArgb(210, 214, 222);
            bandpassFadeOctavesLabel.Location = new Point(18, 344);
            bandpassFadeOctavesLabel.Name = "bandpassFadeOctavesLabel";
            bandpassFadeOctavesLabel.Size = new Size(88, 15);
            bandpassFadeOctavesLabel.TabIndex = 9;
            bandpassFadeOctavesLabel.Text = "Fade width, oct";
            // 
            // bandpassFadeOctavesNumeric
            // 
            bandpassFadeOctavesNumeric.BackColor = Color.FromArgb(55, 60, 72);
            bandpassFadeOctavesNumeric.DecimalPlaces = 1;
            bandpassFadeOctavesNumeric.ForeColor = Color.White;
            bandpassFadeOctavesNumeric.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            bandpassFadeOctavesNumeric.Location = new Point(180, 340);
            bandpassFadeOctavesNumeric.Maximum = new decimal(new int[] { 8, 0, 0, 0 });
            bandpassFadeOctavesNumeric.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            bandpassFadeOctavesNumeric.MinimumSize = new Size(36, 19);
            bandpassFadeOctavesNumeric.Name = "bandpassFadeOctavesNumeric";
            bandpassFadeOctavesNumeric.Size = new Size(120, 23);
            bandpassFadeOctavesNumeric.TabIndex = 10;
            bandpassFadeOctavesNumeric.TextAlign = HorizontalAlignment.Right;
            bandpassFadeOctavesNumeric.ThousandsSeparator = false;
            bandpassFadeOctavesNumeric.Value = new decimal(new int[] { 5, 0, 0, 65536 });
            // 
            // bandpassPlotView
            // 
            bandpassPlotView.BackColor = Color.FromArgb(32, 36, 46);
            bandpassPlotView.Location = new Point(18, 404);
            bandpassPlotView.Name = "bandpassPlotView";
            bandpassPlotView.PanCursor = Cursors.Hand;
            bandpassPlotView.Size = new Size(500, 352);
            bandpassPlotView.TabIndex = 11;
            bandpassPlotView.Text = "bandpassPlotView";
            bandpassPlotView.ZoomHorizontalCursor = Cursors.SizeWE;
            bandpassPlotView.ZoomRectangleCursor = Cursors.SizeNWSE;
            bandpassPlotView.ZoomVerticalCursor = Cursors.SizeNS;
            // 
            // envelopePlotView
            // 
            envelopePlotView.BackColor = Color.FromArgb(32, 36, 46);
            envelopePlotView.Location = new Point(580, 404);
            envelopePlotView.MinimumSize = new Size(320, 240);
            envelopePlotView.Name = "envelopePlotView";
            envelopePlotView.PanCursor = Cursors.Hand;
            envelopePlotView.Size = new Size(580, 352);
            envelopePlotView.TabIndex = 12;
            envelopePlotView.Text = "envelopePlotView";
            envelopePlotView.ZoomHorizontalCursor = Cursors.SizeWE;
            envelopePlotView.ZoomRectangleCursor = Cursors.SizeNWSE;
            envelopePlotView.ZoomVerticalCursor = Cursors.SizeNS;
            // 
            // statusTextBox
            // 
            statusTextBox.BackColor = Color.FromArgb(40, 44, 54);
            statusTextBox.BorderStyle = BorderStyle.None;
            statusTextBox.DetectUrls = false;
            statusTextBox.Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point, 204);
            statusTextBox.ForeColor = Color.FromArgb(190, 195, 205);
            statusTextBox.Location = new Point(580, 18);
            statusTextBox.Name = "statusTextBox";
            statusTextBox.ReadOnly = true;
            statusTextBox.ScrollBars = RichTextBoxScrollBars.None;
            statusTextBox.Size = new Size(580, 380);
            statusTextBox.TabIndex = 13;
            statusTextBox.Text = "Run a loopback measurement or load an impulse response file with transfer IR.";
            // 
            // compareLabel
            // 
            compareLabel.Font = new Font("Segoe UI Semibold", 11.25F, FontStyle.Bold, GraphicsUnit.Point, 204);
            compareLabel.ForeColor = Color.FromArgb(210, 214, 222);
            compareLabel.Location = new Point(18, 38);
            compareLabel.Name = "compareLabel";
            compareLabel.Size = new Size(500, 20);
            compareLabel.TabIndex = 14;
            compareLabel.Text = "Compare: -";
            // 
            // TimeAlignmentPanel
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            AutoScroll = true;
            BackColor = Color.FromArgb(40, 44, 54);
            BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(compareLabel);
            Controls.Add(statusTextBox);
            Controls.Add(envelopePlotView);
            Controls.Add(bandpassPlotView);
            Controls.Add(bandpassFadeOctavesNumeric);
            Controls.Add(bandpassFadeOctavesLabel);
            Controls.Add(bandpassPassOctavesNumeric);
            Controls.Add(bandpassPassOctavesLabel);
            Controls.Add(bandpassCenterNumeric);
            Controls.Add(bandpassCenterLabel);
            Controls.Add(bandpassCheckBox);
            Controls.Add(sourceSummaryLabel);
            Controls.Add(helpLabel);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            Name = "TimeAlignmentPanel";
            Size = new Size(1182, 770);
            (bandpassCenterNumeric).EndInit();
            (bandpassPassOctavesNumeric).EndInit();
            (bandpassFadeOctavesNumeric).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Label helpLabel;
        private Label sourceSummaryLabel;
        private CheckBox bandpassCheckBox;
        private Label bandpassCenterLabel;
        private DarkNumericUpDown bandpassCenterNumeric;
        private Label bandpassPassOctavesLabel;
        private DarkNumericUpDown bandpassPassOctavesNumeric;
        private Label bandpassFadeOctavesLabel;
        private DarkNumericUpDown bandpassFadeOctavesNumeric;
        private OxyPlot.WindowsForms.PlotView bandpassPlotView;
        private OxyPlot.WindowsForms.PlotView envelopePlotView;
        private StatusRichTextBox statusTextBox;
        private Label compareLabel;
    }
}
