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
            gainInput = new DarkNumericUpDown();
            fader = new GainFader();
            qInput = new DarkNumericUpDown();
            frequencyInput = new DarkNumericUpDown();
            slotLayout.SuspendLayout();
            (gainInput).BeginInit();
            (qInput).BeginInit();
            (frequencyInput).BeginInit();
            SuspendLayout();
            //
            // slotLayout
            //
            slotLayout.BackColor = Color.FromArgb(44, 50, 60);
            slotLayout.ColumnCount = 1;
            slotLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            slotLayout.Controls.Add(slotLabel, 0, 0);
            slotLayout.Controls.Add(gainInput, 0, 1);
            slotLayout.Controls.Add(fader, 0, 2);
            slotLayout.Controls.Add(qInput, 0, 3);
            slotLayout.Controls.Add(frequencyInput, 0, 4);
            slotLayout.Dock = DockStyle.Fill;
            slotLayout.Location = new Point(0, 0);
            slotLayout.Margin = new Padding(0);
            slotLayout.Name = "slotLayout";
            slotLayout.RowCount = 5;
            slotLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 15F));
            slotLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            slotLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            slotLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            slotLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            slotLayout.Size = new Size(64, 192);
            slotLayout.TabIndex = 0;
            //
            // slotLabel
            //
            slotLabel.Dock = DockStyle.Fill;
            slotLabel.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold);
            slotLabel.ForeColor = Color.FromArgb(210, 214, 222);
            slotLabel.Location = new Point(0, 0);
            slotLabel.Margin = new Padding(0);
            slotLabel.Name = "slotLabel";
            slotLabel.Size = new Size(64, 15);
            slotLabel.TabIndex = 0;
            slotLabel.Text = "1";
            slotLabel.TextAlign = ContentAlignment.MiddleCenter;
            slotLabel.UseCompatibleTextRendering = true;
            //
            // gainInput
            //
            gainInput.BackColor = Color.FromArgb(55, 60, 72);
            gainInput.DecimalPlaces = 1;
            gainInput.Dock = DockStyle.Fill;
            gainInput.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point, 204);
            gainInput.ForeColor = Color.White;
            gainInput.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            gainInput.InlineLabel = "G";
            gainInput.Location = new Point(1, 16);
            gainInput.Margin = new Padding(1);
            gainInput.Maximum = new decimal(new int[] { 6, 0, 0, 0 });
            gainInput.Minimum = new decimal(new int[] { 15, 0, 0, int.MinValue });
            gainInput.MinimumSize = new Size(48, 19);
            gainInput.Name = "gainInput";
            gainInput.Size = new Size(62, 20);
            gainInput.TabIndex = 1;
            gainInput.TextAlign = HorizontalAlignment.Right;
            gainInput.ThousandsSeparator = false;
            gainInput.Value = new decimal(new int[] { 0, 0, 0, 0 });
            //
            // fader
            //
            fader.BackColor = Color.FromArgb(44, 50, 60);
            fader.Dock = DockStyle.Fill;
            fader.Font = new Font("Segoe UI", 7.5F);
            fader.ForeColor = Color.FromArgb(185, 190, 200);
            fader.Location = new Point(2, 38);
            fader.Margin = new Padding(2, 1, 2, 1);
            fader.Name = "fader";
            fader.Size = new Size(60, 109);
            fader.TabIndex = 2;
            fader.TabStop = false;
            //
            // qInput
            //
            qInput.BackColor = Color.FromArgb(55, 60, 72);
            qInput.DecimalPlaces = 1;
            qInput.Dock = DockStyle.Fill;
            qInput.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point, 204);
            qInput.ForeColor = Color.White;
            qInput.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            qInput.InlineLabel = "Q";
            qInput.Location = new Point(1, 149);
            qInput.Margin = new Padding(1);
            qInput.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            qInput.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            qInput.MinimumSize = new Size(48, 19);
            qInput.Name = "qInput";
            qInput.Size = new Size(62, 20);
            qInput.TabIndex = 3;
            qInput.TextAlign = HorizontalAlignment.Right;
            qInput.ThousandsSeparator = false;
            qInput.Value = new decimal(new int[] { 1, 0, 0, 0 });
            //
            // frequencyInput
            //
            frequencyInput.BackColor = Color.FromArgb(55, 60, 72);
            frequencyInput.DecimalPlaces = 0;
            frequencyInput.Dock = DockStyle.Fill;
            frequencyInput.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point, 204);
            frequencyInput.ForeColor = Color.White;
            frequencyInput.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            frequencyInput.InlineLabel = "F";
            frequencyInput.Location = new Point(1, 171);
            frequencyInput.Margin = new Padding(1);
            frequencyInput.Maximum = new decimal(new int[] { 20000, 0, 0, 0 });
            frequencyInput.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            frequencyInput.MinimumSize = new Size(48, 19);
            frequencyInput.Name = "frequencyInput";
            frequencyInput.Size = new Size(62, 20);
            frequencyInput.TabIndex = 4;
            frequencyInput.TextAlign = HorizontalAlignment.Right;
            frequencyInput.ThousandsSeparator = false;
            frequencyInput.Value = new decimal(new int[] { 1000, 0, 0, 0 });
            //
            // PeqSlotControl
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(44, 50, 60);
            Controls.Add(slotLayout);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            MinimumSize = new Size(56, 120);
            Name = "PeqSlotControl";
            Size = new Size(64, 192);
            slotLayout.ResumeLayout(false);
            (gainInput).EndInit();
            (qInput).EndInit();
            (frequencyInput).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel slotLayout;
        private Label slotLabel;
        private DarkNumericUpDown gainInput;
        private GainFader fader;
        private DarkNumericUpDown qInput;
        private DarkNumericUpDown frequencyInput;
    }
}
