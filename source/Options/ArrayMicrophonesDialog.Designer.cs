namespace Resonalyze.Options
{
    partial class ArrayMicrophonesDialog
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
            listViewMicrophones = new ListView();
            columnHeaderInput = new ColumnHeader();
            columnHeaderCalibration = new ColumnHeader();
            columnHeaderNote = new ColumnHeader();
            labelInput = new Label();
            comboBoxInput = new DarkComboBox();
            labelCalibration = new Label();
            comboBoxCalibration = new DarkComboBox();
            labelNote = new Label();
            textBoxNote = new TextBox();
            buttonAdd = new Button();
            buttonUpdate = new Button();
            buttonRemove = new Button();
            labelHint = new Label();
            labelStatus = new Label();
            buttonOk = new Button();
            buttonCancel = new Button();
            SuspendLayout();
            //
            // listViewMicrophones
            //
            listViewMicrophones.BackColor = Color.FromArgb(55, 60, 72);
            listViewMicrophones.BorderStyle = BorderStyle.FixedSingle;
            listViewMicrophones.Columns.AddRange(new ColumnHeader[]
            {
                columnHeaderInput,
                columnHeaderCalibration,
                columnHeaderNote
            });
            listViewMicrophones.ForeColor = Color.White;
            listViewMicrophones.FullRowSelect = true;
            listViewMicrophones.Location = new Point(16, 16);
            listViewMicrophones.MultiSelect = false;
            listViewMicrophones.Name = "listViewMicrophones";
            listViewMicrophones.Size = new Size(520, 190);
            listViewMicrophones.TabIndex = 0;
            listViewMicrophones.UseCompatibleStateImageBehavior = false;
            listViewMicrophones.View = View.Details;
            //
            // columnHeaderInput
            //
            columnHeaderInput.Text = "Input";
            columnHeaderInput.Width = 110;
            //
            // columnHeaderCalibration
            //
            columnHeaderCalibration.Text = "Calibration";
            columnHeaderCalibration.Width = 210;
            //
            // columnHeaderNote
            //
            columnHeaderNote.Text = "Position";
            columnHeaderNote.Width = 196;
            //
            // labelInput
            //
            labelInput.AutoSize = true;
            labelInput.ForeColor = Color.White;
            labelInput.Location = new Point(16, 220);
            labelInput.Name = "labelInput";
            labelInput.Size = new Size(40, 15);
            labelInput.TabIndex = 1;
            labelInput.Text = "Input";
            //
            // comboBoxInput
            //
            comboBoxInput.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxInput.Location = new Point(16, 238);
            comboBoxInput.Name = "comboBoxInput";
            comboBoxInput.Size = new Size(110, 23);
            comboBoxInput.TabIndex = 2;
            //
            // labelCalibration
            //
            labelCalibration.AutoSize = true;
            labelCalibration.ForeColor = Color.White;
            labelCalibration.Location = new Point(136, 220);
            labelCalibration.Name = "labelCalibration";
            labelCalibration.Size = new Size(70, 15);
            labelCalibration.TabIndex = 3;
            labelCalibration.Text = "Calibration";
            //
            // comboBoxCalibration
            //
            comboBoxCalibration.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxCalibration.Location = new Point(136, 238);
            comboBoxCalibration.Name = "comboBoxCalibration";
            comboBoxCalibration.Size = new Size(210, 23);
            comboBoxCalibration.TabIndex = 4;
            //
            // labelNote
            //
            labelNote.AutoSize = true;
            labelNote.ForeColor = Color.White;
            labelNote.Location = new Point(356, 220);
            labelNote.Name = "labelNote";
            labelNote.Size = new Size(52, 15);
            labelNote.TabIndex = 5;
            labelNote.Text = "Position";
            //
            // textBoxNote
            //
            textBoxNote.BackColor = Color.FromArgb(55, 60, 72);
            textBoxNote.BorderStyle = BorderStyle.FixedSingle;
            textBoxNote.ForeColor = Color.White;
            textBoxNote.Location = new Point(356, 238);
            textBoxNote.Name = "textBoxNote";
            textBoxNote.Size = new Size(180, 23);
            textBoxNote.TabIndex = 6;
            //
            // buttonAdd
            //
            buttonAdd.FlatStyle = FlatStyle.Popup;
            buttonAdd.ForeColor = Color.White;
            buttonAdd.Location = new Point(548, 16);
            buttonAdd.Name = "buttonAdd";
            buttonAdd.Size = new Size(126, 28);
            buttonAdd.TabIndex = 7;
            buttonAdd.Text = "Add";
            buttonAdd.UseVisualStyleBackColor = true;
            //
            // buttonUpdate
            //
            buttonUpdate.FlatStyle = FlatStyle.Popup;
            buttonUpdate.ForeColor = Color.White;
            buttonUpdate.Location = new Point(548, 50);
            buttonUpdate.Name = "buttonUpdate";
            buttonUpdate.Size = new Size(126, 28);
            buttonUpdate.TabIndex = 8;
            buttonUpdate.Text = "Update";
            buttonUpdate.UseVisualStyleBackColor = true;
            //
            // buttonRemove
            //
            buttonRemove.FlatStyle = FlatStyle.Popup;
            buttonRemove.ForeColor = Color.White;
            buttonRemove.Location = new Point(548, 84);
            buttonRemove.Name = "buttonRemove";
            buttonRemove.Size = new Size(126, 28);
            buttonRemove.TabIndex = 9;
            buttonRemove.Text = "Remove";
            buttonRemove.UseVisualStyleBackColor = true;
            //
            // labelStatus
            //
            labelStatus.ForeColor = SystemColors.ControlLight;
            labelStatus.Location = new Point(16, 270);
            labelStatus.Name = "labelStatus";
            labelStatus.Size = new Size(520, 20);
            labelStatus.TabIndex = 10;
            //
            // labelHint
            //
            labelHint.ForeColor = SystemColors.ControlLight;
            labelHint.Location = new Point(16, 294);
            labelHint.Name = "labelHint";
            labelHint.Size = new Size(520, 68);
            labelHint.TabIndex = 11;
            labelHint.Text = "Array microphones are further inputs of the SAME interface, recorded through the same sweep and the same loopback. They are averaged into one spatially averaged curve and are never used for timing, so their placement is free — spread them through the listening volume.";
            //
            // buttonOk
            //
            buttonOk.DialogResult = DialogResult.OK;
            buttonOk.FlatStyle = FlatStyle.Popup;
            buttonOk.ForeColor = Color.White;
            buttonOk.Location = new Point(548, 300);
            buttonOk.Name = "buttonOk";
            buttonOk.Size = new Size(126, 28);
            buttonOk.TabIndex = 12;
            buttonOk.Text = "OK";
            buttonOk.UseVisualStyleBackColor = true;
            //
            // buttonCancel
            //
            buttonCancel.DialogResult = DialogResult.Cancel;
            buttonCancel.FlatStyle = FlatStyle.Popup;
            buttonCancel.ForeColor = Color.White;
            buttonCancel.Location = new Point(548, 334);
            buttonCancel.Name = "buttonCancel";
            buttonCancel.Size = new Size(126, 28);
            buttonCancel.TabIndex = 13;
            buttonCancel.Text = "Cancel";
            buttonCancel.UseVisualStyleBackColor = true;
            //
            // ArrayMicrophonesDialog
            //
            AcceptButton = buttonOk;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(45, 50, 60);
            CancelButton = buttonCancel;
            ClientSize = new Size(690, 378);
            Controls.Add(listViewMicrophones);
            Controls.Add(labelInput);
            Controls.Add(comboBoxInput);
            Controls.Add(labelCalibration);
            Controls.Add(comboBoxCalibration);
            Controls.Add(labelNote);
            Controls.Add(textBoxNote);
            Controls.Add(buttonAdd);
            Controls.Add(buttonUpdate);
            Controls.Add(buttonRemove);
            Controls.Add(labelStatus);
            Controls.Add(labelHint);
            Controls.Add(buttonOk);
            Controls.Add(buttonCancel);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "ArrayMicrophonesDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Array microphones";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ListView listViewMicrophones;
        private ColumnHeader columnHeaderInput;
        private ColumnHeader columnHeaderCalibration;
        private ColumnHeader columnHeaderNote;
        private Label labelInput;
        private DarkComboBox comboBoxInput;
        private Label labelCalibration;
        private DarkComboBox comboBoxCalibration;
        private Label labelNote;
        private TextBox textBoxNote;
        private Button buttonAdd;
        private Button buttonUpdate;
        private Button buttonRemove;
        private Label labelStatus;
        private Label labelHint;
        private Button buttonOk;
        private Button buttonCancel;
    }
}
