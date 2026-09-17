namespace Resonalyze
{
    partial class OverlayTargetSettingsDialog
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
            components = new System.ComponentModel.Container();
            nameLabel = new Label();
            nameTextBox = new TextBox();
            sourceLabel = new Label();
            sourceComboBox = new ThemedComboBox();
            presetLabel = new Label();
            presetComboBox = new ThemedComboBox();
            toleranceLabel = new Label();
            toleranceInput = new ThemedNumericUpDown();
            tiltLabel = new Label();
            tiltInput = new ThemedNumericUpDown();
            deviationLabel = new Label();
            deviationModeComboBox = new ThemedComboBox();
            gainHeaderLabel = new Label();
            freqHeaderLabel = new Label();
            widthHeaderLabel = new Label();
            bassLabel = new Label();
            bassGainInput = new ThemedNumericUpDown();
            bassFrequencyInput = new ThemedNumericUpDown();
            bassWidthInput = new ThemedNumericUpDown();
            trebleLabel = new Label();
            trebleGainInput = new ThemedNumericUpDown();
            trebleFrequencyInput = new ThemedNumericUpDown();
            trebleWidthInput = new ThemedNumericUpDown();
            presenceLabel = new Label();
            presenceGainInput = new ThemedNumericUpDown();
            presenceFrequencyInput = new ThemedNumericUpDown();
            presenceWidthInput = new ThemedNumericUpDown();
            colorLabel = new Label();
            colorButton = new ReleaseClickButton();
            thicknessLabel = new Label();
            thicknessInput = new ThemedNumericUpDown();
            styleLabel = new Label();
            styleComboBox = new ThemedComboBox();
            smoothingLabel = new Label();
            smoothingComboBox = new ThemedComboBox();
            opacityLabel = new Label();
            opacityTrackBar = new TrackBar();
            opacityValueLabel = new Label();
            previewLabel = new Label();
            previewPlot = new OxyPlot.WindowsForms.PlotView();
            cancelButton = new ReleaseClickButton();
            saveButton = new ReleaseClickButton();
            toolTip = new WrappingToolTip(components);
            (toleranceInput).BeginInit();
            (tiltInput).BeginInit();
            (bassGainInput).BeginInit();
            (bassFrequencyInput).BeginInit();
            (bassWidthInput).BeginInit();
            (trebleGainInput).BeginInit();
            (trebleFrequencyInput).BeginInit();
            (trebleWidthInput).BeginInit();
            (presenceGainInput).BeginInit();
            (presenceFrequencyInput).BeginInit();
            (presenceWidthInput).BeginInit();
            (thicknessInput).BeginInit();
            ((System.ComponentModel.ISupportInitialize)opacityTrackBar).BeginInit();
            SuspendLayout();
            // 
            // nameLabel
            // 
            nameLabel.AutoSize = true;
            nameLabel.ForeColor = UiPalette.TextSecondary;
            nameLabel.Location = new Point(20, 16);
            nameLabel.Name = "nameLabel";
            nameLabel.Size = new Size(39, 15);
            nameLabel.TabIndex = 0;
            nameLabel.Text = "Name";
            // 
            // nameTextBox
            // 
            nameTextBox.BackColor = UiPalette.InputSurface;
            nameTextBox.ForeColor = UiPalette.TextPrimary;
            nameTextBox.Location = new Point(20, 36);
            nameTextBox.MaxLength = 80;
            nameTextBox.Name = "nameTextBox";
            nameTextBox.Size = new Size(460, 23);
            nameTextBox.TabIndex = 0;
            // 
            // sourceLabel
            // 
            sourceLabel.AutoSize = true;
            sourceLabel.ForeColor = UiPalette.TextSecondary;
            sourceLabel.Location = new Point(20, 68);
            sourceLabel.Name = "sourceLabel";
            sourceLabel.Size = new Size(43, 15);
            sourceLabel.TabIndex = 1;
            sourceLabel.Text = "Source";
            // 
            // sourceComboBox
            // 
            sourceComboBox.BackColor = UiPalette.InputSurface;
            sourceComboBox.ForeColor = UiPalette.TextPrimary;
            sourceComboBox.Location = new Point(20, 88);
            sourceComboBox.Margin = new Padding(0);
            sourceComboBox.MinimumSize = new Size(36, 19);
            sourceComboBox.Name = "sourceComboBox";
            sourceComboBox.Size = new Size(460, 24);
            sourceComboBox.TabIndex = 1;
            // 
            // presetLabel
            // 
            presetLabel.AutoSize = true;
            presetLabel.ForeColor = UiPalette.TextSecondary;
            presetLabel.Location = new Point(20, 120);
            presetLabel.Name = "presetLabel";
            presetLabel.Size = new Size(39, 15);
            presetLabel.TabIndex = 2;
            presetLabel.Text = "Preset";
            // 
            // presetComboBox
            // 
            presetComboBox.BackColor = UiPalette.InputSurface;
            presetComboBox.ForeColor = UiPalette.TextPrimary;
            presetComboBox.Location = new Point(20, 140);
            presetComboBox.Margin = new Padding(0);
            presetComboBox.MinimumSize = new Size(36, 19);
            presetComboBox.Name = "presetComboBox";
            presetComboBox.Size = new Size(200, 24);
            presetComboBox.TabIndex = 2;
            // 
            // toleranceLabel
            // 
            toleranceLabel.AutoSize = true;
            toleranceLabel.ForeColor = UiPalette.TextSecondary;
            toleranceLabel.Location = new Point(260, 120);
            toleranceLabel.Name = "toleranceLabel";
            toleranceLabel.Size = new Size(83, 15);
            toleranceLabel.TabIndex = 3;
            toleranceLabel.Text = "Tolerance ±dB";
            // 
            // toleranceInput
            // 
            toleranceInput.BackColor = UiPalette.InputSurface;
            toleranceInput.DecimalPlaces = 1;
            toleranceInput.ForeColor = UiPalette.TextPrimary;
            toleranceInput.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            toleranceInput.Location = new Point(260, 140);
            toleranceInput.Maximum = new decimal(new int[] { 12, 0, 0, 0 });
            toleranceInput.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            toleranceInput.MinimumSize = new Size(36, 19);
            toleranceInput.Name = "toleranceInput";
            toleranceInput.Size = new Size(220, 24);
            toleranceInput.TabIndex = 3;
            toleranceInput.TextAlign = HorizontalAlignment.Right;
            toleranceInput.ThousandsSeparator = false;
            toleranceInput.Value = new decimal(new int[] { 0, 0, 0, 0 });
            // 
            // tiltLabel
            // 
            tiltLabel.AutoSize = true;
            tiltLabel.ForeColor = UiPalette.TextSecondary;
            tiltLabel.Location = new Point(20, 176);
            tiltLabel.Name = "tiltLabel";
            tiltLabel.Size = new Size(63, 15);
            tiltLabel.TabIndex = 4;
            tiltLabel.Text = "Tilt dB/oct";
            // 
            // tiltInput
            // 
            tiltInput.BackColor = UiPalette.InputSurface;
            tiltInput.DecimalPlaces = 1;
            tiltInput.ForeColor = UiPalette.TextPrimary;
            tiltInput.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            tiltInput.Location = new Point(20, 196);
            tiltInput.Maximum = new decimal(new int[] { 6, 0, 0, 0 });
            tiltInput.Minimum = new decimal(new int[] { 6, 0, 0, int.MinValue });
            tiltInput.MinimumSize = new Size(36, 19);
            tiltInput.Name = "tiltInput";
            tiltInput.Size = new Size(200, 24);
            tiltInput.TabIndex = 4;
            tiltInput.TextAlign = HorizontalAlignment.Right;
            tiltInput.ThousandsSeparator = false;
            tiltInput.Value = new decimal(new int[] { 0, 0, 0, 0 });
            // 
            // deviationLabel
            // 
            deviationLabel.AutoSize = true;
            deviationLabel.ForeColor = UiPalette.TextSecondary;
            deviationLabel.Location = new Point(260, 176);
            deviationLabel.Name = "deviationLabel";
            deviationLabel.Size = new Size(57, 15);
            deviationLabel.TabIndex = 5;
            deviationLabel.Text = "Deviation";
            // 
            // deviationModeComboBox
            // 
            deviationModeComboBox.BackColor = UiPalette.InputSurface;
            deviationModeComboBox.ForeColor = UiPalette.TextPrimary;
            deviationModeComboBox.Location = new Point(260, 196);
            deviationModeComboBox.Margin = new Padding(0);
            deviationModeComboBox.MinimumSize = new Size(36, 19);
            deviationModeComboBox.Name = "deviationModeComboBox";
            deviationModeComboBox.Size = new Size(220, 24);
            deviationModeComboBox.TabIndex = 5;
            // 
            // gainHeaderLabel
            // 
            gainHeaderLabel.AutoSize = true;
            gainHeaderLabel.ForeColor = UiPalette.TextSecondary;
            gainHeaderLabel.Location = new Point(150, 234);
            gainHeaderLabel.Name = "gainHeaderLabel";
            gainHeaderLabel.Size = new Size(48, 15);
            gainHeaderLabel.TabIndex = 6;
            gainHeaderLabel.Text = "Gain dB";
            // 
            // freqHeaderLabel
            // 
            freqHeaderLabel.AutoSize = true;
            freqHeaderLabel.ForeColor = UiPalette.TextSecondary;
            freqHeaderLabel.Location = new Point(260, 234);
            freqHeaderLabel.Name = "freqHeaderLabel";
            freqHeaderLabel.Size = new Size(47, 15);
            freqHeaderLabel.TabIndex = 7;
            freqHeaderLabel.Text = "Freq Hz";
            // 
            // widthHeaderLabel
            // 
            widthHeaderLabel.AutoSize = true;
            widthHeaderLabel.ForeColor = UiPalette.TextSecondary;
            widthHeaderLabel.Location = new Point(370, 234);
            widthHeaderLabel.Name = "widthHeaderLabel";
            widthHeaderLabel.Size = new Size(59, 15);
            widthHeaderLabel.TabIndex = 8;
            widthHeaderLabel.Text = "Width oct";
            // 
            // bassLabel
            // 
            bassLabel.AutoSize = true;
            bassLabel.ForeColor = UiPalette.TextSecondary;
            bassLabel.Location = new Point(20, 256);
            bassLabel.Name = "bassLabel";
            bassLabel.Size = new Size(58, 15);
            bassLabel.TabIndex = 9;
            bassLabel.Text = "Bass shelf";
            // 
            // bassGainInput
            // 
            bassGainInput.BackColor = UiPalette.InputSurface;
            bassGainInput.DecimalPlaces = 1;
            bassGainInput.ForeColor = UiPalette.TextPrimary;
            bassGainInput.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            bassGainInput.Location = new Point(150, 254);
            bassGainInput.Maximum = new decimal(new int[] { 30, 0, 0, 0 });
            bassGainInput.Minimum = new decimal(new int[] { 12, 0, 0, int.MinValue });
            bassGainInput.MinimumSize = new Size(36, 19);
            bassGainInput.Name = "bassGainInput";
            bassGainInput.Size = new Size(100, 24);
            bassGainInput.TabIndex = 6;
            bassGainInput.TextAlign = HorizontalAlignment.Right;
            bassGainInput.ThousandsSeparator = false;
            bassGainInput.Value = new decimal(new int[] { 0, 0, 0, 0 });
            // 
            // bassFrequencyInput
            // 
            bassFrequencyInput.BackColor = UiPalette.InputSurface;
            bassFrequencyInput.DecimalPlaces = 0;
            bassFrequencyInput.ForeColor = UiPalette.TextPrimary;
            bassFrequencyInput.Increment = new decimal(new int[] { 1, 0, 0, 0 });
            bassFrequencyInput.Location = new Point(260, 254);
            bassFrequencyInput.Maximum = new decimal(new int[] { 500, 0, 0, 0 });
            bassFrequencyInput.Minimum = new decimal(new int[] { 20, 0, 0, 0 });
            bassFrequencyInput.MinimumSize = new Size(36, 19);
            bassFrequencyInput.Name = "bassFrequencyInput";
            bassFrequencyInput.Size = new Size(100, 24);
            bassFrequencyInput.TabIndex = 7;
            bassFrequencyInput.TextAlign = HorizontalAlignment.Right;
            bassFrequencyInput.ThousandsSeparator = false;
            bassFrequencyInput.Value = new decimal(new int[] { 20, 0, 0, 0 });
            // 
            // bassWidthInput
            // 
            bassWidthInput.BackColor = UiPalette.InputSurface;
            bassWidthInput.DecimalPlaces = 1;
            bassWidthInput.ForeColor = UiPalette.TextPrimary;
            bassWidthInput.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            bassWidthInput.Location = new Point(370, 254);
            bassWidthInput.Maximum = new decimal(new int[] { 4, 0, 0, 0 });
            bassWidthInput.Minimum = new decimal(new int[] { 2, 0, 0, 65536 });
            bassWidthInput.MinimumSize = new Size(36, 19);
            bassWidthInput.Name = "bassWidthInput";
            bassWidthInput.Size = new Size(100, 24);
            bassWidthInput.TabIndex = 8;
            bassWidthInput.TextAlign = HorizontalAlignment.Right;
            bassWidthInput.ThousandsSeparator = false;
            bassWidthInput.Value = new decimal(new int[] { 2, 0, 0, 65536 });
            // 
            // trebleLabel
            // 
            trebleLabel.AutoSize = true;
            trebleLabel.ForeColor = UiPalette.TextSecondary;
            trebleLabel.Location = new Point(20, 288);
            trebleLabel.Name = "trebleLabel";
            trebleLabel.Size = new Size(67, 15);
            trebleLabel.TabIndex = 10;
            trebleLabel.Text = "Treble shelf";
            // 
            // trebleGainInput
            // 
            trebleGainInput.BackColor = UiPalette.InputSurface;
            trebleGainInput.DecimalPlaces = 1;
            trebleGainInput.ForeColor = UiPalette.TextPrimary;
            trebleGainInput.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            trebleGainInput.Location = new Point(150, 286);
            trebleGainInput.Maximum = new decimal(new int[] { 12, 0, 0, 0 });
            trebleGainInput.Minimum = new decimal(new int[] { 18, 0, 0, int.MinValue });
            trebleGainInput.MinimumSize = new Size(36, 19);
            trebleGainInput.Name = "trebleGainInput";
            trebleGainInput.Size = new Size(100, 24);
            trebleGainInput.TabIndex = 9;
            trebleGainInput.TextAlign = HorizontalAlignment.Right;
            trebleGainInput.ThousandsSeparator = false;
            trebleGainInput.Value = new decimal(new int[] { 0, 0, 0, 0 });
            // 
            // trebleFrequencyInput
            // 
            trebleFrequencyInput.BackColor = UiPalette.InputSurface;
            trebleFrequencyInput.DecimalPlaces = 0;
            trebleFrequencyInput.ForeColor = UiPalette.TextPrimary;
            trebleFrequencyInput.Increment = new decimal(new int[] { 100, 0, 0, 0 });
            trebleFrequencyInput.Location = new Point(260, 286);
            trebleFrequencyInput.Maximum = new decimal(new int[] { 16000, 0, 0, 0 });
            trebleFrequencyInput.Minimum = new decimal(new int[] { 1000, 0, 0, 0 });
            trebleFrequencyInput.MinimumSize = new Size(36, 19);
            trebleFrequencyInput.Name = "trebleFrequencyInput";
            trebleFrequencyInput.Size = new Size(100, 24);
            trebleFrequencyInput.TabIndex = 10;
            trebleFrequencyInput.TextAlign = HorizontalAlignment.Right;
            trebleFrequencyInput.ThousandsSeparator = false;
            trebleFrequencyInput.Value = new decimal(new int[] { 1000, 0, 0, 0 });
            // 
            // trebleWidthInput
            // 
            trebleWidthInput.BackColor = UiPalette.InputSurface;
            trebleWidthInput.DecimalPlaces = 1;
            trebleWidthInput.ForeColor = UiPalette.TextPrimary;
            trebleWidthInput.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            trebleWidthInput.Location = new Point(370, 286);
            trebleWidthInput.Maximum = new decimal(new int[] { 4, 0, 0, 0 });
            trebleWidthInput.Minimum = new decimal(new int[] { 2, 0, 0, 65536 });
            trebleWidthInput.MinimumSize = new Size(36, 19);
            trebleWidthInput.Name = "trebleWidthInput";
            trebleWidthInput.Size = new Size(100, 24);
            trebleWidthInput.TabIndex = 11;
            trebleWidthInput.TextAlign = HorizontalAlignment.Right;
            trebleWidthInput.ThousandsSeparator = false;
            trebleWidthInput.Value = new decimal(new int[] { 2, 0, 0, 65536 });
            // 
            // presenceLabel
            // 
            presenceLabel.AutoSize = true;
            presenceLabel.ForeColor = UiPalette.TextSecondary;
            presenceLabel.Location = new Point(20, 320);
            presenceLabel.Name = "presenceLabel";
            presenceLabel.Size = new Size(54, 15);
            presenceLabel.TabIndex = 12;
            presenceLabel.Text = "Presence";
            // 
            // presenceGainInput
            // 
            presenceGainInput.BackColor = UiPalette.InputSurface;
            presenceGainInput.DecimalPlaces = 1;
            presenceGainInput.ForeColor = UiPalette.TextPrimary;
            presenceGainInput.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            presenceGainInput.Location = new Point(150, 318);
            presenceGainInput.Maximum = new decimal(new int[] { 12, 0, 0, 0 });
            presenceGainInput.Minimum = new decimal(new int[] { 12, 0, 0, int.MinValue });
            presenceGainInput.MinimumSize = new Size(36, 19);
            presenceGainInput.Name = "presenceGainInput";
            presenceGainInput.Size = new Size(100, 24);
            presenceGainInput.TabIndex = 12;
            presenceGainInput.TextAlign = HorizontalAlignment.Right;
            presenceGainInput.ThousandsSeparator = false;
            presenceGainInput.Value = new decimal(new int[] { 0, 0, 0, 0 });
            // 
            // presenceFrequencyInput
            // 
            presenceFrequencyInput.BackColor = UiPalette.InputSurface;
            presenceFrequencyInput.DecimalPlaces = 0;
            presenceFrequencyInput.ForeColor = UiPalette.TextPrimary;
            presenceFrequencyInput.Increment = new decimal(new int[] { 50, 0, 0, 0 });
            presenceFrequencyInput.Location = new Point(260, 318);
            presenceFrequencyInput.Maximum = new decimal(new int[] { 8000, 0, 0, 0 });
            presenceFrequencyInput.Minimum = new decimal(new int[] { 500, 0, 0, 0 });
            presenceFrequencyInput.MinimumSize = new Size(36, 19);
            presenceFrequencyInput.Name = "presenceFrequencyInput";
            presenceFrequencyInput.Size = new Size(100, 24);
            presenceFrequencyInput.TabIndex = 13;
            presenceFrequencyInput.TextAlign = HorizontalAlignment.Right;
            presenceFrequencyInput.ThousandsSeparator = false;
            presenceFrequencyInput.Value = new decimal(new int[] { 500, 0, 0, 0 });
            // 
            // presenceWidthInput
            // 
            presenceWidthInput.BackColor = UiPalette.InputSurface;
            presenceWidthInput.DecimalPlaces = 1;
            presenceWidthInput.ForeColor = UiPalette.TextPrimary;
            presenceWidthInput.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            presenceWidthInput.Location = new Point(370, 318);
            presenceWidthInput.Maximum = new decimal(new int[] { 3, 0, 0, 0 });
            presenceWidthInput.Minimum = new decimal(new int[] { 2, 0, 0, 65536 });
            presenceWidthInput.MinimumSize = new Size(36, 19);
            presenceWidthInput.Name = "presenceWidthInput";
            presenceWidthInput.Size = new Size(100, 24);
            presenceWidthInput.TabIndex = 14;
            presenceWidthInput.TextAlign = HorizontalAlignment.Right;
            presenceWidthInput.ThousandsSeparator = false;
            presenceWidthInput.Value = new decimal(new int[] { 2, 0, 0, 65536 });
            // 
            // colorLabel
            // 
            colorLabel.AutoSize = true;
            colorLabel.ForeColor = UiPalette.TextSecondary;
            colorLabel.Location = new Point(20, 360);
            colorLabel.Name = "colorLabel";
            colorLabel.Size = new Size(36, 15);
            colorLabel.TabIndex = 15;
            colorLabel.Text = "Color";
            // 
            // colorButton
            // 
            colorButton.BackColor = UiPalette.ButtonBackground;
            colorButton.FlatAppearance.BorderSize = 0;
            colorButton.FlatStyle = FlatStyle.Flat;
            colorButton.ForeColor = UiPalette.TextPrimary;
            colorButton.Location = new Point(20, 380);
            colorButton.Name = "colorButton";
            colorButton.Size = new Size(122, 24);
            colorButton.TabIndex = 15;
            colorButton.UseVisualStyleBackColor = false;
            // 
            // thicknessLabel
            // 
            thicknessLabel.AutoSize = true;
            thicknessLabel.ForeColor = UiPalette.TextSecondary;
            thicknessLabel.Location = new Point(162, 360);
            thicknessLabel.Name = "thicknessLabel";
            thicknessLabel.Size = new Size(59, 15);
            thicknessLabel.TabIndex = 16;
            thicknessLabel.Text = "Thickness";
            // 
            // thicknessInput
            // 
            thicknessInput.BackColor = UiPalette.InputSurface;
            thicknessInput.DecimalPlaces = 1;
            thicknessInput.ForeColor = UiPalette.TextPrimary;
            thicknessInput.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            thicknessInput.Location = new Point(162, 380);
            thicknessInput.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            thicknessInput.Minimum = new decimal(new int[] { 5, 0, 0, 65536 });
            thicknessInput.MinimumSize = new Size(36, 19);
            thicknessInput.Name = "thicknessInput";
            thicknessInput.Size = new Size(80, 24);
            thicknessInput.TabIndex = 16;
            thicknessInput.TextAlign = HorizontalAlignment.Right;
            thicknessInput.ThousandsSeparator = false;
            thicknessInput.Value = new decimal(new int[] { 5, 0, 0, 65536 });
            // 
            // styleLabel
            // 
            styleLabel.AutoSize = true;
            styleLabel.ForeColor = UiPalette.TextSecondary;
            styleLabel.Location = new Point(262, 360);
            styleLabel.Name = "styleLabel";
            styleLabel.Size = new Size(32, 15);
            styleLabel.TabIndex = 17;
            styleLabel.Text = "Style";
            // 
            // styleComboBox
            // 
            styleComboBox.BackColor = UiPalette.InputSurface;
            styleComboBox.ForeColor = UiPalette.TextPrimary;
            styleComboBox.Location = new Point(262, 380);
            styleComboBox.Margin = new Padding(0);
            styleComboBox.MinimumSize = new Size(36, 19);
            styleComboBox.Name = "styleComboBox";
            styleComboBox.Size = new Size(218, 24);
            styleComboBox.TabIndex = 17;
            // 
            // smoothingLabel
            // 
            smoothingLabel.AutoSize = true;
            smoothingLabel.ForeColor = UiPalette.TextSecondary;
            smoothingLabel.Location = new Point(20, 418);
            smoothingLabel.Name = "smoothingLabel";
            smoothingLabel.Size = new Size(66, 15);
            smoothingLabel.TabIndex = 18;
            smoothingLabel.Text = "Smoothing";
            // 
            // smoothingComboBox
            // 
            smoothingComboBox.BackColor = UiPalette.InputSurface;
            smoothingComboBox.ForeColor = UiPalette.TextPrimary;
            smoothingComboBox.Location = new Point(20, 438);
            smoothingComboBox.Margin = new Padding(0);
            smoothingComboBox.MinimumSize = new Size(36, 19);
            smoothingComboBox.Name = "smoothingComboBox";
            smoothingComboBox.Size = new Size(460, 24);
            smoothingComboBox.TabIndex = 18;
            // 
            // opacityLabel
            // 
            opacityLabel.AutoSize = true;
            opacityLabel.ForeColor = UiPalette.TextSecondary;
            opacityLabel.Location = new Point(20, 474);
            opacityLabel.Name = "opacityLabel";
            opacityLabel.Size = new Size(48, 15);
            opacityLabel.TabIndex = 19;
            opacityLabel.Text = "Opacity";
            // 
            // opacityTrackBar
            // 
            opacityTrackBar.Location = new Point(14, 494);
            opacityTrackBar.Maximum = 100;
            opacityTrackBar.Minimum = 10;
            opacityTrackBar.Name = "opacityTrackBar";
            opacityTrackBar.Size = new Size(380, 45);
            opacityTrackBar.TabIndex = 19;
            opacityTrackBar.TickFrequency = 10;
            opacityTrackBar.Value = 100;
            // 
            // opacityValueLabel
            // 
            opacityValueLabel.AutoSize = true;
            opacityValueLabel.ForeColor = UiPalette.TextBright;
            opacityValueLabel.Location = new Point(410, 501);
            opacityValueLabel.Name = "opacityValueLabel";
            opacityValueLabel.Size = new Size(35, 15);
            opacityValueLabel.TabIndex = 20;
            opacityValueLabel.Text = "100%";
            // 
            // previewLabel
            // 
            previewLabel.AutoSize = true;
            previewLabel.ForeColor = UiPalette.TextSecondary;
            previewLabel.Location = new Point(20, 544);
            previewLabel.Name = "previewLabel";
            previewLabel.Size = new Size(84, 15);
            previewLabel.TabIndex = 21;
            previewLabel.Text = "Target preview";
            // 
            // previewPlot
            // 
            previewPlot.BackColor = UiPalette.GraphSurfaceMuted;
            previewPlot.Location = new Point(20, 564);
            previewPlot.Name = "previewPlot";
            previewPlot.PanCursor = Cursors.Hand;
            previewPlot.Size = new Size(460, 160);
            previewPlot.TabIndex = 20;
            previewPlot.ZoomHorizontalCursor = Cursors.SizeWE;
            previewPlot.ZoomRectangleCursor = Cursors.SizeNWSE;
            previewPlot.ZoomVerticalCursor = Cursors.SizeNS;
            // 
            // cancelButton
            // 
            cancelButton.BackColor = UiPalette.ButtonBackground;
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.FlatAppearance.BorderSize = 0;
            cancelButton.FlatStyle = FlatStyle.Flat;
            cancelButton.ForeColor = UiPalette.TextPrimary;
            cancelButton.Location = new Point(286, 740);
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new Size(94, 30);
            cancelButton.TabIndex = 21;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = false;
            // 
            // saveButton
            // 
            saveButton.DialogResult = DialogResult.OK;
            saveButton.FlatAppearance.BorderSize = 0;
            saveButton.FlatStyle = FlatStyle.Flat;
            saveButton.ForeColor = UiPalette.TextPrimary;
            saveButton.Location = new Point(386, 740);
            saveButton.Name = "saveButton";
            saveButton.Size = new Size(94, 30);
            saveButton.TabIndex = 22;
            saveButton.Text = "Save";
            saveButton.UseVisualStyleBackColor = false;
            // 
            // OverlayTargetSettingsDialog
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiPalette.DialogBackground;
            ClientSize = new Size(500, 781);
            Controls.Add(nameLabel);
            Controls.Add(nameTextBox);
            Controls.Add(sourceLabel);
            Controls.Add(sourceComboBox);
            Controls.Add(presetLabel);
            Controls.Add(presetComboBox);
            Controls.Add(toleranceLabel);
            Controls.Add(toleranceInput);
            Controls.Add(tiltLabel);
            Controls.Add(tiltInput);
            Controls.Add(deviationLabel);
            Controls.Add(deviationModeComboBox);
            Controls.Add(gainHeaderLabel);
            Controls.Add(freqHeaderLabel);
            Controls.Add(widthHeaderLabel);
            Controls.Add(bassLabel);
            Controls.Add(bassGainInput);
            Controls.Add(bassFrequencyInput);
            Controls.Add(bassWidthInput);
            Controls.Add(trebleLabel);
            Controls.Add(trebleGainInput);
            Controls.Add(trebleFrequencyInput);
            Controls.Add(trebleWidthInput);
            Controls.Add(presenceLabel);
            Controls.Add(presenceGainInput);
            Controls.Add(presenceFrequencyInput);
            Controls.Add(presenceWidthInput);
            Controls.Add(colorLabel);
            Controls.Add(colorButton);
            Controls.Add(thicknessLabel);
            Controls.Add(thicknessInput);
            Controls.Add(styleLabel);
            Controls.Add(styleComboBox);
            Controls.Add(smoothingLabel);
            Controls.Add(smoothingComboBox);
            Controls.Add(opacityLabel);
            Controls.Add(opacityTrackBar);
            Controls.Add(opacityValueLabel);
            Controls.Add(previewLabel);
            Controls.Add(previewPlot);
            Controls.Add(cancelButton);
            Controls.Add(saveButton);
            Font = new Font("Segoe UI", 9F);
            ForeColor = UiPalette.TextBright;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "OverlayTargetSettingsDialog";
            Padding = new Padding(20);
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Target overlay settings";
            (toleranceInput).EndInit();
            (tiltInput).EndInit();
            (bassGainInput).EndInit();
            (bassFrequencyInput).EndInit();
            (bassWidthInput).EndInit();
            (trebleGainInput).EndInit();
            (trebleFrequencyInput).EndInit();
            (trebleWidthInput).EndInit();
            (presenceGainInput).EndInit();
            (presenceFrequencyInput).EndInit();
            (presenceWidthInput).EndInit();
            (thicknessInput).EndInit();
            ((System.ComponentModel.ISupportInitialize)opacityTrackBar).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label nameLabel;
        private TextBox nameTextBox;
        private Label sourceLabel;
        private ThemedComboBox sourceComboBox;
        private Label presetLabel;
        private ThemedComboBox presetComboBox;
        private Label toleranceLabel;
        private ThemedNumericUpDown toleranceInput;
        private Label tiltLabel;
        private ThemedNumericUpDown tiltInput;
        private Label deviationLabel;
        private ThemedComboBox deviationModeComboBox;
        private Label gainHeaderLabel;
        private Label freqHeaderLabel;
        private Label widthHeaderLabel;
        private Label bassLabel;
        private ThemedNumericUpDown bassGainInput;
        private ThemedNumericUpDown bassFrequencyInput;
        private ThemedNumericUpDown bassWidthInput;
        private Label trebleLabel;
        private ThemedNumericUpDown trebleGainInput;
        private ThemedNumericUpDown trebleFrequencyInput;
        private ThemedNumericUpDown trebleWidthInput;
        private Label presenceLabel;
        private ThemedNumericUpDown presenceGainInput;
        private ThemedNumericUpDown presenceFrequencyInput;
        private ThemedNumericUpDown presenceWidthInput;
        private Label colorLabel;
        private ReleaseClickButton colorButton;
        private Label thicknessLabel;
        private ThemedNumericUpDown thicknessInput;
        private Label styleLabel;
        private ThemedComboBox styleComboBox;
        private Label smoothingLabel;
        private ThemedComboBox smoothingComboBox;
        private Label opacityLabel;
        private TrackBar opacityTrackBar;
        private Label opacityValueLabel;
        private Label previewLabel;
        private OxyPlot.WindowsForms.PlotView previewPlot;
        private ReleaseClickButton cancelButton;
        private ReleaseClickButton saveButton;
        private WrappingToolTip toolTip;
    }
}
