namespace Resonalyze.Options
{
    partial class PROpt
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
            this.label10 = new System.Windows.Forms.Label();
            this.label9 = new System.Windows.Forms.Label();
            this.numericSmothInvOctaves = new System.Windows.Forms.NumericUpDown();
            this.numericRightWindow = new System.Windows.Forms.NumericUpDown();
            this.numericLeftWindow = new System.Windows.Forms.NumericUpDown();
            this.label5 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.numericWindow = new System.Windows.Forms.NumericUpDown();
            this.label1 = new System.Windows.Forms.Label();
            this.button2 = new System.Windows.Forms.Button();
            this.button1 = new System.Windows.Forms.Button();
            this.numericOffset = new System.Windows.Forms.NumericUpDown();
            this.label11 = new System.Windows.Forms.Label();
            this.checkBoxUnwrap = new System.Windows.Forms.CheckBox();
            this.label2 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.numericSmothInvOctaves)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericRightWindow)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericLeftWindow)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericWindow)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericOffset)).BeginInit();
            this.SuspendLayout();
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label10.Location = new System.Drawing.Point(193, 87);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(21, 15);
            this.label10.TabIndex = 41;
            this.label10.Text = "1 /";
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label9.Location = new System.Drawing.Point(12, 85);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(93, 15);
            this.label9.TabIndex = 40;
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
            this.numericSmothInvOctaves.TabIndex = 39;
            this.numericSmothInvOctaves.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericSmothInvOctaves.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
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
            this.numericRightWindow.TabIndex = 38;
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
            this.numericLeftWindow.TabIndex = 37;
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
            this.label5.TabIndex = 36;
            this.label5.Text = "Tukey Window Right";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label4.Location = new System.Drawing.Point(12, 35);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(108, 15);
            this.label4.TabIndex = 35;
            this.label4.Text = "Tukey Window Left";
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
            this.numericWindow.TabIndex = 34;
            this.numericWindow.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericWindow.Value = new decimal(new int[] {
            8192,
            0,
            0,
            0});
            this.numericWindow.ValueChanged += new System.EventHandler(this.numericWindow_ValueChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label1.Location = new System.Drawing.Point(12, 14);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(51, 15);
            this.label1.TabIndex = 33;
            this.label1.Text = "Window";
            // 
            // button2
            // 
            this.button2.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.button2.Location = new System.Drawing.Point(153, 237);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(100, 23);
            this.button2.TabIndex = 32;
            this.button2.Text = "Cancel";
            this.button2.UseVisualStyleBackColor = true;
            // 
            // button1
            // 
            this.button1.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.button1.Location = new System.Drawing.Point(12, 237);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(100, 23);
            this.button1.TabIndex = 31;
            this.button1.Text = "Ok";
            this.button1.UseVisualStyleBackColor = true;
            // 
            // numericOffset
            // 
            this.numericOffset.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericOffset.Location = new System.Drawing.Point(193, 111);
            this.numericOffset.Maximum = new decimal(new int[] {
            32768,
            0,
            0,
            0});
            this.numericOffset.Minimum = new decimal(new int[] {
            32768,
            0,
            0,
            -2147483648});
            this.numericOffset.Name = "numericOffset";
            this.numericOffset.Size = new System.Drawing.Size(60, 19);
            this.numericOffset.TabIndex = 43;
            this.numericOffset.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // label11
            // 
            this.label11.AutoSize = true;
            this.label11.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label11.Location = new System.Drawing.Point(12, 110);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(39, 15);
            this.label11.TabIndex = 42;
            this.label11.Text = "Offset";
            // 
            // checkBoxUnwrap
            // 
            this.checkBoxUnwrap.AutoSize = true;
            this.checkBoxUnwrap.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.checkBoxUnwrap.Location = new System.Drawing.Point(238, 136);
            this.checkBoxUnwrap.Name = "checkBoxUnwrap";
            this.checkBoxUnwrap.Size = new System.Drawing.Size(15, 14);
            this.checkBoxUnwrap.TabIndex = 45;
            this.checkBoxUnwrap.UseVisualStyleBackColor = true;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label2.Location = new System.Drawing.Point(12, 135);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(48, 15);
            this.label2.TabIndex = 44;
            this.label2.Text = "Unwrap";
            // 
            // PROpt
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(50)))), ((int)(((byte)(50)))));
            this.ClientSize = new System.Drawing.Size(265, 272);
            this.Controls.Add(this.checkBoxUnwrap);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.numericOffset);
            this.Controls.Add(this.label11);
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
            this.Name = "PROpt";
            this.ShowInTaskbar = false;
            this.Text = "Phase Responce Options";
            ((System.ComponentModel.ISupportInitialize)(this.numericSmothInvOctaves)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericRightWindow)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericLeftWindow)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericWindow)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericOffset)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private Label label10;
        private Label label9;
        private NumericUpDown numericSmothInvOctaves;
        private NumericUpDown numericRightWindow;
        private NumericUpDown numericLeftWindow;
        private Label label5;
        private Label label4;
        private NumericUpDown numericWindow;
        private Label label1;
        private Button button2;
        private Button button1;
        private NumericUpDown numericOffset;
        private Label label11;
        private CheckBox checkBoxUnwrap;
        private Label label2;
    }
}
