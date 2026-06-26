namespace Resonalyze.Options
{
    public partial class LiveSpectrumOpt : Form
    {
        private static readonly int[] SequenceLengths =
            { 256, 512, 1024, 2048, 4096, 8192, 16384 };
        private static readonly int[] OverlapPercents = { 0, 50, 75 };
        private readonly ToolTip toolTip = new();

        public LiveSpectrumOpt()
        {
            InitializeComponent();
            SmoothingPresetOptions.Configure(comboSmoothingInverseOctaves);
            InitializeToolTips();
            FormClosed += (_, _) => toolTip.Dispose();
        }

        public void Init(LiveSpectrumOptions options)
        {
            modeComboBox.Items.Clear();
            modeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            modeComboBox.Items.Add(new LiveSpectrumModeOption(
                LiveSpectrumMode.TransferFunction,
                "Transfer Function"));
            modeComboBox.Items.Add(new LiveSpectrumModeOption(
                LiveSpectrumMode.InputSpectrum,
                "Input Spectrum"));
            modeComboBox.SelectedIndex = FindModeIndex(options.Mode);

            sequenceLengthComboBox.Items.Clear();
            foreach (int sequenceLength in SequenceLengths)
            {
                sequenceLengthComboBox.Items.Add(sequenceLength);
            }
            sequenceLengthComboBox.SelectedItem = NormalizeSequenceLength(options.SequenceLength);

            overlapComboBox.Items.Clear();
            foreach (int overlapPercent in OverlapPercents)
            {
                overlapComboBox.Items.Add(new OverlapOption(overlapPercent));
            }
            overlapComboBox.SelectedIndex = FindOverlapIndex(options.OverlapPercent);

            comboSmoothingInverseOctaves.SelectedItem =
                SmoothingPresetOptions.Normalize(options.SmoothingInverseOctaves);

            checkUseCalibration.Checked = options.UseCalibration;
        }

        public void SetOptions(LiveSpectrumOptions options)
        {
            options.Mode = modeComboBox.SelectedItem is LiveSpectrumModeOption modeOption
                ? modeOption.Mode
                : LiveSpectrumMode.TransferFunction;
            options.UseCalibration = checkUseCalibration.Checked;
            options.SequenceLength = sequenceLengthComboBox.SelectedItem is int sequenceLength
                ? sequenceLength
                : SequenceLengths[0];
            options.OverlapPercent =
                overlapComboBox.SelectedItem is OverlapOption overlapOption
                    ? overlapOption.Percent
                    : OverlapPercents[0];
            options.SmoothingInverseOctaves =
                comboSmoothingInverseOctaves.SelectedItem is int inverseOctaves
                    ? inverseOctaves
                    : SmoothingPresetOptions.SupportedInverseOctaves[0];
        }

        private static int FindOverlapIndex(int overlapPercent)
        {
            int normalizedIndex = 0;
            for (int index = 0; index < OverlapPercents.Length; index++)
            {
                if (overlapPercent >= OverlapPercents[index])
                {
                    normalizedIndex = index;
                }
            }

            return normalizedIndex;
        }

        private sealed class OverlapOption
        {
            public OverlapOption(int percent)
            {
                Percent = percent;
            }

            public int Percent { get; }

            public override string ToString()
            {
                return Percent == 0 ? "Off" : $"{Percent}%";
            }
        }

        private static int NormalizeSequenceLength(int sequenceLength)
        {
            int normalized = SequenceLengths[0];
            foreach (int candidate in SequenceLengths)
            {
                if (sequenceLength >= candidate)
                {
                    normalized = candidate;
                }
            }

            return normalized;
        }

        private int FindModeIndex(LiveSpectrumMode mode)
        {
            for (int index = 0; index < modeComboBox.Items.Count; index++)
            {
                if (modeComboBox.Items[index] is LiveSpectrumModeOption option &&
                    option.Mode == mode)
                {
                    return index;
                }
            }

            return 0;
        }

        private sealed class LiveSpectrumModeOption
        {
            public LiveSpectrumModeOption(LiveSpectrumMode mode, string displayName)
            {
                Mode = mode;
                DisplayName = displayName;
            }

            public LiveSpectrumMode Mode { get; }

            public string DisplayName { get; }

            public override string ToString()
            {
                return DisplayName;
            }
        }

        private void InitializeToolTips()
        {
            toolTip.SetToolTip(
                modeComboBox,
                "Chooses whether Live Spectrum shows the direct input spectrum or the transfer function relative to the selected loopback channel.");
            toolTip.SetToolTip(
                sequenceLengthComboBox,
                "Sets the FFT block size. Longer sequences give finer frequency resolution but slower visual updates.");
            toolTip.SetToolTip(
                overlapComboBox,
                "Overlaps successive analysis frames by sliding the FFT window a fraction of its size. Higher overlap gives faster, smoother averaging at the cost of more CPU.");
            toolTip.SetToolTip(
                comboSmoothingInverseOctaves,
                "Applies fractional-octave smoothing to the displayed Live Spectrum curve.");
            toolTip.SetToolTip(
                checkUseCalibration,
                "Applies the loaded microphone calibration file to Live Spectrum.");
        }
    }
}
