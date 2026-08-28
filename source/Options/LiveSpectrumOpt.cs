using System.Drawing;
using Resonalyze.Dsp;
using Resonalyze.Ui;

namespace Resonalyze.Options
{
    public partial class LiveSpectrumOpt : Form
    {
        // Shared with the settings schema, which used to keep its own copy and would
        // floor a newly offered length straight back out of the saved file.
        private static readonly IReadOnlyList<int> SequenceLengths =
            LiveSequenceLengths.Supported;
        private static readonly int[] OverlapPercents = { 0, 50, 75 };
        private static readonly int[] CoherenceLimits = { 0, 10, 20, 25, 30, 40, 50 };
        private readonly WrappingToolTip toolTip = new();

        // The user's chosen analysis window and overlap, tracked independently of the
        // combos so they survive the periodic-pink override that forces the window to
        // Rectangular and the overlap to Off.
        private WindowType userWindowType = WindowType.Hann;
        private int userOverlapPercent = 50;

        // The user's RTA (input magnitude) choice, tracked independently so RTA mode
        // — which forces the RTA on and locks its checkbox, it being the only curve
        // there — can restore the real preference when the panel returns to Transfer
        // mode.
        private bool userShowInputMagnitude;

        // The user's last chosen signal, remembered so a mode switch keeps it when
        // the new mode still offers it, and restores it on the way back. Silent (an
        // ambient RTA with no excitation) is the one mode-exclusive signal — a
        // transfer function has nothing to correlate against without an excitation —
        // so it exists in RTA mode only; every real noise colour is shared.
        private NoiseColor userSignalType = NoiseColor.PinkPeriodic;

        // The user's real choices for the three settings MMM pins. MMM forces the
        // controls themselves (see UpdateMmmPinnedControls), so without these a trip
        // through MMM and back would silently rewrite the RTA preferences — the same
        // contract userWindowType and userOverlapPercent already have for periodic
        // pink. Captured on a real toggle only, never from the forced state.
        private bool userCompensateNoiseTilt;
        private bool userSplScale;
        private AveragingSpeed userAveragingSpeed = AveragingSpeed.Medium;
        private int userSmoothingInverseOctaves = 6;

        // The designer's normal text colours, restored when a choice leaves its
        // amber conflict state.
        private readonly Color splChoiceReadyForeColor;
        private readonly Color transferChoiceReadyForeColor;

        // Whether the configured input carries a loopback reference — the
        // prerequisite of Transfer mode. Without one the effective mode falls back
        // to RTA, and a SELECTED Transfer choice is coloured amber to say so. Kept
        // as a field so mode clicks can recolour between availability refreshes.
        private bool hasTransferReference = true;

        // Whether dB SPL currently has no matching calibration while a live curve
        // exists that the view-only state would hide — the one situation the SPL
        // choice is coloured amber for. Kept as a field for the same reason.
        private bool splViewOnlyConflict;

        /// <summary>
        /// Raised when the user clicks Reset Average. Handled live (without an
        /// Apply / restart) so the Infinite averaging preset can be cleared.
        /// </summary>
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
            // The signal list is populated per mode in UpdateModeDependentControls
            // below; remember the stored signal so it survives a mode round-trip.
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

            // The selection follows the options verbatim: dB SPL is choosable even
            // without a matching calibration (view-only), and Transfer even without
            // a loopback (effective mode falls back to RTA) — neither is silently
            // rewritten here; amber and the tooltips do the explaining.
            RefreshAvailability(isSplAvailable, hasLiveCurve, hasTransferReference);
            checkSpl.Checked =
                options.MagnitudeScale == MagnitudeScale.SoundPressureLevel;
            userSplScale = checkSpl.Checked;
            // Assign all three: WinForms clears the siblings when one is set, so the
            // single true value wins whichever order the assignments run in.
            radioModeMmm.Checked = options.AnalysisMode == LiveAnalysisMode.Mmm;
            radioModeRta.Checked = options.AnalysisMode == LiveAnalysisMode.Rta;
            radioModeTransfer.Checked =
                options.AnalysisMode == LiveAnalysisMode.TransferFunction;
            UpdateModeDependentControls();

            ShowRigCalibration(options.CalibrationId, calibrationEntries);
        }

        /// <summary>
        /// Rebuilds the calibration list without disturbing the selection — the
        /// host calls this when the configured calibrations change while the
        /// panel is open.
        /// </summary>
        internal void RefreshCalibrationEntries(
            string? calibrationId,
            IReadOnlyList<MicrophoneCalibrationEntry> calibrationEntries) =>
            ShowRigCalibration(calibrationId, calibrationEntries);

