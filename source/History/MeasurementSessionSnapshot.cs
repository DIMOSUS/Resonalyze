namespace Resonalyze.History;

internal sealed class MeasurementSessionSnapshot
{
    public ModeTab ActiveMode { get; set; } = ModeTab.Frequency;
    public MeasurementSettingsFile.FrequencyResponseSettings FrequencyResponse { get; set; } = new();
    public MeasurementSettingsFile.FrequencyResponseSettings PhaseResponse { get; set; } = new();
    public MeasurementSettingsFile.FrequencyResponseSettings GroupDelay { get; set; } = new();
    public MeasurementSettingsFile.ImpulseResponseSettings ImpulseResponse { get; set; } = new();
    public MeasurementSettingsFile.WaterfallSettings Waterfall { get; set; } = new();
    public MeasurementSettingsFile.WaterfallSettings BurstDecay { get; set; } = new();
    public MeasurementSettingsFile.LiveSpectrumSettings LiveSpectrum { get; set; } = new();
    public MeasurementSettingsFile.TimeAlignmentSettings TimeAlignment { get; set; } = new();

    // Only the slots that were active (shown) for the active mode are recorded.
    // The overlays themselves keep their own separate on-disk storage
    // (overlays/&lt;Mode&gt;/overlay-NN.json) and are reloaded from there on restore.
    public List<int> ActiveOverlaySlots { get; set; } = [];
}
