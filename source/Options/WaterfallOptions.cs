namespace Resonalyze.Options
{
    /// <summary>Binds the Waterfall settings to a <see cref="WaterfallSettingsSession"/>.</summary>
    public partial class WaterfallOptions : WaterfallSettingsForm
    {
        public WaterfallOptions()
        {
            InitializeComponent();
            BindWaterfall(
                WaterfallSettingsSession.ForWaterfall(),
                numericSampleRate,
                numericWindow,
                numericLeftWindow,
                numericRightWindow,
                numericCaptureTime,
                numericDbRange,
                comboSmoothingInverseOctaves,
                numericOffset,
                irPlotView);
            numericSlices.ApplyFieldRange(ModeSettingsLimits.Slices);
            numericStep.ApplyFieldRange(ModeSettingsLimits.Step);
            Bind(numericSlices, value => Session.SetSliceCount((int)value));
            Bind(numericStep, value => Session.SetStep((int)value));
            InitializeToolTips();
        }

        internal void Init(
            AnalyzerDocument document,
            int configuredSampleRate,
            WaterfallGenerateOptions waterfallGenerateOptions) =>
            InitWaterfall(document, configuredSampleRate, waterfallGenerateOptions);

        public void SetOptions(WaterfallGenerateOptions waterfallGenerateOptions) =>
            WriteWaterfall(waterfallGenerateOptions);

        private protected override void PresentControls()
        {
            base.PresentControls();
            Show(numericSlices, Session.SliceCount);
            Show(numericStep, Session.Step);
        }

        private void InitializeToolTips()
        {
            numericWindow.ApplyToolTip(
                toolTip,
                "Sets the FFT window length for each waterfall slice.");
            numericSlices.ApplyToolTip(
                toolTip,
                "Controls how many slices are drawn in depth.");
            numericStep.ApplyToolTip(
                toolTip,
                "Sets the shift in samples between neighboring slices. Larger values cover more time with fewer overlapping slices.");
            numericLeftWindow.ApplyToolTip(
                toolTip,
                "Controls the fade-in part of the Tukey window before the analyzed region.");
            numericRightWindow.ApplyToolTip(
                toolTip,
                "Controls the fade-out part of the Tukey window after the analyzed region.");
            numericDbRange.ApplyToolTip(
                toolTip,
                "Sets the lower display limit in decibels for the waterfall plot.");
            toolTip.SetToolTip(
                comboSmoothingInverseOctaves,
                "Applies octave smoothing to each resampled frequency slice.");
            numericOffset.ApplyToolTip(
                toolTip,
                "Shifts the whole waterfall analysis window relative to the detected impulse-response peak.");
            toolTip.SetToolTip(
                irPlotView,
                "Preview of the impulse response and the analysis window used for waterfall generation.");
        }
    }
}
