namespace Resonalyze
{
    partial class EqWizardPanel
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
            titleLabel = new Label();
            plotView1 = new OxyPlot.WindowsForms.PlotView();
            panelPEQ = new Panel();
            labelBands = new Label();
            darkComboBoxSource = new DarkComboBox();
            labelSource = new Label();
            darkComboBoxBands = new DarkComboBox();
            SuspendLayout();
            // 
            // titleLabel
            // 
            titleLabel.AutoSize = true;
            titleLabel.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold, GraphicsUnit.Point, 204);
            titleLabel.ForeColor = Color.FromArgb(210, 214, 222);
            titleLabel.Location = new Point(49, 323);
            titleLabel.Margin = new Padding(3);
            titleLabel.Name = "titleLabel";
            titleLabel.Size = new Size(84, 21);
            titleLabel.TabIndex = 0;
            titleLabel.Text = "EQ Wizard";
            // 
            // plotView1
            // 
            plotView1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            plotView1.BackColor = Color.FromArgb(50, 55, 100);
            plotView1.Location = new Point(12, 12);
            plotView1.Margin = new Padding(6);
            plotView1.Name = "plotView1";
            plotView1.PanCursor = Cursors.Hand;
            plotView1.Size = new Size(1158, 302);
            plotView1.TabIndex = 1;
            plotView1.Text = "plotView1";
            plotView1.ZoomHorizontalCursor = Cursors.SizeWE;
            plotView1.ZoomRectangleCursor = Cursors.SizeNWSE;
            plotView1.ZoomVerticalCursor = Cursors.SizeNS;
            // 
            // panelPEQ
            // 
            panelPEQ.BackColor = Color.FromArgb(20, 22, 30);
            panelPEQ.BorderStyle = BorderStyle.FixedSingle;
            panelPEQ.Location = new Point(197, 323);
            panelPEQ.Name = "panelPEQ";
            panelPEQ.Size = new Size(973, 374);
            panelPEQ.TabIndex = 2;
            // 
            // labelBands
            // 
            labelBands.AutoSize = true;
            labelBands.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelBands.ForeColor = Color.FromArgb(210, 214, 222);
            labelBands.Location = new Point(12, 383);
            labelBands.Margin = new Padding(3);
            labelBands.Name = "labelBands";
            labelBands.Size = new Size(46, 19);
            labelBands.TabIndex = 4;
            labelBands.Text = "Bands";
            // 
            // darkComboBoxSource
            // 
            darkComboBoxSource.BackColor = Color.FromArgb(55, 60, 72);
            darkComboBoxSource.ForeColor = Color.White;
            darkComboBoxSource.Location = new Point(70, 358);
            darkComboBoxSource.MinimumSize = new Size(36, 19);
            darkComboBoxSource.Name = "darkComboBoxSource";
            darkComboBoxSource.Size = new Size(121, 19);
            darkComboBoxSource.TabIndex = 5;
            // 
            // labelSource
            // 
            labelSource.AutoSize = true;
            labelSource.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelSource.ForeColor = Color.FromArgb(210, 214, 222);
            labelSource.Location = new Point(12, 358);
            labelSource.Margin = new Padding(3);
            labelSource.Name = "labelSource";
            labelSource.Size = new Size(52, 19);
            labelSource.TabIndex = 6;
            labelSource.Text = "Source";
            // 
            // darkComboBoxBands
            // 
            darkComboBoxBands.BackColor = Color.FromArgb(55, 60, 72);
            darkComboBoxBands.ForeColor = Color.White;
            darkComboBoxBands.Location = new Point(70, 383);
            darkComboBoxBands.MinimumSize = new Size(36, 19);
            darkComboBoxBands.Name = "darkComboBoxBands";
            darkComboBoxBands.Size = new Size(121, 19);
            darkComboBoxBands.TabIndex = 7;
            // 
            // EqWizardPanel
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            AutoScroll = true;
            BackColor = Color.FromArgb(40, 44, 54);
            BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(darkComboBoxBands);
            Controls.Add(labelSource);
            Controls.Add(darkComboBoxSource);
            Controls.Add(labelBands);
            Controls.Add(panelPEQ);
            Controls.Add(plotView1);
            Controls.Add(titleLabel);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            Name = "EqWizardPanel";
            Padding = new Padding(6);
            Size = new Size(1182, 706);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label titleLabel;
        private OxyPlot.WindowsForms.PlotView plotView1;
        private Panel panelPEQ;
        private Label labelBands;
        private DarkComboBox darkComboBoxSource;
        private Label labelSource;
        private DarkComboBox darkComboBoxBands;
    }
}
