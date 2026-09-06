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
                // Rebuilt on every open (see ShowTargetMenu), so the last one is not
                // owned by the designer container and would otherwise leak its handle.
                targetMenu?.Dispose();
                targetMenu = null;
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
            buttonAddChannel = new ReleaseClickButton();
            buttonRemoveChannel = new ReleaseClickButton();
            buttonResetChannels = new ReleaseClickButton();
            sideSelectorPanel = new Panel();
            radioSideLeft = new ReleaseClickRadioButton();
            radioSideRight = new ReleaseClickRadioButton();
            buttonCopyLeftToRight = new ReleaseClickButton();
            buttonCopyRightToLeft = new ReleaseClickButton();
            checkBoxSideLock = new ReleaseClickCheckBox();
            labelView = new Label();
            labelGroupView = new Label();
            comboBoxGroupView = new DarkComboBox();
            labelCurves = new Label();
            checkBoxShowTarget = new ReleaseClickCheckBox();
            numericTargetLevel = new DarkNumericUpDown();
            buttonTargetSettings = new ReleaseClickButton();
            labelCalibration = new Label();
            checkBoxShowSum = new ReleaseClickCheckBox();
            checkBoxHybrid = new ReleaseClickCheckBox();
            labelSumLoss = new Label();
            comboBoxSumLoss = new DarkComboBox();
            radioViewMagnitude = new ReleaseClickRadioButton();
            radioViewPhase = new ReleaseClickRadioButton();
            radioViewImpulse = new ReleaseClickRadioButton();
            labelSmoothing = new Label();
            comboBoxSmoothing = new DarkComboBox();
            buttonAutoDelay = new ReleaseClickButton();
            buttonAi = new ReleaseClickButton();
            buttonAutoSetup = new ReleaseClickButton();
            buttonDspProcessor = new ReleaseClickButton();
            buttonCaptureOverlay = new ReleaseClickButton();
            buttonExport = new ReleaseClickButton();
            buttonPhaseGate = new ReleaseClickButton();
            comboBoxCalibration = new DarkComboBox();
            buttonSessionImport = new ReleaseClickButton();
            buttonSessionExport = new ReleaseClickButton();
            buttonAudition = new ReleaseClickButton();
            dspModePanel = new RoundedPanel();
            labelDspMode = new Label();
            radioDspMagnitude = new ReleaseClickRadioButton();
            radioDspPhase = new ReleaseClickRadioButton();
            radioDspGroupDelay = new ReleaseClickRadioButton();
            radioDspCorrelation = new ReleaseClickRadioButton();
            radioDspCoherence = new ReleaseClickRadioButton();
            comboBoxCorrelationPair = new DarkComboBox();
            panel1 = new RoundedPanel();
            panel2 = new RoundedPanel();
            curvesPanel = new RoundedPanel();
            sumLossPanel = new RoundedPanel();
            targetPanel = new RoundedPanel();
            sideSelectorPanel.SuspendLayout();
            (numericTargetLevel).BeginInit();
            dspModePanel.SuspendLayout();
            panel1.SuspendLayout();
            panel2.SuspendLayout();
            curvesPanel.SuspendLayout();
            sumLossPanel.SuspendLayout();
            targetPanel.SuspendLayout();
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
            dspPlotView.Location = new Point(490, 470);
            dspPlotView.Name = "dspPlotView";
            dspPlotView.PanCursor = Cursors.Hand;
            dspPlotView.Size = new Size(739, 260);
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
            buttonAddChannel.Size = new Size(112, 24);
            buttonAddChannel.TabIndex = 4;
            buttonAddChannel.Text = "Add";
            buttonAddChannel.UseVisualStyleBackColor = false;
            // 
            // buttonRemoveChannel
            // 
            buttonRemoveChannel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            buttonRemoveChannel.BackColor = Color.FromArgb(46, 51, 67);
            buttonRemoveChannel.FlatStyle = FlatStyle.Popup;
            buttonRemoveChannel.ForeColor = Color.White;
            buttonRemoveChannel.Location = new Point(123, 700);
            buttonRemoveChannel.Name = "buttonRemoveChannel";
            buttonRemoveChannel.Size = new Size(112, 24);
            buttonRemoveChannel.TabIndex = 5;
            buttonRemoveChannel.Text = "Remove";
            buttonRemoveChannel.UseVisualStyleBackColor = false;
            // 
            // buttonResetChannels
            // 
            buttonResetChannels.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            buttonResetChannels.BackColor = Color.FromArgb(46, 51, 67);
            buttonResetChannels.FlatStyle = FlatStyle.Popup;
            buttonResetChannels.ForeColor = Color.White;
            buttonResetChannels.Location = new Point(240, 700);
            buttonResetChannels.Name = "buttonResetChannels";
            buttonResetChannels.Size = new Size(113, 24);
            buttonResetChannels.TabIndex = 6;
            buttonResetChannels.Text = "Reset";
            buttonResetChannels.UseVisualStyleBackColor = false;
            // 
            // sideSelectorPanel
            // 
            sideSelectorPanel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            sideSelectorPanel.BackColor = Color.FromArgb(40, 44, 54);
            sideSelectorPanel.Controls.Add(radioSideLeft);
            sideSelectorPanel.Controls.Add(radioSideRight);
            sideSelectorPanel.Controls.Add(buttonCopyLeftToRight);
            sideSelectorPanel.Controls.Add(buttonCopyRightToLeft);
            sideSelectorPanel.Controls.Add(checkBoxSideLock);
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
            radioSideLeft.Size = new Size(31, 19);
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
            radioSideRight.Size = new Size(32, 19);
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
            // checkBoxSideLock
            //
            checkBoxSideLock.Appearance = Appearance.Button;
            checkBoxSideLock.BackColor = Color.FromArgb(46, 51, 67);
            checkBoxSideLock.Checked = true;
            checkBoxSideLock.CheckState = CheckState.Checked;
            checkBoxSideLock.FlatAppearance.CheckedBackColor = Color.FromArgb(80, 100, 140);
            checkBoxSideLock.FlatStyle = FlatStyle.Flat;
            checkBoxSideLock.ForeColor = Color.White;
            checkBoxSideLock.Location = new Point(239, 0);
            checkBoxSideLock.Name = "checkBoxSideLock";
            checkBoxSideLock.Size = new Size(56, 23);
            checkBoxSideLock.TabIndex = 4;
            checkBoxSideLock.Text = "Lock";
            checkBoxSideLock.TextAlign = ContentAlignment.MiddleCenter;
            checkBoxSideLock.UseCompatibleTextRendering = true;
            checkBoxSideLock.UseVisualStyleBackColor = false;
            //
            // labelView
            // 
            labelView.AutoSize = true;
            labelView.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelView.ForeColor = Color.FromArgb(210, 214, 222);
            labelView.Location = new Point(358, 447);
            labelView.Name = "labelView";
            labelView.Size = new Size(36, 15);
            labelView.TabIndex = 6;
            labelView.Text = "View:";
            // 
            // labelGroupView
            // 
            labelGroupView.AutoSize = true;
            labelGroupView.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelGroupView.ForeColor = Color.FromArgb(210, 214, 222);
            labelGroupView.Location = new Point(853, 447);
            labelGroupView.Name = "labelGroupView";
            labelGroupView.Size = new Size(40, 15);
            labelGroupView.TabIndex = 26;
            labelGroupView.Text = "Show:";
            // 
            // comboBoxGroupView
            // 
            comboBoxGroupView.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxGroupView.ForeColor = Color.White;
            comboBoxGroupView.Location = new Point(896, 445);
            comboBoxGroupView.MinimumSize = new Size(36, 19);
            comboBoxGroupView.Name = "comboBoxGroupView";
            comboBoxGroupView.Size = new Size(130, 19);
            comboBoxGroupView.TabIndex = 27;
            // 
            // labelCurves
            // 
            labelCurves.AutoSize = true;
            labelCurves.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelCurves.ForeColor = Color.FromArgb(210, 214, 222);
            labelCurves.Location = new Point(358, 413);
            labelCurves.Name = "labelCurves";
            labelCurves.Size = new Size(45, 15);
            labelCurves.TabIndex = 26;
            labelCurves.Text = "Curves:";
            // 
            // checkBoxShowTarget
            // 
            checkBoxShowTarget.AutoSize = true;
            checkBoxShowTarget.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            checkBoxShowTarget.ForeColor = Color.FromArgb(55, 200, 160);
            checkBoxShowTarget.Location = new Point(6, 4);
            checkBoxShowTarget.Name = "checkBoxShowTarget";
            checkBoxShowTarget.Size = new Size(59, 19);
            checkBoxShowTarget.TabIndex = 27;
            checkBoxShowTarget.Text = "Target";
            checkBoxShowTarget.UseVisualStyleBackColor = true;
            // 
            // numericTargetLevel
            // 
            numericTargetLevel.BackColor = Color.FromArgb(55, 60, 72);
            numericTargetLevel.DecimalPlaces = 0;
            numericTargetLevel.ForeColor = Color.White;
            numericTargetLevel.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericTargetLevel.Location = new Point(71, 4);
            numericTargetLevel.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            numericTargetLevel.Minimum = new decimal(new int[] { 120, 0, 0, int.MinValue });
            numericTargetLevel.MinimumSize = new Size(36, 19);
            numericTargetLevel.Name = "numericTargetLevel";
            numericTargetLevel.Size = new Size(72, 19);
            numericTargetLevel.TabIndex = 28;
            numericTargetLevel.TextAlign = HorizontalAlignment.Right;
            numericTargetLevel.ThousandsSeparator = false;
            numericTargetLevel.Value = new decimal(new int[] { 0, 0, 0, 0 });
            numericTargetLevel.ValueSuffix = "dB";
            // 
            // buttonTargetSettings
            // 
            buttonTargetSettings.FlatStyle = FlatStyle.Popup;
            buttonTargetSettings.ForeColor = Color.White;
            buttonTargetSettings.Location = new Point(149, 2);
            buttonTargetSettings.Name = "buttonTargetSettings";
            buttonTargetSettings.Size = new Size(80, 22);
            buttonTargetSettings.TabIndex = 29;
            buttonTargetSettings.Text = "Target...";
            buttonTargetSettings.UseVisualStyleBackColor = true;
            // 
            // labelCalibration
            // 
            labelCalibration.AutoSize = true;
            labelCalibration.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelCalibration.ForeColor = Color.FromArgb(210, 214, 222);
            labelCalibration.Location = new Point(878, 413);
            labelCalibration.Name = "labelCalibration";
            labelCalibration.Size = new Size(48, 15);
            labelCalibration.TabIndex = 30;
            labelCalibration.Text = "Mic cal:";
            // 
            // checkBoxShowSum
            // 
            checkBoxShowSum.AutoSize = true;
            checkBoxShowSum.Checked = true;
            checkBoxShowSum.CheckState = CheckState.Checked;
            checkBoxShowSum.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            checkBoxShowSum.ForeColor = Color.FromArgb(210, 214, 222);
            checkBoxShowSum.Location = new Point(6, 7);
            checkBoxShowSum.Name = "checkBoxShowSum";
            checkBoxShowSum.Size = new Size(51, 19);
            checkBoxShowSum.TabIndex = 7;
            checkBoxShowSum.Text = "Sum";
            checkBoxShowSum.UseVisualStyleBackColor = true;
            // 
            // checkBoxHybrid
            // 
            checkBoxHybrid.AutoSize = true;
            checkBoxHybrid.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            checkBoxHybrid.ForeColor = Color.FromArgb(210, 214, 222);
            checkBoxHybrid.Location = new Point(1058, 411);
            checkBoxHybrid.Name = "checkBoxHybrid";
            checkBoxHybrid.Size = new Size(62, 19);
            checkBoxHybrid.TabIndex = 30;
            checkBoxHybrid.Text = "Hybrid";
            checkBoxHybrid.UseVisualStyleBackColor = true;
            // 
            // labelSumLoss
            // 
            labelSumLoss.AutoSize = true;
            labelSumLoss.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelSumLoss.ForeColor = Color.FromArgb(210, 214, 222);
            labelSumLoss.Location = new Point(6, 6);
            labelSumLoss.Name = "labelSumLoss";
            labelSumLoss.Size = new Size(55, 15);
            labelSumLoss.TabIndex = 8;
            labelSumLoss.Text = "Sum loss";
            // 
            // comboBoxSumLoss
            // 
            comboBoxSumLoss.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxSumLoss.ForeColor = Color.White;
            comboBoxSumLoss.Location = new Point(65, 4);
            comboBoxSumLoss.MinimumSize = new Size(36, 19);
            comboBoxSumLoss.Name = "comboBoxSumLoss";
            comboBoxSumLoss.Size = new Size(82, 19);
            comboBoxSumLoss.TabIndex = 8;
            // 
            // radioViewMagnitude
            // 
            radioViewMagnitude.AutoSize = true;
            radioViewMagnitude.Checked = true;
            radioViewMagnitude.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            radioViewMagnitude.ForeColor = Color.FromArgb(210, 214, 222);
            radioViewMagnitude.Location = new Point(5, 2);
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
            radioViewPhase.Location = new Point(97, 2);
            radioViewPhase.Name = "radioViewPhase";
            radioViewPhase.Size = new Size(56, 19);
            radioViewPhase.TabIndex = 10;
            radioViewPhase.Text = "Phase";
            radioViewPhase.UseVisualStyleBackColor = true;
            // 
            // radioViewImpulse
            // 
            radioViewImpulse.AutoSize = true;
            radioViewImpulse.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            radioViewImpulse.ForeColor = Color.FromArgb(210, 214, 222);
            radioViewImpulse.Location = new Point(162, 2);
            radioViewImpulse.Name = "radioViewImpulse";
            radioViewImpulse.Size = new Size(68, 19);
            radioViewImpulse.TabIndex = 11;
            radioViewImpulse.Text = "Impulse";
            radioViewImpulse.UseVisualStyleBackColor = true;
            // 
            // labelSmoothing
            // 
            labelSmoothing.AutoSize = true;
            labelSmoothing.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelSmoothing.ForeColor = Color.FromArgb(210, 214, 222);
            labelSmoothing.Location = new Point(656, 447);
            labelSmoothing.Name = "labelSmoothing";
            labelSmoothing.Size = new Size(70, 15);
            labelSmoothing.TabIndex = 10;
            labelSmoothing.Text = "Smoothing:";
            // 
            // comboBoxSmoothing
            // 
            comboBoxSmoothing.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxSmoothing.ForeColor = Color.White;
            comboBoxSmoothing.Location = new Point(730, 445);
            comboBoxSmoothing.MinimumSize = new Size(36, 19);
            comboBoxSmoothing.Name = "comboBoxSmoothing";
            comboBoxSmoothing.Size = new Size(100, 19);
            comboBoxSmoothing.TabIndex = 11;
            // 
            // buttonAutoDelay
            // 
            buttonAutoDelay.BackColor = Color.FromArgb(46, 51, 67);
            buttonAutoDelay.FlatStyle = FlatStyle.Popup;
            buttonAutoDelay.ForeColor = Color.White;
            buttonAutoDelay.Location = new Point(359, 530);
            buttonAutoDelay.Name = "buttonAutoDelay";
            buttonAutoDelay.Size = new Size(125, 24);
            buttonAutoDelay.TabIndex = 12;
            buttonAutoDelay.Text = "Auto delay...";
            buttonAutoDelay.UseVisualStyleBackColor = false;
            // 
            // buttonAi
            // 
            buttonAi.BackColor = Color.FromArgb(46, 51, 67);
            buttonAi.FlatStyle = FlatStyle.Popup;
            buttonAi.ForeColor = Color.White;
            buttonAi.Location = new Point(359, 560);
            buttonAi.Name = "buttonAi";
            buttonAi.Size = new Size(125, 24);
            buttonAi.TabIndex = 22;
            buttonAi.Text = "AI assistant...";
            buttonAi.UseVisualStyleBackColor = false;
            // 
            // buttonAutoSetup
            // 
            buttonAutoSetup.BackColor = Color.FromArgb(46, 51, 67);
            buttonAutoSetup.FlatStyle = FlatStyle.Popup;
            buttonAutoSetup.ForeColor = Color.White;
            buttonAutoSetup.Location = new Point(359, 500);
            buttonAutoSetup.Name = "buttonAutoSetup";
            buttonAutoSetup.Size = new Size(125, 24);
            buttonAutoSetup.TabIndex = 19;
            buttonAutoSetup.Text = "Auto crossover...";
            buttonAutoSetup.UseVisualStyleBackColor = false;
            // 
            // buttonDspProcessor
            // 
            buttonDspProcessor.BackColor = Color.FromArgb(46, 51, 67);
            buttonDspProcessor.FlatStyle = FlatStyle.Popup;
            buttonDspProcessor.ForeColor = Color.White;
            buttonDspProcessor.Location = new Point(359, 470);
            buttonDspProcessor.Name = "buttonDspProcessor";
            buttonDspProcessor.Size = new Size(125, 24);
            buttonDspProcessor.TabIndex = 20;
            buttonDspProcessor.Text = "DSP processor...";
            buttonDspProcessor.UseVisualStyleBackColor = false;
            // 
            // buttonCaptureOverlay
            // 
            buttonCaptureOverlay.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
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
            buttonExport.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
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
            buttonPhaseGate.Location = new Point(1132, 409);
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
            comboBoxCalibration.Location = new Point(931, 411);
            comboBoxCalibration.MinimumSize = new Size(36, 19);
            comboBoxCalibration.Name = "comboBoxCalibration";
            comboBoxCalibration.Size = new Size(110, 19);
            comboBoxCalibration.TabIndex = 20;
            // 
            // buttonSessionImport
            // 
            buttonSessionImport.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
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
            buttonSessionExport.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            buttonSessionExport.FlatStyle = FlatStyle.Popup;
            buttonSessionExport.ForeColor = Color.White;
            buttonSessionExport.Location = new Point(358, 642);
            buttonSessionExport.Name = "buttonSessionExport";
            buttonSessionExport.Size = new Size(125, 24);
            buttonSessionExport.TabIndex = 18;
            buttonSessionExport.Text = "Save session...";
            buttonSessionExport.UseVisualStyleBackColor = true;
            // 
            // buttonAudition
            // 
            buttonAudition.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            buttonAudition.FlatStyle = FlatStyle.Popup;
            buttonAudition.ForeColor = Color.White;
            buttonAudition.Location = new Point(358, 612);
            buttonAudition.Name = "buttonAudition";
            buttonAudition.Size = new Size(125, 24);
            buttonAudition.TabIndex = 21;
            buttonAudition.Text = "Audition track...";
            buttonAudition.UseVisualStyleBackColor = true;
            // 
            // dspModePanel
            // 
            dspModePanel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            dspModePanel.BackColor = Color.FromArgb(46, 50, 62);
            dspModePanel.Controls.Add(labelDspMode);
            dspModePanel.Controls.Add(radioDspMagnitude);
            dspModePanel.Controls.Add(radioDspPhase);
            dspModePanel.Controls.Add(radioDspGroupDelay);
            dspModePanel.CornerRadius = 4;
            dspModePanel.Location = new Point(490, 733);
            dspModePanel.Name = "dspModePanel";
            dspModePanel.Size = new Size(302, 23);
            dspModePanel.TabIndex = 20;
            // 
            // labelDspMode
            // 
            labelDspMode.AutoSize = true;
            labelDspMode.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelDspMode.ForeColor = Color.FromArgb(210, 214, 222);
            labelDspMode.Location = new Point(2, 4);
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
            radioDspMagnitude.Location = new Point(42, 2);
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
            radioDspPhase.Location = new Point(131, 2);
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
            radioDspGroupDelay.Location = new Point(193, 2);
            radioDspGroupDelay.Name = "radioDspGroupDelay";
            radioDspGroupDelay.Size = new Size(89, 19);
            radioDspGroupDelay.TabIndex = 2;
            radioDspGroupDelay.Text = "Group delay";
            radioDspGroupDelay.UseVisualStyleBackColor = true;
            // 
            // radioDspCorrelation
            // 
            radioDspCorrelation.AutoSize = true;
            radioDspCorrelation.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            radioDspCorrelation.ForeColor = Color.FromArgb(210, 214, 222);
            radioDspCorrelation.Location = new Point(5, 2);
            radioDspCorrelation.Name = "radioDspCorrelation";
            radioDspCorrelation.Size = new Size(83, 19);
            radioDspCorrelation.TabIndex = 3;
            radioDspCorrelation.Text = "Correlation";
            radioDspCorrelation.UseVisualStyleBackColor = true;
            // 
            // radioDspCoherence
            // 
            radioDspCoherence.AutoSize = true;
            radioDspCoherence.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            radioDspCoherence.ForeColor = Color.FromArgb(210, 214, 222);
            radioDspCoherence.Location = new Point(88, 2);
            radioDspCoherence.Name = "radioDspCoherence";
            radioDspCoherence.Size = new Size(81, 19);
            radioDspCoherence.TabIndex = 4;
            radioDspCoherence.Text = "Coherence";
            radioDspCoherence.UseVisualStyleBackColor = true;
            // 
            // comboBoxCorrelationPair
            // 
            comboBoxCorrelationPair.BackColor = Color.FromArgb(55, 60, 72);
            comboBoxCorrelationPair.Enabled = false;
            comboBoxCorrelationPair.ForeColor = Color.White;
            comboBoxCorrelationPair.Location = new Point(178, 2);
            comboBoxCorrelationPair.MinimumSize = new Size(36, 19);
            comboBoxCorrelationPair.Name = "comboBoxCorrelationPair";
            comboBoxCorrelationPair.Size = new Size(74, 19);
            comboBoxCorrelationPair.TabIndex = 21;
            // 
            // panel1
            // 
            panel1.BackColor = Color.FromArgb(46, 50, 62);
            panel1.Controls.Add(radioViewMagnitude);
            panel1.Controls.Add(radioViewImpulse);
            panel1.Controls.Add(radioViewPhase);
            panel1.CornerRadius = 4;
            panel1.Location = new Point(406, 443);
            panel1.Name = "panel1";
            panel1.Size = new Size(233, 23);
            panel1.TabIndex = 24;
            // 
            // panel2
            // 
            panel2.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            panel2.BackColor = Color.FromArgb(46, 50, 62);
            panel2.Controls.Add(radioDspCorrelation);
            panel2.Controls.Add(radioDspCoherence);
            panel2.Controls.Add(comboBoxCorrelationPair);
            panel2.CornerRadius = 4;
            panel2.Location = new Point(798, 733);
            panel2.Name = "panel2";
            panel2.Size = new Size(255, 23);
            panel2.TabIndex = 25;
            // 
            // curvesPanel
            // 
            curvesPanel.Controls.Add(checkBoxShowSum);
            curvesPanel.Controls.Add(sumLossPanel);
            curvesPanel.Controls.Add(targetPanel);
            curvesPanel.CornerRadius = 4;
            curvesPanel.Location = new Point(406, 406);
            curvesPanel.Name = "curvesPanel";
            curvesPanel.Size = new Size(463, 32);
            curvesPanel.TabIndex = 7;
            // 
            // sumLossPanel
            // 
            sumLossPanel.BackColor = Color.FromArgb(46, 50, 62);
            sumLossPanel.Controls.Add(labelSumLoss);
            sumLossPanel.Controls.Add(comboBoxSumLoss);
            sumLossPanel.CornerRadius = 4;
            sumLossPanel.Location = new Point(63, 3);
            sumLossPanel.Name = "sumLossPanel";
            sumLossPanel.Size = new Size(153, 26);
            sumLossPanel.TabIndex = 8;
            // 
            // targetPanel
            // 
            targetPanel.BackColor = Color.FromArgb(46, 50, 62);
            targetPanel.Controls.Add(checkBoxShowTarget);
            targetPanel.Controls.Add(numericTargetLevel);
            targetPanel.Controls.Add(buttonTargetSettings);
            targetPanel.CornerRadius = 4;
            targetPanel.Location = new Point(222, 3);
            targetPanel.Name = "targetPanel";
            targetPanel.Size = new Size(235, 26);
            targetPanel.TabIndex = 27;
            // 
            // VirtualCrossoverPanel
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScroll = true;
            BackColor = Color.FromArgb(40, 44, 54);
            BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(panel2);
            Controls.Add(panel1);
            Controls.Add(labelView);
            Controls.Add(labelGroupView);
            Controls.Add(comboBoxGroupView);
            Controls.Add(labelCurves);
            Controls.Add(curvesPanel);
            Controls.Add(labelCalibration);
            Controls.Add(checkBoxHybrid);
            Controls.Add(labelSmoothing);
            Controls.Add(comboBoxSmoothing);
            Controls.Add(buttonAutoDelay);
            Controls.Add(buttonAi);
            Controls.Add(buttonAutoSetup);
            Controls.Add(buttonDspProcessor);
            Controls.Add(buttonCaptureOverlay);
            Controls.Add(buttonExport);
            Controls.Add(buttonPhaseGate);
            Controls.Add(comboBoxCalibration);
            Controls.Add(buttonSessionImport);
            Controls.Add(buttonSessionExport);
            Controls.Add(buttonAudition);
            Controls.Add(dspModePanel);
            Controls.Add(channelListPanel);
            Controls.Add(buttonAddChannel);
            Controls.Add(buttonRemoveChannel);
            Controls.Add(buttonResetChannels);
            Controls.Add(sideSelectorPanel);
            Controls.Add(dspPlotView);
            Controls.Add(mainPlotView);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            Name = "VirtualCrossoverPanel";
            Padding = new Padding(6);
            Size = new Size(1246, 770);
            sideSelectorPanel.ResumeLayout(false);
            sideSelectorPanel.PerformLayout();
            (numericTargetLevel).EndInit();
            dspModePanel.ResumeLayout(false);
            dspModePanel.PerformLayout();
            panel1.ResumeLayout(false);
            panel1.PerformLayout();
            panel2.ResumeLayout(false);
            panel2.PerformLayout();
            curvesPanel.ResumeLayout(false);
            curvesPanel.PerformLayout();
            sumLossPanel.ResumeLayout(false);
            sumLossPanel.PerformLayout();
            targetPanel.ResumeLayout(false);
            targetPanel.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private OxyPlot.WindowsForms.PlotView mainPlotView;
        private OxyPlot.WindowsForms.PlotView dspPlotView;
        private FlowLayoutPanel channelListPanel;
        private ReleaseClickButton buttonAddChannel;
        private ReleaseClickButton buttonRemoveChannel;
        private ReleaseClickButton buttonResetChannels;
        private Panel sideSelectorPanel;
        private ReleaseClickRadioButton radioSideLeft;
        private ReleaseClickRadioButton radioSideRight;
        private ReleaseClickButton buttonCopyLeftToRight;
        private ReleaseClickButton buttonCopyRightToLeft;
        private ReleaseClickCheckBox checkBoxSideLock;
        private Label labelView;
        private Label labelGroupView;
        private DarkComboBox comboBoxGroupView;
        private Label labelCurves;
        private ReleaseClickCheckBox checkBoxShowTarget;
        private DarkNumericUpDown numericTargetLevel;
        private ReleaseClickButton buttonTargetSettings;
        private Label labelCalibration;
        private ReleaseClickCheckBox checkBoxShowSum;
        private ReleaseClickCheckBox checkBoxHybrid;
        private Label labelSumLoss;
        private DarkComboBox comboBoxSumLoss;
        private RoundedPanel curvesPanel;
        private RoundedPanel sumLossPanel;
        private RoundedPanel targetPanel;
        private ReleaseClickRadioButton radioViewMagnitude;
        private ReleaseClickRadioButton radioViewPhase;
        private ReleaseClickRadioButton radioViewImpulse;
        private Label labelSmoothing;
        private DarkComboBox comboBoxSmoothing;
        private ReleaseClickButton buttonAutoDelay;
        private ReleaseClickButton buttonAi;
        private ReleaseClickButton buttonAutoSetup;
        private ReleaseClickButton buttonDspProcessor;
        private ReleaseClickButton buttonCaptureOverlay;
        private ReleaseClickButton buttonExport;
        private ReleaseClickButton buttonPhaseGate;
        private DarkComboBox comboBoxCalibration;
        private ReleaseClickButton buttonSessionImport;
        private ReleaseClickButton buttonSessionExport;
        private ReleaseClickButton buttonAudition;
        private RoundedPanel dspModePanel;
        private Label labelDspMode;
        private ReleaseClickRadioButton radioDspMagnitude;
        private ReleaseClickRadioButton radioDspPhase;
        private ReleaseClickRadioButton radioDspGroupDelay;
        private ReleaseClickRadioButton radioDspCorrelation;
        private ReleaseClickRadioButton radioDspCoherence;
        private DarkComboBox comboBoxCorrelationPair;
        private RoundedPanel panel1;
        private RoundedPanel panel2;
    }
}
