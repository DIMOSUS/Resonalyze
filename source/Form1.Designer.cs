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
            buttonFR = new Button();
            buttonPR = new Button();
            buttonWaterfall = new Button();
            buttonGD = new Button();
            overlays = new Panel();
            overlayPanel1 = new Panel();
            buttonClearOverlay = new Button();
            numericUpDown1 = new NumericUpDown();
            checkBox1 = new CheckBox();
            buttonSaveOverlay = new Button();
            buttonIR = new Button();
            buttonRecordOpt = new Button();
            buttonWaterfallOpt = new Button();
            buttonFROpt = new Button();
            buttonBurstDecay = new Button();
            buttonBurstDecayOpt = new Button();
            buttonGDOpt = new Button();
            buttonPROpt = new Button();
            buttonImpOpt = new Button();
            buttonNoise = new Button();
            buttonAutocorrelation = new Button();
            buttonSave = new Button();
            buttonLoad = new Button();
            toolTip1 = new ToolTip(components);
            buttonClear = new Button();
            overlays.SuspendLayout();
            overlayPanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numericUpDown1).BeginInit();
            SuspendLayout();
            // 
            // buttonRecord
            // 
            buttonRecord.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonRecord.BackColor = Color.FromArgb(255, 255, 192);
            buttonRecord.Location = new Point(1098, 12);
            buttonRecord.Name = "buttonRecord";
            buttonRecord.Size = new Size(116, 23);
            buttonRecord.TabIndex = 0;
            buttonRecord.Text = "Start";
            buttonRecord.UseVisualStyleBackColor = false;
            buttonRecord.Click += buttonRecord_Click;
            // 
            // plotView1
            // 
            plotView1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            plotView1.BackColor = Color.FromArgb(35, 40, 80);
            plotView1.ForeColor = Color.White;
            plotView1.Location = new Point(12, 12);
            plotView1.Name = "plotView1";
            plotView1.PanCursor = Cursors.Hand;
            plotView1.Size = new Size(1080, 705);
            plotView1.TabIndex = 1;
            plotView1.Text = "plotView1";
            plotView1.ZoomHorizontalCursor = Cursors.SizeWE;
            plotView1.ZoomRectangleCursor = Cursors.SizeNWSE;
            plotView1.ZoomVerticalCursor = Cursors.SizeNS;
            // 
            // buttonFR
            // 
            buttonFR.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonFR.Location = new Point(1098, 128);
            buttonFR.Name = "buttonFR";
            buttonFR.Size = new Size(116, 23);
            buttonFR.TabIndex = 2;
            buttonFR.Text = "Frequency";
            buttonFR.UseVisualStyleBackColor = true;
            buttonFR.Click += buttonFR_Click;
            // 
            // buttonPR
            // 
            buttonPR.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonPR.Location = new Point(1098, 157);
            buttonPR.Name = "buttonPR";
            buttonPR.Size = new Size(116, 23);
            buttonPR.TabIndex = 3;
            buttonPR.Text = "Phase";
            buttonPR.UseVisualStyleBackColor = true;
            buttonPR.Click += buttonPR_Click;
            // 
            // buttonWaterfall
            // 
            buttonWaterfall.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonWaterfall.Location = new Point(1098, 215);
            buttonWaterfall.Name = "buttonWaterfall";
            buttonWaterfall.Size = new Size(116, 23);
            buttonWaterfall.TabIndex = 4;
            buttonWaterfall.Text = "Waterfall";
            buttonWaterfall.UseVisualStyleBackColor = true;
            buttonWaterfall.Click += buttonWaterfall_Click;
            // 
            // buttonGD
            // 
            buttonGD.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonGD.Location = new Point(1098, 186);
            buttonGD.Name = "buttonGD";
            buttonGD.Size = new Size(116, 23);
            buttonGD.TabIndex = 5;
            buttonGD.Text = "Group Delay";
            buttonGD.UseVisualStyleBackColor = true;
            buttonGD.Click += buttonGD_Click;
            // 
            // overlays
            // 
            overlays.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            overlays.BorderStyle = BorderStyle.FixedSingle;
            overlays.Controls.Add(overlayPanel1);
            overlays.Location = new Point(1098, 376);
            overlays.Name = "overlays";
            overlays.Size = new Size(154, 341);
            overlays.TabIndex = 6;
            // 
            // overlayPanel1
            // 
            overlayPanel1.BackColor = Color.OrangeRed;
            overlayPanel1.Controls.Add(buttonClearOverlay);
            overlayPanel1.Controls.Add(numericUpDown1);
            overlayPanel1.Controls.Add(checkBox1);
            overlayPanel1.Controls.Add(buttonSaveOverlay);
            overlayPanel1.Location = new Point(3, 3);
            overlayPanel1.Name = "overlayPanel1";
            overlayPanel1.Size = new Size(146, 25);
            overlayPanel1.TabIndex = 3;
            // 
            // buttonClearOverlay
            // 
            buttonClearOverlay.FlatStyle = FlatStyle.System;
            buttonClearOverlay.Location = new Point(92, 3);
            buttonClearOverlay.Name = "buttonClearOverlay";
            buttonClearOverlay.Size = new Size(19, 19);
            buttonClearOverlay.TabIndex = 3;
            buttonClearOverlay.Text = "C";
            // 
            // numericUpDown1
            // 
            numericUpDown1.BorderStyle = BorderStyle.None;
            numericUpDown1.Location = new Point(46, 3);
            numericUpDown1.Maximum = new decimal(new int[] { 180, 0, 0, 0 });
            numericUpDown1.Minimum = new decimal(new int[] { 180, 0, 0, int.MinValue });
            numericUpDown1.Name = "numericUpDown1";
            numericUpDown1.Size = new Size(40, 19);
            numericUpDown1.TabIndex = 2;
            numericUpDown1.TextAlign = HorizontalAlignment.Right;
            // 
            // checkBox1
            // 
            checkBox1.AutoSize = true;
            checkBox1.Location = new Point(5, 6);
            checkBox1.Name = "checkBox1";
            checkBox1.Size = new Size(15, 14);
            checkBox1.TabIndex = 0;
            checkBox1.UseVisualStyleBackColor = true;
            // 
            // buttonSaveOverlay
            // 
            buttonSaveOverlay.FlatStyle = FlatStyle.System;
            buttonSaveOverlay.Location = new Point(22, 3);
            buttonSaveOverlay.Name = "buttonSaveOverlay";
            buttonSaveOverlay.Size = new Size(19, 19);
            buttonSaveOverlay.TabIndex = 1;
            buttonSaveOverlay.Text = "1";
            // 
            // buttonIR
            // 
            buttonIR.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonIR.Location = new Point(1098, 99);
            buttonIR.Name = "buttonIR";
            buttonIR.Size = new Size(116, 23);
            buttonIR.TabIndex = 7;
            buttonIR.Text = "Impulse";
            buttonIR.UseVisualStyleBackColor = true;
            buttonIR.Click += buttonIR_Click;
            // 
            // buttonRecordOpt
            // 
            buttonRecordOpt.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonRecordOpt.Font = new Font("Segoe UI Emoji", 9.75F);
            buttonRecordOpt.Image = (Image)resources.GetObject("buttonRecordOpt.Image");
            buttonRecordOpt.Location = new Point(1220, 12);
            buttonRecordOpt.Name = "buttonRecordOpt";
            buttonRecordOpt.Size = new Size(32, 23);
            buttonRecordOpt.TabIndex = 8;
            buttonRecordOpt.UseCompatibleTextRendering = true;
            buttonRecordOpt.UseVisualStyleBackColor = true;
            buttonRecordOpt.Click += buttonRecordOpt_Click;
            // 
            // buttonWaterfallOpt
            // 
            buttonWaterfallOpt.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonWaterfallOpt.Font = new Font("Segoe UI Emoji", 9.75F);
            buttonWaterfallOpt.Image = (Image)resources.GetObject("buttonWaterfallOpt.Image");
            buttonWaterfallOpt.Location = new Point(1220, 215);
            buttonWaterfallOpt.Name = "buttonWaterfallOpt";
            buttonWaterfallOpt.Size = new Size(32, 23);
            buttonWaterfallOpt.TabIndex = 9;
            buttonWaterfallOpt.UseCompatibleTextRendering = true;
            buttonWaterfallOpt.UseVisualStyleBackColor = true;
            buttonWaterfallOpt.Click += buttonWaterfallOpt_Click;
            // 
            // buttonFROpt
            // 
            buttonFROpt.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonFROpt.Font = new Font("Segoe UI Emoji", 9.75F);
            buttonFROpt.Image = (Image)resources.GetObject("buttonFROpt.Image");
            buttonFROpt.Location = new Point(1220, 128);
            buttonFROpt.Name = "buttonFROpt";
            buttonFROpt.Size = new Size(32, 23);
            buttonFROpt.TabIndex = 10;
            buttonFROpt.UseCompatibleTextRendering = true;
            buttonFROpt.UseVisualStyleBackColor = true;
            buttonFROpt.Click += buttonFROpt_Click;
            // 
            // buttonBurstDecay
            // 
            buttonBurstDecay.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonBurstDecay.Location = new Point(1098, 244);
            buttonBurstDecay.Name = "buttonBurstDecay";
            buttonBurstDecay.Size = new Size(116, 23);
            buttonBurstDecay.TabIndex = 11;
            buttonBurstDecay.Text = "Burst Decay";
            buttonBurstDecay.UseVisualStyleBackColor = true;
            buttonBurstDecay.Click += buttonBurstDecay_Click;
            // 
            // buttonBurstDecayOpt
            // 
            buttonBurstDecayOpt.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonBurstDecayOpt.Font = new Font("Segoe UI Emoji", 9.75F);
            buttonBurstDecayOpt.Image = (Image)resources.GetObject("buttonBurstDecayOpt.Image");
            buttonBurstDecayOpt.Location = new Point(1220, 244);
            buttonBurstDecayOpt.Name = "buttonBurstDecayOpt";
            buttonBurstDecayOpt.Size = new Size(32, 23);
            buttonBurstDecayOpt.TabIndex = 12;
            buttonBurstDecayOpt.UseCompatibleTextRendering = true;
            buttonBurstDecayOpt.UseVisualStyleBackColor = true;
            buttonBurstDecayOpt.Click += buttonBurstDecayOpt_Click;
            // 
            // buttonGDOpt
            // 
            buttonGDOpt.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonGDOpt.Font = new Font("Segoe UI Emoji", 9.75F);
            buttonGDOpt.Image = (Image)resources.GetObject("buttonGDOpt.Image");
            buttonGDOpt.Location = new Point(1220, 186);
            buttonGDOpt.Name = "buttonGDOpt";
            buttonGDOpt.Size = new Size(32, 23);
            buttonGDOpt.TabIndex = 13;
            buttonGDOpt.UseCompatibleTextRendering = true;
            buttonGDOpt.UseVisualStyleBackColor = true;
            buttonGDOpt.Click += buttonGDOpt_Click;
            // 
            // buttonPROpt
            // 
            buttonPROpt.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonPROpt.Font = new Font("Segoe UI Emoji", 9.75F);
            buttonPROpt.Image = (Image)resources.GetObject("buttonPROpt.Image");
            buttonPROpt.Location = new Point(1220, 157);
            buttonPROpt.Name = "buttonPROpt";
            buttonPROpt.Size = new Size(32, 23);
            buttonPROpt.TabIndex = 14;
            buttonPROpt.UseCompatibleTextRendering = true;
            buttonPROpt.UseVisualStyleBackColor = true;
            buttonPROpt.Click += buttonPROpt_Click;
            // 
            // buttonImpOpt
            // 
            buttonImpOpt.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonImpOpt.Font = new Font("Segoe UI Emoji", 9.75F);
            buttonImpOpt.Image = (Image)resources.GetObject("buttonImpOpt.Image");
            buttonImpOpt.Location = new Point(1220, 99);
            buttonImpOpt.Name = "buttonImpOpt";
            buttonImpOpt.Size = new Size(32, 23);
            buttonImpOpt.TabIndex = 15;
            buttonImpOpt.UseCompatibleTextRendering = true;
            buttonImpOpt.UseVisualStyleBackColor = true;
            buttonImpOpt.Click += buttonImpOpt_Click;
            // 
            // buttonNoise
            // 
            buttonNoise.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonNoise.Location = new Point(1098, 273);
            buttonNoise.Name = "buttonNoise";
            buttonNoise.Size = new Size(116, 23);
            buttonNoise.TabIndex = 16;
            buttonNoise.Text = "Live Spectrum";
            buttonNoise.UseVisualStyleBackColor = true;
            buttonNoise.Click += buttonNoise_Click;
            // 
            // buttonAutocorrelation
            // 
            buttonAutocorrelation.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonAutocorrelation.Location = new Point(1098, 302);
            buttonAutocorrelation.Name = "buttonAutocorrelation";
            buttonAutocorrelation.Size = new Size(116, 23);
            buttonAutocorrelation.TabIndex = 17;
            buttonAutocorrelation.Text = "Autocorrelation";
            buttonAutocorrelation.UseVisualStyleBackColor = true;
            buttonAutocorrelation.Click += buttonGetAutocorrelation_Click;
            // 
            // buttonSave
            // 
            buttonSave.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonSave.Location = new Point(1098, 41);
            buttonSave.Name = "buttonSave";
            buttonSave.Size = new Size(56, 23);
            buttonSave.TabIndex = 18;
            buttonSave.Text = "Save";
            buttonSave.UseVisualStyleBackColor = true;
            buttonSave.Click += buttonSave_Click;
            // 
            // buttonLoad
            // 
            buttonLoad.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonLoad.Location = new Point(1158, 41);
            buttonLoad.Name = "buttonLoad";
            buttonLoad.Size = new Size(56, 23);
            buttonLoad.TabIndex = 19;
            buttonLoad.Text = "Load";
            buttonLoad.UseVisualStyleBackColor = true;
            buttonLoad.Click += buttonLoad_Click;
            //
            // buttonClear
            //
            buttonClear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonClear.Location = new Point(1098, 70);
            buttonClear.Name = "buttonClear";
            buttonClear.Size = new Size(116, 23);
            buttonClear.TabIndex = 20;
            buttonClear.Text = "Clear";
            buttonClear.UseVisualStyleBackColor = true;
            buttonClear.Click += buttonClear_Click;
            //
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(50, 50, 50);
            ClientSize = new Size(1264, 729);
            Controls.Add(buttonClear);
            Controls.Add(buttonLoad);
            Controls.Add(buttonSave);
            Controls.Add(buttonAutocorrelation);
            Controls.Add(buttonNoise);
            Controls.Add(buttonImpOpt);
            Controls.Add(buttonPROpt);
            Controls.Add(buttonGDOpt);
            Controls.Add(buttonBurstDecayOpt);
            Controls.Add(buttonBurstDecay);
            Controls.Add(buttonFROpt);
            Controls.Add(buttonWaterfallOpt);
            Controls.Add(buttonRecordOpt);
            Controls.Add(buttonIR);
            Controls.Add(buttonGD);
            Controls.Add(buttonWaterfall);
            Controls.Add(buttonPR);
            Controls.Add(buttonFR);
            Controls.Add(plotView1);
            Controls.Add(buttonRecord);
            Controls.Add(overlays);
            MinimumSize = new Size(1280, 768);
            Name = "Form1";
            Text = "Resonalyze";
            overlays.ResumeLayout(false);
            overlayPanel1.ResumeLayout(false);
            overlayPanel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numericUpDown1).EndInit();
            ResumeLayout(false);

        }

        #endregion

        private Button buttonRecord;
        private OxyPlot.WindowsForms.PlotView plotView1;
        private Button buttonFR;
        private Button buttonPR;
        private Button buttonWaterfall;
        private Button buttonGD;
        private Panel overlays;
        private Button buttonSaveOverlay;
        private CheckBox checkBox1;
        private NumericUpDown numericUpDown1;
        private Panel overlayPanel1;
        private Button buttonIR;
        private Button buttonRecordOpt;
        private Button buttonWaterfallOpt;
        private Button buttonFROpt;
        private Button buttonBurstDecay;
        private Button buttonBurstDecayOpt;
        private Button buttonGDOpt;
        private Button buttonPROpt;
        private Button buttonImpOpt;
        private Button buttonNoise;
        private Button buttonAutocorrelation;
        private Button buttonSave;
        private Button buttonLoad;
        private ToolTip toolTip1;
        private Button buttonClearOverlay;
        private Button buttonClear;
    }
}
