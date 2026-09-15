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

    // Overlay contents are reloaded from their own files on restore.
    public List<int> ActiveOverlaySlots { get; set; } = [];
}
