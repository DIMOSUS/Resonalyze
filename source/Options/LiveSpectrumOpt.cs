using System.Drawing;
using Resonalyze.Dsp;
using Resonalyze.Ui;

namespace Resonalyze.Options
{
    public partial class LiveSpectrumOpt : Form
    {
        // Shared with the settings schema, so a newly offered length is not floored out of the saved file.
        private static readonly IReadOnlyList<int> SequenceLengths =
            LiveSequenceLengths.Supported;
        private static readonly int[] OverlapPercents = { 0, 50, 75 };
        private static readonly int[] CoherenceLimits = { 0, 10, 20, 25, 30, 40, 50 };
        private readonly WrappingToolTip toolTip = new();

        // The user's picks, surviving the periodic-pink override (Rectangular, overlap Off).
        private WindowType userWindowType = WindowType.Hann;
        private int userOverlapPercent = 50;

        // Restored when leaving RTA mode, which forces the RTA on and locks it.
        private bool userShowInputMagnitude;

        // Silent is RTA-only (a transfer function needs an excitation); every noise colour is shared.
        private NoiseColor userSignalType = NoiseColor.PinkPeriodic;

        // Real choices for the settings MMM pins, captured only on real toggles, never from the forced state.
        private bool userCompensateNoiseTilt;
        private bool userSplScale;
        private AveragingSpeed userAveragingSpeed = AveragingSpeed.Medium;
        private int userSmoothingInverseOctaves = 6;

        private readonly Color splChoiceReadyForeColor;
        private readonly Color transferChoiceReadyForeColor;

        // Without a loopback the effective mode falls back to RTA and a selected Transfer is coloured amber.
        private bool hasTransferReference = true;

        // dB SPL has no calibration while a live curve exists that view-only would hide: the amber case.
        private bool splViewOnlyConflict;

        /// <summary>Handled live, without Apply, so the Infinite averaging preset can be cleared.</summary>
        public event Action? ResetAverageRequested;

        public LiveSpectrumOpt()
        {
            InitializeComponent();
            splChoiceReadyForeColor = labelSpl.ForeColor;
            transferChoiceReadyForeColor = radioModeTransfer.ForeColor;
            SmoothingPresetOptions.Configure(
                comboSmoothingInverseOctaves, includePsychoacoustic: true);
            buttonResetAverage.Click += (_, _) => ResetAverageRequested?.Invoke();
            signalTypeComboBox.SelectionChangeCommitted += (_, _) =>
            {
                CaptureUserSignalType();
                UpdatePeriodicPinkControls();
                UpdateTiltAvailability();
            };
            windowComboBox.SelectionChangeCommitted += (_, _) => CaptureUserWindow();
            overlapComboBox.SelectionChangeCommitted += (_, _) => CaptureUserOverlap();
            radioModeRta.CheckedChanged += (_, _) => UpdateModeDependentControls();
            radioModeMmm.CheckedChanged += (_, _) => UpdateModeDependentControls();
            checkInputMagnitude.Click += (_, _) => CaptureUserInputMagnitude();
            checkTilt.Click += (_, _) => CaptureUserTilt();
            checkSpl.Click += (_, _) => CaptureUserSplScale();
            averagingComboBox.SelectionChangeCommitted += (_, _) => CaptureUserAveraging();
            comboSmoothingInverseOctaves.SelectionChangeCommitted +=
                (_, _) => CaptureUserSmoothing();
            InitializeToolTips();
            Disposed += (_, _) => toolTip.Dispose();
        }

