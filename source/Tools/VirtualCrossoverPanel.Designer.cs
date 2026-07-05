namespace Resonalyze
{
    partial class VirtualCrossoverPanel
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
            mainPlotView = new OxyPlot.WindowsForms.PlotView();
            dspPlotView = new OxyPlot.WindowsForms.PlotView();
            virtualCrossoverChannelControl1 = new VirtualCrossoverChannelControl();
            virtualCrossoverChannelControl2 = new VirtualCrossoverChannelControl();
            virtualCrossoverChannelControl3 = new VirtualCrossoverChannelControl();
            labelView = new Label();
            checkBoxShowSum = new CheckBox();
            checkBoxShowLoss = new CheckBox();
            radioViewMagnitude = new RadioButton();
            radioViewPhase = new RadioButton();
            labelSmoothing = new Label();
            comboBoxSmoothing = new DarkComboBox();
            buttonAutoDelay = new Button();
            buttonAutoSetup = new Button();
            buttonCaptureOverlay = new Button();
            buttonExport = new Button();
            buttonPhaseGate = new Button();
            buttonSessionImport = new Button();
            buttonSessionExport = new Button();
            labelMetric = new Label();
            labelCrossoverWarning = new Label();
            SuspendLayout();
            // 
            // mainPlotView
            // 
            mainPlotView.BackColor = Color.FromArgb(40, 44, 80);
            mainPlotView.Location = new Point(349, 9);
            mainPlotView.Name = "mainPlotView";
            mainPlotView.PanCursor = Cursors.Hand;
            mainPlotView.Size = new Size(818, 392);
            mainPlotView.TabIndex = 1;
            mainPlotView.Text = "plotView1";
            mainPlotView.ZoomHorizontalCursor = Cursors.SizeWE;
            mainPlotView.ZoomRectangleCursor = Cursors.SizeNWSE;
            mainPlotView.ZoomVerticalCursor = Cursors.SizeNS;
            // 
            // dspPlotView
            // 
            dspPlotView.BackColor = Color.FromArgb(40, 44, 80);
            dspPlotView.Location = new Point(485, 471);
            dspPlotView.Name = "dspPlotView";
            dspPlotView.PanCursor = Cursors.Hand;
            dspPlotView.Size = new Size(682, 224);
            dspPlotView.TabIndex = 2;
            dspPlotView.Text = "plotView2";
            dspPlotView.ZoomHorizontalCursor = Cursors.SizeWE;
            dspPlotView.ZoomRectangleCursor = Cursors.SizeNWSE;
            dspPlotView.ZoomVerticalCursor = Cursors.SizeNS;
            // 
            // virtualCrossoverChannelControl1
            // 
            virtualCrossoverChannelControl1.BackColor = Color.FromArgb(46, 51, 62);
            virtualCrossoverChannelControl1.BorderStyle = BorderStyle.FixedSingle;
            virtualCrossoverChannelControl1.Font = new Font("Segoe UI", 9F);
            virtualCrossoverChannelControl1.ForeColor = Color.White;
            virtualCrossoverChannelControl1.Location = new Point(9, 9);
            virtualCrossoverChannelControl1.MaximumSize = new Size(334, 226);
            virtualCrossoverChannelControl1.MinimumSize = new Size(334, 226);
            virtualCrossoverChannelControl1.Name = "virtualCrossoverChannelControl1";
            virtualCrossoverChannelControl1.Size = new Size(334, 226);
            virtualCrossoverChannelControl1.TabIndex = 3;
            // 
            // virtualCrossoverChannelControl2
            // 
            virtualCrossoverChannelControl2.BackColor = Color.FromArgb(46, 51, 62);
            virtualCrossoverChannelControl2.BorderStyle = BorderStyle.FixedSingle;
            virtualCrossoverChannelControl2.ChannelName = "B";
            virtualCrossoverChannelControl2.Font = new Font("Segoe UI", 9F);
            virtualCrossoverChannelControl2.ForeColor = Color.White;
            virtualCrossoverChannelControl2.Location = new Point(9, 240);
            virtualCrossoverChannelControl2.MaximumSize = new Size(334, 226);
            virtualCrossoverChannelControl2.MinimumSize = new Size(334, 226);
            virtualCrossoverChannelControl2.Name = "virtualCrossoverChannelControl2";
            virtualCrossoverChannelControl2.Size = new Size(334, 226);
            virtualCrossoverChannelControl2.TabIndex = 4;
            // 
            // virtualCrossoverChannelControl3
            // 
            virtualCrossoverChannelControl3.BackColor = Color.FromArgb(46, 51, 62);
            virtualCrossoverChannelControl3.BorderStyle = BorderStyle.FixedSingle;
            virtualCrossoverChannelControl3.ChannelName = "C";
            virtualCrossoverChannelControl3.Font = new Font("Segoe UI", 9F);
            virtualCrossoverChannelControl3.ForeColor = Color.White;
            virtualCrossoverChannelControl3.Location = new Point(9, 471);
            virtualCrossoverChannelControl3.MaximumSize = new Size(334, 226);
            virtualCrossoverChannelControl3.MinimumSize = new Size(334, 226);
            virtualCrossoverChannelControl3.Name = "virtualCrossoverChannelControl3";
            virtualCrossoverChannelControl3.Size = new Size(334, 226);
            virtualCrossoverChannelControl3.TabIndex = 5;
            // 
            // labelView
            // 
            labelView.AutoSize = true;
            labelView.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelView.ForeColor = Color.FromArgb(210, 214, 222);
            labelView.Location = new Point(349, 414);
            labelView.Name = "labelView";
            labelView.Size = new Size(33, 15);
            labelView.TabIndex = 6;
            labelView.Text = "View";
            // 
            // checkBoxShowSum
            // 
            checkBoxShowSum.AutoSize = true;
            checkBoxShowSum.Checked = true;
            checkBoxShowSum.CheckState = CheckState.Checked;
            checkBoxShowSum.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            checkBoxShowSum.ForeColor = Color.FromArgb(210, 214, 222);
            checkBoxShowSum.Location = new Point(399, 412);
            checkBoxShowSum.Name = "checkBoxShowSum";
            checkBoxShowSum.Size = new Size(51, 19);
            checkBoxShowSum.TabIndex = 7;
            checkBoxShowSum.Text = "Sum";
            checkBoxShowSum.UseVisualStyleBackColor = true;
            // 
            // checkBoxShowLoss
            // 
            checkBoxShowLoss.AutoSize = true;
            checkBoxShowLoss.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            checkBoxShowLoss.ForeColor = Color.FromArgb(210, 214, 222);
            checkBoxShowLoss.Location = new Point(457, 412);
            checkBoxShowLoss.Name = "checkBoxShowLoss";
            checkBoxShowLoss.Size = new Size(74, 19);
            checkBoxShowLoss.TabIndex = 8;
            checkBoxShowLoss.Text = "Sum loss";
            checkBoxShowLoss.UseVisualStyleBackColor = true;
            // 
            // radioViewMagnitude
            // 
            radioViewMagnitude.AutoSize = true;
            radioViewMagnitude.Checked = true;
            radioViewMagnitude.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            radioViewMagnitude.ForeColor = Color.FromArgb(210, 214, 222);
            radioViewMagnitude.Location = new Point(541, 411);
            radioViewMagnitude.Name = "radioViewMagnitude";
            radioViewMagnitude.Size = new Size(83, 19);
            radioViewMagnitude.TabIndex = 9;
            radioViewMagnitude.TabStop = true;
            radioViewMagnitude.Text = "Magnitude";
            radioViewMagnitude.UseVisualStyleBackColor = true;
            // 
            // radioViewPhase
            // 
            radioViewPhase.AutoSize = true;
            radioViewPhase.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            radioViewPhase.ForeColor = Color.FromArgb(210, 214, 222);
            radioViewPhase.Location = new Point(633, 411);
            radioViewPhase.Name = "radioViewPhase";
            radioViewPhase.Size = new Size(56, 19);
            radioViewPhase.TabIndex = 10;
            radioViewPhase.Text = "Phase";
            radioViewPhase.UseVisualStyleBackColor = true;
            // 
            // labelSmoothing
            // 
            labelSmoothing.AutoSize = true;
            labelSmoothing.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelSmoothing.ForeColor = Color.FromArgb(210, 214, 222);
            labelSmoothing.Location = new Point(703, 414);
            labelSmoothing.Name = "labelSmoothing";
            labelSmoothing.Size = new Size(67, 15);
            labelSmoothing.TabIndex = 10;
            labelSmoothing.Text = "Smoothing";
            // 
            // comboBoxSmoothing
            // 
            comboBoxSmoothing.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxSmoothing.ForeColor = Color.White;
            comboBoxSmoothing.Location = new Point(777, 412);
            comboBoxSmoothing.MinimumSize = new Size(36, 19);
            comboBoxSmoothing.Name = "comboBoxSmoothing";
            comboBoxSmoothing.Size = new Size(90, 19);
            comboBoxSmoothing.TabIndex = 11;
            // 
            // buttonAutoDelay
            // 
            buttonAutoDelay.BackColor = Color.FromArgb(46, 51, 67);
            buttonAutoDelay.FlatStyle = FlatStyle.Popup;
            buttonAutoDelay.ForeColor = Color.White;
            buttonAutoDelay.Location = new Point(349, 503);
            buttonAutoDelay.Name = "buttonAutoDelay";
            buttonAutoDelay.Size = new Size(130, 24);
            buttonAutoDelay.TabIndex = 12;
            buttonAutoDelay.Text = "Auto delay";
            buttonAutoDelay.UseVisualStyleBackColor = false;
            // 
            // buttonAutoSetup
            // 
            buttonAutoSetup.BackColor = Color.FromArgb(46, 51, 67);
            buttonAutoSetup.FlatStyle = FlatStyle.Popup;
            buttonAutoSetup.ForeColor = Color.White;
            buttonAutoSetup.Location = new Point(349, 473);
            buttonAutoSetup.Name = "buttonAutoSetup";
            buttonAutoSetup.Size = new Size(130, 24);
            buttonAutoSetup.TabIndex = 19;
            buttonAutoSetup.Text = "Auto crossover...";
            buttonAutoSetup.UseVisualStyleBackColor = false;
            // 
            // buttonCaptureOverlay
            // 
            buttonCaptureOverlay.FlatStyle = FlatStyle.Popup;
            buttonCaptureOverlay.ForeColor = Color.White;
            buttonCaptureOverlay.Location = new Point(349, 642);
            buttonCaptureOverlay.Name = "buttonCaptureOverlay";
            buttonCaptureOverlay.Size = new Size(130, 24);
            buttonCaptureOverlay.TabIndex = 13;
            buttonCaptureOverlay.Text = "Capture to overlay";
            buttonCaptureOverlay.UseVisualStyleBackColor = true;
            // 
            // buttonExport
            // 
            buttonExport.FlatStyle = FlatStyle.Popup;
            buttonExport.ForeColor = Color.White;
            buttonExport.Location = new Point(349, 672);
            buttonExport.Name = "buttonExport";
            buttonExport.Size = new Size(130, 24);
            buttonExport.TabIndex = 14;
            buttonExport.Text = "Export...";
            buttonExport.UseVisualStyleBackColor = true;
            // 
            // buttonPhaseGate
            // 
            buttonPhaseGate.FlatStyle = FlatStyle.Popup;
            buttonPhaseGate.ForeColor = Color.White;
            buttonPhaseGate.Location = new Point(873, 410);
            buttonPhaseGate.Name = "buttonPhaseGate";
            buttonPhaseGate.Size = new Size(80, 24);
            buttonPhaseGate.TabIndex = 16;
            buttonPhaseGate.Text = "Gate...";
            buttonPhaseGate.UseVisualStyleBackColor = true;
            // 
            // buttonSessionImport
            // 
            buttonSessionImport.FlatStyle = FlatStyle.Popup;
            buttonSessionImport.ForeColor = Color.White;
            buttonSessionImport.Location = new Point(349, 612);
            buttonSessionImport.Name = "buttonSessionImport";
            buttonSessionImport.Size = new Size(130, 24);
            buttonSessionImport.TabIndex = 17;
            buttonSessionImport.Text = "Load session...";
            buttonSessionImport.UseVisualStyleBackColor = true;
            // 
            // buttonSessionExport
            // 
            buttonSessionExport.FlatStyle = FlatStyle.Popup;
            buttonSessionExport.ForeColor = Color.White;
            buttonSessionExport.Location = new Point(349, 582);
            buttonSessionExport.Name = "buttonSessionExport";
            buttonSessionExport.Size = new Size(130, 24);
            buttonSessionExport.TabIndex = 18;
            buttonSessionExport.Text = "Save session...";
            buttonSessionExport.UseVisualStyleBackColor = true;
            // 
            // labelMetric
            // 
            labelMetric.AutoSize = true;
            labelMetric.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelMetric.ForeColor = Color.FromArgb(230, 184, 0);
            labelMetric.Location = new Point(349, 437);
            labelMetric.Name = "labelMetric";
            labelMetric.Size = new Size(95, 15);
            labelMetric.TabIndex = 15;
            labelMetric.Text = "Sum loss avg: —";
            // 
            // labelCrossoverWarning
            // 
            labelCrossoverWarning.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelCrossoverWarning.ForeColor = Color.FromArgb(235, 110, 95);
            labelCrossoverWarning.Location = new Point(349, 452);
            labelCrossoverWarning.Name = "labelCrossoverWarning";
            labelCrossoverWarning.Size = new Size(818, 16);
            labelCrossoverWarning.TabIndex = 19;
            labelCrossoverWarning.Visible = false;
            // 
            // VirtualCrossoverPanel
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            AutoScroll = true;
            BackColor = Color.FromArgb(40, 44, 54);
            BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(labelView);
            Controls.Add(checkBoxShowSum);
            Controls.Add(checkBoxShowLoss);
            Controls.Add(radioViewMagnitude);
            Controls.Add(radioViewPhase);
            Controls.Add(labelSmoothing);
            Controls.Add(comboBoxSmoothing);
            Controls.Add(buttonAutoDelay);
            Controls.Add(buttonAutoSetup);
            Controls.Add(buttonCaptureOverlay);
            Controls.Add(buttonExport);
            Controls.Add(buttonPhaseGate);
            Controls.Add(buttonSessionImport);
            Controls.Add(buttonSessionExport);
            Controls.Add(labelMetric);
            Controls.Add(labelCrossoverWarning);
            Controls.Add(virtualCrossoverChannelControl3);
            Controls.Add(virtualCrossoverChannelControl2);
            Controls.Add(virtualCrossoverChannelControl1);
            Controls.Add(dspPlotView);
            Controls.Add(mainPlotView);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            Name = "VirtualCrossoverPanel";
            Padding = new Padding(6);
            Size = new Size(1182, 706);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private OxyPlot.WindowsForms.PlotView mainPlotView;
        private OxyPlot.WindowsForms.PlotView dspPlotView;
        private VirtualCrossoverChannelControl virtualCrossoverChannelControl1;
        private VirtualCrossoverChannelControl virtualCrossoverChannelControl2;
        private VirtualCrossoverChannelControl virtualCrossoverChannelControl3;
        private Label labelView;
        private CheckBox checkBoxShowSum;
        private CheckBox checkBoxShowLoss;
        private RadioButton radioViewMagnitude;
        private RadioButton radioViewPhase;
        private Label labelSmoothing;
        private DarkComboBox comboBoxSmoothing;
        private Button buttonAutoDelay;
        private Button buttonAutoSetup;
        private Button buttonCaptureOverlay;
        private Button buttonExport;
        private Button buttonPhaseGate;
        private Button buttonSessionImport;
        private Button buttonSessionExport;
        private Label labelMetric;
        private Label labelCrossoverWarning;
    }
}
