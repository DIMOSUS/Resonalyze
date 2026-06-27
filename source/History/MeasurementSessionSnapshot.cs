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
    public List<OverlaySessionSnapshot> ActiveOverlays { get; set; } = [];
}

internal sealed class OverlaySessionSnapshot
{
    public int Slot { get; set; }
    public bool Visible { get; set; }
    public OverlayFile? File { get; set; }
}