        internal void Init(
            LiveSpectrumOptions options,
            IReadOnlyList<MicrophoneCalibrationEntry> calibrationEntries,
            bool isSplAvailable,
            bool hasLiveCurve,
            bool hasTransferReference,
            int sampleRateHz)
        {
            signalTypeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            userSignalType = options.NoiseColor;

            sequenceLengthComboBox.Items.Clear();
            foreach (int sequenceLength in SequenceLengths)
            {
                sequenceLengthComboBox.Items.Add(
                    new SequenceLengthOption(sequenceLength, sampleRateHz));
            }

            sequenceLengthComboBox.SelectedIndex =
                FloorIndex(SequenceLengths, options.SequenceLength);

            overlapComboBox.Items.Clear();
            foreach (int overlapPercent in OverlapPercents)
            {
                overlapComboBox.Items.Add(new OverlapOption(overlapPercent));
            }
            userOverlapPercent = options.OverlapPercent;
            overlapComboBox.SelectedIndex = FindOverlapIndex(options.OverlapPercent);

            userSmoothingInverseOctaves =
                SmoothingPresetOptions.Normalize(options.SmoothingInverseOctaves);
            comboSmoothingInverseOctaves.SelectedItem = userSmoothingInverseOctaves;

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

            averagingComboBox.Items.Clear();
            averagingComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            averagingComboBox.Items.Add(new AveragingOption(AveragingSpeed.Fast, "Fast"));
            averagingComboBox.Items.Add(new AveragingOption(AveragingSpeed.Medium, "Medium"));
            averagingComboBox.Items.Add(new AveragingOption(AveragingSpeed.Slow, "Slow"));
            averagingComboBox.Items.Add(
                new AveragingOption(AveragingSpeed.Infinite, "Infinite"));
            userAveragingSpeed = options.AveragingSpeed;
            averagingComboBox.SelectedIndex = FindAveragingIndex(options.AveragingSpeed);

            coherenceLimitComboBox.Items.Clear();
            foreach (int limit in CoherenceLimits)
            {
                coherenceLimitComboBox.Items.Add(new CoherenceLimitOption(limit));
            }
            coherenceLimitComboBox.SelectedIndex =
                FindCoherenceLimitIndex(options.CoherenceThresholdPercent);

            checkMainCurve.Checked = options.ShowMainCurve;
            checkInputMagnitude.Checked = options.ShowInputMagnitude;
            userShowInputMagnitude = options.ShowInputMagnitude;
            checkPeakHold.Checked = options.PeakHold;
            checkCoherence.Checked = options.ShowCoherence;
            checkTilt.Checked = options.CompensateNoiseTilt;
            userCompensateNoiseTilt = options.CompensateNoiseTilt;

            // Selection follows options verbatim (SPL without calibration, Transfer without loopback); amber and tooltips explain.
            RefreshAvailability(isSplAvailable, hasLiveCurve, hasTransferReference);
            checkSpl.Checked =
                options.MagnitudeScale == MagnitudeScale.SoundPressureLevel;
            userSplScale = checkSpl.Checked;
            // Assign all three: WinForms clears siblings, so the single true value wins in any order.
            radioModeMmm.Checked = options.AnalysisMode == LiveAnalysisMode.Mmm;
            radioModeRta.Checked = options.AnalysisMode == LiveAnalysisMode.Rta;
            radioModeTransfer.Checked =
                options.AnalysisMode == LiveAnalysisMode.TransferFunction;
            UpdateModeDependentControls();

            // Placeholder; the shell calls ShowCalibration right after Init.
            ShowCalibration(string.Empty);
        }

        /// <summary>Read-out of what the plot is corrected through, not a selection.</summary>
        /// <remarks>A loaded capture's calibration need not exist here, and a second selection could let MMM and sweeps
        /// disagree about the microphone. Disabled on every call.</remarks>
        internal void ShowCalibration(string text)
        {
            comboCalibration.Items.Clear();
            comboCalibration.Items.Add(text ?? string.Empty);
            comboCalibration.SelectedIndex = 0;
            comboCalibration.Enabled = false;
        }

