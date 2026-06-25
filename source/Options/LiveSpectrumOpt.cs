namespace Resonalyze.Options
{
    public partial class LiveSpectrumOpt : Form
    {
        private static readonly int[] SequenceLengths =
            { 256, 512, 1024, 2048, 4096, 8192, 16384 };
        private readonly ToolTip toolTip = new();

        public LiveSpectrumOpt()
        {
            InitializeComponent();
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
                checkUseCalibration,
                "Applies the loaded microphone calibration file to Live Spectrum.");
        }
    }
}
