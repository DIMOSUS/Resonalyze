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
            buttonOverlaySettings1 = new Button();
            numericUpDown1 = new NumericUpDown();
            checkBox1 = new CheckBox();
            buttonSaveOverlay = new Button();
            buttonRecordOpt = new Button();
            buttonSave = new Button();
            buttonLoad = new Button();
            toolTip1 = new ToolTip(components);
            buttonClear = new Button();
            buttonCurrentModeSettings = new Button();
            buttonDraw = new Button();
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
            plotView1.BackColor = Color.FromArgb(50, 55, 100);
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
            overlayPanel1.Controls.Add(buttonOverlaySettings1);
            overlayPanel1.Controls.Add(numericUpDown1);
            overlayPanel1.Controls.Add(checkBox1);
            overlayPanel1.Controls.Add(buttonSaveOverlay);
            overlayPanel1.Location = new Point(3, 3);
            overlayPanel1.Name = "overlayPanel1";
            overlayPanel1.Size = new Size(146, 25);
            overlayPanel1.TabIndex = 3;
            // 
            // buttonOverlaySettings1
            // 
            buttonOverlaySettings1.FlatStyle = FlatStyle.System;
            buttonOverlaySettings1.Location = new Point(117, 3);
            buttonOverlaySettings1.Name = "buttonOverlaySettings1";
            buttonOverlaySettings1.Size = new Size(26, 19);
            buttonOverlaySettings1.TabIndex = 21;
            buttonOverlaySettings1.Text = "...";
            buttonOverlaySettings1.UseVisualStyleBackColor = true;
            // 
            // numericUpDown1
            // 
            numericUpDown1.BorderStyle = BorderStyle.None;
            numericUpDown1.Location = new Point(72, 3);
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
            buttonSaveOverlay.Size = new Size(44, 19);
            buttonSaveOverlay.TabIndex = 1;
            buttonSaveOverlay.Text = "1";
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
            // buttonSave
            // 
            buttonSave.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonSave.Location = new Point(1098, 39);
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
            buttonLoad.Location = new Point(1158, 39);
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
            buttonClear.Location = new Point(1098, 126);
            buttonClear.Name = "buttonClear";
            buttonClear.Size = new Size(116, 23);
            buttonClear.TabIndex = 20;
            buttonClear.Text = "Clear";
            buttonClear.UseVisualStyleBackColor = true;
            buttonClear.Click += buttonClear_Click;
            // 
            // buttonCurrentModeSettings
            // 
            buttonCurrentModeSettings.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonCurrentModeSettings.Location = new Point(1098, 68);
            buttonCurrentModeSettings.Name = "buttonCurrentModeSettings";
            buttonCurrentModeSettings.Size = new Size(116, 23);
            buttonCurrentModeSettings.TabIndex = 21;
            buttonCurrentModeSettings.Text = "Mode Settings...";
            buttonCurrentModeSettings.UseVisualStyleBackColor = true;
            buttonCurrentModeSettings.Click += buttonCurrentModeSettings_Click;
            // 
            // buttonDraw
            // 
            buttonDraw.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonDraw.Location = new Point(1098, 97);
            buttonDraw.Name = "buttonDraw";
            buttonDraw.Size = new Size(116, 23);
            buttonDraw.TabIndex = 22;
            buttonDraw.Text = "Draw";
            buttonDraw.UseVisualStyleBackColor = true;
            buttonDraw.Click += buttonDraw_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
            ClientSize = new Size(1264, 729);
            Controls.Add(buttonDraw);
            Controls.Add(buttonCurrentModeSettings);
            Controls.Add(buttonClear);
            Controls.Add(buttonLoad);
            Controls.Add(buttonSave);
            Controls.Add(buttonRecordOpt);
            Controls.Add(plotView1);
            Controls.Add(buttonRecord);
            Controls.Add(overlays);
            Icon = (Icon)resources.GetObject("$this.Icon");
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
        private Panel overlays;
        private Button buttonSaveOverlay;
        private CheckBox checkBox1;
        private NumericUpDown numericUpDown1;
        private Panel overlayPanel1;
        private Button buttonRecordOpt;
        private Button buttonSave;
        private Button buttonLoad;
        private ToolTip toolTip1;
        private Button buttonClear;
        private Button buttonOverlaySettings1;
        private Button buttonCurrentModeSettings;
        private Button buttonDraw;
    }
}
