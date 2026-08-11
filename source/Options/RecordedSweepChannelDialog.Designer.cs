namespace Resonalyze.Options
{
    partial class RecordedSweepChannelDialog
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            labelInstruction = new Label();
            channelGridView = new DataGridView();
            ColumnChannel = new DataGridViewTextBoxColumn();
            ColumnMatch = new DataGridViewTextBoxColumn();
            ColumnRms = new DataGridViewTextBoxColumn();
            ColumnPeak = new DataGridViewTextBoxColumn();
            buttonMeasure = new Button();
            buttonCancel = new Button();
            ((System.ComponentModel.ISupportInitialize)channelGridView).BeginInit();
            SuspendLayout();
            //
            // labelInstruction
            //
            labelInstruction.ForeColor = SystemColors.ControlLight;
            labelInstruction.Location = new Point(16, 16);
            labelInstruction.Name = "labelInstruction";
            labelInstruction.Size = new Size(430, 72);
            labelInstruction.TabIndex = 0;
            labelInstruction.Text = "More than one channel holds this sweep. A track that recorded the sweep itself — a reference or loopback written beside the microphone — matches best of all, and measures as a flat response. Pick the microphone.";
            //
            // channelGridView
            //
            channelGridView.AllowUserToAddRows = false;
            channelGridView.AllowUserToDeleteRows = false;
            channelGridView.AllowUserToResizeColumns = false;
            channelGridView.AllowUserToResizeRows = false;
            channelGridView.BackgroundColor = Color.FromArgb(40, 42, 48);
            channelGridView.BorderStyle = BorderStyle.None;
            channelGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            channelGridView.Columns.AddRange(new DataGridViewColumn[] { ColumnChannel, ColumnMatch, ColumnRms, ColumnPeak });
            channelGridView.Location = new Point(16, 96);
            channelGridView.Margin = new Padding(0);
            channelGridView.MultiSelect = false;
            channelGridView.Name = "channelGridView";
            channelGridView.ReadOnly = true;
            channelGridView.RowHeadersVisible = false;
            channelGridView.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            channelGridView.Size = new Size(430, 150);
            channelGridView.TabIndex = 1;
            channelGridView.CellDoubleClick += channelGridView_CellDoubleClick;
            //
            // ColumnChannel
            //
            ColumnChannel.HeaderText = "Channel";
            ColumnChannel.Name = "ColumnChannel";
            ColumnChannel.ReadOnly = true;
            ColumnChannel.Width = 160;
            //
            // ColumnMatch
            //
            ColumnMatch.HeaderText = "Match";
            ColumnMatch.Name = "ColumnMatch";
            ColumnMatch.ReadOnly = true;
            ColumnMatch.Width = 80;
            //
            // ColumnRms
            //
            ColumnRms.HeaderText = "RMS";
            ColumnRms.Name = "ColumnRms";
            ColumnRms.ReadOnly = true;
            ColumnRms.Width = 92;
            //
            // ColumnPeak
            //
            ColumnPeak.HeaderText = "Peak";
            ColumnPeak.Name = "ColumnPeak";
            ColumnPeak.ReadOnly = true;
            ColumnPeak.Width = 92;
            //
            // buttonMeasure
            //
            buttonMeasure.DialogResult = DialogResult.OK;
            buttonMeasure.FlatStyle = FlatStyle.Popup;
            buttonMeasure.ForeColor = Color.White;
            buttonMeasure.Location = new Point(266, 258);
            buttonMeasure.Name = "buttonMeasure";
            buttonMeasure.Size = new Size(104, 30);
            buttonMeasure.TabIndex = 2;
            buttonMeasure.Text = "Measure";
            buttonMeasure.UseVisualStyleBackColor = true;
            //
            // buttonCancel
            //
            buttonCancel.DialogResult = DialogResult.Cancel;
            buttonCancel.FlatStyle = FlatStyle.Popup;
            buttonCancel.ForeColor = Color.White;
            buttonCancel.Location = new Point(376, 258);
            buttonCancel.Name = "buttonCancel";
            buttonCancel.Size = new Size(70, 30);
            buttonCancel.TabIndex = 3;
            buttonCancel.Text = "Cancel";
            buttonCancel.UseVisualStyleBackColor = true;
            //
            // RecordedSweepChannelDialog
            //
            AcceptButton = buttonMeasure;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
            CancelButton = buttonCancel;
            ClientSize = new Size(462, 302);
            Controls.Add(labelInstruction);
            Controls.Add(channelGridView);
            Controls.Add(buttonMeasure);
            Controls.Add(buttonCancel);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "RecordedSweepChannelDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Choose the channel to measure";
            ((System.ComponentModel.ISupportInitialize)channelGridView).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private Label labelInstruction;
        private DataGridView channelGridView;
        private DataGridViewTextBoxColumn ColumnChannel;
        private DataGridViewTextBoxColumn ColumnMatch;
        private DataGridViewTextBoxColumn ColumnRms;
        private DataGridViewTextBoxColumn ColumnPeak;
        private Button buttonMeasure;
        private Button buttonCancel;
    }
}
