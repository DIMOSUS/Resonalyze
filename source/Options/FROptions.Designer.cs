namespace Resonalyze.Options
{
    partial class FROptions
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
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.button2 = new System.Windows.Forms.Button();
            this.button1 = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.numericWindow = new System.Windows.Forms.NumericUpDown();
            this.numericRightWindow = new System.Windows.Forms.NumericUpDown();
            this.numericLeftWindow = new System.Windows.Forms.NumericUpDown();
            this.label5 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label10 = new System.Windows.Forms.Label();
            this.label9 = new System.Windows.Forms.Label();
            this.numericSmothInvOctaves = new System.Windows.Forms.NumericUpDown();
            this.checkUseCalibration = new System.Windows.Forms.CheckBox();
            this.label2 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.numericWindow)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericRightWindow)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericLeftWindow)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericSmothInvOctaves)).BeginInit();
            this.SuspendLayout();
            // 
            // button2
            // 
            this.button2.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.button2.Location = new System.Drawing.Point(153, 237);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(100, 23);
            this.button2.TabIndex = 15;
            this.button2.Text = "Cancel";
            this.button2.UseVisualStyleBackColor = true;
            // 
            // button1
            // 
            this.button1.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.button1.Location = new System.Drawing.Point(12, 237);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(100, 23);
            this.button1.TabIndex = 14;
            this.button1.Text = "Ok";
            this.button1.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label1.Location = new System.Drawing.Point(12, 14);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(51, 15);
            this.label1.TabIndex = 16;
            this.label1.Text = "Window";
            // 
            // numericWindow
            // 
            this.numericWindow.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericWindow.Location = new System.Drawing.Point(193, 12);
            this.numericWindow.Maximum = new decimal(new int[] {
            32768,
            0,
            0,
            0});
            this.numericWindow.Minimum = new decimal(new int[] {
            4,
            0,
            0,
            0});
            this.numericWindow.Name = "numericWindow";
            this.numericWindow.Size = new System.Drawing.Size(60, 19);
            this.numericWindow.TabIndex = 17;
            this.numericWindow.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericWindow.Value = new decimal(new int[] {
            8192,
            0,
            0,
            0});
            this.numericWindow.ValueChanged += new System.EventHandler(this.numericWindow_ValueChanged);
            // 
            // numericRightWindow
            // 
            this.numericRightWindow.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericRightWindow.Location = new System.Drawing.Point(193, 61);
            this.numericRightWindow.Maximum = new decimal(new int[] {
            16384,
            0,
            0,
            0});
            this.numericRightWindow.Name = "numericRightWindow";
            this.numericRightWindow.Size = new System.Drawing.Size(60, 19);
            this.numericRightWindow.TabIndex = 21;
            this.numericRightWindow.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericRightWindow.Value = new decimal(new int[] {
            256,
            0,
            0,
            0});
            // 
            // numericLeftWindow
            // 
            this.numericLeftWindow.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericLeftWindow.Location = new System.Drawing.Point(193, 36);
            this.numericLeftWindow.Maximum = new decimal(new int[] {
            16384,
            0,
            0,
            0});
            this.numericLeftWindow.Name = "numericLeftWindow";
            this.numericLeftWindow.Size = new System.Drawing.Size(60, 19);
            this.numericLeftWindow.TabIndex = 20;
            this.numericLeftWindow.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericLeftWindow.Value = new decimal(new int[] {
            256,
            0,
            0,
            0});
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label5.Location = new System.Drawing.Point(12, 60);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(116, 15);
            this.label5.TabIndex = 19;
            this.label5.Text = "Tukey Window Right";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label4.Location = new System.Drawing.Point(12, 35);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(108, 15);
            this.label4.TabIndex = 18;
            this.label4.Text = "Tukey Window Left";
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label10.Location = new System.Drawing.Point(193, 87);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(21, 15);
            this.label10.TabIndex = 30;
            this.label10.Text = "1 /";
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label9.Location = new System.Drawing.Point(12, 85);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(93, 15);
            this.label9.TabIndex = 29;
            this.label9.Text = "Smoth (octaves)";
            // 
            // numericSmothInvOctaves
            // 
            this.numericSmothInvOctaves.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericSmothInvOctaves.Location = new System.Drawing.Point(214, 86);
            this.numericSmothInvOctaves.Maximum = new decimal(new int[] {
            48,
            0,
            0,
            0});
            this.numericSmothInvOctaves.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numericSmothInvOctaves.Name = "numericSmothInvOctaves";
            this.numericSmothInvOctaves.Size = new System.Drawing.Size(39, 19);
            this.numericSmothInvOctaves.TabIndex = 28;
            this.numericSmothInvOctaves.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericSmothInvOctaves.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            // 
            // checkUseCalibration
            // 
            this.checkUseCalibration.AutoSize = true;
            this.checkUseCalibration.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.checkUseCalibration.Location = new System.Drawing.Point(238, 111);
            this.checkUseCalibration.Name = "checkUseCalibration";
            this.checkUseCalibration.Size = new System.Drawing.Size(15, 14);
            this.checkUseCalibration.TabIndex = 47;
            this.checkUseCalibration.UseVisualStyleBackColor = true;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label2.Location = new System.Drawing.Point(12, 110);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(87, 15);
            this.label2.TabIndex = 46;
            this.label2.Text = "Use Calibration";
            // 
            // FROptions
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(50)))), ((int)(((byte)(50)))));
            this.ClientSize = new System.Drawing.Size(265, 272);
            this.Controls.Add(this.checkUseCalibration);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label10);
            this.Controls.Add(this.label9);
            this.Controls.Add(this.numericSmothInvOctaves);
            this.Controls.Add(this.numericRightWindow);
            this.Controls.Add(this.numericLeftWindow);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.numericWindow);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.button1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FROptions";
            this.ShowInTaskbar = false;
            this.Text = "Frequence Responce Options";
            ((System.ComponentModel.ISupportInitialize)(this.numericWindow)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericRightWindow)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericLeftWindow)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericSmothInvOctaves)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private Button button2;
        private Button button1;
        private Label label1;
        private NumericUpDown numericWindow;
        private NumericUpDown numericRightWindow;
        private NumericUpDown numericLeftWindow;
        private Label label5;
        private Label label4;
        private Label label10;
        private Label label9;
        private NumericUpDown numericSmothInvOctaves;
        private CheckBox checkUseCalibration;
        private Label label2;
    }
}
