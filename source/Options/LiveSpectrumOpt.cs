using System.Drawing;
using Resonalyze.Dsp;
using Resonalyze.Ui;

namespace Resonalyze.Options
{
    /// <summary>Binds the Live Spectrum settings to a <see cref="LiveSpectrumSettingsSession"/>: every edit goes to the
    /// session, and <see cref="Present"/> writes every control back from it.</summary>
    public partial class LiveSpectrumOpt : Form
    {
        private readonly WrappingToolTip toolTip = new();
        private readonly LiveSpectrumSettingsSession session = new();

        private readonly Color splChoiceReadyForeColor;
        private readonly Color transferChoiceReadyForeColor;

        private bool presenting;

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
            WireFields();
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
            windowComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            averagingComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            // Selection follows options verbatim (SPL without calibration, Transfer without loopback); amber and tooltips explain.
            session.Load(options, isSplAvailable, hasLiveCurve, hasTransferReference, sampleRateHz);
            FillLists(sampleRateHz);
            Present();
        }

        /// <summary>Read-out of what the plot is corrected through, not a selection.</summary>
        /// <remarks>A loaded capture's calibration need not exist here, and a second selection could let MMM and sweeps
        /// disagree about the microphone. Disabled on every call.</remarks>
        internal void ShowCalibration(string text)
        {
            session.SetCalibration(text);
            Present();
        }

        /// <summary>Recolours dB SPL and Transfer without changing selections; called when calibration or routing changes.</summary>
        public void RefreshAvailability(
            bool isSplAvailable,
            bool hasLiveCurve,
            bool hasTransferReference)
        {
            session.SetAvailability(isSplAvailable, hasLiveCurve, hasTransferReference);
            Present();
        }

        /// <summary>Switches mode without a user click (e.g. opening a stored capture).</summary>
        /// <remarks>Otherwise the stale radio would be written back on the next apply.</remarks>
        public void ForceAnalysisMode(LiveAnalysisMode mode)
        {
            session.SelectMode(mode);
            Present();
        }

        public void ForceSplScaleOff()
        {
            session.ForceSplOff();
            Present();
        }

        public void SetOptions(LiveSpectrumOptions options) => session.WriteTo(options);

        private void WireFields()
        {
            radioModeRta.CheckedChanged += (_, _) => Edit(() => session.SelectMode(CheckedMode()));
            radioModeMmm.CheckedChanged += (_, _) => Edit(() => session.SelectMode(CheckedMode()));
            signalTypeComboBox.SelectedIndexChanged += (_, _) =>
            {
                if (signalTypeComboBox.SelectedItem is NoiseColorOption option)
                {
                    Edit(() => session.MoveSignal(option.NoiseColor));
                }
            };
            signalTypeComboBox.SelectionChangeCommitted += (_, _) => Edit(session.CommitSignal);
            sequenceLengthComboBox.SelectedIndexChanged += (_, _) =>
            {
                if (sequenceLengthComboBox.SelectedItem is SequenceLengthOption option)
                {
                    Edit(() => session.MoveSequenceLength(option.Length));
                }
            };
            windowComboBox.SelectedIndexChanged += (_, _) =>
            {
                if (windowComboBox.SelectedItem is WindowOption option)
                {
                    Edit(() => session.MoveWindow(option.WindowType));
                }
            };
            windowComboBox.SelectionChangeCommitted += (_, _) => Edit(session.CommitWindow);
            overlapComboBox.SelectedIndexChanged += (_, _) =>
            {
                if (overlapComboBox.SelectedItem is OverlapOption option)
                {
                    Edit(() => session.MoveOverlap(option.Percent));
                }
            };
            overlapComboBox.SelectionChangeCommitted += (_, _) => Edit(session.CommitOverlap);
            comboSmoothingInverseOctaves.SelectedIndexChanged += (_, _) =>
            {
                if (comboSmoothingInverseOctaves.SelectedItem is int inverseOctaves)
                {
                    Edit(() => session.MoveSmoothing(inverseOctaves));
                }
            };
            comboSmoothingInverseOctaves.SelectionChangeCommitted += (_, _) => Edit(session.CommitSmoothing);
            averagingComboBox.SelectedIndexChanged += (_, _) =>
            {
                if (averagingComboBox.SelectedItem is AveragingOption option)
                {
                    Edit(() => session.MoveAveraging(option.Speed));
                }
            };
            averagingComboBox.SelectionChangeCommitted += (_, _) => Edit(session.CommitAveraging);
            coherenceLimitComboBox.SelectedIndexChanged += (_, _) =>
            {
                if (coherenceLimitComboBox.SelectedItem is CoherenceLimitOption option)
                {
                    Edit(() => session.MoveCoherenceLimit(option.Percent));
                }
            };
            checkMainCurve.CheckedChanged += (_, _) => Edit(() => session.SetMainCurve(checkMainCurve.Checked));
            checkPeakHold.CheckedChanged += (_, _) => Edit(() => session.SetPeakHold(checkPeakHold.Checked));
            checkCoherence.CheckedChanged += (_, _) => Edit(() => session.SetCoherence(checkCoherence.Checked));
            checkInputMagnitude.CheckedChanged +=
                (_, _) => Edit(() => session.SetInputMagnitude(checkInputMagnitude.Checked));
            checkInputMagnitude.Click += (_, _) => Edit(session.ClickInputMagnitude);
            checkTilt.CheckedChanged += (_, _) => Edit(() => session.SetTilt(checkTilt.Checked));
            checkTilt.Click += (_, _) => Edit(session.ClickTilt);
            checkSpl.CheckedChanged += (_, _) => Edit(() => session.SetSpl(checkSpl.Checked));
            checkSpl.Click += (_, _) => Edit(session.ClickSpl);
        }

