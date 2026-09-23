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

        /// <summary>Every tooltip the panel shows, including the ones it rewrites as the session changes.</summary>
        internal ToolTip ToolTips => toolTip;

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
    }
}