        /// <summary>Recolours dB SPL and Transfer without changing selections; called when calibration or routing changes.</summary>
        public void RefreshAvailability(
            bool isSplAvailable,
            bool hasLiveCurve,
            bool hasTransferReference)
        {
            // Never locked: the SPL axis still shows SPL overlays. Amber only when a live curve would be hidden.
            splViewOnlyConflict = !isSplAvailable && hasLiveCurve;
            this.hasTransferReference = hasTransferReference;
            UpdateSplChoiceColor();
            UpdateTransferChoiceColor();
            string splDescription = DescribeSplChoice(isSplAvailable, splViewOnlyConflict);
            toolTip.SetToolTip(labelSpl, splDescription);
            toolTip.SetToolTip(checkSpl, splDescription);
        }

        // Precedence: muted (Transfer) → amber → normal. Managed manually: SetTextEnabledLook memorizes the colour it
        // replaces when muting and would restore a stale amber.
        private void UpdateSplChoiceColor()
        {
            // MMM is band-power dB SPL by definition: pinned, not muted. SetTextEnabledLook would undo the pin.
            if (radioModeMmm.Checked)
            {
                labelSpl.ForeColor = splChoiceReadyForeColor;
                return;
            }

            bool rta = radioModeRta.Checked;
            labelSpl.ForeColor = !rta
                ? UiPalette.TextDisabled
                : splViewOnlyConflict
                    ? UiPalette.Warning
                    : splChoiceReadyForeColor;
            UiStyle.SetTextEnabledLook(checkSpl, rta, interactive: true);
        }

        // Amber only for an active override: Transfer selected without a loopback.
        private void UpdateTransferChoiceColor()
        {
            radioModeTransfer.ForeColor =
                radioModeTransfer.Checked && !hasTransferReference
                    ? UiPalette.Warning
                    : transferChoiceReadyForeColor;
            toolTip.SetToolTip(
                radioModeTransfer, DescribeTransferChoice(hasTransferReference));
        }

        private static string DescribeTransferChoice(bool hasTransferReference)
        {
            const string Base =
                "Dual-channel transfer function: the microphone divided by the " +
                "loopback reference, with coherence.";
            if (hasTransferReference)
            {
                return Base;
            }

            return Base + "\r\n" +
                "No loopback reference channel is configured (Measurement Options), " +
                "so the analyzer runs as a reference-free RTA regardless of this " +
                "choice.";
        }

        private static string DescribeSplChoice(bool isSplAvailable, bool viewOnlyConflict)
        {
            const string Base =
                "Shows the RTA in absolute dB SPL (microphone plus the SPL " +
                "calibration offset). RTA mode only: the transfer function is a " +
                "dimensionless ratio with no scalar SPL under noise excitation.";
            if (isSplAvailable)
            {
                return Base;
            }

            if (viewOnlyConflict)
            {
                return Base + "\r\n" +
                    "View-only right now: no SPL calibration is configured for the " +
                    "live input (or it was captured on a different input), so the " +
                    "live curve is hidden — only overlays captured in dB SPL are " +
                    "shown. Configure it in Measurement Options — Calibration; " +
                    "starting the analyzer in this state switches the display back " +
                    "to relative.";
            }

            return Base + "\r\n" +
                "No SPL calibration is configured for the live input (Measurement " +
                "Options — Calibration). Starting the analyzer without one switches " +
                "the display back to relative; overlays captured in dB SPL are " +
                "shown either way.";
        }

        /// <summary>Switches mode without a user click (e.g. opening a stored capture).</summary>
        /// <remarks>Otherwise the stale radio would be written back on the next apply. Checked runs the pinning handler.</remarks>
        public void ForceAnalysisMode(LiveAnalysisMode mode)
        {
            radioModeMmm.Checked = mode == LiveAnalysisMode.Mmm;
            radioModeRta.Checked = mode == LiveAnalysisMode.Rta;
            radioModeTransfer.Checked = mode == LiveAnalysisMode.TransferFunction;
        }

        public void ForceSplScaleOff()
        {
            // Unchecking raises no Click, and SetOptions persists the cache, so reset the cache too.
            userSplScale = false;
            checkSpl.Checked = false;
        }

