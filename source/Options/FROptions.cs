using OxyPlot.WindowsForms;
using Resonalyze.Dsp;
using Resonalyze.Ui;

namespace Resonalyze.Options
{
    /// <summary>Binds the Frequency Response settings to a <see cref="FrequencyResponseSettingsSession"/>.</summary>
    public partial class FROptions : ImpulsePreviewOptionsForm
    {
        private readonly FrequencyResponseSettingsSession session = new();
        private readonly Color splChoiceReadyForeColor;
        private IReadOnlyList<MicrophoneCalibrationOption>? shownCalibrations;

        public FROptions()
        {
            InitializeComponent();
            PlotInteraction.Enable(irPlotView);
            splChoiceReadyForeColor = radioMagnitudeSpl.ForeColor;
            numericWindow.ApplyFieldRange(ModeSettingsLimits.FrequencyResponseWindow);
            numericLeftWindow.ApplyFieldRange(ModeSettingsLimits.TukeyFade);
            numericRightWindow.ApplyFieldRange(ModeSettingsLimits.TukeyFade);
            comboSmoothingInverseOctaves.FillSmoothingPresets(includePsychoacoustic: true);
            comboCalibration.DropDownStyle = ComboBoxStyle.DropDownList;
            WireFields();
            InitializeToolTips();
        }

        internal void Init(
            AnalyzerDocument document,
            int configuredSampleRate,
            FrequencyResponseOptions frequencyResponseOptions,
            CurveVisibilityOptions visibility,
            IReadOnlyList<MicrophoneCalibrationEntry> calibrationEntries)
        {
            Follow(document, configuredSampleRate);
            session.Follow(OpenMeasurement);
            session.Load(frequencyResponseOptions, visibility, calibrationEntries);
            Redraw();
        }

        /// <summary>A selection the list no longer holds stays selected and marked missing.</summary>
        internal void RefreshCalibrationEntries(
            IReadOnlyList<MicrophoneCalibrationEntry> calibrationEntries) =>
            SelectCalibration(session.CalibrationId, calibrationEntries);

        /// <summary>Shows the calibration a loaded measurement carries.</summary>
        internal void SelectCalibration(
            string? calibrationId,
            IReadOnlyList<MicrophoneCalibrationEntry> calibrationEntries)
        {
            session.SelectCalibration(calibrationId, calibrationEntries);
            Present();
        }

        public void SetOptions(
            FrequencyResponseOptions frequencyResponseOptions,
            CurveVisibilityOptions visibility)
        {
            session.WriteTo(frequencyResponseOptions, visibility);
            Redraw();
        }

        /// <summary>Called when a run starts in view-only SPL, so the fresh measurement is not born hidden.</summary>
        public void ForceRelativeScale()
        {
            session.Spl = false;
            Present();
        }

        // A new measurement may carry an SPL anchor or lack one; recolours without changing the selection.
        private protected override void OnMeasurementChanged()
        {
            session.Follow(OpenMeasurement);
            Redraw();
        }

        private protected override PlotView PreviewView => irPlotView;

        private protected override ImpulsePreviewInput PreviewInput => session.Preview;

        private void WireFields()
        {
            BindIndex(comboWindowMode, index =>
                session.WindowMode = session.WindowMode with { Mode = WindowModeChoice.ModeAt(index) });
            BindItem<int>(comboFdwCycles, cycles => session.WindowMode = session.WindowMode with { Cycles = cycles });
            Bind(numericWindow, value => session.Fades.SetWindow((int)value));
            Bind(numericLeftWindow, value => session.Fades.SetLeft((int)value));
            Bind(numericRightWindow, value => session.Fades.SetRight((int)value));
            BindItem<int>(comboSmoothingInverseOctaves, value => session.SmoothingInverseOctaves = value);
            BindIndex(comboCalibration, index => session.CalibrationIndex = index);
            Bind(checkBoxShowPrimary, on => session.Curves.ShowPrimary = on);
            Bind(checkBoxShowCoherence, on => session.Curves.ShowCoherence = on);
            Bind(checkBoxShowArrayAverage, on => session.Curves.ShowArrayAverage = on);
            Bind(checkBoxShowArrayMicrophones, on => session.Curves.ShowArrayMicrophones = on);
            Bind(checkBoxShowArraySpread, on => session.Curves.ShowArraySpread = on);
            Bind(checkBoxShowHd2, on => session.Curves.ShowHd2 = on);
            Bind(checkBoxShowHd3, on => session.Curves.ShowHd3 = on);
            Bind(checkBoxShowHd4, on => session.Curves.ShowHd4 = on);
            Bind(checkBoxShowThdPlusNoise, on => session.Curves.ShowThdPlusNoise = on);
            Bind(checkBoxShowNoiseFloor, on => session.Curves.ShowNoiseFloor = on);
            // A radio clears its sibling before it raises CheckedChanged, so both read the final pick.
            Bind(radioMagnitudeSpl, _ => session.Spl = radioMagnitudeSpl.Checked);
            Bind(radioMagnitudeRelative, _ => session.Spl = radioMagnitudeSpl.Checked);
        }

