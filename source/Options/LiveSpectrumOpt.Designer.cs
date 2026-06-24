namespace Resonalyze.Options
{
    partial class LiveSpectrumOpt
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
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            label1 = new Label();
            button1 = new Button();
            checkUseCalibration = new CheckBox();
            label2 = new Label();
            modeComboBox = new ComboBox();
            label3 = new Label();
            sequenceLengthComboBox = new ComboBox();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.ForeColor = SystemColors.ControlLight;
            label1.Location = new Point(12, 14);
            label1.Name = "label1";
            label1.Size = new Size(38, 15);
            label1.TabIndex = 37;
            label1.Text = "Mode";
            // 
            // button1
            // 
            button1.BackColor = Color.FromArgb(50, 55, 80);
            button1.DialogResult = DialogResult.OK;
            button1.FlatStyle = FlatStyle.Popup;
            button1.ForeColor = Color.White;
            button1.Location = new Point(12, 237);
            button1.Name = "button1";
            button1.Size = new Size(241, 23);
            button1.TabIndex = 35;
            button1.Text = "Apply settings";
            button1.UseVisualStyleBackColor = false;
            // 
            // checkUseCalibration
            // 
            checkUseCalibration.AutoSize = true;
            checkUseCalibration.ForeColor = SystemColors.ControlLight;
            checkUseCalibration.Location = new Point(238, 74);
            checkUseCalibration.Name = "checkUseCalibration";
            checkUseCalibration.Size = new Size(15, 14);
            checkUseCalibration.TabIndex = 47;
            checkUseCalibration.UseVisualStyleBackColor = true;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.ForeColor = SystemColors.ControlLight;
            label2.Location = new Point(12, 74);
            label2.Name = "label2";
            label2.Size = new Size(87, 15);
            label2.TabIndex = 46;
            label2.Text = "Use Calibration";
            // 
            // modeComboBox
            // 
            modeComboBox.FormattingEnabled = true;
            modeComboBox.Location = new Point(132, 11);
            modeComboBox.Name = "modeComboBox";
            modeComboBox.Size = new Size(121, 23);
            modeComboBox.TabIndex = 48;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.ForeColor = SystemColors.ControlLight;
            label3.Location = new Point(12, 43);
            label3.Name = "label3";
            label3.Size = new Size(98, 15);
            label3.TabIndex = 49;
            label3.Text = "Sequence Length";
            // 
            // sequenceLengthComboBox
            // 
            sequenceLengthComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            sequenceLengthComboBox.FormattingEnabled = true;
            sequenceLengthComboBox.Location = new Point(132, 40);
            sequenceLengthComboBox.Name = "sequenceLengthComboBox";
            sequenceLengthComboBox.Size = new Size(121, 23);
            sequenceLengthComboBox.TabIndex = 50;
            // 
            // LiveSpectrumOpt
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(45, 50, 60);
            ClientSize = new Size(265, 272);
            Controls.Add(sequenceLengthComboBox);
            Controls.Add(label3);
            Controls.Add(modeComboBox);
            Controls.Add(checkUseCalibration);
            Controls.Add(label2);
            Controls.Add(label1);
            Controls.Add(button1);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "LiveSpectrumOpt";
            ShowInTaskbar = false;
            Text = "Live Spectrum Options";
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion
        private Label label1;
        private Button button1;
        private CheckBox checkUseCalibration;
        private Label label2;
        private ComboBox modeComboBox;
        private Label label3;
        private ComboBox sequenceLengthComboBox;
    }
}
