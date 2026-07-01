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
            slotLayout = new TableLayoutPanel();
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
            slotLayout.SuspendLayout();
            SuspendLayout();
            //
            // slotLayout
            //
            slotLayout.ColumnCount = 7;
            slotLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22F));
            slotLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 10F));
            slotLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            slotLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 12F));
            slotLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31F));
            slotLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 12F));
            slotLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31F));
            slotLayout.Controls.Add(slotLabel, 0, 0);
            slotLayout.Controls.Add(frequencyLabel, 1, 0);
            slotLayout.Controls.Add(frequencyInput, 2, 0);
            slotLayout.Controls.Add(qLabel, 3, 0);
            slotLayout.Controls.Add(qInput, 4, 0);
            slotLayout.Controls.Add(gainLabel, 5, 0);
            slotLayout.Controls.Add(gainInput, 6, 0);
            slotLayout.Dock = DockStyle.Fill;
            slotLayout.Location = new Point(0, 0);
            slotLayout.Margin = Padding.Empty;
            slotLayout.Name = "slotLayout";
            slotLayout.RowCount = 1;
            slotLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            slotLayout.Size = new Size(236, 30);
            slotLayout.TabIndex = 0;
            //
            // slotLabel
            //
            slotLabel.Dock = DockStyle.Fill;
            slotLabel.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
            slotLabel.ForeColor = Color.FromArgb(210, 214, 222);
            slotLabel.Location = new Point(0, 0);
            slotLabel.Margin = Padding.Empty;
            slotLabel.Name = "slotLabel";
            slotLabel.Size = new Size(22, 30);
            slotLabel.TabIndex = 0;
            slotLabel.Text = "1";
            slotLabel.TextAlign = ContentAlignment.MiddleCenter;
            slotLabel.UseCompatibleTextRendering = true;
            //
            // frequencyLabel
            //
            frequencyLabel.Dock = DockStyle.Fill;
            frequencyLabel.ForeColor = Color.FromArgb(170, 176, 190);
            frequencyLabel.Location = new Point(22, 0);
            frequencyLabel.Margin = Padding.Empty;
            frequencyLabel.Name = "frequencyLabel";
            frequencyLabel.Size = new Size(10, 30);
            frequencyLabel.TabIndex = 1;
            frequencyLabel.Text = "F";
            frequencyLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // frequencyInput
            //
            frequencyInput.BackColor = Color.FromArgb(55, 60, 72);
            frequencyInput.DecimalPlaces = 0;
            frequencyInput.Dock = DockStyle.Fill;
            frequencyInput.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            frequencyInput.ForeColor = Color.White;
            frequencyInput.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            frequencyInput.Location = new Point(34, 4);
            frequencyInput.Margin = new Padding(2, 4, 3, 3);
            frequencyInput.Maximum = new decimal(new int[] { 20000, 0, 0, 0 });
            frequencyInput.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            frequencyInput.MinimumSize = new Size(62, 19);
            frequencyInput.Name = "frequencyInput";
            frequencyInput.Size = new Size(66, 23);
            frequencyInput.TabIndex = 2;
            frequencyInput.TextAlign = HorizontalAlignment.Right;
            frequencyInput.ThousandsSeparator = false;
            frequencyInput.Value = new decimal(new int[] { 1000, 0, 0, 0 });
            //
            // qLabel
            //
            qLabel.Dock = DockStyle.Fill;
            qLabel.ForeColor = Color.FromArgb(170, 176, 190);
            qLabel.Location = new Point(100, 0);
            qLabel.Margin = Padding.Empty;
            qLabel.Name = "qLabel";
            qLabel.Size = new Size(12, 30);
            qLabel.TabIndex = 3;
            qLabel.Text = "Q";
            qLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // qInput
            //
            qInput.BackColor = Color.FromArgb(55, 60, 72);
            qInput.DecimalPlaces = 1;
            qInput.Dock = DockStyle.Fill;
            qInput.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            qInput.ForeColor = Color.White;
            qInput.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            qInput.Location = new Point(114, 4);
            qInput.Margin = new Padding(2, 4, 3, 3);
            qInput.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            qInput.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            qInput.MinimumSize = new Size(48, 19);
            qInput.Name = "qInput";
            qInput.Size = new Size(55, 23);
            qInput.TabIndex = 4;
            qInput.TextAlign = HorizontalAlignment.Right;
            qInput.ThousandsSeparator = false;
            qInput.Value = new decimal(new int[] { 1, 0, 0, 0 });
            //
            // gainLabel
            //
            gainLabel.Dock = DockStyle.Fill;
            gainLabel.ForeColor = Color.FromArgb(170, 176, 190);
            gainLabel.Location = new Point(172, 0);
            gainLabel.Margin = Padding.Empty;
            gainLabel.Name = "gainLabel";
            gainLabel.Size = new Size(12, 30);
            gainLabel.TabIndex = 5;
            gainLabel.Text = "G";
            gainLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // gainInput
            //
            gainInput.BackColor = Color.FromArgb(55, 60, 72);
            gainInput.DecimalPlaces = 1;
            gainInput.Dock = DockStyle.Fill;
            gainInput.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            gainInput.ForeColor = Color.White;
            gainInput.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            gainInput.Location = new Point(186, 4);
            gainInput.Margin = new Padding(2, 4, 3, 3);
            gainInput.Maximum = new decimal(new int[] { 6, 0, 0, 0 });
            gainInput.Minimum = new decimal(new int[] { 15, 0, 0, int.MinValue });
            gainInput.MinimumSize = new Size(48, 19);
            gainInput.Name = "gainInput";
            gainInput.Size = new Size(47, 23);
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
            Controls.Add(slotLayout);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            MinimumSize = new Size(236, 30);
            Name = "PeqSlotControl";
            Size = new Size(236, 30);
            (frequencyInput).EndInit();
            (qInput).EndInit();
            (gainInput).EndInit();
            slotLayout.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel slotLayout;
        private Label slotLabel;
        private Label frequencyLabel;
        private DarkNumericUpDown frequencyInput;
        private Label qLabel;
        private DarkNumericUpDown qInput;
        private Label gainLabel;
        private DarkNumericUpDown gainInput;
    }
}