        // A radio clears its siblings before it raises CheckedChanged, so the handler reads the final pick.
        private LiveAnalysisMode CheckedMode() =>
            radioModeMmm.Checked
                ? LiveAnalysisMode.Mmm
                : radioModeRta.Checked
                    ? LiveAnalysisMode.Rta
                    : LiveAnalysisMode.TransferFunction;

        private void Edit(Action edit)
        {
            if (presenting)
            {
                return;
            }

            edit();
            Present();
        }

        private void FillLists(int sampleRateHz)
        {
            sequenceLengthComboBox.Items.Clear();
            foreach (int sequenceLength in LiveSpectrumSettingsChoices.SequenceLengths)
            {
                sequenceLengthComboBox.Items.Add(new SequenceLengthOption(sequenceLength, sampleRateHz));
            }

            overlapComboBox.Items.Clear();
            foreach (int overlapPercent in LiveSpectrumSettingsChoices.OverlapPercents)
            {
                overlapComboBox.Items.Add(new OverlapOption(overlapPercent));
            }

            windowComboBox.Items.Clear();
            foreach ((WindowType window, string label) in LiveSpectrumSettingsChoices.Windows)
            {
                windowComboBox.Items.Add(new WindowOption(window, label));
            }

            averagingComboBox.Items.Clear();
            foreach ((AveragingSpeed speed, string label) in LiveSpectrumSettingsChoices.Averagings)
            {
                averagingComboBox.Items.Add(new AveragingOption(speed, label));
            }

            coherenceLimitComboBox.Items.Clear();
            foreach (int limit in LiveSpectrumSettingsChoices.CoherenceLimits)
            {
                coherenceLimitComboBox.Items.Add(new CoherenceLimitOption(limit));
            }
        }

        // Writes every control from the session; the controls' own change events are ignored meanwhile.
        private void Present()
        {
            presenting = true;
            try
            {
                // Assign all three: WinForms clears siblings, so the single true value wins in any order.
                radioModeMmm.Checked = session.Mode == LiveAnalysisMode.Mmm;
                radioModeRta.Checked = session.Mode == LiveAnalysisMode.Rta;
                radioModeTransfer.Checked = session.Mode == LiveAnalysisMode.TransferFunction;
                PresentSignals();
                Select(sequenceLengthComboBox, item => item is SequenceLengthOption option && option.Length == session.SequenceLength);
                Select(windowComboBox, item => item is WindowOption option && option.WindowType == session.Window);
                windowComboBox.Enabled = session.WindowEditable;
                Select(overlapComboBox, item => item is OverlapOption option && option.Percent == session.OverlapPercent);
                overlapComboBox.Enabled = session.OverlapEditable;
                if (!Equals(comboSmoothingInverseOctaves.SelectedItem, session.SmoothingInverseOctaves))
                {
                    comboSmoothingInverseOctaves.SelectedItem = session.SmoothingInverseOctaves;
                }

                comboSmoothingInverseOctaves.Enabled = session.SmoothingEditable;
                Select(averagingComboBox, item => item is AveragingOption option && option.Speed == session.Averaging);
                averagingComboBox.Enabled = session.AveragingEditable;
                Select(coherenceLimitComboBox, item => item is CoherenceLimitOption option && option.Percent == session.CoherenceLimitPercent);
                coherenceLimitComboBox.Enabled = session.CoherenceLimitEditable;
                checkMainCurve.Checked = session.MainCurve;
                checkInputMagnitude.Checked = session.InputMagnitude;
                checkPeakHold.Checked = session.PeakHold;
                checkCoherence.Checked = session.Coherence;
                checkTilt.Checked = session.Tilt;
                checkSpl.Checked = session.Spl;
                PresentLooks();
                PresentCalibration();
            }
            finally
            {
                presenting = false;
            }
        }

