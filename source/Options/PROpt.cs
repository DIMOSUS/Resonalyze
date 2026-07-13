using System;
using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.Options
{
    public partial class PROpt : ImpulsePreviewOptionsForm
    {
        private Func<CompareAnalysisSource?>? getCompare;
        private readonly ComboBox comboWindowMode = new();
        private readonly ComboBox comboFdwCycles = new();
        private readonly ComboBox comboDetrendMode = new();
        private readonly Label labelAutoDetrend = new();

        public PROpt()
        {
            InitializeComponent();
            InitializePhaseAnalysisControls();
            numericLeftWindow.ValueChanged += Gate_ValueChanged;
            numericRightWindow.ValueChanged += Gate_ValueChanged;
            numericGateOffset.ValueChanged += (_, _) => UpdateIrPreview();
            buttonFit.Click += buttonFit_Click;
            buttonTauSlope.Click += (_, _) => ApplyEstimatedTau(useSlope: true);
            buttonTauPeak.Click += (_, _) => ApplyEstimatedTau(useSlope: false);
            ConfigureResetDefaults();
            SmoothingPresetOptions.Configure(comboSmoothingInverseOctaves);
            InitializeToolTips();
        }

        public void Init(
            ExpSweepMeasurement expSweepMeasurement,
            FrequencyResponseOptions opt,
            CurveVisibilityOptions visibility,
            Func<CompareAnalysisSource?>? getCompare = null)
        {
            AttachMeasurement(expSweepMeasurement);
            this.getCompare = getCompare;
            InitializeControls(() =>
            {
                numericGateOffset.Value = ClampToControl(numericGateOffset, opt.PhaseGateOffsetMs);
                numericWindow.Value = ClampToControl(numericWindow, opt.PhasePlateauMs);
                numericLeftWindow.Value = ClampToControl(numericLeftWindow, opt.PhaseLeftMs);
                numericRightWindow.Value = ClampToControl(numericRightWindow, opt.PhaseRightMs);
                comboSmoothingInverseOctaves.SelectedItem =
                    SmoothingPresetOptions.Normalize(opt.SmoothingInverseOctaves);
                numericOffset.Value = ClampToControl(numericOffset, opt.PhaseDetrendMs);
                comboWindowMode.SelectedIndex = opt.PhaseWindowMode == PhaseWindowMode.Fixed ? 0 : 1;
                comboFdwCycles.SelectedItem = opt.PhaseFdwCycles is 4 or 6 or 8
                    ? opt.PhaseFdwCycles
                    : PhaseAnalysisSettings.DefaultFdwCycles;
                comboDetrendMode.SelectedIndex = (int)opt.PhaseDetrendMode;
                checkBoxUnwrap.Checked = opt.Unwrap;
                checkBoxShowMeasured.Checked = visibility.ShowMeasuredPhase;
                checkBoxShowMinimum.Checked = visibility.ShowMinimumPhase;
                checkBoxShowExcess.Checked = visibility.ShowExcessPhase;
                checkBoxShowCoherence.Checked = visibility.ShowCoherence;
            });
            UpdateMinFrequencyLabel();
            UpdatePhaseControlState();
            UpdateIrPreview();
        }

        public void SetOptions(FrequencyResponseOptions opt, CurveVisibilityOptions visibility)
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
            opt.PhaseWindowMode = comboWindowMode.SelectedIndex == 0
                ? PhaseWindowMode.Fixed
                : PhaseWindowMode.FrequencyDependent;
            opt.PhaseFdwCycles = comboFdwCycles.SelectedItem is int cycles
                ? cycles
                : PhaseAnalysisSettings.DefaultFdwCycles;
            opt.PhaseDetrendMode = Enum.IsDefined(
                (PhaseDetrendMode)comboDetrendMode.SelectedIndex)
                    ? (PhaseDetrendMode)comboDetrendMode.SelectedIndex
                    : PhaseDetrendMode.Auto;
            opt.Unwrap = checkBoxUnwrap.Checked;
            visibility.ShowMeasuredPhase = checkBoxShowMeasured.Checked;
            visibility.ShowMinimumPhase = checkBoxShowMinimum.Checked;
            visibility.ShowExcessPhase = checkBoxShowExcess.Checked;
            visibility.ShowCoherence = checkBoxShowCoherence.Checked;
            UpdateIrPreview();
        }

        private void InitializePhaseAnalysisControls()
        {
            const int addedHeight = 92;
            foreach (Control control in Controls.Cast<Control>().ToArray())
            {
                control.Top += addedHeight;
            }
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + addedHeight);

            AddModeControl("Window", comboWindowMode, 10);
            comboWindowMode.Items.AddRange(["Fixed", "FDW"]);
            AddModeControl("FDW cycles", comboFdwCycles, 36);
            comboFdwCycles.Items.AddRange([4, 6, 8]);
            AddModeControl("Detrend", comboDetrendMode, 62);
            comboDetrendMode.Items.AddRange(["Off", "Auto", "Manual"]);
            labelAutoDetrend.AutoSize = true;
            labelAutoDetrend.ForeColor = Color.Gainsboro;
            labelAutoDetrend.Location = new Point(12, 84);
            Controls.Add(labelAutoDetrend);

            comboWindowMode.SelectedIndexChanged += (_, _) => UpdatePhaseControlState();
            comboFdwCycles.SelectedIndexChanged += (_, _) => UpdatePhaseControlState();
            comboDetrendMode.SelectedIndexChanged += (_, _) => UpdatePhaseControlState();
        }

        private void AddModeControl(string text, ComboBox combo, int top)
        {
            var label = new Label
            {
                AutoSize = true,
                ForeColor = Color.Gainsboro,
                Location = new Point(12, top + 4),
                Text = text
            };
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Location = new Point(153, top);
            combo.Size = new Size(100, 23);
            Controls.Add(label);
            Controls.Add(combo);
        }

        private void UpdatePhaseControlState()
        {
            comboFdwCycles.Enabled = comboWindowMode.SelectedIndex == 1;
            bool manual = comboDetrendMode.SelectedIndex == (int)PhaseDetrendMode.Manual;
            numericOffset.Enabled = manual;
            buttonTauSlope.Enabled = manual;
            buttonTauPeak.Enabled = manual;
            labelAutoDetrend.Text = comboDetrendMode.SelectedIndex == (int)PhaseDetrendMode.Auto
                ? ResolveAutoDetrendLabel()
                : string.Empty;
        }

        private string ResolveAutoDetrendLabel()
        {
            try
            {
                if (Measurement is not { InProgress: false } measurement ||
                    measurement.TransferImpulseResponse is not { Length: > 0 })
                {
                    return "Auto detrend: —";
                }
                IImpulseMeasurement impulse =
                    new MeasurementPlotContext(measurement).CreatePrimaryMeasurement();
                var settings = new PhaseAnalysisSettings(
                    comboWindowMode.SelectedIndex == 0
                        ? PhaseWindowMode.Fixed
                        : PhaseWindowMode.FrequencyDependent,
                    comboFdwCycles.SelectedItem is int cycles
                        ? cycles
                        : PhaseAnalysisSettings.DefaultFdwCycles,
                    PhaseDetrendMode.Auto,
                    (double)numericOffset.Value,
                    (double)numericGateOffset.Value,
                    (double)numericLeftWindow.Value,
                    (double)numericWindow.Value,
                    (double)numericRightWindow.Value,
                    checkBoxUnwrap.Checked,
                    comboSmoothingInverseOctaves.SelectedItem is int smoothing
                        ? smoothing
                        : FrequencyResponseOptions.DefaultPhaseSmoothingInverseOctaves);
                double resolved = DataHelper.ResolvePhaseDetrendMilliseconds(impulse, settings);
                return $"Auto detrend: {resolved:0.00} ms";
            }
            catch (InvalidOperationException)
            {
                return "Auto detrend: —";
            }
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
            if (Measurement is not { } measurement ||
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
            if (Measurement is not { } measurement ||
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

        protected override void RenderIrPreview()
        {
            if (Measurement == null || Measurement.SampleRate <= 0)
            {
                return;
            }

            ImpulseWindowPreview.UpdateGated(
                irPlotView,
                Measurement,
                (double)numericGateOffset.Value,
                (double)numericLeftWindow.Value,
                (double)numericWindow.Value,
                (double)numericRightWindow.Value,
                IrPreviewSource.Primary,
                getCompare?.Invoke());
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
                "Preview of the impulse response and the gate window used for phase calculation. FDW phase uses shorter high-frequency windows; Group Delay remains fixed-gate and is not its exact integral.");
            toolTip.SetToolTip(
                comboWindowMode,
                "Fixed uses one time gate for the entire spectrum. FDW shortens the analysis window as frequency rises to suppress late cabin reflections.");
            toolTip.SetToolTip(
                comboFdwCycles,
                "Periods retained by FDW: 4 suppresses reflections most, 6 is recommended, and 8 retains more reflected detail.");
            toolTip.SetToolTip(
                comboDetrendMode,
                "Removes a constant delay before unwrapping. Auto flattens excess phase, Manual uses the entered delay, and Off keeps the absolute phase slope.");
        }
    }
}
