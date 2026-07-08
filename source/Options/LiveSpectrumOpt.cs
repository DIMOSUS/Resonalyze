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

        // The user's chosen analysis window and overlap, tracked independently of the
        // combos so they survive the periodic-pink override that forces the window to
        // Rectangular and the overlap to Off.
        private WindowType userWindowType = WindowType.Hann;
        private int userOverlapPercent = 50;

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
            signalTypeComboBox.SelectionChangeCommitted += (_, _) => UpdatePeriodicPinkControls();
            windowComboBox.SelectionChangeCommitted += (_, _) => CaptureUserWindow();
            overlapComboBox.SelectionChangeCommitted += (_, _) => CaptureUserOverlap();
            InitializeToolTips();
            Disposed += (_, _) => toolTip.Dispose();
        }

        public void Init(
            LiveSpectrumOptions options,
            bool hasZeroDegreeCalibration,
            bool hasNinetyDegreeCalibration)
        {
            signalTypeComboBox.Items.Clear();
            signalTypeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            signalTypeComboBox.Items.Add(
                new NoiseColorOption(NoiseColor.PinkPeriodic, "Pink noise (periodic)"));
            signalTypeComboBox.Items.Add(new NoiseColorOption(NoiseColor.Pink, "Pink noise"));
            signalTypeComboBox.Items.Add(new NoiseColorOption(NoiseColor.Brown, "Brown / red noise"));
            signalTypeComboBox.Items.Add(new NoiseColorOption(NoiseColor.White, "White noise"));
            signalTypeComboBox.SelectedIndex = FindNoiseColorIndex(options.NoiseColor);

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
            userOverlapPercent = options.OverlapPercent;
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
            userWindowType = options.WindowType;
            windowComboBox.SelectedIndex = FindWindowIndex(options.WindowType);
            UpdatePeriodicPinkControls();

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
            MicrophoneCalibrationComboHelper.Configure(
                comboCalibration,
                options.CalibrationMode,
                hasZeroDegreeCalibration,
                hasNinetyDegreeCalibration);
        }

        public void SetOptions(LiveSpectrumOptions options)
        {
            options.NoiseColor =
                signalTypeComboBox.SelectedItem is NoiseColorOption noiseColorOption
                    ? noiseColorOption.NoiseColor
                    : NoiseColor.PinkPeriodic;
            options.CalibrationMode =
                MicrophoneCalibrationComboHelper.GetSelectedMode(comboCalibration);
            options.SequenceLength = sequenceLengthComboBox.SelectedItem is int sequenceLength
                ? sequenceLength
                : SequenceLengths[0];
            // Persist the user's real overlap choice, not the Off value the combo is
            // forced to (and disabled at) while periodic pink noise is selected.
            options.OverlapPercent = userOverlapPercent;
            options.SmoothingInverseOctaves =
                comboSmoothingInverseOctaves.SelectedItem is int inverseOctaves
                    ? inverseOctaves
                    : SmoothingPresetOptions.SupportedInverseOctaves[0];
            // Persist the user's real window choice, not the Rectangular value the combo
            // is forced to (and disabled at) while periodic pink noise is selected.
            options.WindowType = userWindowType;
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

        private static int FindCoherenceLimitIndex(int thresholdPercent) =>
            FloorIndex(CoherenceLimits, thresholdPercent);

        // Index of the largest entry that does not exceed target, or 0 when target
        // sits below the whole array. Shared by the coherence-limit, overlap and
        // sequence-length combos, whose option arrays are all ascending.
        private static int FloorIndex(IReadOnlyList<int> ascending, int target)
        {
            int index = 0;
            for (int i = 0; i < ascending.Count; i++)
            {
                if (target >= ascending[i])
                {
                    index = i;
                }
            }

            return index;
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

        // Periodic pink noise is measured leakage-free with a rectangular window and
        // gains nothing from overlap, so both controls are forced (Rectangular / Off)
        // and disabled while it is selected. Any other signal restores the user's picks.
        private void UpdatePeriodicPinkControls()
        {
            bool periodicPink =
                signalTypeComboBox.SelectedItem is NoiseColorOption option &&
                option.NoiseColor == NoiseColor.PinkPeriodic;

            if (periodicPink)
            {
                windowComboBox.SelectedIndex = FindWindowIndex(WindowType.Rectangular);
                windowComboBox.Enabled = false;
                overlapComboBox.SelectedIndex = FindOverlapIndex(0);
                overlapComboBox.Enabled = false;
            }
            else
            {
                windowComboBox.Enabled = true;
                windowComboBox.SelectedIndex = FindWindowIndex(userWindowType);
                overlapComboBox.Enabled = true;
                overlapComboBox.SelectedIndex = FindOverlapIndex(userOverlapPercent);
            }
        }

        private void CaptureUserWindow()
        {
            if (windowComboBox.SelectedItem is WindowOption option)
            {
                userWindowType = option.WindowType;
            }
        }

        private void CaptureUserOverlap()
        {
            if (overlapComboBox.SelectedItem is OverlapOption option)
            {
                userOverlapPercent = option.Percent;
            }
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

        private static int FindOverlapIndex(int overlapPercent) =>
            FloorIndex(OverlapPercents, overlapPercent);

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

        private static int NormalizeSequenceLength(int sequenceLength) =>
            SequenceLengths[FloorIndex(SequenceLengths, sequenceLength)];

        private int FindNoiseColorIndex(NoiseColor noiseColor)
        {
            for (int index = 0; index < signalTypeComboBox.Items.Count; index++)
            {
                if (signalTypeComboBox.Items[index] is NoiseColorOption option &&
                    option.NoiseColor == noiseColor)
                {
                    return index;
                }
            }

            return 0;
        }

        private sealed class NoiseColorOption
        {
            public NoiseColorOption(NoiseColor noiseColor, string displayName)
            {
                NoiseColor = noiseColor;
                DisplayName = displayName;
            }

            public NoiseColor NoiseColor { get; }

            public string DisplayName { get; }

            public override string ToString() => DisplayName;
        }

        private void InitializeToolTips()
        {
            toolTip.SetToolTip(
                signalTypeComboBox,
                "Excitation noise played during the measurement.\r\n" +
                "• Pink noise (periodic): one FFT-length period of exactly pink noise, looped. Deterministic and leakage-free, so the transfer function converges fastest. Recommended.\r\n" +
                "• Pink noise: continuous random pink noise, -3 dB/octave.\r\n" +
                "• Brown / red noise: -6 dB/octave, more low-frequency drive for subwoofer and room-mode work.\r\n" +
                "• White noise: equal energy per hertz.");
            toolTip.SetToolTip(
                sequenceLengthComboBox,
                "Sets the FFT block size. Longer sequences give finer frequency resolution but slower visual updates.");
            toolTip.SetToolTip(
                overlapComboBox,
                "Overlaps successive analysis frames by sliding the FFT window a fraction of its size. Higher overlap gives faster, smoother averaging at the cost of more CPU.\r\nForced to Off for periodic pink noise, where overlapped frames are correlated and add no averaging.");
            toolTip.SetToolTip(
                comboSmoothingInverseOctaves,
                "Applies fractional-octave smoothing to the displayed Live Spectrum curve.");
            toolTip.SetToolTip(
                windowComboBox,
                "Analysis window applied before the FFT. Hann is a good default; Flat Top maximizes amplitude accuracy; Blackman-Harris suppresses spectral leakage; Rectangular leaves the block unwindowed.\r\nForced to Rectangular for periodic pink noise, which is already leakage-free.");
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
                comboCalibration,
                "Applies the selected microphone calibration file to Live Spectrum.");
        }
    }
}