        public void SetOptions(LiveSpectrumOptions options)
        {
            options.AnalysisMode = radioModeMmm.Checked
                ? LiveAnalysisMode.Mmm
                : radioModeRta.Checked
                    ? LiveAnalysisMode.Rta
                    : LiveAnalysisMode.TransferFunction;
            // MMM offers periodic pink only; persist the user's real choice.
            options.NoiseColor = radioModeMmm.Checked
                ? userSignalType
                : signalTypeComboBox.SelectedItem is NoiseColorOption noiseColorOption
                    ? noiseColorOption.NoiseColor
                    : NoiseColor.PinkPeriodic;
            options.SequenceLength =
                sequenceLengthComboBox.SelectedItem is SequenceLengthOption lengthOption
                    ? lengthOption.Length
                    : SequenceLengths[0];
            // Persist the user's real choices below, not values forced by periodic pink, MMM or RTA mode.
            options.OverlapPercent = userOverlapPercent;
            options.SmoothingInverseOctaves = userSmoothingInverseOctaves;
            options.WindowType = userWindowType;
            options.AveragingSpeed = userAveragingSpeed;
            options.ShowMainCurve = checkMainCurve.Checked;
            options.ShowInputMagnitude = userShowInputMagnitude;
            options.PeakHold = checkPeakHold.Checked;
            options.ShowCoherence = checkCoherence.Checked;
            options.CoherenceThresholdPercent =
                coherenceLimitComboBox.SelectedItem is CoherenceLimitOption limitOption
                    ? limitOption.Percent
                    : CoherenceLimits[0];
            options.CompensateNoiseTilt = userCompensateNoiseTilt;
            options.MagnitudeScale = userSplScale
                ? MagnitudeScale.SoundPressureLevel
                : MagnitudeScale.Relative;
        }

        private static int FindCoherenceLimitIndex(int thresholdPercent) =>
            FloorIndex(CoherenceLimits, thresholdPercent);

        // Largest entry not exceeding target, or 0. Arrays are ascending.
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

        // Shown with its duration: resolution is 2/T (rect) or 4/T (Hann), so 32768 is 341 ms at 96 kHz but 683 ms at 48 kHz.
        private sealed class SequenceLengthOption
        {
            private readonly int sampleRateHz;

            public SequenceLengthOption(int length, int sampleRateHz)
            {
                Length = length;
                this.sampleRateHz = sampleRateHz;
            }

            public int Length { get; }

            public override string ToString() =>
                sampleRateHz > 0
                    ? $"{Length} — {1000.0 * Length / sampleRateHz:0} ms"
                    : $"{Length}";
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

        // Periodic pink is leakage-free with a rectangular window and gains nothing from overlap.
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

        // RTA mode: no transfer/coherence curves (muted); RTA forced on. SPL and tilt are muted in Transfer mode instead.
        private void UpdateModeDependentControls()
        {
            bool mmm = radioModeMmm.Checked;
            // MMM shares the mic-only RTA path.
            bool rta = mmm || radioModeRta.Checked;
            UpdateSignalTypesForMode(rta, mmm);
            UpdateMmmPinnedControls(mmm);

            // Mute rather than disable, for the theme's muted colour instead of system grey.
            UiStyle.SetTextEnabledLook(labelMainCurve, !rta);
            UiStyle.SetTextEnabledLook(checkMainCurve, !rta, interactive: true);
            UiStyle.SetTextEnabledLook(labelInputMagnitude, !rta);
            UiStyle.SetTextEnabledLook(checkInputMagnitude, !rta, interactive: true);
            UiStyle.SetTextEnabledLook(label9, !rta);
            UiStyle.SetTextEnabledLook(checkCoherence, !rta, interactive: true);
            UiStyle.SetTextEnabledLook(label10, !rta);
            coherenceLimitComboBox.Enabled = !rta;

            UpdateSplChoiceColor();
            UpdateTiltAvailability();
            UpdateTransferChoiceColor();

            checkInputMagnitude.Checked = rta || userShowInputMagnitude;
        }

