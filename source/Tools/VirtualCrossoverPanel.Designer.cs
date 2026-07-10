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
            channelListPanel = new FlowLayoutPanel();
            buttonAddChannel = new Button();
            buttonRemoveChannel = new Button();
            sideSelectorPanel = new Panel();
            radioSideLeft = new RadioButton();
            radioSideRight = new RadioButton();
            buttonCopyLeftToRight = new Button();
            buttonCopyRightToLeft = new Button();
            labelSceneOffset = new Label();
            numericSceneOffset = new DarkNumericUpDown();
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
            comboBoxCalibration = new DarkComboBox();
            buttonSessionImport = new Button();
            buttonSessionExport = new Button();
            labelCrossoverWarning = new Label();
            dspModePanel = new Panel();
            labelDspMode = new Label();
            radioDspMagnitude = new RadioButton();
            radioDspPhase = new RadioButton();
            radioDspGroupDelay = new RadioButton();
            dspModePanel.SuspendLayout();
            sideSelectorPanel.SuspendLayout();
            (numericSceneOffset).BeginInit();
            SuspendLayout();
            // 
            // mainPlotView
            // 
            mainPlotView.BackColor = Color.FromArgb(40, 44, 80);
            mainPlotView.Location = new Point(358, 9);
            mainPlotView.Name = "mainPlotView";
            mainPlotView.PanCursor = Cursors.Hand;
            mainPlotView.Size = new Size(870, 392);
            mainPlotView.TabIndex = 1;
            mainPlotView.Text = "plotView1";
            mainPlotView.ZoomHorizontalCursor = Cursors.SizeWE;
            mainPlotView.ZoomRectangleCursor = Cursors.SizeNWSE;
            mainPlotView.ZoomVerticalCursor = Cursors.SizeNS;
            // 
            // dspPlotView
            // 
            dspPlotView.BackColor = Color.FromArgb(40, 44, 80);
            dspPlotView.Location = new Point(489, 499);
            dspPlotView.Name = "dspPlotView";
            dspPlotView.PanCursor = Cursors.Hand;
            dspPlotView.Size = new Size(739, 256);
            dspPlotView.TabIndex = 2;
            dspPlotView.Text = "plotView2";
            dspPlotView.ZoomHorizontalCursor = Cursors.SizeWE;
            dspPlotView.ZoomRectangleCursor = Cursors.SizeNWSE;
            dspPlotView.ZoomVerticalCursor = Cursors.SizeNS;
            // 
            // channelListPanel
            // 
            channelListPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            channelListPanel.AutoScroll = true;
            channelListPanel.BackColor = Color.FromArgb(40, 44, 54);
            channelListPanel.FlowDirection = FlowDirection.TopDown;
            channelListPanel.Location = new Point(6, 6);
            channelListPanel.Name = "channelListPanel";
            channelListPanel.Size = new Size(347, 684);
            channelListPanel.TabIndex = 3;
            channelListPanel.WrapContents = false;
            // 
            // buttonAddChannel
            // 
            buttonAddChannel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            buttonAddChannel.BackColor = Color.FromArgb(46, 51, 67);
            buttonAddChannel.FlatStyle = FlatStyle.Popup;
            buttonAddChannel.ForeColor = Color.White;
            buttonAddChannel.Location = new Point(6, 700);
            buttonAddChannel.Name = "buttonAddChannel";
            buttonAddChannel.Size = new Size(158, 24);
            buttonAddChannel.TabIndex = 4;
            buttonAddChannel.Text = "Add channel";
            buttonAddChannel.UseVisualStyleBackColor = false;
            // 
            // buttonRemoveChannel
            // 
            buttonRemoveChannel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            buttonRemoveChannel.BackColor = Color.FromArgb(46, 51, 67);
            buttonRemoveChannel.FlatStyle = FlatStyle.Popup;
            buttonRemoveChannel.ForeColor = Color.White;
            buttonRemoveChannel.Location = new Point(171, 700);
            buttonRemoveChannel.Name = "buttonRemoveChannel";
            buttonRemoveChannel.Size = new Size(158, 24);
            buttonRemoveChannel.TabIndex = 5;
            buttonRemoveChannel.Text = "Remove channel";
            buttonRemoveChannel.UseVisualStyleBackColor = false;
            //
            // sideSelectorPanel
            //
            // The side radios live in their own container so they form a radio
            // group of their own — as direct panel children they would join the
            // Magnitude/Phase view group.
            sideSelectorPanel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            sideSelectorPanel.BackColor = Color.FromArgb(40, 44, 54);
            sideSelectorPanel.Controls.Add(radioSideLeft);
            sideSelectorPanel.Controls.Add(radioSideRight);
            sideSelectorPanel.Controls.Add(buttonCopyLeftToRight);
            sideSelectorPanel.Controls.Add(buttonCopyRightToLeft);
            sideSelectorPanel.Location = new Point(6, 730);
            sideSelectorPanel.Name = "sideSelectorPanel";
            sideSelectorPanel.Size = new Size(347, 24);
            sideSelectorPanel.TabIndex = 21;
            //
            // radioSideLeft
            //
            radioSideLeft.AutoSize = true;
            radioSideLeft.Checked = true;
            radioSideLeft.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            radioSideLeft.ForeColor = Color.FromArgb(210, 214, 222);
            radioSideLeft.Location = new Point(0, 2);
            radioSideLeft.Name = "radioSideLeft";
            radioSideLeft.Size = new Size(50, 19);
            radioSideLeft.TabIndex = 0;
            radioSideLeft.TabStop = true;
            radioSideLeft.Text = "L";
            radioSideLeft.UseVisualStyleBackColor = true;
            //
            // radioSideRight
            //
            radioSideRight.AutoSize = true;
            radioSideRight.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            radioSideRight.ForeColor = Color.FromArgb(210, 214, 222);
            radioSideRight.Location = new Point(52, 2);
            radioSideRight.Name = "radioSideRight";
            radioSideRight.Size = new Size(50, 19);
            radioSideRight.TabIndex = 1;
            radioSideRight.Text = "R";
            radioSideRight.UseVisualStyleBackColor = true;
            //
            // buttonCopyLeftToRight
            //
            buttonCopyLeftToRight.BackColor = Color.FromArgb(46, 51, 67);
            buttonCopyLeftToRight.FlatStyle = FlatStyle.Popup;
            buttonCopyLeftToRight.ForeColor = Color.White;
            buttonCopyLeftToRight.Location = new Point(115, 0);
            buttonCopyLeftToRight.Name = "buttonCopyLeftToRight";
            buttonCopyLeftToRight.Size = new Size(56, 23);
            buttonCopyLeftToRight.TabIndex = 2;
            buttonCopyLeftToRight.Text = "L→R";
            buttonCopyLeftToRight.UseVisualStyleBackColor = false;
            //
            // buttonCopyRightToLeft
            //
            buttonCopyRightToLeft.BackColor = Color.FromArgb(46, 51, 67);
            buttonCopyRightToLeft.FlatStyle = FlatStyle.Popup;
            buttonCopyRightToLeft.ForeColor = Color.White;
            buttonCopyRightToLeft.Location = new Point(177, 0);
            buttonCopyRightToLeft.Name = "buttonCopyRightToLeft";
            buttonCopyRightToLeft.Size = new Size(56, 23);
            buttonCopyRightToLeft.TabIndex = 3;
            buttonCopyRightToLeft.Text = "R→L";
            buttonCopyRightToLeft.UseVisualStyleBackColor = false;
            //
            // labelSceneOffset
            //
            labelSceneOffset.AutoSize = true;
            labelSceneOffset.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelSceneOffset.ForeColor = Color.FromArgb(210, 214, 222);
            labelSceneOffset.Location = new Point(358, 532);
            labelSceneOffset.Name = "labelSceneOffset";
            labelSceneOffset.Size = new Size(90, 15);
            labelSceneOffset.TabIndex = 22;
            labelSceneOffset.Text = "L/R offset, ms";
            //
            // numericSceneOffset
            //
            numericSceneOffset.BackColor = Color.FromArgb(55, 60, 72);
            numericSceneOffset.DecimalPlaces = 2;
            numericSceneOffset.ForeColor = Color.White;
            numericSceneOffset.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            numericSceneOffset.Location = new Point(358, 550);
            numericSceneOffset.Maximum = new decimal(new int[] { 5, 0, 0, 0 });
            numericSceneOffset.Minimum = new decimal(new int[] { 5, 0, 0, int.MinValue });
            numericSceneOffset.MinimumSize = new Size(36, 19);
            numericSceneOffset.Name = "numericSceneOffset";
            numericSceneOffset.Size = new Size(125, 19);
            numericSceneOffset.TabIndex = 23;
            numericSceneOffset.TextAlign = HorizontalAlignment.Right;
            numericSceneOffset.ThousandsSeparator = false;
            numericSceneOffset.Value = new decimal(new int[] { 25, 0, 0, 131072 });
            //
            // labelView
            // 
            labelView.AutoSize = true;
            labelView.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelView.ForeColor = Color.FromArgb(210, 214, 222);
            labelView.Location = new Point(358, 414);
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
            checkBoxShowSum.Location = new Point(403, 412);
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
            checkBoxShowLoss.Location = new Point(461, 412);
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
            radioViewMagnitude.Location = new Point(545, 411);
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
            radioViewPhase.Location = new Point(637, 411);
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
            labelSmoothing.Location = new Point(707, 414);
            labelSmoothing.Name = "labelSmoothing";
            labelSmoothing.Size = new Size(67, 15);
            labelSmoothing.TabIndex = 10;
            labelSmoothing.Text = "Smoothing";
            // 
            // comboBoxSmoothing
            // 
            comboBoxSmoothing.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxSmoothing.ForeColor = Color.White;
            comboBoxSmoothing.Location = new Point(781, 412);
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
            buttonAutoDelay.Location = new Point(358, 577);
            buttonAutoDelay.Name = "buttonAutoDelay";
            buttonAutoDelay.Size = new Size(125, 24);
            buttonAutoDelay.TabIndex = 12;
            buttonAutoDelay.Text = "Auto delay";
            buttonAutoDelay.UseVisualStyleBackColor = false;
            // 
            // buttonAutoSetup
            // 
            buttonAutoSetup.BackColor = Color.FromArgb(46, 51, 67);
            buttonAutoSetup.FlatStyle = FlatStyle.Popup;
            buttonAutoSetup.ForeColor = Color.White;
            buttonAutoSetup.Location = new Point(358, 499);
            buttonAutoSetup.Name = "buttonAutoSetup";
            buttonAutoSetup.Size = new Size(125, 24);
            buttonAutoSetup.TabIndex = 19;
            buttonAutoSetup.Text = "Auto crossover...";
            buttonAutoSetup.UseVisualStyleBackColor = false;
            // 
            // buttonCaptureOverlay
            // 
            buttonCaptureOverlay.FlatStyle = FlatStyle.Popup;
            buttonCaptureOverlay.ForeColor = Color.White;
            buttonCaptureOverlay.Location = new Point(358, 702);
            buttonCaptureOverlay.Name = "buttonCaptureOverlay";
            buttonCaptureOverlay.Size = new Size(125, 24);
            buttonCaptureOverlay.TabIndex = 13;
            buttonCaptureOverlay.Text = "Capture to overlay";
            buttonCaptureOverlay.UseVisualStyleBackColor = true;
            // 
            // buttonExport
            // 
            buttonExport.FlatStyle = FlatStyle.Popup;
            buttonExport.ForeColor = Color.White;
            buttonExport.Location = new Point(358, 732);
            buttonExport.Name = "buttonExport";
            buttonExport.Size = new Size(125, 24);
            buttonExport.TabIndex = 14;
            buttonExport.Text = "Export...";
            buttonExport.UseVisualStyleBackColor = true;
            // 
            // buttonPhaseGate
            // 
            buttonPhaseGate.FlatStyle = FlatStyle.Popup;
            buttonPhaseGate.ForeColor = Color.White;
            buttonPhaseGate.Location = new Point(877, 410);
            buttonPhaseGate.Name = "buttonPhaseGate";
            buttonPhaseGate.Size = new Size(80, 24);
            buttonPhaseGate.TabIndex = 16;
            buttonPhaseGate.Text = "Gate...";
            buttonPhaseGate.UseVisualStyleBackColor = true;
            // 
            // comboBoxCalibration
            // 
            comboBoxCalibration.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxCalibration.ForeColor = Color.White;
            comboBoxCalibration.Location = new Point(965, 413);
            comboBoxCalibration.MinimumSize = new Size(36, 19);
            comboBoxCalibration.Name = "comboBoxCalibration";
            comboBoxCalibration.Size = new Size(110, 19);
            comboBoxCalibration.TabIndex = 20;
            // 
            // buttonSessionImport
            // 
            buttonSessionImport.FlatStyle = FlatStyle.Popup;
            buttonSessionImport.ForeColor = Color.White;
            buttonSessionImport.Location = new Point(358, 672);
            buttonSessionImport.Name = "buttonSessionImport";
            buttonSessionImport.Size = new Size(125, 24);
            buttonSessionImport.TabIndex = 17;
            buttonSessionImport.Text = "Load session...";
            buttonSessionImport.UseVisualStyleBackColor = true;
            // 
            // buttonSessionExport
            // 
            buttonSessionExport.FlatStyle = FlatStyle.Popup;
            buttonSessionExport.ForeColor = Color.White;
            buttonSessionExport.Location = new Point(358, 642);
            buttonSessionExport.Name = "buttonSessionExport";
            buttonSessionExport.Size = new Size(125, 24);
            buttonSessionExport.TabIndex = 18;
            buttonSessionExport.Text = "Save session...";
            buttonSessionExport.UseVisualStyleBackColor = true;
            // 
            // labelCrossoverWarning
            // 
            labelCrossoverWarning.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelCrossoverWarning.ForeColor = Color.FromArgb(235, 110, 95);
            labelCrossoverWarning.Location = new Point(358, 434);
            labelCrossoverWarning.Name = "labelCrossoverWarning";
            labelCrossoverWarning.Size = new Size(870, 16);
            labelCrossoverWarning.TabIndex = 19;
            labelCrossoverWarning.Visible = false;
            // 
            // dspModePanel
            // 
            dspModePanel.BackColor = Color.FromArgb(40, 44, 54);
            dspModePanel.Controls.Add(labelDspMode);
            dspModePanel.Controls.Add(radioDspMagnitude);
            dspModePanel.Controls.Add(radioDspPhase);
            dspModePanel.Controls.Add(radioDspGroupDelay);
            dspModePanel.Location = new Point(489, 474);
            dspModePanel.Name = "dspModePanel";
            dspModePanel.Size = new Size(360, 22);
            dspModePanel.TabIndex = 20;
            // 
            // labelDspMode
            // 
            labelDspMode.AutoSize = true;
            labelDspMode.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelDspMode.ForeColor = Color.FromArgb(210, 214, 222);
            labelDspMode.Location = new Point(0, 3);
            labelDspMode.Name = "labelDspMode";
            labelDspMode.Size = new Size(30, 15);
            labelDspMode.TabIndex = 0;
            labelDspMode.Text = "DSP";
            // 
            // radioDspMagnitude
            // 
            radioDspMagnitude.AutoSize = true;
            radioDspMagnitude.Checked = true;
            radioDspMagnitude.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            radioDspMagnitude.ForeColor = Color.FromArgb(210, 214, 222);
            radioDspMagnitude.Location = new Point(40, 1);
            radioDspMagnitude.Name = "radioDspMagnitude";
            radioDspMagnitude.Size = new Size(83, 19);
            radioDspMagnitude.TabIndex = 0;
            radioDspMagnitude.TabStop = true;
            radioDspMagnitude.Text = "Magnitude";
            radioDspMagnitude.UseVisualStyleBackColor = true;
            // 
            // radioDspPhase
            // 
            radioDspPhase.AutoSize = true;
            radioDspPhase.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            radioDspPhase.ForeColor = Color.FromArgb(210, 214, 222);
            radioDspPhase.Location = new Point(129, 1);
            radioDspPhase.Name = "radioDspPhase";
            radioDspPhase.Size = new Size(56, 19);
            radioDspPhase.TabIndex = 1;
            radioDspPhase.Text = "Phase";
            radioDspPhase.UseVisualStyleBackColor = true;
            // 
            // radioDspGroupDelay
            // 
            radioDspGroupDelay.AutoSize = true;
            radioDspGroupDelay.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            radioDspGroupDelay.ForeColor = Color.FromArgb(210, 214, 222);
            radioDspGroupDelay.Location = new Point(191, 1);
            radioDspGroupDelay.Name = "radioDspGroupDelay";
            radioDspGroupDelay.Size = new Size(89, 19);
            radioDspGroupDelay.TabIndex = 2;
            radioDspGroupDelay.Text = "Group delay";
            radioDspGroupDelay.UseVisualStyleBackColor = true;
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
            Controls.Add(comboBoxCalibration);
            Controls.Add(buttonSessionImport);
            Controls.Add(buttonSessionExport);
            Controls.Add(labelCrossoverWarning);
            Controls.Add(dspModePanel);
            Controls.Add(channelListPanel);
            Controls.Add(buttonAddChannel);
            Controls.Add(buttonRemoveChannel);
            Controls.Add(sideSelectorPanel);
            Controls.Add(labelSceneOffset);
            Controls.Add(numericSceneOffset);
            Controls.Add(dspPlotView);
            Controls.Add(mainPlotView);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            Name = "VirtualCrossoverPanel";
            Padding = new Padding(6);
            Size = new Size(1246, 770);
            dspModePanel.ResumeLayout(false);
            dspModePanel.PerformLayout();
            sideSelectorPanel.ResumeLayout(false);
            sideSelectorPanel.PerformLayout();
            (numericSceneOffset).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private OxyPlot.WindowsForms.PlotView mainPlotView;
        private OxyPlot.WindowsForms.PlotView dspPlotView;
        private FlowLayoutPanel channelListPanel;
        private Button buttonAddChannel;
        private Button buttonRemoveChannel;
        private Panel sideSelectorPanel;
        private RadioButton radioSideLeft;
        private RadioButton radioSideRight;
        private Button buttonCopyLeftToRight;
        private Button buttonCopyRightToLeft;
        private Label labelSceneOffset;
        private DarkNumericUpDown numericSceneOffset;
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
        private DarkComboBox comboBoxCalibration;
        private Button buttonSessionImport;
        private Button buttonSessionExport;
        private Label labelCrossoverWarning;
        private Panel dspModePanel;
        private Label labelDspMode;
        private RadioButton radioDspMagnitude;
        private RadioButton radioDspPhase;
        private RadioButton radioDspGroupDelay;
    }
}
