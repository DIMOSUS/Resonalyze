namespace Resonalyze
{
    partial class AgentProgressDialog
    {
        private System.ComponentModel.IContainer components = null;

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
            labelStep = new Label();
            labelDone = new Label();
            progressBar = new ProgressBar();
            SuspendLayout();
            //
            // labelStep
            //
            labelStep.AutoEllipsis = true;
            labelStep.ForeColor = Color.FromArgb(210, 214, 222);
            labelStep.Location = new Point(12, 12);
            labelStep.Name = "labelStep";
            labelStep.Size = new Size(400, 30);
            labelStep.TabIndex = 0;
            labelStep.Text = "Working…";
            //
            // labelDone
            //
            labelDone.AutoEllipsis = true;
            labelDone.ForeColor = Color.FromArgb(150, 156, 168);
            labelDone.Location = new Point(12, 76);
            labelDone.Name = "labelDone";
            labelDone.Size = new Size(400, 48);
            labelDone.TabIndex = 2;
            labelDone.Text = string.Empty;
            //
            // progressBar
            //
            progressBar.Location = new Point(12, 48);
            progressBar.MarqueeAnimationSpeed = 30;
            progressBar.Name = "progressBar";
            progressBar.Size = new Size(400, 16);
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.TabIndex = 1;
            //
            // AgentProgressDialog
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(40, 44, 54);
            ClientSize = new Size(424, 145);
            // No close box: the work cannot be interrupted, and a window that
            // can be dismissed while it runs would leave the user watching
            // nothing happen.
            ControlBox = false;
            Controls.Add(labelDone);
            Controls.Add(progressBar);
            Controls.Add(labelStep);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "AgentProgressDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "AI assistant";
            ResumeLayout(false);
        }

        #endregion

        private Label labelStep;
        private Label labelDone;
        private ProgressBar progressBar;
    }
}