        private void PresentSignals()
        {
            if (!signalTypeComboBox.Items.Cast<object>().Select(item => ((NoiseColorOption)item).NoiseColor)
                    .SequenceEqual(session.Signals))
            {
                signalTypeComboBox.Items.Clear();
                foreach (NoiseColor signal in session.Signals)
                {
                    signalTypeComboBox.Items.Add(
                        new NoiseColorOption(signal, LiveSpectrumSettingsChoices.SignalLabel(signal)));
                }
            }

            Select(signalTypeComboBox, item => item is NoiseColorOption option && option.NoiseColor == session.Signal);
            signalTypeComboBox.Enabled = session.SignalEditable;
        }

        private static void Select(ThemedComboBox combo, Func<object, bool> matches)
        {
            for (int index = 0; index < combo.Items.Count; index++)
            {
                if (matches(combo.Items[index]!))
                {
                    if (combo.SelectedIndex != index)
                    {
                        combo.SelectedIndex = index;
                    }

                    return;
                }
            }
        }

        // Mute rather than disable, for the theme's muted colour instead of system grey.
        private void PresentLooks()
        {
            bool curvesMuted = LiveSpectrumSettingsLook.CurvesMuted(session);
            UiStyle.SetTextEnabledLook(labelMainCurve, !curvesMuted);
            PresentBox(checkMainCurve, curvesMuted, !curvesMuted);
            UiStyle.SetTextEnabledLook(labelInputMagnitude, !curvesMuted);
            PresentBox(checkInputMagnitude, curvesMuted, session.InputMagnitudeInteractive);
            UiStyle.SetTextEnabledLook(label9, !curvesMuted);
            PresentBox(checkCoherence, curvesMuted, !curvesMuted);
            UiStyle.SetTextEnabledLook(label10, !curvesMuted);

            string splDescription = LiveSpectrumSettingsToolTips.Spl(session);
            toolTip.SetToolTip(labelSpl, splDescription);
            toolTip.SetToolTip(checkSpl, splDescription);
            // Set directly: SetTextEnabledLook memorizes the colour it replaces when muting and would restore a stale amber.
            LiveSettingTone spl = LiveSpectrumSettingsLook.Spl(session);
            labelSpl.ForeColor = ColorOf(spl, splChoiceReadyForeColor);
            PresentBox(checkSpl, spl == LiveSettingTone.Muted, session.SplInteractive);

            LiveSettingTone tilt = LiveSpectrumSettingsLook.Tilt(session);
            UiStyle.SetTextEnabledLook(labelTilt, tilt != LiveSettingTone.Muted);
            PresentBox(checkTilt, tilt == LiveSettingTone.Muted, session.TiltInteractive);

            radioModeTransfer.ForeColor = ColorOf(LiveSpectrumSettingsLook.Transfer(session), transferChoiceReadyForeColor);
            toolTip.SetToolTip(radioModeTransfer, LiveSpectrumSettingsToolTips.Transfer(session));
        }

        // A box that takes no click keeps its state; muted or not is only its colour.
        private static void PresentBox(CheckBox box, bool muted, bool interactive)
        {
            UiStyle.SetTextEnabledLook(box, !muted);
            box.AutoCheck = interactive;
            box.TabStop = interactive;
        }

        private static Color ColorOf(LiveSettingTone tone, Color normal) => tone switch
        {
            LiveSettingTone.Warning => UiPalette.Warning,
            LiveSettingTone.Muted => UiPalette.TextDisabled,
            _ => normal
        };

        private void PresentCalibration()
        {
            if (comboCalibration.Items.Count != 1 || !Equals(comboCalibration.Items[0], session.Calibration))
            {
                comboCalibration.Items.Clear();
                comboCalibration.Items.Add(session.Calibration);
                comboCalibration.SelectedIndex = 0;
            }

            comboCalibration.Enabled = false;
        }

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
                LiveSpectrumSettingsChoices.SequenceLengthLabel(Length, sampleRateHz);
        }

        private sealed class CoherenceLimitOption
        {
            public CoherenceLimitOption(int percent)
            {
                Percent = percent;
            }

            public int Percent { get; }

            public override string ToString() => LiveSpectrumSettingsChoices.PercentLabel(Percent);
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

        private sealed class OverlapOption
        {
            public OverlapOption(int percent)
            {
                Percent = percent;
            }

            public int Percent { get; }

            public override string ToString() => LiveSpectrumSettingsChoices.PercentLabel(Percent);
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
            // radioModeTransfer, labelSpl and checkSpl take their tooltips in PresentLooks.
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
                "Analysis window applied before the FFT. Hann is a good\r\n" +
                "default; Flat Top for amplitude accuracy; Blackman-Harris\r\n" +
                "against leakage; Rectangular leaves the block unwindowed.\r\n" +
                "Forced to Rectangular for periodic pink noise.");
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
        }
    }
}
