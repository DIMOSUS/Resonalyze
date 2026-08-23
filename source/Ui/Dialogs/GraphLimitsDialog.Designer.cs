namespace Resonalyze
{
    partial class GraphLimitsDialog
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
            labelVerticalAxis = new Label();
            labelTop = new Label();
            numericTop = new DarkNumericUpDown();
            labelBottom = new Label();
            numericBottom = new DarkNumericUpDown();
            labelHorizontalAxis = new Label();
            labelLeft = new Label();
            numericLeft = new DarkNumericUpDown();
            labelRight = new Label();
            numericRight = new DarkNumericUpDown();
            buttonFit = new Button();
            buttonDefaults = new Button();
            buttonFitY = new Button();
            buttonApply = new Button();
            buttonClose = new Button();
            (numericTop).BeginInit();
            (numericBottom).BeginInit();
            (numericLeft).BeginInit();
            (numericRight).BeginInit();
            SuspendLayout();
            //
            // labelVerticalAxis
            //
            labelVerticalAxis.AutoSize = true;
            labelVerticalAxis.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelVerticalAxis.ForeColor = Color.FromArgb(210, 214, 222);
            labelVerticalAxis.Location = new Point(12, 12);
            labelVerticalAxis.Name = "labelVerticalAxis";
            labelVerticalAxis.Size = new Size(84, 15);
            labelVerticalAxis.TabIndex = 0;
            labelVerticalAxis.Text = "Vertical axis";
            //
            // labelTop
            //
            labelTop.AutoSize = true;
            labelTop.ForeColor = Color.FromArgb(210, 214, 222);
            labelTop.Location = new Point(28, 40);
            labelTop.Name = "labelTop";
            labelTop.Size = new Size(28, 15);
            labelTop.TabIndex = 1;
            labelTop.Text = "Top";
            //
            // numericTop
            //
            numericTop.BackColor = Color.FromArgb(55, 60, 72);
            numericTop.DecimalPlaces = 2;
            numericTop.ForeColor = Color.White;
            numericTop.Location = new Point(150, 36);
            numericTop.Name = "numericTop";
            numericTop.Size = new Size(166, 23);
            numericTop.TabIndex = 2;
            numericTop.TextAlign = HorizontalAlignment.Right;
            //
            // labelBottom
            //
            labelBottom.AutoSize = true;
            labelBottom.ForeColor = Color.FromArgb(210, 214, 222);
            labelBottom.Location = new Point(28, 70);
            labelBottom.Name = "labelBottom";
            labelBottom.Size = new Size(48, 15);
            labelBottom.TabIndex = 3;
            labelBottom.Text = "Bottom";
            //
            // numericBottom
            //
            numericBottom.BackColor = Color.FromArgb(55, 60, 72);
            numericBottom.DecimalPlaces = 2;
            numericBottom.ForeColor = Color.White;
            numericBottom.Location = new Point(150, 66);
            numericBottom.Name = "numericBottom";
            numericBottom.Size = new Size(166, 23);
            numericBottom.TabIndex = 4;
            numericBottom.TextAlign = HorizontalAlignment.Right;
            //
            // labelHorizontalAxis
            //
            labelHorizontalAxis.AutoSize = true;
            labelHorizontalAxis.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            labelHorizontalAxis.ForeColor = Color.FromArgb(210, 214, 222);
            labelHorizontalAxis.Location = new Point(12, 104);
            labelHorizontalAxis.Name = "labelHorizontalAxis";
            labelHorizontalAxis.Size = new Size(97, 15);
            labelHorizontalAxis.TabIndex = 5;
            labelHorizontalAxis.Text = "Horizontal axis";
            //
            // labelLeft
            //
            labelLeft.AutoSize = true;
            labelLeft.ForeColor = Color.FromArgb(210, 214, 222);
            labelLeft.Location = new Point(28, 132);
            labelLeft.Name = "labelLeft";
            labelLeft.Size = new Size(28, 15);
            labelLeft.TabIndex = 6;
            labelLeft.Text = "Left";
            //
            // numericLeft
            //
            numericLeft.BackColor = Color.FromArgb(55, 60, 72);
            numericLeft.DecimalPlaces = 2;
            numericLeft.ForeColor = Color.White;
            numericLeft.Location = new Point(150, 128);
            numericLeft.Name = "numericLeft";
            numericLeft.Size = new Size(166, 23);
            numericLeft.TabIndex = 7;
            numericLeft.TextAlign = HorizontalAlignment.Right;
            //
            // labelRight
            //
            labelRight.AutoSize = true;
            labelRight.ForeColor = Color.FromArgb(210, 214, 222);
            labelRight.Location = new Point(28, 162);
            labelRight.Name = "labelRight";
            labelRight.Size = new Size(38, 15);
            labelRight.TabIndex = 8;
            labelRight.Text = "Right";
            //
            // numericRight
            //
            numericRight.BackColor = Color.FromArgb(55, 60, 72);
            numericRight.DecimalPlaces = 2;
            numericRight.ForeColor = Color.White;
            numericRight.Location = new Point(150, 158);
            numericRight.Name = "numericRight";
            numericRight.Size = new Size(166, 23);
            numericRight.TabIndex = 9;
            numericRight.TextAlign = HorizontalAlignment.Right;
            //
            // buttonFit
            //
            buttonFit.BackColor = Color.FromArgb(46, 51, 67);
            buttonFit.FlatStyle = FlatStyle.Popup;
            buttonFit.ForeColor = Color.White;
            buttonFit.Location = new Point(12, 196);
            buttonFit.Name = "buttonFit";
            buttonFit.Size = new Size(96, 26);
            buttonFit.TabIndex = 10;
            buttonFit.Text = "Fit to data";
            buttonFit.UseVisualStyleBackColor = false;
            //
            // buttonFitY
            //
            buttonFitY.BackColor = Color.FromArgb(46, 51, 67);
            buttonFitY.FlatStyle = FlatStyle.Popup;
            buttonFitY.ForeColor = Color.White;
            buttonFitY.Location = new Point(116, 196);
            buttonFitY.Name = "buttonFitY";
            buttonFitY.Size = new Size(96, 26);
            buttonFitY.TabIndex = 11;
            buttonFitY.Text = "Fit Y to data";
            buttonFitY.UseVisualStyleBackColor = false;
            //
            // buttonDefaults
            //
            buttonDefaults.BackColor = Color.FromArgb(46, 51, 67);
            buttonDefaults.FlatStyle = FlatStyle.Popup;
            buttonDefaults.ForeColor = Color.White;
            buttonDefaults.Location = new Point(220, 196);
            buttonDefaults.Name = "buttonDefaults";
            buttonDefaults.Size = new Size(96, 26);
            buttonDefaults.TabIndex = 12;
            buttonDefaults.Text = "Defaults";
            buttonDefaults.UseVisualStyleBackColor = false;
            //
            // buttonApply
            //
            buttonApply.BackColor = Color.FromArgb(46, 51, 67);
            buttonApply.FlatStyle = FlatStyle.Popup;
            buttonApply.ForeColor = Color.White;
            buttonApply.Location = new Point(116, 232);
            buttonApply.Name = "buttonApply";
            buttonApply.Size = new Size(96, 26);
            buttonApply.TabIndex = 13;
            buttonApply.Text = "Apply";
            buttonApply.UseVisualStyleBackColor = false;
            //
            // buttonClose
            //
            buttonClose.DialogResult = DialogResult.Cancel;
            buttonClose.FlatStyle = FlatStyle.Popup;
            buttonClose.ForeColor = Color.White;
            buttonClose.Location = new Point(220, 232);
            buttonClose.Name = "buttonClose";
            buttonClose.Size = new Size(96, 26);
            buttonClose.TabIndex = 14;
            buttonClose.Text = "Close";
            buttonClose.UseVisualStyleBackColor = true;
            //
            // GraphLimitsDialog
            //
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(40, 44, 54);
            CancelButton = buttonClose;
            ClientSize = new Size(328, 270);
            Controls.Add(labelVerticalAxis);
            Controls.Add(labelTop);
            Controls.Add(numericTop);
            Controls.Add(labelBottom);
            Controls.Add(numericBottom);
            Controls.Add(labelHorizontalAxis);
            Controls.Add(labelLeft);
            Controls.Add(numericLeft);
            Controls.Add(labelRight);
            Controls.Add(numericRight);
            Controls.Add(buttonFit);
            Controls.Add(buttonFitY);
            Controls.Add(buttonDefaults);
            Controls.Add(buttonApply);
            Controls.Add(buttonClose);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ForeColor = Color.White;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "GraphLimitsDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Graph limits";
            (numericTop).EndInit();
            (numericBottom).EndInit();
            (numericLeft).EndInit();
            (numericRight).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label labelVerticalAxis;
        private Label labelTop;
        private DarkNumericUpDown numericTop;
        private Label labelBottom;
        private DarkNumericUpDown numericBottom;
        private Label labelHorizontalAxis;
        private Label labelLeft;
        private DarkNumericUpDown numericLeft;
        private Label labelRight;
        private DarkNumericUpDown numericRight;
        private Button buttonFit;
        private Button buttonDefaults;
        private Button buttonFitY;
        private Button buttonApply;
        private Button buttonClose;
    }
}
