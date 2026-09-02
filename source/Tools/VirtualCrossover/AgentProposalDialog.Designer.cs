namespace Resonalyze
{
    partial class AgentProposalDialog
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
            labelSummary = new Label();
            labelWarnings = new Label();
            gridView = new DataGridView();
            ColumnApply = new DataGridViewCheckBoxColumn();
            ColumnChannel = new DataGridViewTextBoxColumn();
            ColumnParameter = new DataGridViewTextBoxColumn();
            ColumnCurrent = new DataGridViewTextBoxColumn();
            ColumnProposed = new DataGridViewTextBoxColumn();
            ColumnStatus = new DataGridViewTextBoxColumn();
            ColumnReason = new DataGridViewTextBoxColumn();
            labelDetail = new Label();
            textBoxDetail = new TextBox();
            labelFootnote = new Label();
            buttonApply = new ReleaseClickButton();
            buttonCancel = new ReleaseClickButton();
            ((System.ComponentModel.ISupportInitialize)gridView).BeginInit();
            SuspendLayout();
            //
            // labelSummary
            //
            labelSummary.AutoSize = true;
            labelSummary.ForeColor = Color.FromArgb(210, 214, 222);
            labelSummary.Location = new Point(12, 12);
            labelSummary.MaximumSize = new Size(876, 60);
            labelSummary.Name = "labelSummary";
            labelSummary.Size = new Size(876, 15);
            labelSummary.TabIndex = 0;
            labelSummary.Text = "summary";
            //
            // labelWarnings
            //
            labelWarnings.AutoSize = true;
            labelWarnings.ForeColor = Color.FromArgb(230, 184, 0);
            labelWarnings.Location = new Point(12, 76);
            labelWarnings.MaximumSize = new Size(876, 30);
            labelWarnings.Name = "labelWarnings";
            labelWarnings.Size = new Size(0, 15);
            labelWarnings.TabIndex = 1;
            //
            // gridView
            //
            gridView.AllowUserToAddRows = false;
            gridView.AllowUserToDeleteRows = false;
            gridView.AllowUserToResizeRows = false;
            gridView.BackgroundColor = Color.FromArgb(40, 42, 48);
            gridView.BorderStyle = BorderStyle.None;
            gridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            gridView.Columns.AddRange(new DataGridViewColumn[] { ColumnApply, ColumnChannel, ColumnParameter, ColumnCurrent, ColumnProposed, ColumnStatus, ColumnReason });
            gridView.Location = new Point(12, 110);
            gridView.Margin = new Padding(0);
            gridView.MultiSelect = false;
            gridView.Name = "gridView";
            gridView.RowHeadersVisible = false;
            gridView.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            gridView.Size = new Size(876, 240);
            gridView.TabIndex = 2;
            //
            // ColumnApply
            //
            ColumnApply.HeaderText = "Apply";
            ColumnApply.Name = "ColumnApply";
            ColumnApply.Width = 50;
            //
            // ColumnChannel
            //
            ColumnChannel.HeaderText = "Channel";
            ColumnChannel.Name = "ColumnChannel";
            ColumnChannel.ReadOnly = true;
            ColumnChannel.Width = 80;
            //
            // ColumnParameter
            //
            ColumnParameter.HeaderText = "Parameter";
            ColumnParameter.Name = "ColumnParameter";
            ColumnParameter.ReadOnly = true;
            ColumnParameter.Width = 90;
            //
            // ColumnCurrent
            //
            ColumnCurrent.HeaderText = "Current";
            ColumnCurrent.Name = "ColumnCurrent";
            ColumnCurrent.ReadOnly = true;
            ColumnCurrent.Width = 170;
            //
            // ColumnProposed
            //
            ColumnProposed.HeaderText = "Proposed";
            ColumnProposed.Name = "ColumnProposed";
            ColumnProposed.ReadOnly = true;
            ColumnProposed.Width = 170;
            //
            // ColumnStatus
            //
            ColumnStatus.HeaderText = "Status";
            ColumnStatus.Name = "ColumnStatus";
            ColumnStatus.ReadOnly = true;
            ColumnStatus.Width = 80;
            //
            // ColumnReason
            //
            ColumnReason.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            ColumnReason.HeaderText = "Reason";
            ColumnReason.Name = "ColumnReason";
            ColumnReason.ReadOnly = true;
            //
            // labelDetail
            //
            labelDetail.AutoSize = true;
            labelDetail.ForeColor = Color.FromArgb(185, 190, 200);
            labelDetail.Location = new Point(12, 360);
            labelDetail.Name = "labelDetail";
            labelDetail.Size = new Size(120, 15);
            labelDetail.TabIndex = 3;
            labelDetail.Text = "Selected row and advice:";
            //
            // textBoxDetail
            //
            textBoxDetail.BackColor = Color.FromArgb(33, 36, 45);
            textBoxDetail.BorderStyle = BorderStyle.FixedSingle;
            textBoxDetail.ForeColor = Color.FromArgb(210, 214, 222);
            textBoxDetail.Location = new Point(12, 380);
            textBoxDetail.Multiline = true;
            textBoxDetail.Name = "textBoxDetail";
            textBoxDetail.ReadOnly = true;
            textBoxDetail.ScrollBars = ScrollBars.Vertical;
            textBoxDetail.Size = new Size(876, 120);
            textBoxDetail.TabIndex = 4;
            //
            // labelFootnote
            //
            labelFootnote.AutoSize = true;
            labelFootnote.ForeColor = Color.FromArgb(150, 156, 168);
            labelFootnote.Location = new Point(12, 512);
            labelFootnote.MaximumSize = new Size(640, 30);
            labelFootnote.Name = "labelFootnote";
            labelFootnote.Size = new Size(640, 30);
            labelFootnote.TabIndex = 5;
            labelFootnote.Text = "Ticked rows are written to the channels as one set. Rejected rows cannot be " +
                "ticked; the reason is in the box above. Validity is not quality: listen before you trust it.";
            //
            // buttonApply
            //
            buttonApply.BackColor = Color.FromArgb(46, 51, 67);
            buttonApply.DialogResult = DialogResult.OK;
            buttonApply.FlatStyle = FlatStyle.Popup;
            buttonApply.ForeColor = Color.White;
            buttonApply.Location = new Point(688, 514);
            buttonApply.Name = "buttonApply";
            buttonApply.Size = new Size(108, 26);
            buttonApply.TabIndex = 6;
            buttonApply.Text = "Apply selected";
            buttonApply.UseVisualStyleBackColor = false;
            //
            // buttonCancel
            //
            buttonCancel.DialogResult = DialogResult.Cancel;
            buttonCancel.FlatStyle = FlatStyle.Popup;
            buttonCancel.ForeColor = Color.White;
            buttonCancel.Location = new Point(804, 514);
            buttonCancel.Name = "buttonCancel";
            buttonCancel.Size = new Size(84, 26);
            buttonCancel.TabIndex = 7;
            buttonCancel.Text = "Cancel";
            buttonCancel.UseVisualStyleBackColor = true;
            //
            // AgentProposalDialog
            //
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(40, 44, 54);
            CancelButton = buttonCancel;
            ClientSize = new Size(900, 552);
            Controls.Add(labelSummary);
            Controls.Add(labelWarnings);
            Controls.Add(gridView);
            Controls.Add(labelDetail);
            Controls.Add(textBoxDetail);
            Controls.Add(labelFootnote);
            Controls.Add(buttonApply);
            Controls.Add(buttonCancel);
            Font = new Font("Segoe UI", 9F);
            ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "AgentProposalDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Import AI proposal";
            ((System.ComponentModel.ISupportInitialize)gridView).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label labelSummary;
        private Label labelWarnings;
        private DataGridView gridView;
        private DataGridViewCheckBoxColumn ColumnApply;
        private DataGridViewTextBoxColumn ColumnChannel;
        private DataGridViewTextBoxColumn ColumnParameter;
        private DataGridViewTextBoxColumn ColumnCurrent;
        private DataGridViewTextBoxColumn ColumnProposed;
        private DataGridViewTextBoxColumn ColumnStatus;
        private DataGridViewTextBoxColumn ColumnReason;
        private Label labelDetail;
        private TextBox textBoxDetail;
        private Label labelFootnote;
        private ReleaseClickButton buttonApply;
        private ReleaseClickButton buttonCancel;
    }
}