        // Needs a known excitation spectrum: RTA with a real noise only (transfer divides it out; Silent is unknown).
        private void UpdateTiltAvailability()
        {
            if (radioModeMmm.Checked)
            {
                UiStyle.SetTextEnabledLook(labelTilt, true);
                return;
            }

            bool applicable =
                radioModeRta.Checked && SelectedNoiseColor() != NoiseColor.Silent;
            UiStyle.SetTextEnabledLook(labelTilt, applicable);
            UiStyle.SetTextEnabledLook(checkTilt, applicable, interactive: true);
        }

        // MMM settings are forced, not muted (mandatory, not ignored): normal colour, unresponsive, tooltip says why.
        private void UpdateMmmPinnedControls(bool mmm)
        {
            if (mmm)
            {
                checkSpl.Checked = true;
                checkTilt.Checked = true;
                averagingComboBox.SelectedIndex =
                    FindAveragingIndex(AveragingSpeed.Infinite);
                comboSmoothingInverseOctaves.SelectedItem = 0;
            }
            else
            {
                checkSpl.Checked = userSplScale;
                checkTilt.Checked = userCompensateNoiseTilt;
                averagingComboBox.SelectedIndex = FindAveragingIndex(userAveragingSpeed);
                comboSmoothingInverseOctaves.SelectedItem = userSmoothingInverseOctaves;
            }

            SetPinned(checkSpl, mmm);
            SetPinned(checkTilt, mmm);
            averagingComboBox.Enabled = !mmm;
            comboSmoothingInverseOctaves.Enabled = !mmm;
        }

        // AutoCheck:false keeps the state on click without the muted colour.
        private static void SetPinned(CheckBox checkBox, bool pinned)
        {
            checkBox.AutoCheck = !pinned;
            checkBox.TabStop = !pinned;
        }

        // Only real toggles update the preference; the pinned state sets Checked programmatically.
        private void CaptureUserTilt()
        {
            if (checkTilt.AutoCheck)
            {
                userCompensateNoiseTilt = checkTilt.Checked;
            }
        }

        private void CaptureUserSplScale()
        {
            if (checkSpl.AutoCheck)
            {
                userSplScale = checkSpl.Checked;
            }
        }

        private void CaptureUserSmoothing()
        {
            if (comboSmoothingInverseOctaves.SelectedItem is int inverseOctaves)
            {
                userSmoothingInverseOctaves = inverseOctaves;
            }
        }

        private void CaptureUserAveraging()
        {
            if (averagingComboBox.SelectedItem is AveragingOption option)
            {
                userAveragingSpeed = option.Speed;
            }
        }

        private NoiseColor SelectedNoiseColor() =>
            signalTypeComboBox.SelectedItem is NoiseColorOption option
                ? option.NoiseColor
                : NoiseColor.PinkPeriodic;

        // Keep the last signal when the new mode offers it, so a round-trip does not swap the excitation.
        private void UpdateSignalTypesForMode(bool referenceFree, bool mmm)
        {
            signalTypeComboBox.Items.Clear();
            if (mmm)
            {
                // MMM: periodic pink only; its exact 1/√f spectrum, unlike the Kellett bank, does not move with sample rate.
                signalTypeComboBox.Items.Add(
                    new NoiseColorOption(NoiseColor.PinkPeriodic, "Pink noise (periodic)"));
                signalTypeComboBox.SelectedIndex = 0;
                signalTypeComboBox.Enabled = false;
                UpdatePeriodicPinkControls();
                return;
            }

            signalTypeComboBox.Enabled = true;
            if (referenceFree)
            {
                signalTypeComboBox.Items.Add(new NoiseColorOption(NoiseColor.Silent, "Silent"));
            }

            signalTypeComboBox.Items.Add(
                new NoiseColorOption(NoiseColor.PinkPeriodic, "Pink noise (periodic)"));
            signalTypeComboBox.Items.Add(new NoiseColorOption(NoiseColor.Pink, "Pink noise"));
            signalTypeComboBox.Items.Add(new NoiseColorOption(NoiseColor.Brown, "Brown / red noise"));
            signalTypeComboBox.Items.Add(new NoiseColorOption(NoiseColor.White, "White noise"));

            // Only Silent can be missing (leaving RTA): fall back like the controller's normalization.
            int index = TryFindNoiseColorIndex(userSignalType);
            if (index < 0)
            {
                index = FindNoiseColorIndex(NoiseColor.PinkPeriodic);
            }

            signalTypeComboBox.SelectedIndex = index;
            UpdatePeriodicPinkControls();
        }

