using Resonalyze.Dsp;

namespace Resonalyze.Options
{
    public partial class LiveSpectrumOpt : Form
    {
        private static readonly int[] SequenceLengths =
            { 256, 512, 1024, 2048, 4096, 8192, 16384 };
        private static readonly int[] OverlapPercents = { 0, 50, 75 };
        private static readonly int[] CoherenceLimits = { 0, 10, 20, 25, 30, 40, 50 };
        private readonly ToolTip toolTip = new();

        /// <summary>
        /// Raised when the user clicks Reset Average. Handled live (without an
        /// Apply / restart) so the Infinite averaging preset can be cleared.
        /// </summary>
        public event Action? ResetAverageRequested;

        public LiveSpectrumOpt()
        {
            InitializeComponent();
            SmoothingPresetOptions.Configure(comboSmoothingInverseOctaves);
            buttonResetAverage.Click += (_, _) => ResetAverageRequested?.Invoke();
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

            windowComboBox.Items.Clear();
            windowComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            windowComboBox.Items.Add(new WindowOption(WindowType.Hann, "Hann"));
            windowComboBox.Items.Add(new WindowOption(WindowType.FlatTop, "Flat Top"));
            windowComboBox.Items.Add(
                new WindowOption(WindowType.BlackmanHarris, "Blackman-Harris"));
            windowComboBox.Items.Add(
                new WindowOption(WindowType.Rectangular, "Rectangular"));
            windowComboBox.SelectedIndex = FindWindowIndex(options.WindowType);

            averagingComboBox.Items.Clear();
            averagingComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            averagingComboBox.Items.Add(new AveragingOption(AveragingSpeed.Fast, "Fast"));
            averagingComboBox.Items.Add(new AveragingOption(AveragingSpeed.Medium, "Medium"));
            averagingComboBox.Items.Add(new AveragingOption(AveragingSpeed.Slow, "Slow"));
            averagingComboBox.Items.Add(
                new AveragingOption(AveragingSpeed.Infinite, "Infinite"));
            averagingComboBox.SelectedIndex = FindAveragingIndex(options.AveragingSpeed);

            coherenceLimitComboBox.Items.Clear();
            foreach (int limit in CoherenceLimits)
            {
                coherenceLimitComboBox.Items.Add(new CoherenceLimitOption(limit));
            }
            coherenceLimitComboBox.SelectedIndex =
                FindCoherenceLimitIndex(options.CoherenceThresholdPercent);

            checkMainCurve.Checked = options.ShowMainCurve;
            checkPeakHold.Checked = options.PeakHold;
            checkCoherence.Checked = options.ShowCoherence;
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
            options.WindowType = windowComboBox.SelectedItem is WindowOption windowOption
                ? windowOption.WindowType
                : WindowType.Hann;
            options.AveragingSpeed =
                averagingComboBox.SelectedItem is AveragingOption averagingOption
                    ? averagingOption.Speed
                    : AveragingSpeed.Medium;
            options.ShowMainCurve = checkMainCurve.Checked;
            options.PeakHold = checkPeakHold.Checked;
            options.ShowCoherence = checkCoherence.Checked;
            options.CoherenceThresholdPercent =
                coherenceLimitComboBox.SelectedItem is CoherenceLimitOption limitOption
                    ? limitOption.Percent
                    : CoherenceLimits[0];
        }

        private static int FindCoherenceLimitIndex(int thresholdPercent)
        {
            int normalizedIndex = 0;
            for (int index = 0; index < CoherenceLimits.Length; index++)
            {
                if (thresholdPercent >= CoherenceLimits[index])
                {
                    normalizedIndex = index;
                }
            }

            return normalizedIndex;
        }

        private sealed class CoherenceLimitOption
        {
            public CoherenceLimitOption(int percent)
            {
                Percent = percent;
            }

            public int Percent { get; }

            public override string ToString() => Percent == 0 ? "Off" : $"{Percent}%";
        }

        private int FindWindowIndex(WindowType windowType)
        {
            for (int index = 0; index < windowComboBox.Items.Count; index++)
            {
                if (windowComboBox.Items[index] is WindowOption option &&
                    option.WindowType == windowType)
                {
                    return index;
                }
            }

            return 0;
        }

        private int FindAveragingIndex(AveragingSpeed speed)
        {
            for (int index = 0; index < averagingComboBox.Items.Count; index++)
            {
                if (averagingComboBox.Items[index] is AveragingOption option &&
                    option.Speed == speed)
                {
                    return index;
                }
            }

            return 0;
        }

        private sealed class WindowOption
        {
            public WindowOption(WindowType windowType, string displayName)
            {
                WindowType = windowType;
                DisplayName = displayName;
            }

            public WindowType WindowType { get; }

            public string DisplayName { get; }

            public override string ToString() => DisplayName;
        }

        private sealed class AveragingOption
        {
            public AveragingOption(AveragingSpeed speed, string displayName)
            {
                Speed = speed;
                DisplayName = displayName;
            }

            public AveragingSpeed Speed { get; }

            public string DisplayName { get; }

            public override string ToString() => DisplayName;
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
                windowComboBox,
                "Analysis window applied before the FFT. Hann is a good default; Flat Top maximizes amplitude accuracy; Blackman-Harris suppresses spectral leakage; Rectangular leaves the block unwindowed.");
            toolTip.SetToolTip(
                averagingComboBox,
                "Averaging speed. Fast/Medium/Slow set the exponential time constant; Infinite integrates indefinitely until you reset it.");
            toolTip.SetToolTip(
                checkMainCurve,
                "Shows the main live trace (the spectrum / transfer-function curve).");
            toolTip.SetToolTip(
                checkPeakHold,
                "Overlays a peak-hold envelope that retains the maximum level seen on the curve until reset.");
            toolTip.SetToolTip(
                checkCoherence,
                "Shows the coherence (\u03B3\u00B2) curve on a 0-to-1 axis in Transfer Function mode.");
            toolTip.SetToolTip(
                coherenceLimitComboBox,
                "Frequencies whose coherence falls below this limit are drawn dimmed and dashed to flag where the transfer function is unreliable. Off disables the marking.");
            toolTip.SetToolTip(
                buttonResetAverage,
                "Clears the running average and peak-hold envelope without restarting the measurement.");
            toolTip.SetToolTip(
                checkUseCalibration,
                "Applies the loaded microphone calibration file to Live Spectrum.");
        }
    }
}