        private protected override void PresentControls()
        {
            ShowIndex(comboWindowMode, session.WindowMode.ModeIndex);
            ShowItem(comboFdwCycles, session.WindowMode.Cycles);
            // In FDW mode the window fields still define the outer gate.
            comboFdwCycles.Enabled = session.WindowMode.CyclesEditable;
            Show(numericWindow, session.Fades.Window);
            Show(numericLeftWindow, session.Fades.Left, session.Fades.LeftMaximum);
            Show(numericRightWindow, session.Fades.Right, session.Fades.RightMaximum);
            ShowItem(comboSmoothingInverseOctaves, session.SmoothingInverseOctaves);
            PresentCalibrations();
            checkBoxShowPrimary.Checked = session.Curves.ShowPrimary;
            checkBoxShowCoherence.Checked = session.Curves.ShowCoherence;
            checkBoxShowArrayAverage.Checked = session.Curves.ShowArrayAverage;
            checkBoxShowArrayMicrophones.Checked = session.Curves.ShowArrayMicrophones;
            checkBoxShowArraySpread.Checked = session.Curves.ShowArraySpread;
            checkBoxShowHd2.Checked = session.Curves.ShowHd2;
            checkBoxShowHd3.Checked = session.Curves.ShowHd3;
            checkBoxShowHd4.Checked = session.Curves.ShowHd4;
            checkBoxShowThdPlusNoise.Checked = session.Curves.ShowThdPlusNoise;
            checkBoxShowNoiseFloor.Checked = session.Curves.ShowNoiseFloor;
            radioMagnitudeSpl.Checked = session.Spl;
            radioMagnitudeRelative.Checked = !session.Spl;
            radioMagnitudeSpl.ForeColor = FrequencyResponseSplChoice.ViewOnlyConflict(session.Measurement)
                ? UiPalette.Warning
                : splChoiceReadyForeColor;
            toolTip.SetToolTip(radioMagnitudeSpl, FrequencyResponseSplChoice.ToolTip(session.Measurement));
        }

        private void PresentCalibrations()
        {
            if (!ReferenceEquals(shownCalibrations, session.Calibrations))
            {
                comboCalibration.Items.Clear();
                foreach (MicrophoneCalibrationOption option in session.Calibrations)
                {
                    comboCalibration.Items.Add(option);
                }

                shownCalibrations = session.Calibrations;
            }

            ShowIndex(comboCalibration, session.CalibrationIndex);
            comboCalibration.Enabled = session.Calibrations.Count > 1;
        }

        private void InitializeToolTips()
        {
            toolTip.SetToolTip(
                comboWindowMode,
                "Fixed uses one time window for the entire spectrum (the steady-state " +
                "in-room response). FDW shortens the analysis window as frequency rises " +
                "to suppress late cabin reflections (a quasi-anechoic response).");
            toolTip.SetToolTip(
                comboFdwCycles,
                "Periods retained by FDW: 4 suppresses reflections most, 6 is " +
                "recommended, and 8 retains more reflected detail. The Tukey window " +
                "below remains the outer gate FDW never exceeds.");
            numericWindow.ApplyToolTip(
                toolTip,
                "Sets the FFT window length used to calculate the frequency response.");
            numericLeftWindow.ApplyToolTip(
                toolTip,
                "Controls the fade-in part of the Tukey window before the main impulse region.");
            numericRightWindow.ApplyToolTip(
                toolTip,
                "Controls the fade-out part of the Tukey window after the main impulse region.");
            toolTip.SetToolTip(
                comboSmoothingInverseOctaves,
                "Applies octave smoothing to the resulting frequency-response curve.");
            toolTip.SetToolTip(
                comboCalibration,
                "Applies the selected microphone calibration file to the displayed frequency response.");
            toolTip.SetToolTip(
                labelScale,
                "Vertical scale of the magnitude plot.");
            toolTip.SetToolTip(
                radioMagnitudeRelative,
                "Native scale: the response in dBr (relative to the loopback reference), " +
                "distortion and noise in dBc (relative to the fundamental).");
            // radioMagnitudeSpl's tooltip is owned by PresentControls.
            toolTip.SetToolTip(
                checkBoxShowPrimary,
                "Shows the primary frequency-response curve.");
            toolTip.SetToolTip(
                checkBoxShowCoherence,
                "Shows the measurement coherence (\u03B3\u00B2) curve when the IR was captured with 2+ averaged runs.");
            toolTip.SetToolTip(
                checkBoxShowArrayAverage,
                "Shows the spatial average of the microphone array this measurement " +
                "was recorded with. It is a steady-state curve and does not follow " +
                "the time window.");
            toolTip.SetToolTip(
                checkBoxShowArrayMicrophones,
                "Shows each array position behind the average, levelled onto the " +
                "measurement microphone.");
            toolTip.SetToolTip(
                checkBoxShowArraySpread,
                "Shows how far apart the array positions sit at each frequency, on " +
                "its own axis — where they disagree, a single-point measurement is " +
                "describing that spot rather than the seat.");
            toolTip.SetToolTip(
                checkBoxShowHd2,
                "Shows the 2nd harmonic distortion curve.");
            toolTip.SetToolTip(
                checkBoxShowHd3,
                "Shows the 3rd harmonic distortion curve.");
            toolTip.SetToolTip(
                checkBoxShowHd4,
                "Shows the 4th harmonic distortion curve.");
            toolTip.SetToolTip(
                checkBoxShowThdPlusNoise,
                "Shows the total harmonic distortion (THD) curve — harmonics only.");
            toolTip.SetToolTip(
                checkBoxShowNoiseFloor,
                "Shows the measurement noise floor as its own trace; its label states the "
                + "analysis bandwidth the level is measured at.");
            toolTip.SetToolTip(
                irPlotView,
                "Preview of the transfer impulse response and the analysis window used " +
                "for the primary curve. In FDW mode this window is the outer gate; " +
                "higher frequencies use shorter windows inside it. The harmonic curves " +
                "window the sweep-deconvolution IR with automatically derived windows.");
        }
    }
}
