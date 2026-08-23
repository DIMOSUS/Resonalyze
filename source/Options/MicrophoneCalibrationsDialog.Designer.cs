namespace Resonalyze.Options
{
    partial class MicrophoneCalibrationsDialog
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
            listViewCalibrations = new ListView();
            columnHeaderName = new ColumnHeader();
            columnHeaderKind = new ColumnHeader();
            columnHeaderDetails = new ColumnHeader();
            columnHeaderStatus = new ColumnHeader();
            buttonAddFile = new Button();
            buttonAddAngle = new Button();
            buttonEdit = new Button();
            buttonRename = new Button();
            buttonRemove = new Button();
            labelHint = new Label();
            buttonOk = new Button();
            buttonCancel = new Button();
            SuspendLayout();
            //
            // listViewCalibrations
            //
            listViewCalibrations.BackColor = Color.FromArgb(55, 60, 72);
            listViewCalibrations.BorderStyle = BorderStyle.FixedSingle;
            listViewCalibrations.Columns.AddRange(new ColumnHeader[]
            {
                columnHeaderName,
                columnHeaderKind,
                columnHeaderDetails,
                columnHeaderStatus
            });
            listViewCalibrations.ForeColor = Color.White;
            listViewCalibrations.FullRowSelect = true;
            listViewCalibrations.LabelEdit = true;
            listViewCalibrations.Location = new Point(16, 16);
            listViewCalibrations.MultiSelect = false;
            listViewCalibrations.Name = "listViewCalibrations";
            listViewCalibrations.Size = new Size(520, 258);
            listViewCalibrations.TabIndex = 0;
            listViewCalibrations.UseCompatibleStateImageBehavior = false;
            listViewCalibrations.View = View.Details;
            //
            // columnHeaderName
            //
            columnHeaderName.Text = "Name";
            columnHeaderName.Width = 140;
            //
            // columnHeaderKind
            //
            columnHeaderKind.Text = "Kind";
            columnHeaderKind.Width = 70;
            //
            // columnHeaderDetails
            //
            columnHeaderDetails.Text = "Details";
            columnHeaderDetails.Width = 190;
            //
            // columnHeaderStatus
            //
            columnHeaderStatus.Text = "Status";
            columnHeaderStatus.Width = 110;
            //
            // buttonAddFile
            //
            buttonAddFile.FlatStyle = FlatStyle.Popup;
            buttonAddFile.ForeColor = Color.White;
            buttonAddFile.Location = new Point(548, 16);
            buttonAddFile.Name = "buttonAddFile";
            buttonAddFile.Size = new Size(126, 28);
            buttonAddFile.TabIndex = 1;
            buttonAddFile.Text = "Add file...";
            buttonAddFile.UseVisualStyleBackColor = true;
            //
            // buttonAddAngle
            //
            buttonAddAngle.FlatStyle = FlatStyle.Popup;
            buttonAddAngle.ForeColor = Color.White;
            buttonAddAngle.Location = new Point(548, 50);
            buttonAddAngle.Name = "buttonAddAngle";
            buttonAddAngle.Size = new Size(126, 28);
            buttonAddAngle.TabIndex = 2;
            buttonAddAngle.Text = "Add angle...";
            buttonAddAngle.UseVisualStyleBackColor = true;
            //
            // buttonEdit
            //
            buttonEdit.FlatStyle = FlatStyle.Popup;
            buttonEdit.ForeColor = Color.White;
            buttonEdit.Location = new Point(548, 92);
            buttonEdit.Name = "buttonEdit";
            buttonEdit.Size = new Size(126, 28);
            buttonEdit.TabIndex = 3;
            buttonEdit.Text = "Edit...";
            buttonEdit.UseVisualStyleBackColor = true;
            //
            // buttonRename
            //
            buttonRename.FlatStyle = FlatStyle.Popup;
            buttonRename.ForeColor = Color.White;
            buttonRename.Location = new Point(548, 126);
            buttonRename.Name = "buttonRename";
            buttonRename.Size = new Size(126, 28);
            buttonRename.TabIndex = 4;
            buttonRename.Text = "Rename";
            buttonRename.UseVisualStyleBackColor = true;
            //
            // buttonRemove
            //
            buttonRemove.FlatStyle = FlatStyle.Popup;
            buttonRemove.ForeColor = Color.White;
            buttonRemove.Location = new Point(548, 160);
            buttonRemove.Name = "buttonRemove";
            buttonRemove.Size = new Size(126, 28);
            buttonRemove.TabIndex = 5;
            buttonRemove.Text = "Remove";
            buttonRemove.UseVisualStyleBackColor = true;
            //
            // labelHint
            //
            labelHint.ForeColor = SystemColors.ControlLight;
            labelHint.Location = new Point(16, 284);
            labelHint.Name = "labelHint";
            labelHint.Size = new Size(520, 52);
            labelHint.TabIndex = 6;
            labelHint.Text = "An angle entry is ESTIMATED from the geometry of your microphone and GRAS reference measurements — it is not a measurement of your own microphone off axis.";
            //
            // buttonOk
            //
            buttonOk.DialogResult = DialogResult.OK;
            buttonOk.FlatStyle = FlatStyle.Popup;
            buttonOk.ForeColor = Color.White;
            buttonOk.Location = new Point(548, 274);
            buttonOk.Name = "buttonOk";
            buttonOk.Size = new Size(126, 28);
            buttonOk.TabIndex = 7;
            buttonOk.Text = "OK";
            buttonOk.UseVisualStyleBackColor = true;
            //
            // buttonCancel
            //
            buttonCancel.DialogResult = DialogResult.Cancel;
            buttonCancel.FlatStyle = FlatStyle.Popup;
            buttonCancel.ForeColor = Color.White;
            buttonCancel.Location = new Point(548, 308);
            buttonCancel.Name = "buttonCancel";
            buttonCancel.Size = new Size(126, 28);
            buttonCancel.TabIndex = 8;
            buttonCancel.Text = "Cancel";
            buttonCancel.UseVisualStyleBackColor = true;
            //
            // MicrophoneCalibrationsDialog
            //
            AcceptButton = buttonOk;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(45, 50, 60);
            CancelButton = buttonCancel;
            ClientSize = new Size(690, 352);
            Controls.Add(listViewCalibrations);
            Controls.Add(buttonAddFile);
            Controls.Add(buttonAddAngle);
            Controls.Add(buttonEdit);
            Controls.Add(buttonRename);
            Controls.Add(buttonRemove);
            Controls.Add(labelHint);
            Controls.Add(buttonOk);
            Controls.Add(buttonCancel);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "MicrophoneCalibrationsDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Additional microphone calibrations";
            ResumeLayout(false);
        }

        #endregion

        private ListView listViewCalibrations;
        private ColumnHeader columnHeaderName;
        private ColumnHeader columnHeaderKind;
        private ColumnHeader columnHeaderDetails;
        private ColumnHeader columnHeaderStatus;
        private Button buttonAddFile;
        private Button buttonAddAngle;
        private Button buttonEdit;
        private Button buttonRename;
        private Button buttonRemove;
        private Label labelHint;
        private Button buttonOk;
        private Button buttonCancel;
    }
}
