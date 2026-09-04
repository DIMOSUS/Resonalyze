namespace Resonalyze.Ui.Dialogs
{
    partial class GraphHelpDialog
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
            tableRows = new TableLayoutPanel();
            panelButtons = new Panel();
            buttonClose = new ReleaseClickButton();
            labelIntroduction = new Label();
            panelButtons.SuspendLayout();
            SuspendLayout();
            //
            // tableRows
            //
            tableRows.AutoScroll = true;
            tableRows.ColumnCount = 2;
            tableRows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tableRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableRows.Dock = DockStyle.Fill;
            tableRows.GrowStyle = TableLayoutPanelGrowStyle.AddRows;
            tableRows.Location = new Point(16, 66);
            tableRows.Name = "tableRows";
            tableRows.RowCount = 0;
            tableRows.Size = new Size(628, 536);
            tableRows.TabIndex = 1;
            //
            // panelButtons
            //
            panelButtons.Controls.Add(buttonClose);
            panelButtons.Dock = DockStyle.Bottom;
            panelButtons.Location = new Point(16, 602);
            panelButtons.Name = "panelButtons";
            panelButtons.Size = new Size(628, 42);
            panelButtons.TabIndex = 2;
            //
            // buttonClose
            //
            buttonClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonClose.FlatStyle = FlatStyle.Popup;
            buttonClose.ForeColor = Color.White;
            buttonClose.Location = new Point(532, 10);
            buttonClose.Name = "buttonClose";
            buttonClose.Size = new Size(96, 26);
            buttonClose.TabIndex = 0;
            buttonClose.Text = "Close";
            buttonClose.UseVisualStyleBackColor = true;
            //
            // labelIntroduction
            //
            labelIntroduction.AutoSize = true;
            labelIntroduction.Dock = DockStyle.Top;
            labelIntroduction.ForeColor = Color.FromArgb(185, 190, 200);
            labelIntroduction.Location = new Point(16, 16);
            labelIntroduction.MaximumSize = new Size(620, 0);
            labelIntroduction.Name = "labelIntroduction";
            labelIntroduction.Padding = new Padding(0, 0, 0, 12);
            labelIntroduction.Size = new Size(620, 50);
            labelIntroduction.TabIndex = 0;
            //
            // GraphHelpDialog
            //
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(40, 44, 54);
            ClientSize = new Size(660, 660);
            // Docked in the reverse of the order they are added: WinForms lays out the
            // LAST child first, so the note and the buttons claim their bands and the
            // card fills what is left of the client area.
            Controls.Add(tableRows);
            Controls.Add(panelButtons);
            Controls.Add(labelIntroduction);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            Name = "GraphHelpDialog";
            Padding = new Padding(16);
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Graph controls";
            panelButtons.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TableLayoutPanel tableRows;
        private Panel panelButtons;
        private ReleaseClickButton buttonClose;
        private Label labelIntroduction;
    }
}
