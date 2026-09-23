namespace Resonalyze.Options
{
    /// <summary>Binds the Burst Decay settings to a <see cref="WaterfallSettingsSession"/>.</summary>
    public partial class BDOpt : WaterfallSettingsForm
    {
        public BDOpt()
        {
            InitializeComponent();
            BindWaterfall(
                WaterfallSettingsSession.ForBurstDecay(),
                numericSampleRate,
                numericWindow,
                numericLeftWindow,
                numericRightWindow,
                numericCaptureTime,
                numericDbRange,
                comboSmoothingInverseOctaves,
                numericOffset,
                irPlotView);
            numericPeriods.ApplyFieldRange(ModeSettingsLimits.Periods);
            Bind(numericPeriods, value => Session.Periods = (int)value);
            InitializeToolTips();
        }

        internal void Init(
            AnalyzerDocument document,
            int configuredSampleRate,
            WaterfallGenerateOptions burstDecayGenOptions) =>
            InitWaterfall(document, configuredSampleRate, burstDecayGenOptions);

        public void SetOptions(WaterfallGenerateOptions burstDecayGenOptions) => WriteWaterfall(burstDecayGenOptions);

        private protected override void PresentControls()
        {
            base.PresentControls();
            Show(numericPeriods, Session.Periods);
        }

        private void InitializeToolTips()
        {
            numericWindow.ApplyToolTip(
                toolTip,
                "Sets the impulse-response window length used for burst-decay analysis.");
            numericLeftWindow.ApplyToolTip(
                toolTip,
                "Controls the fade-in part of the Tukey window before the analyzed region.");
            numericRightWindow.ApplyToolTip(
                toolTip,
                "Controls the fade-out part of the Tukey window after the analyzed region.");
            toolTip.SetToolTip(
                comboSmoothingInverseOctaves,
                "Sets the analysis bandwidth in octaves for each burst-decay slice. Narrower bands increase frequency resolution but make traces less stable.");
            numericDbRange.ApplyToolTip(
                toolTip,
                "Sets the lower display limit in decibels for the burst-decay plot.");
            numericOffset.ApplyToolTip(
                toolTip,
                "Shifts the burst-decay analysis window relative to the detected impulse-response peak.");
            numericPeriods.ApplyToolTip(
                toolTip,
                "Sets how many signal periods are shown on the horizontal axis for each frequency slice.");
            toolTip.SetToolTip(
                irPlotView,
                "Preview of the impulse response and the analysis window used for burst-decay generation.");
        }
    }
}
