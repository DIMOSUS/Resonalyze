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
            FillLists();
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

        /// <summary>Every tooltip the panel shows, including the ones it rewrites as the session changes.</summary>
        internal WrappingToolTip ToolTips => toolTip;

        private void WireFields()
        {
            radioModeRta.CheckedChanged += (_, _) => Edit(() => session.SelectMode(CheckedMode()));
            radioModeMmm.CheckedChanged += (_, _) => Edit(() => session.SelectMode(CheckedMode()));
            Bind<NoiseColor>(signalTypeComboBox, value => session.Signal = value, session.CommitSignal);
            Bind<int>(sequenceLengthComboBox, value => session.SequenceLength = value);
            Bind<WindowType>(windowComboBox, value => session.Window = value, session.CommitWindow);
            Bind<int>(overlapComboBox, value => session.OverlapPercent = value, session.CommitOverlap);
            Bind<int>(comboSmoothingInverseOctaves, value => session.SmoothingInverseOctaves = value, session.CommitSmoothing);
            Bind<AveragingSpeed>(averagingComboBox, value => session.Averaging = value, session.CommitAveraging);
            Bind<int>(coherenceLimitComboBox, value => session.CoherenceLimitPercent = value);
            Bind(checkMainCurve, on => session.MainCurve = on);
            Bind(checkPeakHold, on => session.PeakHold = on);
            Bind(checkCoherence, on => session.Coherence = on);
            Bind(checkInputMagnitude, on => session.InputMagnitude = on, session.ClickInputMagnitude);
            Bind(checkTilt, on => session.Tilt = on, session.ClickTilt);
            Bind(checkSpl, on => session.Spl = on, session.ClickSpl);
        }

        // A moved list is shown at once; only a commit (a pick, not an arrow key) reaches the user's pick.
        private void Bind<T>(ThemedComboBox combo, Action<T> move, Action? commit = null)
        {
            combo.SelectedIndexChanged += (_, _) =>
            {
                // The smoothing list holds its values bare.
                if (combo.SelectedItem is Choice<T> choice)
                {
                    Edit(() => move(choice.Value));
                }
                else if (combo.SelectedItem is T value)
                {
                    Edit(() => move(value));
                }
            };
            if (commit != null)
            {
                combo.SelectionChangeCommitted += (_, _) => Edit(commit);
            }
        }

        private void Bind(CheckBox box, Action<bool> show, Action? click = null)
        {
            box.CheckedChanged += (_, _) => Edit(() => show(box.Checked));
            if (click != null)
            {
                box.Click += (_, _) => Edit(click);
            }
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
    }
}