        /// <summary>
        /// Shows the rig's calibration without offering it as a choice.
        /// </summary>
        /// <remarks>
        /// A read-out: a live capture is taken on the rig, through the capsule Record
        /// Settings names, and a second selection here would let an MMM pass and the
        /// sweeps beside it disagree about which microphone was used — a difference
        /// nothing downstream could see. It has to be disabled after EVERY rebuild of
        /// the list, because <see cref="MicrophoneCalibrationComboHelper.Configure"/>
        /// enables the box whenever it holds more than one entry: leaving it to the
        /// panel's construction alone made the field editable again the moment a
        /// calibration was added or removed, and a selection made in it would have
        /// changed nothing and then snapped back.
        /// </remarks>
        private void ShowRigCalibration(
            string? calibrationId,
            IReadOnlyList<MicrophoneCalibrationEntry> calibrationEntries)
        {
            MicrophoneCalibrationComboHelper.Configure(
                comboCalibration,
                calibrationId,
                calibrationEntries);
            comboCalibration.Enabled = false;
        }

        /// <summary>
        /// Recolours the dB SPL and Transfer choices, in both directions, without
        /// disturbing the selections: dB SPL stays selectable and is merely view-only
        /// (overlays, no live curves) until a matching SPL calibration exists for the
        /// live input, and Transfer stays selectable while a missing loopback merely
        /// forces the effective mode to RTA. The host calls this when the configured
        /// calibration or the audio routing changes while the panel is open.
        /// </summary>
        public void RefreshAvailability(
            bool isSplAvailable,
            bool hasLiveCurve,
            bool hasTransferReference)
        {
            // The SPL choice is never locked: without a calibration the dB SPL axis
            // is still useful for VIEWING overlays captured in SPL, so it stays
            // clickable. Amber flags a REAL conflict only — a live curve exists that
            // the view-only state would hide. On a freshly started application there
            // is nothing to hide and nothing to warn about, so the choice keeps its
            // normal colour and the tooltip does the explaining.
            splViewOnlyConflict = !isSplAvailable && hasLiveCurve;
            this.hasTransferReference = hasTransferReference;
            UpdateSplChoiceColor();
            UpdateTransferChoiceColor();
            string splDescription = DescribeSplChoice(isSplAvailable, splViewOnlyConflict);
            toolTip.SetToolTip(labelSpl, splDescription);
            toolTip.SetToolTip(checkSpl, splDescription);
        }

        // Colour precedence for the dB SPL row: muted (not applicable in Transfer
        // mode) → amber (a real view-only conflict) → normal. The label's colour is
        // managed manually rather than through SetTextEnabledLook, which memorizes
        // whatever colour it mutes and would hand a stale amber back on restore; the
        // textless checkbox only needs its AutoCheck toggled.
        private void UpdateSplChoiceColor()
        {
            // MMM renders band-power dB SPL by definition and needs no anchor, so the
            // row is neither muted nor amber there — it is pinned, and
            // UpdateMmmPinnedControls owns its interactivity. Calling
            // SetTextEnabledLook here would hand AutoCheck back and undo that pin.
            if (radioModeMmm.Checked)
            {
                labelSpl.ForeColor = splChoiceReadyForeColor;
                return;
            }

            bool rta = radioModeRta.Checked;
            labelSpl.ForeColor = !rta
                ? UiPalette.TextDisabled
                : splViewOnlyConflict
                    ? UiPalette.WarningAmber
                    : splChoiceReadyForeColor;
            UiStyle.SetTextEnabledLook(checkSpl, rta, interactive: true);
        }

