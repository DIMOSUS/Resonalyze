using System;
using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.Options
{
    public partial class PROpt : Form
    {
        private readonly ToolTip toolTip = new();
        private ExpSweepMeasurement? expSweepMeasurement;
        private Func<CompareAnalysisSource?>? getCompare;

        public PROpt()
        {
            InitializeComponent();
            numericLeftWindow.ValueChanged += Gate_ValueChanged;
            numericRightWindow.ValueChanged += Gate_ValueChanged;
            numericGateOffset.ValueChanged += (_, _) => UpdateIrPreview();
            buttonFit.Click += buttonFit_Click;
            buttonTauSlope.Click += (_, _) => ApplyEstimatedTau(useSlope: true);
            buttonTauPeak.Click += (_, _) => ApplyEstimatedTau(useSlope: false);
            ConfigureResetDefaults();
            SmoothingPresetOptions.Configure(comboSmoothingInverseOctaves);
            InitializeToolTips();
            // Disposed, not FormClosed: a dialog disposed without ever having been
            // shown (e.g. the docked host closing it while the owner is minimized)
            // never raises FormClosed, which leaked the measurement subscription.
            Disposed += PROpt_Disposed;
        }

        public void Init(
            ExpSweepMeasurement expSweepMeasurement,
            FrequencyResponseOptions opt,
            Func<CompareAnalysisSource?>? getCompare = null)
        {
            if (!ReferenceEquals(this.expSweepMeasurement, expSweepMeasurement))
            {
                if (this.expSweepMeasurement != null)
                {
                    this.expSweepMeasurement.ImpulseResponseChanged -= ExpSweepMeasurement_ImpulseResponseChanged;
                }

                expSweepMeasurement.ImpulseResponseChanged += ExpSweepMeasurement_ImpulseResponseChanged;
            }

            this.expSweepMeasurement = expSweepMeasurement;
            this.getCompare = getCompare;
            numericGateOffset.Value = ClampToControl(numericGateOffset, opt.PhaseGateOffsetMs);
            numericWindow.Value = ClampToControl(numericWindow, opt.PhasePlateauMs);
            numericLeftWindow.Value = ClampToControl(numericLeftWindow, opt.PhaseLeftMs);
            numericRightWindow.Value = ClampToControl(numericRightWindow, opt.PhaseRightMs);
            comboSmoothingInverseOctaves.SelectedItem =
                SmoothingPresetOptions.Normalize(opt.SmoothingInverseOctaves);
            numericOffset.Value = ClampToControl(numericOffset, opt.PhaseDetrendMs);
            checkBoxUnwrap.Checked = opt.Unwrap;
            checkBoxShowMeasured.Checked = opt.ShowMeasuredPhase;
            checkBoxShowMinimum.Checked = opt.ShowMinimumPhase;
            checkBoxShowExcess.Checked = opt.ShowExcessPhase;
            checkBoxShowCoherence.Checked = opt.ShowCoherence;
            UpdateMinFrequencyLabel();
            UpdateIrPreview();
        }

        public void SetOptions(FrequencyResponseOptions opt)
        {
            opt.PhaseGateOffsetMs = (double)numericGateOffset.Value;
            opt.PhasePlateauMs = (double)numericWindow.Value;
            opt.PhaseLeftMs = (double)numericLeftWindow.Value;
            opt.PhaseRightMs = (double)numericRightWindow.Value;
            opt.SmoothingInverseOctaves =
                comboSmoothingInverseOctaves.SelectedItem is int inverseOctaves
                    ? inverseOctaves
                    : SmoothingPresetOptions.SupportedInverseOctaves[0];
            opt.PhaseDetrendMs = (double)numericOffset.Value;
            opt.Unwrap = checkBoxUnwrap.Checked;
            opt.ShowMeasuredPhase = checkBoxShowMeasured.Checked;
            opt.ShowMinimumPhase = checkBoxShowMinimum.Checked;
            opt.ShowExcessPhase = checkBoxShowExcess.Checked;
            opt.ShowCoherence = checkBoxShowCoherence.Checked;
            UpdateIrPreview();
        }

        // Points each field's "R" reset button at the built-in default values.
        private void ConfigureResetDefaults()
        {
            var defaults = new FrequencyResponseOptions();
            numericLeftWindow.DefaultValue = (decimal)defaults.PhaseLeftMs;
            numericWindow.DefaultValue = (decimal)defaults.PhasePlateauMs;
            numericRightWindow.DefaultValue = (decimal)defaults.PhaseRightMs;
            numericOffset.DefaultValue = (decimal)defaults.PhaseDetrendMs;
            comboSmoothingInverseOctaves.DefaultSelectedItem =
                SmoothingPresetOptions.Normalize(
                    FrequencyResponseOptions.DefaultPhaseSmoothingInverseOctaves);
        }

        // Estimates τ from the current gate and writes it into the τ field. Slope flattens
        // the average excess-phase trend; peak references the dominant arrival.
        private void ApplyEstimatedTau(bool useSlope)
        {
            // Phase analysis (and therefore τ) only works with a transfer IR, so
            // gate the auto-estimate on the same condition as Fit and the plot.
            if (expSweepMeasurement is not { } measurement ||
                measurement.TransferImpulseResponse is not { Length: > 0 } ||
                measurement.InProgress)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            try
            {
                IImpulseMeasurement impulse =
                    new MeasurementPlotContext(measurement).CreatePrimaryMeasurement();
                (double slopeMs, double peakMs) = DataHelper.EstimatePhaseDetrend(
                    impulse,
                    (double)numericGateOffset.Value,
                    (double)numericLeftWindow.Value,
                    (double)numericWindow.Value,
                    (double)numericRightWindow.Value);
                numericOffset.Value = ClampToControl(
                    numericOffset,
                    useSlope ? slopeMs : peakMs);
            }
            catch (InvalidOperationException)
            {
                System.Media.SystemSounds.Beep.Play();
            }
        }

        // Snap the gate offset to the transfer IR peak (deterministic, gate-independent).
        private void buttonFit_Click(object? sender, EventArgs e)
        {
            if (expSweepMeasurement is not { } measurement ||
                measurement.Transfer is not { ImpulseResponse.Length: > 0 } transfer ||
                measurement.SampleRate <= 0)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            double onsetMs = transfer.PeakIndex * 1000.0 / measurement.SampleRate;
            numericGateOffset.Value = ClampToControl(numericGateOffset, onsetMs);
        }

        private void numericWindow_ValueChanged(object sender, EventArgs e)
        {
            UpdateMinFrequencyLabel();
            UpdateIrPreview();
        }

        private void Gate_ValueChanged(object? sender, EventArgs e)
        {
            UpdateMinFrequencyLabel();
            UpdateIrPreview();
        }

        private void UpdateMinFrequencyLabel()
        {
            double hz = FrequencyResponseOptions.GateMinReliableFrequencyHz(
                (double)numericLeftWindow.Value,
                (double)numericWindow.Value,
                (double)numericRightWindow.Value);
            labelMinFrequency.Text = hz > 0
                ? $"Reliable from ≈ {hz:0}+ Hz"
                : "Reliable from ≈ — Hz";
        }

        // Re-draws the preview after the Compare selection changes while docked.
        public void RefreshComparePreview() => UpdateIrPreview();

        private void UpdateIrPreview()
        {
            if (expSweepMeasurement == null || expSweepMeasurement.SampleRate <= 0)
            {
                return;
            }

            ImpulseWindowPreview.UpdateGated(
                irPlotView,
                expSweepMeasurement,
                (double)numericGateOffset.Value,
                (double)numericLeftWindow.Value,
                (double)numericWindow.Value,
                (double)numericRightWindow.Value,
                IrPreviewSource.Primary,
                getCompare?.Invoke());
        }

        private static decimal ClampToControl(DarkNumericUpDown control, double value)
        {
            decimal candidate = double.IsFinite(value) ? (decimal)value : 0m;
            return Math.Clamp(candidate, control.Minimum, control.Maximum);
        }

        private void ExpSweepMeasurement_ImpulseResponseChanged()
        {
            if (IsDisposed)
            {
                return;
            }

            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke((MethodInvoker)UpdateIrPreview);
                return;
            }

            UpdateIrPreview();
        }

        private void PROpt_Disposed(object? sender, EventArgs e)
        {
            if (expSweepMeasurement != null)
            {
                expSweepMeasurement.ImpulseResponseChanged -= ExpSweepMeasurement_ImpulseResponseChanged;
            }

            toolTip.Dispose();
        }

        private void InitializeToolTips()
        {
            numericGateOffset.ApplyToolTip(
                toolTip,
                "Gate position: time from the IR start to the end of the left Tukey shoulder. Use Fit to snap it to the transfer IR peak.");
            toolTip.SetToolTip(
                buttonFit,
                "Snap the gate offset to the transfer IR peak.");
            numericWindow.ApplyToolTip(
                toolTip,
                "Flat (weight 1) part of the gate after the peak, in milliseconds.");
            numericLeftWindow.ApplyToolTip(
                toolTip,
                "Tukey fade-in before the peak, in milliseconds. Keep short.");
            numericRightWindow.ApplyToolTip(
                toolTip,
                "Tukey fade-out gate after the plateau, in milliseconds. End it before the first reflection.");
            toolTip.SetToolTip(
                comboSmoothingInverseOctaves,
                "Applies octave smoothing to the phase traces.");
            numericOffset.ApplyToolTip(
                toolTip,
                "τ: linear-phase reference (delay) in milliseconds, removed to make the excess phase readable. Enter the same τ on two measurements to compare their relative phase.");
            toolTip.SetToolTip(
                buttonTauSlope,
                "Auto-find τ from the energy-weighted average group delay (flattens the excess-phase trend).");
            toolTip.SetToolTip(
                buttonTauPeak,
                "Auto-find τ from the dominant arrival (bulk delay).");
            toolTip.SetToolTip(
                checkBoxUnwrap,
                "Removes 360-degree phase wraps to display a continuous phase curve.");
            toolTip.SetToolTip(
                checkBoxShowMeasured,
                "Shows the measured phase (orange): the raw response including delay and reflections.");
            toolTip.SetToolTip(
                checkBoxShowMinimum,
                "Shows the minimum phase (cyan): the part tied to the magnitude, correctable with EQ.");
            toolTip.SetToolTip(
                checkBoxShowExcess,
                "Shows the excess phase (green): measured minus minimum — the part EQ cannot fix.");
            toolTip.SetToolTip(
                checkBoxShowCoherence,
                "Shows the measurement coherence (\u03B3\u00B2) curve when the IR was captured with 2+ averaged runs.");
            toolTip.SetToolTip(
                labelMinFrequency,
                "Lowest frequency the current gate can resolve (≈ 1 / gate length). Below it the curve is not reliable.");
            toolTip.SetToolTip(
                irPlotView,
                "Preview of the impulse response and the gate window used for phase calculation.");
        }
    }
}
