namespace Resonalyze
{
    partial class EqResultsPanel
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
            tableLayout = new TableLayoutPanel();
            headerLabel = new Label();
            rmsCaption = new Label();
            rmsValue = new Label();
            maxCaption = new Label();
            maxValue = new Label();
            filtersCaption = new Label();
            filtersValue = new Label();
            boostCaption = new Label();
            boostValue = new Label();
            cutCaption = new Label();
            cutValue = new Label();
            headroomCaption = new Label();
            headroomValue = new Label();
            tableLayout.SuspendLayout();
            SuspendLayout();
            // 
            // tableLayout
            // 
            tableLayout.AutoSize = true;
            tableLayout.BackColor = Color.FromArgb(20, 22, 30);
            tableLayout.ColumnCount = 2;
            tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52.173912F));
            tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47.826088F));
            tableLayout.Controls.Add(headerLabel, 0, 0);
            tableLayout.Controls.Add(rmsCaption, 0, 1);
            tableLayout.Controls.Add(rmsValue, 1, 1);
            tableLayout.Controls.Add(maxCaption, 0, 2);
            tableLayout.Controls.Add(maxValue, 1, 2);
            tableLayout.Controls.Add(filtersCaption, 0, 3);
            tableLayout.Controls.Add(filtersValue, 1, 3);
            tableLayout.Controls.Add(boostCaption, 0, 4);
            tableLayout.Controls.Add(boostValue, 1, 4);
            tableLayout.Controls.Add(cutCaption, 0, 5);
            tableLayout.Controls.Add(cutValue, 1, 5);
            tableLayout.Controls.Add(headroomCaption, 0, 6);
            tableLayout.Controls.Add(headroomValue, 1, 6);
            tableLayout.Dock = DockStyle.Top;
            tableLayout.Location = new Point(8, 8);
            tableLayout.Name = "tableLayout";
            tableLayout.RowCount = 7;
            tableLayout.RowStyles.Add(new RowStyle());
            tableLayout.RowStyles.Add(new RowStyle());
            tableLayout.RowStyles.Add(new RowStyle());
            tableLayout.RowStyles.Add(new RowStyle());
            tableLayout.RowStyles.Add(new RowStyle());
            tableLayout.RowStyles.Add(new RowStyle());
            tableLayout.RowStyles.Add(new RowStyle());
            tableLayout.Size = new Size(138, 164);
            tableLayout.TabIndex = 0;
            // 
            // headerLabel
            // 
            headerLabel.AutoSize = true;
            tableLayout.SetColumnSpan(headerLabel, 2);
            headerLabel.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            headerLabel.ForeColor = Color.FromArgb(210, 214, 222);
            headerLabel.Location = new Point(3, 3);
            headerLabel.Margin = new Padding(3, 3, 3, 10);
            headerLabel.Name = "headerLabel";
            headerLabel.Size = new Size(97, 19);
            headerLabel.TabIndex = 0;
            headerLabel.Text = "Tuning results";
            // 
            // rmsCaption
            // 
            rmsCaption.Anchor = AnchorStyles.Left;
            rmsCaption.AutoSize = true;
            rmsCaption.ForeColor = Color.FromArgb(170, 176, 190);
            rmsCaption.Location = new Point(3, 35);
            rmsCaption.Margin = new Padding(3, 3, 3, 4);
            rmsCaption.Name = "rmsCaption";
            rmsCaption.Size = new Size(59, 15);
            rmsCaption.TabIndex = 1;
            rmsCaption.Text = "RMS error";
            // 
            // rmsValue
            // 
            rmsValue.Anchor = AnchorStyles.Right;
            rmsValue.AutoSize = true;
            rmsValue.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
            rmsValue.ForeColor = Color.FromArgb(225, 228, 235);
            rmsValue.Location = new Point(123, 35);
            rmsValue.Margin = new Padding(3, 3, 3, 4);
            rmsValue.Name = "rmsValue";
            rmsValue.Size = new Size(12, 15);
            rmsValue.TabIndex = 2;
            rmsValue.Text = "-";
            // 
            // maxCaption
            // 
            maxCaption.Anchor = AnchorStyles.Left;
            maxCaption.AutoSize = true;
            maxCaption.ForeColor = Color.FromArgb(170, 176, 190);
            maxCaption.Location = new Point(3, 57);
            maxCaption.Margin = new Padding(3, 3, 3, 4);
            maxCaption.Name = "maxCaption";
            maxCaption.Size = new Size(57, 15);
            maxCaption.TabIndex = 3;
            maxCaption.Text = "Max error";
            // 
            // maxValue
            // 
            maxValue.Anchor = AnchorStyles.Right;
            maxValue.AutoSize = true;
            maxValue.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
            maxValue.ForeColor = Color.FromArgb(225, 228, 235);
            maxValue.Location = new Point(123, 57);
            maxValue.Margin = new Padding(3, 3, 3, 4);
            maxValue.Name = "maxValue";
            maxValue.Size = new Size(12, 15);
            maxValue.TabIndex = 4;
            maxValue.Text = "-";
            // 
            // filtersCaption
            // 
            filtersCaption.Anchor = AnchorStyles.Left;
            filtersCaption.AutoSize = true;
            filtersCaption.ForeColor = Color.FromArgb(170, 176, 190);
            filtersCaption.Location = new Point(3, 79);
            filtersCaption.Margin = new Padding(3, 3, 3, 4);
            filtersCaption.Name = "filtersCaption";
            filtersCaption.Size = new Size(66, 15);
            filtersCaption.TabIndex = 5;
            filtersCaption.Text = "Filters used";
            // 
            // filtersValue
            // 
            filtersValue.Anchor = AnchorStyles.Right;
            filtersValue.AutoSize = true;
            filtersValue.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
            filtersValue.ForeColor = Color.FromArgb(225, 228, 235);
            filtersValue.Location = new Point(123, 79);
            filtersValue.Margin = new Padding(3, 3, 3, 4);
            filtersValue.Name = "filtersValue";
            filtersValue.Size = new Size(12, 15);
            filtersValue.TabIndex = 6;
            filtersValue.Text = "-";
            // 
            // boostCaption
            // 
            boostCaption.Anchor = AnchorStyles.Left;
            boostCaption.AutoSize = true;
            boostCaption.ForeColor = Color.FromArgb(170, 176, 190);
            boostCaption.Location = new Point(3, 101);
            boostCaption.Margin = new Padding(3, 3, 3, 4);
            boostCaption.Name = "boostCaption";
            boostCaption.Size = new Size(65, 15);
            boostCaption.TabIndex = 7;
            boostCaption.Text = "Peak boost";
            // 
            // boostValue
            // 
            boostValue.Anchor = AnchorStyles.Right;
            boostValue.AutoSize = true;
            boostValue.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
            boostValue.ForeColor = Color.FromArgb(225, 228, 235);
            boostValue.Location = new Point(123, 101);
            boostValue.Margin = new Padding(3, 3, 3, 4);
            boostValue.Name = "boostValue";
            boostValue.Size = new Size(12, 15);
            boostValue.TabIndex = 8;
            boostValue.Text = "-";
            // 
            // cutCaption
            // 
            cutCaption.Anchor = AnchorStyles.Left;
            cutCaption.AutoSize = true;
            cutCaption.ForeColor = Color.FromArgb(170, 176, 190);
            cutCaption.Location = new Point(3, 123);
            cutCaption.Margin = new Padding(3, 3, 3, 4);
            cutCaption.Name = "cutCaption";
            cutCaption.Size = new Size(52, 15);
            cutCaption.TabIndex = 9;
            cutCaption.Text = "Peak cut";
            // 
            // cutValue
            // 
            cutValue.Anchor = AnchorStyles.Right;
            cutValue.AutoSize = true;
            cutValue.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
            cutValue.ForeColor = Color.FromArgb(225, 228, 235);
            cutValue.Location = new Point(123, 123);
            cutValue.Margin = new Padding(3, 3, 3, 4);
            cutValue.Name = "cutValue";
            cutValue.Size = new Size(12, 15);
            cutValue.TabIndex = 10;
            cutValue.Text = "-";
            // 
            // headroomCaption
            // 
            headroomCaption.Anchor = AnchorStyles.Left;
            headroomCaption.AutoSize = true;
            headroomCaption.ForeColor = Color.FromArgb(170, 176, 190);
            headroomCaption.Location = new Point(3, 145);
            headroomCaption.Margin = new Padding(3, 3, 3, 4);
            headroomCaption.Name = "headroomCaption";
            headroomCaption.Size = new Size(64, 15);
            headroomCaption.TabIndex = 11;
            headroomCaption.Text = "Headroom";
            // 
            // headroomValue
            // 
            headroomValue.Anchor = AnchorStyles.Right;
            headroomValue.AutoSize = true;
            headroomValue.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
            headroomValue.ForeColor = Color.FromArgb(225, 228, 235);
            headroomValue.Location = new Point(123, 145);
            headroomValue.Margin = new Padding(3, 3, 3, 4);
            headroomValue.Name = "headroomValue";
            headroomValue.Size = new Size(12, 15);
            headroomValue.TabIndex = 12;
            headroomValue.Text = "-";
            // 
            // EqResultsPanel
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(20, 22, 30);
            BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(tableLayout);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.FromArgb(225, 228, 235);
            Name = "EqResultsPanel";
            Padding = new Padding(8);
            Size = new Size(154, 341);
            tableLayout.ResumeLayout(false);
            tableLayout.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TableLayoutPanel tableLayout;
        private Label headerLabel;
        private Label rmsCaption;
        private Label rmsValue;
        private Label maxCaption;
        private Label maxValue;
        private Label filtersCaption;
        private Label filtersValue;
        private Label boostCaption;
        private Label boostValue;
        private Label cutCaption;
        private Label cutValue;
        private Label headroomCaption;
        private Label headroomValue;
    }
}
