namespace Resonalyze.Options
{
    partial class IROpt
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
            this.numericLength = new System.Windows.Forms.NumericUpDown();
            this.label1 = new System.Windows.Forms.Label();
            this.button2 = new System.Windows.Forms.Button();
            this.button1 = new System.Windows.Forms.Button();
            this.checkLogarithmic = new System.Windows.Forms.CheckBox();
            this.label2 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.numericLength)).BeginInit();
            this.SuspendLayout();
            // 
            // numericLength
            // 
            this.numericLength.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.numericLength.Location = new System.Drawing.Point(193, 12);
            this.numericLength.Maximum = new decimal(new int[] {
            32768,
            0,
            0,
            0});
            this.numericLength.Minimum = new decimal(new int[] {
            4,
            0,
            0,
            0});
            this.numericLength.Name = "numericLength";
            this.numericLength.Size = new System.Drawing.Size(60, 19);
            this.numericLength.TabIndex = 38;
            this.numericLength.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericLength.Value = new decimal(new int[] {
            8192,
            0,
            0,
            0});
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label1.Location = new System.Drawing.Point(12, 14);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(98, 15);
            this.label1.TabIndex = 37;
            this.label1.Text = "Length (samples)";
            // 
            // button2
            // 
            this.button2.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.button2.Location = new System.Drawing.Point(153, 237);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(100, 23);
            this.button2.TabIndex = 36;
            this.button2.Text = "Cancel";
            this.button2.UseVisualStyleBackColor = true;
            // 
            // button1
            // 
            this.button1.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.button1.Location = new System.Drawing.Point(12, 237);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(100, 23);
            this.button1.TabIndex = 35;
            this.button1.Text = "Ok";
            this.button1.UseVisualStyleBackColor = true;
            // 
            // checkLogarithmic
            // 
            this.checkLogarithmic.AutoSize = true;
            this.checkLogarithmic.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.checkLogarithmic.Location = new System.Drawing.Point(238, 39);
            this.checkLogarithmic.Name = "checkLogarithmic";
            this.checkLogarithmic.Size = new System.Drawing.Size(15, 14);
            this.checkLogarithmic.TabIndex = 47;
            this.checkLogarithmic.UseVisualStyleBackColor = true;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.ForeColor = System.Drawing.SystemColors.ControlLight;
            this.label2.Location = new System.Drawing.Point(12, 38);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(101, 15);
            this.label2.TabIndex = 46;
            this.label2.Text = "Logarithmic Sacle";
            // 
            // IROpt
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(50)))), ((int)(((byte)(50)))));
            this.ClientSize = new System.Drawing.Size(265, 272);
            this.Controls.Add(this.checkLogarithmic);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.numericLength);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.button1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "IROpt";
            this.ShowInTaskbar = false;
            this.Text = "Impulse Responce Options";
            ((System.ComponentModel.ISupportInitialize)(this.numericLength)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private NumericUpDown numericLength;
        private Label label1;
        private Button button2;
        private Button button1;
        private CheckBox checkLogarithmic;
        private Label label2;
    }
}
