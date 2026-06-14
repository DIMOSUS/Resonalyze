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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.buttonRecord = new System.Windows.Forms.Button();
            this.plotView1 = new OxyPlot.WindowsForms.PlotView();
            this.buttonFR = new System.Windows.Forms.Button();
            this.buttonPR = new System.Windows.Forms.Button();
            this.buttonWaterfall = new System.Windows.Forms.Button();
            this.buttonGD = new System.Windows.Forms.Button();
            this.overlays = new System.Windows.Forms.Panel();
            this.overlayPanel1 = new System.Windows.Forms.Panel();
            this.numericUpDown1 = new System.Windows.Forms.NumericUpDown();
            this.checkBox1 = new System.Windows.Forms.CheckBox();
            this.button6 = new System.Windows.Forms.Button();
            this.buttonIR = new System.Windows.Forms.Button();
            this.buttonRecordOpt = new System.Windows.Forms.Button();
            this.buttonWaterfallOpt = new System.Windows.Forms.Button();
            this.buttonFROpt = new System.Windows.Forms.Button();
            this.buttonBurstDecay = new System.Windows.Forms.Button();
            this.buttonBurstDecayOpt = new System.Windows.Forms.Button();
            this.buttonGDOpt = new System.Windows.Forms.Button();
            this.buttonPROpt = new System.Windows.Forms.Button();
            this.buttonImpOpt = new System.Windows.Forms.Button();
            this.buttonNoise = new System.Windows.Forms.Button();
            this.buttonAutocorrelation = new System.Windows.Forms.Button();
            this.overlays.SuspendLayout();
            this.overlayPanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).BeginInit();
            this.SuspendLayout();
            // 
            // buttonRecord
            // 
            this.buttonRecord.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonRecord.Location = new System.Drawing.Point(1114, 12);
            this.buttonRecord.Name = "buttonRecord";
            this.buttonRecord.Size = new System.Drawing.Size(100, 23);
            this.buttonRecord.TabIndex = 0;
            this.buttonRecord.Text = "Start";
            this.buttonRecord.UseVisualStyleBackColor = true;
            this.buttonRecord.Click += new System.EventHandler(this.buttonRecord_Click);
            // 
            // plotView1
            // 
            this.plotView1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.plotView1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(40)))), ((int)(((byte)(80)))));
            this.plotView1.ForeColor = System.Drawing.Color.White;
            this.plotView1.Location = new System.Drawing.Point(12, 12);
            this.plotView1.Name = "plotView1";
            this.plotView1.PanCursor = System.Windows.Forms.Cursors.Hand;
            this.plotView1.Size = new System.Drawing.Size(1096, 705);
            this.plotView1.TabIndex = 1;
            this.plotView1.Text = "plotView1";
            this.plotView1.ZoomHorizontalCursor = System.Windows.Forms.Cursors.SizeWE;
            this.plotView1.ZoomRectangleCursor = System.Windows.Forms.Cursors.SizeNWSE;
            this.plotView1.ZoomVerticalCursor = System.Windows.Forms.Cursors.SizeNS;
            // 
            // buttonFR
            // 
            this.buttonFR.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonFR.Location = new System.Drawing.Point(1114, 70);
            this.buttonFR.Name = "buttonFR";
            this.buttonFR.Size = new System.Drawing.Size(100, 23);
            this.buttonFR.TabIndex = 2;
            this.buttonFR.Text = "Frequency";
            this.buttonFR.UseVisualStyleBackColor = true;
            this.buttonFR.Click += new System.EventHandler(this.buttonFR_Click);
            // 
            // buttonPR
            // 
            this.buttonPR.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonPR.Location = new System.Drawing.Point(1114, 99);
            this.buttonPR.Name = "buttonPR";
            this.buttonPR.Size = new System.Drawing.Size(100, 23);
            this.buttonPR.TabIndex = 3;
            this.buttonPR.Text = "Phase";
            this.buttonPR.UseVisualStyleBackColor = true;
            this.buttonPR.Click += new System.EventHandler(this.buttonPR_Click);
            // 
            // buttonWaterfall
            // 
            this.buttonWaterfall.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonWaterfall.Location = new System.Drawing.Point(1114, 157);
            this.buttonWaterfall.Name = "buttonWaterfall";
            this.buttonWaterfall.Size = new System.Drawing.Size(100, 23);
            this.buttonWaterfall.TabIndex = 4;
            this.buttonWaterfall.Text = "Waterfall";
            this.buttonWaterfall.UseVisualStyleBackColor = true;
            this.buttonWaterfall.Click += new System.EventHandler(this.buttonWaterfall_Click);
            // 
            // buttonGD
            // 
            this.buttonGD.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonGD.Location = new System.Drawing.Point(1114, 128);
            this.buttonGD.Name = "buttonGD";
            this.buttonGD.Size = new System.Drawing.Size(100, 23);
            this.buttonGD.TabIndex = 5;
            this.buttonGD.Text = "Group Delay";
            this.buttonGD.UseVisualStyleBackColor = true;
            this.buttonGD.Click += new System.EventHandler(this.buttonGD_Click);
            // 
            // overlays
            // 
            this.overlays.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.overlays.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.overlays.Controls.Add(this.overlayPanel1);
            this.overlays.Location = new System.Drawing.Point(1114, 318);
            this.overlays.Name = "overlays";
            this.overlays.Size = new System.Drawing.Size(138, 399);
            this.overlays.TabIndex = 6;
            // 
            // overlayPanel1
            // 
            this.overlayPanel1.BackColor = System.Drawing.Color.OrangeRed;
            this.overlayPanel1.Controls.Add(this.numericUpDown1);
            this.overlayPanel1.Controls.Add(this.checkBox1);
            this.overlayPanel1.Controls.Add(this.button6);
            this.overlayPanel1.Location = new System.Drawing.Point(3, 3);
            this.overlayPanel1.Name = "overlayPanel1";
            this.overlayPanel1.Size = new System.Drawing.Size(130, 25);
            this.overlayPanel1.TabIndex = 3;
            // 
            // numericUpDown1
            // 
            this.numericUpDown1.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericUpDown1.Location = new System.Drawing.Point(49, 3);
            this.numericUpDown1.Maximum = new decimal(new int[] {
            180,
            0,
            0,
            0});
            this.numericUpDown1.Minimum = new decimal(new int[] {
            180,
            0,
            0,
            -2147483648});
            this.numericUpDown1.Name = "numericUpDown1";
            this.numericUpDown1.Size = new System.Drawing.Size(43, 19);
            this.numericUpDown1.TabIndex = 2;
            this.numericUpDown1.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // checkBox1
            // 
            this.checkBox1.AutoSize = true;
            this.checkBox1.Location = new System.Drawing.Point(5, 6);
            this.checkBox1.Name = "checkBox1";
            this.checkBox1.Size = new System.Drawing.Size(15, 14);
            this.checkBox1.TabIndex = 0;
            this.checkBox1.UseVisualStyleBackColor = true;
            // 
            // button6
            // 
            this.button6.FlatStyle = System.Windows.Forms.FlatStyle.System;
            this.button6.Location = new System.Drawing.Point(24, 3);
            this.button6.Name = "button6";
            this.button6.Size = new System.Drawing.Size(19, 19);
            this.button6.TabIndex = 1;
            this.button6.Text = "1";
            // 
            // buttonIR
            // 
            this.buttonIR.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonIR.Location = new System.Drawing.Point(1114, 41);
            this.buttonIR.Name = "buttonIR";
            this.buttonIR.Size = new System.Drawing.Size(100, 23);
            this.buttonIR.TabIndex = 7;
            this.buttonIR.Text = "Impulse";
            this.buttonIR.UseVisualStyleBackColor = true;
            this.buttonIR.Click += new System.EventHandler(this.buttonIR_Click);
            // 
            // buttonRecordOpt
            // 
            this.buttonRecordOpt.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonRecordOpt.Font = new System.Drawing.Font("Segoe UI Emoji", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.buttonRecordOpt.Image = ((System.Drawing.Image)(resources.GetObject("buttonRecordOpt.Image")));
            this.buttonRecordOpt.Location = new System.Drawing.Point(1220, 12);
            this.buttonRecordOpt.Name = "buttonRecordOpt";
            this.buttonRecordOpt.Size = new System.Drawing.Size(32, 23);
            this.buttonRecordOpt.TabIndex = 8;
            this.buttonRecordOpt.UseCompatibleTextRendering = true;
            this.buttonRecordOpt.UseVisualStyleBackColor = true;
            this.buttonRecordOpt.Click += new System.EventHandler(this.buttonRecordOpt_Click);
            // 
            // buttonWaterfallOpt
            // 
            this.buttonWaterfallOpt.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonWaterfallOpt.Font = new System.Drawing.Font("Segoe UI Emoji", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.buttonWaterfallOpt.Image = ((System.Drawing.Image)(resources.GetObject("buttonWaterfallOpt.Image")));
            this.buttonWaterfallOpt.Location = new System.Drawing.Point(1220, 157);
            this.buttonWaterfallOpt.Name = "buttonWaterfallOpt";
            this.buttonWaterfallOpt.Size = new System.Drawing.Size(32, 23);
            this.buttonWaterfallOpt.TabIndex = 9;
            this.buttonWaterfallOpt.UseCompatibleTextRendering = true;
            this.buttonWaterfallOpt.UseVisualStyleBackColor = true;
            this.buttonWaterfallOpt.Click += new System.EventHandler(this.buttonWaterfallOpt_Click);
            // 
            // buttonFROpt
            // 
            this.buttonFROpt.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonFROpt.Font = new System.Drawing.Font("Segoe UI Emoji", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.buttonFROpt.Image = ((System.Drawing.Image)(resources.GetObject("buttonFROpt.Image")));
            this.buttonFROpt.Location = new System.Drawing.Point(1220, 69);
            this.buttonFROpt.Name = "buttonFROpt";
            this.buttonFROpt.Size = new System.Drawing.Size(32, 23);
            this.buttonFROpt.TabIndex = 10;
            this.buttonFROpt.UseCompatibleTextRendering = true;
            this.buttonFROpt.UseVisualStyleBackColor = true;
            this.buttonFROpt.Click += new System.EventHandler(this.buttonFROpt_Click);
            // 
            // buttonBurstDecay
            // 
            this.buttonBurstDecay.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonBurstDecay.Location = new System.Drawing.Point(1114, 186);
            this.buttonBurstDecay.Name = "buttonBurstDecay";
            this.buttonBurstDecay.Size = new System.Drawing.Size(100, 23);
            this.buttonBurstDecay.TabIndex = 11;
            this.buttonBurstDecay.Text = "Burst Decay";
            this.buttonBurstDecay.UseVisualStyleBackColor = true;
            this.buttonBurstDecay.Click += new System.EventHandler(this.buttonBurstDecay_Click);
            // 
            // buttonBurstDecayOpt
            // 
            this.buttonBurstDecayOpt.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonBurstDecayOpt.Font = new System.Drawing.Font("Segoe UI Emoji", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.buttonBurstDecayOpt.Image = ((System.Drawing.Image)(resources.GetObject("buttonBurstDecayOpt.Image")));
            this.buttonBurstDecayOpt.Location = new System.Drawing.Point(1220, 185);
            this.buttonBurstDecayOpt.Name = "buttonBurstDecayOpt";
            this.buttonBurstDecayOpt.Size = new System.Drawing.Size(32, 23);
            this.buttonBurstDecayOpt.TabIndex = 12;
            this.buttonBurstDecayOpt.UseCompatibleTextRendering = true;
            this.buttonBurstDecayOpt.UseVisualStyleBackColor = true;
            this.buttonBurstDecayOpt.Click += new System.EventHandler(this.buttonBurstDecayOpt_Click);
            // 
            // buttonGDOpt
            // 
            this.buttonGDOpt.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonGDOpt.Font = new System.Drawing.Font("Segoe UI Emoji", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.buttonGDOpt.Image = ((System.Drawing.Image)(resources.GetObject("buttonGDOpt.Image")));
            this.buttonGDOpt.Location = new System.Drawing.Point(1220, 128);
            this.buttonGDOpt.Name = "buttonGDOpt";
            this.buttonGDOpt.Size = new System.Drawing.Size(32, 23);
            this.buttonGDOpt.TabIndex = 13;
            this.buttonGDOpt.UseCompatibleTextRendering = true;
            this.buttonGDOpt.UseVisualStyleBackColor = true;
            this.buttonGDOpt.Click += new System.EventHandler(this.buttonGDOpt_Click);
            // 
            // buttonPROpt
            // 
            this.buttonPROpt.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonPROpt.Font = new System.Drawing.Font("Segoe UI Emoji", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.buttonPROpt.Image = ((System.Drawing.Image)(resources.GetObject("buttonPROpt.Image")));
            this.buttonPROpt.Location = new System.Drawing.Point(1220, 99);
            this.buttonPROpt.Name = "buttonPROpt";
            this.buttonPROpt.Size = new System.Drawing.Size(32, 23);
            this.buttonPROpt.TabIndex = 14;
            this.buttonPROpt.UseCompatibleTextRendering = true;
            this.buttonPROpt.UseVisualStyleBackColor = true;
            this.buttonPROpt.Click += new System.EventHandler(this.buttonPROpt_Click);
            // 
            // buttonImpOpt
            // 
            this.buttonImpOpt.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonImpOpt.Font = new System.Drawing.Font("Segoe UI Emoji", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.buttonImpOpt.Image = ((System.Drawing.Image)(resources.GetObject("buttonImpOpt.Image")));
            this.buttonImpOpt.Location = new System.Drawing.Point(1220, 40);
            this.buttonImpOpt.Name = "buttonImpOpt";
            this.buttonImpOpt.Size = new System.Drawing.Size(32, 23);
            this.buttonImpOpt.TabIndex = 15;
            this.buttonImpOpt.UseCompatibleTextRendering = true;
            this.buttonImpOpt.UseVisualStyleBackColor = true;
            this.buttonImpOpt.Click += new System.EventHandler(this.buttonImpOpt_Click);
            // 
            // buttonNoise
            // 
            this.buttonNoise.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonNoise.Location = new System.Drawing.Point(1114, 215);
            this.buttonNoise.Name = "buttonNoise";
            this.buttonNoise.Size = new System.Drawing.Size(100, 23);
            this.buttonNoise.TabIndex = 16;
            this.buttonNoise.Text = "Live Spectrum";
            this.buttonNoise.UseVisualStyleBackColor = true;
            this.buttonNoise.Click += new System.EventHandler(this.buttonNoise_Click);
            // 
            // buttonAutocorrelation
            // 
            this.buttonAutocorrelation.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonAutocorrelation.Location = new System.Drawing.Point(1114, 244);
            this.buttonAutocorrelation.Name = "buttonAutocorrelation";
            this.buttonAutocorrelation.Size = new System.Drawing.Size(100, 23);
            this.buttonAutocorrelation.TabIndex = 17;
            this.buttonAutocorrelation.Text = "Autocorrelation";
            this.buttonAutocorrelation.UseVisualStyleBackColor = true;
            this.buttonAutocorrelation.Click += new System.EventHandler(this.buttonGetAutocorrelation_Click);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(50)))), ((int)(((byte)(50)))));
            this.ClientSize = new System.Drawing.Size(1264, 729);
            this.Controls.Add(this.buttonAutocorrelation);
            this.Controls.Add(this.buttonNoise);
            this.Controls.Add(this.buttonImpOpt);
            this.Controls.Add(this.buttonPROpt);
            this.Controls.Add(this.buttonGDOpt);
            this.Controls.Add(this.buttonBurstDecayOpt);
            this.Controls.Add(this.buttonBurstDecay);
            this.Controls.Add(this.buttonFROpt);
            this.Controls.Add(this.buttonWaterfallOpt);
            this.Controls.Add(this.buttonRecordOpt);
            this.Controls.Add(this.buttonIR);
            this.Controls.Add(this.overlays);
            this.Controls.Add(this.buttonGD);
            this.Controls.Add(this.buttonWaterfall);
            this.Controls.Add(this.buttonPR);
            this.Controls.Add(this.buttonFR);
            this.Controls.Add(this.plotView1);
            this.Controls.Add(this.buttonRecord);
            this.Name = "Form1";
            this.Text = "Resonalyze";
            this.overlays.ResumeLayout(false);
            this.overlayPanel1.ResumeLayout(false);
            this.overlayPanel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private Button buttonRecord;
        private OxyPlot.WindowsForms.PlotView plotView1;
        private Button buttonFR;
        private Button buttonPR;
        private Button buttonWaterfall;
        private Button buttonGD;
        private Panel overlays;
        private Button button6;
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
    }
}
