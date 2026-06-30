namespace Resonalyze
{
    partial class PeqSlotControl
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
            slotLabel = new Label();
            frequencyLabel = new Label();
            frequencyInput = new DarkNumericUpDown();
            qLabel = new Label();
            qInput = new DarkNumericUpDown();
            gainLabel = new Label();
            gainInput = new DarkNumericUpDown();
            (frequencyInput).BeginInit();
            (qInput).BeginInit();
            (gainInput).BeginInit();
            SuspendLayout();
            // 
            // slotLabel
            // 
            slotLabel.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
            slotLabel.ForeColor = Color.FromArgb(210, 214, 222);
            slotLabel.Location = new Point(2, 0);
            slotLabel.Name = "slotLabel";
            slotLabel.Size = new Size(20, 30);
            slotLabel.TabIndex = 0;
            slotLabel.Text = "1";
            slotLabel.TextAlign = ContentAlignment.MiddleRight;
            slotLabel.UseCompatibleTextRendering = true;
            // 
            // frequencyLabel
            // 
            frequencyLabel.AutoSize = true;
            frequencyLabel.ForeColor = Color.FromArgb(170, 176, 190);
            frequencyLabel.Location = new Point(25, 7);
            frequencyLabel.Name = "frequencyLabel";
            frequencyLabel.Size = new Size(13, 15);
            frequencyLabel.TabIndex = 1;
            frequencyLabel.Text = "F";
            // 
            // frequencyInput
            // 
            frequencyInput.BackColor = Color.FromArgb(55, 60, 72);
            frequencyInput.DecimalPlaces = 0;
            frequencyInput.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            frequencyInput.ForeColor = Color.White;
            frequencyInput.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            frequencyInput.Location = new Point(36, 4);
            frequencyInput.Maximum = new decimal(new int[] { 20000, 0, 0, 0 });
            frequencyInput.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            frequencyInput.MinimumSize = new Size(36, 19);
            frequencyInput.Name = "frequencyInput";
            frequencyInput.Size = new Size(62, 22);
            frequencyInput.TabIndex = 2;
            frequencyInput.TextAlign = HorizontalAlignment.Right;
            frequencyInput.ThousandsSeparator = false;
            frequencyInput.Value = new decimal(new int[] { 1000, 0, 0, 0 });
            // 
            // qLabel
            // 
            qLabel.AutoSize = true;
            qLabel.ForeColor = Color.FromArgb(170, 176, 190);
            qLabel.Location = new Point(101, 7);
            qLabel.Name = "qLabel";
            qLabel.Size = new Size(16, 15);
            qLabel.TabIndex = 3;
            qLabel.Text = "Q";
            // 
            // qInput
            // 
            qInput.BackColor = Color.FromArgb(55, 60, 72);
            qInput.DecimalPlaces = 1;
            qInput.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            qInput.ForeColor = Color.White;
            qInput.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            qInput.Location = new Point(115, 4);
            qInput.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            qInput.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            qInput.MinimumSize = new Size(36, 19);
            qInput.Name = "qInput";
            qInput.Size = new Size(52, 22);
            qInput.TabIndex = 4;
            qInput.TextAlign = HorizontalAlignment.Right;
            qInput.ThousandsSeparator = false;
            qInput.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // gainLabel
            // 
            gainLabel.AutoSize = true;
            gainLabel.ForeColor = Color.FromArgb(170, 176, 190);
            gainLabel.Location = new Point(170, 7);
            gainLabel.Name = "gainLabel";
            gainLabel.Size = new Size(15, 15);
            gainLabel.TabIndex = 5;
            gainLabel.Text = "G";
            // 
            // gainInput
            // 
            gainInput.BackColor = Color.FromArgb(55, 60, 72);
            gainInput.DecimalPlaces = 1;
            gainInput.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            gainInput.ForeColor = Color.White;
            gainInput.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            gainInput.Location = new Point(183, 4);
            gainInput.Maximum = new decimal(new int[] { 6, 0, 0, 0 });
            gainInput.Minimum = new decimal(new int[] { 15, 0, 0, int.MinValue });
            gainInput.MinimumSize = new Size(36, 19);
            gainInput.Name = "gainInput";
            gainInput.Size = new Size(52, 22);
            gainInput.TabIndex = 6;
            gainInput.TextAlign = HorizontalAlignment.Right;
            gainInput.ThousandsSeparator = false;
            gainInput.Value = new decimal(new int[] { 0, 0, 0, 0 });
            // 
            // PeqSlotControl
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(44, 50, 60);
            Controls.Add(gainInput);
            Controls.Add(gainLabel);
            Controls.Add(qInput);
            Controls.Add(qLabel);
            Controls.Add(frequencyInput);
            Controls.Add(frequencyLabel);
            Controls.Add(slotLabel);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            MinimumSize = new Size(236, 30);
            Name = "PeqSlotControl";
            Size = new Size(236, 30);
            (frequencyInput).EndInit();
            (qInput).EndInit();
            (gainInput).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label slotLabel;
        private Label frequencyLabel;
        private DarkNumericUpDown frequencyInput;
        private Label qLabel;
        private DarkNumericUpDown qInput;
        private Label gainLabel;
        private DarkNumericUpDown gainInput;
    }
}