        // Amber flags a real, ACTIVE override only: Transfer is selected but the
        // input has no loopback reference, so the analyzer actually runs as an RTA.
        // An unselected Transfer choice keeps its normal colour; the tooltip warns.
        private void UpdateTransferChoiceColor()
        {
            radioModeTransfer.ForeColor =
                radioModeTransfer.Checked && !hasTransferReference
                    ? UiPalette.WarningAmber
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

        /// <summary>
        /// Unchecks the dB SPL scale. The host calls this when the analyzer starts
        /// (or loses its calibration mid-run) while the display is view-only SPL;
        /// checking it again afterwards stays available.
        /// </summary>
        /// <summary>
        /// Moves the panel onto <paramref name="mode"/> without a user click. The
        /// host calls this when something outside the panel changes the analysis mode
        /// — opening a stored capture switches to the mode that capture belongs to.
        /// </summary>
        /// <remarks>
        /// An open panel that kept showing the old radio would write it straight back
        /// on its next apply-on-change, since SetOptions reads the controls. Setting
        /// Checked runs the CheckedChanged handler, so the pinned settings follow.
        /// </remarks>
        public void ForceAnalysisMode(LiveAnalysisMode mode)
        {
            radioModeMmm.Checked = mode == LiveAnalysisMode.Mmm;
            radioModeRta.Checked = mode == LiveAnalysisMode.Rta;
            radioModeTransfer.Checked = mode == LiveAnalysisMode.TransferFunction;
        }

        public void ForceSplScaleOff()
        {
            // The remembered choice has to go with the checkbox. Unchecking alone
            // raises no Click, so CaptureUserSplScale never runs, and SetOptions —
            // which persists the CACHE, not the control — would write dB SPL straight
            // back on the panel's next apply, undoing the very reset the caller made.
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
            // MMM offers periodic pink alone, so the combo carries no choice there:
            // persist the user's real one instead. NoiseMeasurement pins the played
            // colour through EffectiveNoiseColor either way.
            options.NoiseColor = radioModeMmm.Checked
                ? userSignalType
                : signalTypeComboBox.SelectedItem is NoiseColorOption noiseColorOption
                    ? noiseColorOption.NoiseColor
                    : NoiseColor.PinkPeriodic;
            options.SequenceLength =
                sequenceLengthComboBox.SelectedItem is SequenceLengthOption lengthOption
                    ? lengthOption.Length
                    : SequenceLengths[0];
            // Persist the user's real overlap choice, not the Off value the combo is
            // forced to (and disabled at) while periodic pink noise is selected.
            options.OverlapPercent = userOverlapPercent;
            // The user's real choice, not the Off value MMM forces the combo to.
            options.SmoothingInverseOctaves = userSmoothingInverseOctaves;
            // Persist the user's real window choice, not the Rectangular value the combo
            // is forced to (and disabled at) while periodic pink noise is selected.
            options.WindowType = userWindowType;
            // Likewise the user's real averaging choice, not the Infinite value MMM
            // forces the combo to.
            options.AveragingSpeed = userAveragingSpeed;
            options.ShowMainCurve = checkMainCurve.Checked;
            // Persist the user's real RTA choice, not the value forced (and locked) on
            // while RTA mode is selected.
            options.ShowInputMagnitude = userShowInputMagnitude;
            options.PeakHold = checkPeakHold.Checked;
            options.ShowCoherence = checkCoherence.Checked;
            options.CoherenceThresholdPercent =
                coherenceLimitComboBox.SelectedItem is CoherenceLimitOption limitOption
                    ? limitOption.Percent
                    : CoherenceLimits[0];
            // The SPL and tilt checkboxes are muted (never rewritten) in Transfer
            // mode and FORCED ON in MMM, so these persist the user's real RTA-mode
            // choices; the effective scale and tilt ignore them in both.
            options.CompensateNoiseTilt = userCompensateNoiseTilt;
            options.MagnitudeScale = userSplScale
                ? MagnitudeScale.SoundPressureLevel
                : MagnitudeScale.Relative;
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

        // The analysis frame, shown by its DURATION as well as its sample count.
        // Duration is the rate-independent quantity: a rectangular window resolves
        // 2/T hertz and a Hann one 4/T, so 32768 means 341 ms at 96 kHz and 683 ms
        // at 48 kHz — the same list entry, two very different measurements. Naming
        // the milliseconds is what stops a rate change from silently coarsening the
        // bass of a spatial average. The count alone is shown until the analyzer
        // reports a rate.
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

        // In RTA mode the plot is the reference-free microphone spectrum. The
        // transfer function and coherence do not exist there, so their curve
        // controls are muted; the RTA is the one shown curve, forced on and locked,
        // its Transfer-mode preference kept in userShowInputMagnitude and restored
        // on the way back. The dB SPL scale and the noise-slope compensation are
        // properties of the RTA and are muted in Transfer mode instead.
        private void UpdateModeDependentControls()
        {
            bool mmm = radioModeMmm.Checked;
            // MMM is a reference-free mode too: it shares the whole mic-only path
            // with the RTA and differs only in which settings it allows.
            bool rta = mmm || radioModeRta.Checked;
            UpdateSignalTypesForMode(rta, mmm);
            UpdateMmmPinnedControls(mmm);

            // Mute (rather than WinForms-disable) the transfer/coherence controls so
            // they read as the theme's muted colour, not the near-black system grey.
            UiStyle.SetTextEnabledLook(labelMainCurve, !rta);
            UiStyle.SetTextEnabledLook(checkMainCurve, !rta, interactive: true);
            UiStyle.SetTextEnabledLook(labelInputMagnitude, !rta);
            UiStyle.SetTextEnabledLook(checkInputMagnitude, !rta, interactive: true);
            UiStyle.SetTextEnabledLook(label9, !rta);
            UiStyle.SetTextEnabledLook(checkCoherence, !rta, interactive: true);
            UiStyle.SetTextEnabledLook(label10, !rta);
            // The coherence-limit combo is a DarkComboBox, which mutes itself on Enabled.
            coherenceLimitComboBox.Enabled = !rta;

            UpdateSplChoiceColor();
            UpdateTiltAvailability();
            UpdateTransferChoiceColor();

            checkInputMagnitude.Checked = rta || userShowInputMagnitude;
        }

        // The compensation needs a KNOWN excitation spectrum, so it is offered in
        // RTA mode with a real noise signal only: the transfer function divides the
        // excitation out, and Silent means an external source of unknown colour.
        private void UpdateTiltAvailability()
        {
            // In MMM the compensation is not offered but REQUIRED, so the row keeps
            // its normal colour and is pinned instead of muted (UpdateMmmPinnedControls).
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

        // MMM pins the settings a spatial average is only valid under. They are
        // FORCED, not muted: a muted control reads "not applicable / ignored", and
        // these are the opposite — mandatory. So the rows keep their normal colour
        // and only stop responding, with the tooltip saying why. The user's own
        // choices live in the userXxx fields and come back on the way out.
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

        // Stops a checkbox responding without giving it the muted "not applicable"
        // colour: AutoCheck:false makes a click leave the state untouched, exactly
        // as UiStyle.SetTextEnabledLook does for the muted case.
        private static void SetPinned(CheckBox checkBox, bool pinned)
        {
            checkBox.AutoCheck = !pinned;
            checkBox.TabStop = !pinned;
        }

        // Only a real user toggle updates the remembered preference; the pinned
        // state above sets Checked programmatically and must not pollute it. The
        // AutoCheck guard is the same one CaptureUserInputMagnitude uses.
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

        // The signal list follows the analysis mode. Silent (an ambient RTA with no
        // excitation) is RTA-only — a transfer function has nothing to correlate
        // against without an excitation — while every real noise colour, periodic
        // pink included, is valid in both modes (in RTA it is simply a known
        // excitation, and the one whose spectrum the slope compensation knows
        // exactly). The last signal is kept when the new mode still has it, so a
        // mode round-trip does not silently swap the excitation.
        private void UpdateSignalTypesForMode(bool referenceFree, bool mmm)
        {
            signalTypeComboBox.Items.Clear();
            if (mmm)
            {
                // MMM offers periodic pink alone. Its spectrum is exactly 1/√f and,
                // unlike the Kellett bank behind plain "Pink noise" (whose poles sit
                // in normalized frequency), the model the slope compensation undoes
                // does not move with the sample rate. Silent has no known spectrum
                // at all, so it cannot be compensated even in principle.
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

            // Keep the remembered signal if this mode offers it. Only Silent can be
            // missing (leaving RTA for Transfer): fall back to the transfer
            // reference, matching the controller's normalization.
            int index = TryFindNoiseColorIndex(userSignalType);
            if (index < 0)
            {
                index = FindNoiseColorIndex(NoiseColor.PinkPeriodic);
            }

            signalTypeComboBox.SelectedIndex = index;
            UpdatePeriodicPinkControls();
        }

        // Only a real user commit updates the remembered signal — never the programmatic
        // re-selection above, which would pollute it with an auto-picked default.
        private void CaptureUserSignalType()
        {
            if (signalTypeComboBox.SelectedItem is NoiseColorOption option)
            {
                userSignalType = option.NoiseColor;
            }
        }

        // Only a real user toggle updates the remembered RTA preference. In RTA mode
        // the checkbox is muted (AutoCheck off), so a click cannot change it — guard on
        // that rather than Enabled, which stays true for the muted look.
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
            // radioModeTransfer's tooltip is owned by UpdateTransferChoiceColor: it
            // names the loopback availability, which a static line here cannot.
            toolTip.SetToolTip(
                signalTypeComboBox,
                "Excitation noise played during the measurement.\r\n" +
                "• Pink noise (periodic): one FFT-length period of exactly pink noise, looped. Deterministic and leakage-free, so the transfer function converges fastest. Recommended.\r\n" +
                "• Pink noise: continuous random pink noise, -3 dB/octave.\r\n" +
                "• Brown / red noise: -6 dB/octave, more low-frequency drive for subwoofer and room-mode work.\r\n" +
                "• White noise: equal energy per hertz.\r\n" +
                "• Silent (RTA mode only): no excitation — measures whatever the microphone hears (ambient noise or an external source).");
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
            // labelSpl's / checkSpl's tooltip is owned by RefreshAvailability: it
            // names the current availability state, which a static line here cannot.
        }
    }
}
