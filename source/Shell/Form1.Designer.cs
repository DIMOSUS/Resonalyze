namespace Resonalyze
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeAppResources();
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            buttonRecord = new Button();
            plotView1 = new OxyPlot.WindowsForms.PlotView();
            overlays = new Panel();
            overlayPanel1 = new Panel();
            buttonSaveOverlay = new Button();
            numericUpDown1 = new DarkNumericUpDown();
            checkBox1 = new CheckBox();
            buttonRecordOpt = new Button();
            buttonSave = new Button();
            buttonLoad = new Button();
            toolTip1 = new ToolTip(components);
            buttonOverlayShowAll = new Button();
            buttonOverlayHideAll = new Button();
            buttonCurrentModeSettings = new Button();
            panel1 = new Panel();
            buttonCompare = new Button();
            inputLevelMeterPanel = new InputLevelMeterPanel();
            buttonHistory = new Button();
            chromeTitleBar = new ChromeTitleBar();
            timeAlignmentPanel = new TimeAlignmentPanel();
            eqWizardPanel = new EqWizardPanel();
            signalGeneratorPanel = new SignalGeneratorPanel();
            virtualCrossoverPanel = new VirtualCrossoverPanel();
            eqResultsPanel = new EqResultsPanel();
            virtualDspMetricLabel = new Label();
            overlays.SuspendLayout();
            overlayPanel1.SuspendLayout();
            (numericUpDown1).BeginInit();
            panel1.SuspendLayout();
            SuspendLayout();
            // 
            // buttonRecord
            // 
            buttonRecord.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonRecord.BackColor = Color.FromArgb(50, 55, 80);
            buttonRecord.FlatStyle = FlatStyle.Popup;
            buttonRecord.Font = new Font("Segoe UI", 10F);
            buttonRecord.ForeColor = Color.White;
            buttonRecord.Location = new Point(3, 3);
            buttonRecord.Name = "buttonRecord";
            buttonRecord.Size = new Size(210, 23);
            buttonRecord.TabIndex = 0;
            buttonRecord.Text = "Start";
            buttonRecord.UseCompatibleTextRendering = true;
            buttonRecord.UseVisualStyleBackColor = false;
            buttonRecord.Click += buttonRecord_Click;
            // 
            // plotView1
            // 
            plotView1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            plotView1.BackColor = Color.FromArgb(50, 55, 100);
            plotView1.ForeColor = Color.White;
            plotView1.Location = new Point(12, 52);
            plotView1.Name = "plotView1";
            plotView1.PanCursor = Cursors.Hand;
            plotView1.Size = new Size(1246, 768);
            plotView1.TabIndex = 1;
            plotView1.Text = "plotView1";
            plotView1.ZoomHorizontalCursor = Cursors.SizeWE;
            plotView1.ZoomRectangleCursor = Cursors.SizeNWSE;
            plotView1.ZoomVerticalCursor = Cursors.SizeNS;
            // 
            // overlays
            // 
            overlays.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            overlays.BorderStyle = BorderStyle.FixedSingle;
            overlays.Controls.Add(overlayPanel1);
            overlays.Location = new Point(1264, 479);
            overlays.Name = "overlays";
            overlays.Size = new Size(218, 341);
            overlays.TabIndex = 6;
            // 
            // overlayPanel1
            // 
            overlayPanel1.BackColor = Color.OrangeRed;
            overlayPanel1.Controls.Add(buttonSaveOverlay);
            overlayPanel1.Controls.Add(numericUpDown1);
            overlayPanel1.Controls.Add(checkBox1);
            overlayPanel1.Location = new Point(3, 3);
            overlayPanel1.Name = "overlayPanel1";
            overlayPanel1.Size = new Size(206, 25);
            overlayPanel1.TabIndex = 3;
            // 
            // buttonSaveOverlay
            // 
            buttonSaveOverlay.BackColor = Color.FromArgb(50, 55, 100);
            buttonSaveOverlay.FlatStyle = FlatStyle.Popup;
            buttonSaveOverlay.ForeColor = Color.White;
            buttonSaveOverlay.Location = new Point(23, 3);
            buttonSaveOverlay.Name = "buttonSaveOverlay";
            buttonSaveOverlay.Size = new Size(38, 19);
            buttonSaveOverlay.TabIndex = 1;
            buttonSaveOverlay.Text = "1";
            buttonSaveOverlay.UseCompatibleTextRendering = true;
            buttonSaveOverlay.UseVisualStyleBackColor = false;
            // 
            // numericUpDown1
            // 
            numericUpDown1.BackColor = Color.FromArgb(50, 55, 80);
            numericUpDown1.DecimalPlaces = 0;
            numericUpDown1.ForeColor = Color.White;
            numericUpDown1.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDown1.Location = new Point(145, 3);
            numericUpDown1.Maximum = new decimal(new int[] { 180, 0, 0, 0 });
            numericUpDown1.Minimum = new decimal(new int[] { 180, 0, 0, int.MinValue });
            numericUpDown1.MinimumSize = new Size(36, 19);
            numericUpDown1.Name = "numericUpDown1";
            numericUpDown1.Size = new Size(58, 19);
            numericUpDown1.TabIndex = 2;
            numericUpDown1.TextAlign = HorizontalAlignment.Right;
            numericUpDown1.ThousandsSeparator = false;
            numericUpDown1.Value = new decimal(new int[] { 0, 0, 0, 0 });
            // 
            // checkBox1
            // 
            checkBox1.AutoSize = true;
            checkBox1.BackColor = Color.White;
            checkBox1.FlatStyle = FlatStyle.Flat;
            checkBox1.Location = new Point(5, 7);
            checkBox1.Name = "checkBox1";
            checkBox1.Size = new Size(12, 11);
            checkBox1.TabIndex = 0;
            checkBox1.UseVisualStyleBackColor = false;
            // 
            // buttonRecordOpt
            // 
            buttonRecordOpt.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonRecordOpt.BackColor = Color.FromArgb(50, 55, 80);
            buttonRecordOpt.FlatStyle = FlatStyle.Popup;
            buttonRecordOpt.Font = new Font("Segoe UI Emoji", 10F);
            buttonRecordOpt.ForeColor = Color.White;
            buttonRecordOpt.Location = new Point(3, 32);
            buttonRecordOpt.Name = "buttonRecordOpt";
            buttonRecordOpt.Size = new Size(210, 24);
            buttonRecordOpt.TabIndex = 8;
            buttonRecordOpt.Text = "⛭ Record Settings";
            buttonRecordOpt.UseCompatibleTextRendering = true;
            buttonRecordOpt.UseVisualStyleBackColor = false;
            buttonRecordOpt.Click += buttonRecordOpt_Click;
            // 
            // buttonSave
            // 
            buttonSave.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonSave.BackColor = Color.FromArgb(50, 55, 80);
            buttonSave.FlatStyle = FlatStyle.Popup;
            buttonSave.Font = new Font("Segoe UI", 10F);
            buttonSave.ForeColor = Color.White;
            buttonSave.Location = new Point(3, 62);
            buttonSave.Name = "buttonSave";
            buttonSave.Size = new Size(102, 23);
            buttonSave.TabIndex = 18;
            buttonSave.Text = "Save";
            toolTip1.SetToolTip(buttonSave, "Save Impulse Response");
            buttonSave.UseCompatibleTextRendering = true;
            buttonSave.UseVisualStyleBackColor = false;
            buttonSave.Click += buttonSave_Click;
            // 
            // buttonLoad
            // 
            buttonLoad.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonLoad.BackColor = Color.FromArgb(50, 55, 80);
            buttonLoad.FlatStyle = FlatStyle.Popup;
            buttonLoad.Font = new Font("Segoe UI", 10F);
            buttonLoad.ForeColor = Color.White;
            buttonLoad.Location = new Point(111, 62);
            buttonLoad.Name = "buttonLoad";
            buttonLoad.Size = new Size(102, 23);
            buttonLoad.TabIndex = 19;
            buttonLoad.Text = "Load";
            toolTip1.SetToolTip(buttonLoad, "Load Impulse Response");
            buttonLoad.UseCompatibleTextRendering = true;
            buttonLoad.UseVisualStyleBackColor = false;
            buttonLoad.Click += buttonLoad_Click;
            // 
            // buttonOverlayShowAll
            // 
            buttonOverlayShowAll.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttonOverlayShowAll.BackColor = Color.FromArgb(50, 55, 80);
            buttonOverlayShowAll.FlatStyle = FlatStyle.Popup;
            buttonOverlayShowAll.Font = new Font("Segoe UI", 10F);
            buttonOverlayShowAll.ForeColor = Color.White;
            buttonOverlayShowAll.Location = new Point(1264, 453);
            buttonOverlayShowAll.Name = "buttonOverlayShowAll";
            buttonOverlayShowAll.Size = new Size(106, 23);
            buttonOverlayShowAll.TabIndex = 20;
            buttonOverlayShowAll.Text = "Show all";
            toolTip1.SetToolTip(buttonOverlayShowAll, "Show all overlays for this mode");
            buttonOverlayShowAll.UseCompatibleTextRendering = true;
            buttonOverlayShowAll.UseVisualStyleBackColor = false;
            buttonOverlayShowAll.Click += buttonOverlayShowAll_Click;
            // 
            // buttonOverlayHideAll
            // 
            buttonOverlayHideAll.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttonOverlayHideAll.BackColor = Color.FromArgb(50, 55, 80);
            buttonOverlayHideAll.FlatStyle = FlatStyle.Popup;
            buttonOverlayHideAll.Font = new Font("Segoe UI", 10F);
            buttonOverlayHideAll.ForeColor = Color.White;
            buttonOverlayHideAll.Location = new Point(1376, 453);
            buttonOverlayHideAll.Name = "buttonOverlayHideAll";
            buttonOverlayHideAll.Size = new Size(106, 23);
            buttonOverlayHideAll.TabIndex = 22;
            buttonOverlayHideAll.Text = "Hide all";
            toolTip1.SetToolTip(buttonOverlayHideAll, "Hide all overlays for this mode");
            buttonOverlayHideAll.UseCompatibleTextRendering = true;
            buttonOverlayHideAll.UseVisualStyleBackColor = false;
            buttonOverlayHideAll.Click += buttonOverlayHideAll_Click;
            // 
            // buttonCurrentModeSettings
            // 
            buttonCurrentModeSettings.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonCurrentModeSettings.BackColor = Color.FromArgb(50, 55, 80);
            buttonCurrentModeSettings.FlatStyle = FlatStyle.Popup;
            buttonCurrentModeSettings.Font = new Font("Segoe UI Emoji", 10F);
            buttonCurrentModeSettings.ForeColor = Color.White;
            buttonCurrentModeSettings.Location = new Point(1268, 309);
            buttonCurrentModeSettings.Name = "buttonCurrentModeSettings";
            buttonCurrentModeSettings.Size = new Size(210, 23);
            buttonCurrentModeSettings.TabIndex = 21;
            buttonCurrentModeSettings.Text = "🔧 Mode Settings...";
            buttonCurrentModeSettings.UseCompatibleTextRendering = true;
            buttonCurrentModeSettings.UseVisualStyleBackColor = false;
            buttonCurrentModeSettings.Click += buttonCurrentModeSettings_Click;
            // 
            // panel1
            // 
            panel1.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            panel1.BorderStyle = BorderStyle.FixedSingle;
            panel1.Controls.Add(buttonCompare);
            panel1.Controls.Add(buttonRecord);
            panel1.Controls.Add(buttonRecordOpt);
            panel1.Controls.Add(buttonSave);
            panel1.Controls.Add(buttonLoad);
            panel1.Location = new Point(1264, 146);
            panel1.Name = "panel1";
            panel1.Size = new Size(218, 119);
            panel1.TabIndex = 23;
            // 
            // buttonCompare
            // 
            buttonCompare.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonCompare.BackColor = Color.FromArgb(50, 55, 80);
            buttonCompare.FlatStyle = FlatStyle.Popup;
            buttonCompare.Font = new Font("Segoe UI Emoji", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
            buttonCompare.ForeColor = Color.White;
            buttonCompare.Location = new Point(3, 91);
            buttonCompare.Name = "buttonCompare";
            buttonCompare.Size = new Size(210, 23);
            buttonCompare.TabIndex = 31;
            buttonCompare.Text = "Compare";
            buttonCompare.UseCompatibleTextRendering = true;
            buttonCompare.UseVisualStyleBackColor = false;
            // 
            // inputLevelMeterPanel
            // 
            inputLevelMeterPanel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            inputLevelMeterPanel.BackColor = Color.FromArgb(38, 42, 52);
            inputLevelMeterPanel.Font = new Font("Segoe UI", 8.75F, FontStyle.Bold);
            inputLevelMeterPanel.ForeColor = Color.FromArgb(225, 230, 240);
            inputLevelMeterPanel.Location = new Point(1264, 52);
            inputLevelMeterPanel.Name = "inputLevelMeterPanel";
            inputLevelMeterPanel.Size = new Size(218, 88);
            inputLevelMeterPanel.TabIndex = 24;
            // 
            // buttonHistory
            // 
            buttonHistory.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonHistory.BackColor = Color.FromArgb(50, 55, 80);
            buttonHistory.FlatStyle = FlatStyle.Popup;
            buttonHistory.Font = new Font("Segoe UI Emoji", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            buttonHistory.ForeColor = Color.White;
            buttonHistory.Location = new Point(1268, 280);
            buttonHistory.Name = "buttonHistory";
            buttonHistory.Size = new Size(210, 23);
            buttonHistory.TabIndex = 25;
            buttonHistory.Text = "History";
            buttonHistory.UseCompatibleTextRendering = true;
            buttonHistory.UseVisualStyleBackColor = false;
            buttonHistory.Click += buttonHistory_Click;
            // 
            // chromeTitleBar
            // 
            chromeTitleBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            chromeTitleBar.BackColor = Color.FromArgb(28, 30, 36);
            chromeTitleBar.Location = new Point(0, 0);
            chromeTitleBar.Name = "chromeTitleBar";
            chromeTitleBar.Size = new Size(1494, 40);
            chromeTitleBar.TabIndex = 26;
            // 
            // timeAlignmentPanel
            // 
            timeAlignmentPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            timeAlignmentPanel.AutoScroll = true;
            timeAlignmentPanel.BackColor = Color.FromArgb(40, 44, 54);
            timeAlignmentPanel.BorderStyle = BorderStyle.FixedSingle;
            timeAlignmentPanel.Font = new Font("Segoe UI", 9F);
            timeAlignmentPanel.ForeColor = Color.White;
            timeAlignmentPanel.Location = new Point(12, 52);
            timeAlignmentPanel.Name = "timeAlignmentPanel";
            timeAlignmentPanel.Size = new Size(1246, 768);
            timeAlignmentPanel.TabIndex = 27;
            timeAlignmentPanel.Visible = false;
            // 
            // eqWizardPanel
            // 
            eqWizardPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            eqWizardPanel.AutoScroll = true;
            eqWizardPanel.BackColor = Color.FromArgb(40, 44, 54);
            eqWizardPanel.BorderStyle = BorderStyle.FixedSingle;
            eqWizardPanel.Font = new Font("Segoe UI", 9F);
            eqWizardPanel.ForeColor = Color.White;
            eqWizardPanel.Location = new Point(12, 52);
            eqWizardPanel.Name = "eqWizardPanel";
            eqWizardPanel.Padding = new Padding(6);
            eqWizardPanel.Size = new Size(1246, 768);
            eqWizardPanel.TabIndex = 28;
            eqWizardPanel.Visible = false;
            // 
            // signalGeneratorPanel
            // 
            signalGeneratorPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            signalGeneratorPanel.AutoScroll = true;
            signalGeneratorPanel.BackColor = Color.FromArgb(40, 44, 54);
            signalGeneratorPanel.BorderStyle = BorderStyle.FixedSingle;
            signalGeneratorPanel.Font = new Font("Segoe UI", 9F);
            signalGeneratorPanel.ForeColor = Color.White;
            signalGeneratorPanel.Location = new Point(12, 52);
            signalGeneratorPanel.Name = "signalGeneratorPanel";
            signalGeneratorPanel.Size = new Size(1246, 768);
            signalGeneratorPanel.TabIndex = 29;
            signalGeneratorPanel.Visible = false;
            // 
            // virtualCrossoverPanel
            // 
            virtualCrossoverPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            virtualCrossoverPanel.AutoScroll = true;
            virtualCrossoverPanel.BackColor = Color.FromArgb(40, 44, 54);
            virtualCrossoverPanel.BorderStyle = BorderStyle.FixedSingle;
            virtualCrossoverPanel.Font = new Font("Segoe UI", 9F);
            virtualCrossoverPanel.ForeColor = Color.White;
            virtualCrossoverPanel.Location = new Point(12, 52);
            virtualCrossoverPanel.Name = "virtualCrossoverPanel";
            virtualCrossoverPanel.Padding = new Padding(6);
            virtualCrossoverPanel.Size = new Size(1246, 768);
            virtualCrossoverPanel.TabIndex = 31;
            virtualCrossoverPanel.Visible = false;
            // 
            // eqResultsPanel
            // 
            eqResultsPanel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            eqResultsPanel.BackColor = Color.FromArgb(20, 22, 30);
            eqResultsPanel.BorderStyle = BorderStyle.FixedSingle;
            eqResultsPanel.Font = new Font("Segoe UI", 9F);
            eqResultsPanel.ForeColor = Color.FromArgb(225, 228, 235);
            eqResultsPanel.Location = new Point(1268, 479);
            eqResultsPanel.Name = "eqResultsPanel";
            eqResultsPanel.Padding = new Padding(8);
            eqResultsPanel.Size = new Size(214, 341);
            eqResultsPanel.TabIndex = 30;
            eqResultsPanel.Visible = false;
            // 
            // virtualDspMetricLabel
            // 
            virtualDspMetricLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            virtualDspMetricLabel.BackColor = Color.FromArgb(20, 22, 30);
            virtualDspMetricLabel.BorderStyle = BorderStyle.FixedSingle;
            virtualDspMetricLabel.Font = new Font("Consolas", 9F);
            virtualDspMetricLabel.ForeColor = Color.FromArgb(230, 184, 0);
            virtualDspMetricLabel.Location = new Point(1268, 389);
            virtualDspMetricLabel.Name = "virtualDspMetricLabel";
            virtualDspMetricLabel.Padding = new Padding(8);
            virtualDspMetricLabel.Size = new Size(214, 431);
            virtualDspMetricLabel.TabIndex = 31;
            virtualDspMetricLabel.Visible = false;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
            ClientSize = new Size(1494, 832);
            Controls.Add(overlays);
            Controls.Add(chromeTitleBar);
            Controls.Add(eqResultsPanel);
            Controls.Add(virtualDspMetricLabel);
            Controls.Add(virtualCrossoverPanel);
            Controls.Add(signalGeneratorPanel);
            Controls.Add(eqWizardPanel);
            Controls.Add(timeAlignmentPanel);
            Controls.Add(buttonHistory);
            Controls.Add(inputLevelMeterPanel);
            Controls.Add(panel1);
            Controls.Add(buttonOverlayHideAll);
            Controls.Add(buttonCurrentModeSettings);
            Controls.Add(buttonOverlayShowAll);
            Controls.Add(plotView1);
            FormBorderStyle = FormBorderStyle.None;
            Icon = (Icon)resources.GetObject("$this.Icon");
            MinimumSize = new Size(1494, 832);
            Name = "Form1";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Resonalyze";
            overlays.ResumeLayout(false);
            overlayPanel1.ResumeLayout(false);
            overlayPanel1.PerformLayout();
            (numericUpDown1).EndInit();
            panel1.ResumeLayout(false);
            ResumeLayout(false);

        }

        #endregion

        private Button buttonRecord;
        private OxyPlot.WindowsForms.PlotView plotView1;
        private Panel overlays;
        private Button buttonSaveOverlay;
        private CheckBox checkBox1;
        private Panel overlayPanel1;
        private Button buttonRecordOpt;
        private Button buttonSave;
        private Button buttonLoad;
        private ToolTip toolTip1;
        private Button buttonOverlayShowAll;
        private Button buttonCurrentModeSettings;
        private Button buttonOverlayHideAll;
        private Panel panel1;
        private InputLevelMeterPanel inputLevelMeterPanel;
        private DarkNumericUpDown numericUpDown1;
        private Button buttonHistory;
        private ChromeTitleBar chromeTitleBar;
        private TimeAlignmentPanel timeAlignmentPanel;
        private EqWizardPanel eqWizardPanel;
        private SignalGeneratorPanel signalGeneratorPanel;
        private VirtualCrossoverPanel virtualCrossoverPanel;
        private EqResultsPanel eqResultsPanel;
        private Label virtualDspMetricLabel;
        private Button buttonCompare;
    }
}