        private void CaptureUserSignalType()
        {
            if (signalTypeComboBox.SelectedItem is NoiseColorOption option)
            {
                userSignalType = option.NoiseColor;
            }
        }

        // Guard on AutoCheck, not Enabled (which stays true for the muted look).
        private void CaptureUserInputMagnitude()
        {
            if (checkInputMagnitude.AutoCheck)
            {
                userShowInputMagnitude = checkInputMagnitude.Checked;
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


        private int FindNoiseColorIndex(NoiseColor noiseColor)
        {
            int index = TryFindNoiseColorIndex(noiseColor);
            return index >= 0 ? index : 0;
        }

        private int TryFindNoiseColorIndex(NoiseColor noiseColor)
        {
            for (int index = 0; index < signalTypeComboBox.Items.Count; index++)
            {
                if (signalTypeComboBox.Items[index] is NoiseColorOption option &&
                    option.NoiseColor == noiseColor)
                {
                    return index;
                }
            }

            return -1;
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
                radioModeRta,
                "Reference-free RTA: the magnitude spectrum of the microphone input " +
                "alone, no loopback division. The only mode with an absolute dB SPL " +
                "scale and the noise slope compensation.");
            toolTip.SetToolTip(
                radioModeMmm,
                "Moving-microphone capture: the same reference-free analyzer, pinned " +
                "to the one recipe a spatial average is valid under — periodic pink, " +
                "Infinite averaging, banded dB SPL, slope compensation on, smoothing " +
                "off. Walk the microphone through the listening volume while it " +
                "integrates, then Save.\r\n\r\n" +
                "Measure each driver RAW: bypass EQ, crossovers and delays in your " +
                "own DSP and leave only the configured protective high-pass. Virtual " +
                "DSP adds the channel's chain to the capture itself, so a capture " +
                "taken through a chain gets that chain applied twice — and the result " +
                "still looks entirely plausible.");
            // radioModeTransfer's tooltip is owned by UpdateTransferChoiceColor.
            toolTip.SetToolTip(
                signalTypeComboBox,
                "Excitation noise. Pink (periodic): one looped FFT period,\r\n" +
                "leakage-free — recommended. Pink: continuous. Brown: more LF\r\n" +
                "drive. White: equal energy per hertz. Silent: RTA only, no\r\n" +
                "excitation.");
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
                checkInputMagnitude,
                "Overlays a reference-free RTA curve: the plain magnitude spectrum of the microphone input alone, with no division by the loopback reference. Independent of coherence.");
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
            string tiltDescription =
                "Compensates the spectral slope of the excitation noise itself, so a " +
                "flat system reads flat whatever the noise colour (pink otherwise " +
                "falls -3 dB per octave on the per-bin dB axis, and even a flat white " +
                "PSD tilts +3 dB per octave on the banded dB SPL display). Pinned to " +
                "the level at 1 kHz. Unavailable for Silent, whose excitation " +
                "spectrum is unknown.";
            toolTip.SetToolTip(labelTilt, tiltDescription);
            toolTip.SetToolTip(checkTilt, tiltDescription);
            // labelSpl/checkSpl tooltips are owned by RefreshAvailability.
        }
    }
}
