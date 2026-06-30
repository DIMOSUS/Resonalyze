namespace Resonalyze
{
    partial class IrComparerPanel
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
            titleLabel = new Label();
            SuspendLayout();
            // 
            // titleLabel
            // 
            titleLabel.AutoSize = true;
            titleLabel.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold, GraphicsUnit.Point, 204);
            titleLabel.ForeColor = Color.FromArgb(210, 214, 222);
            titleLabel.Location = new Point(18, 18);
            titleLabel.Name = "titleLabel";
            titleLabel.Size = new Size(98, 21);
            titleLabel.TabIndex = 0;
            titleLabel.Text = "IR Comparer";
            // 
            // IrComparerPanel
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            AutoScroll = true;
            BackColor = Color.FromArgb(40, 44, 54);
            BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(titleLabel);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            Name = "IrComparerPanel";
            Size = new Size(1182, 706);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label titleLabel;
    }
}
