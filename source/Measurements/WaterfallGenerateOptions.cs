namespace Resonalyze;

public enum WaterfallMode
{
    Fourier,
    BurstDecay
}

/// <summary>
/// Waterfall / burst-decay generation settings. This is a persisted schema —
/// <see cref="MeasurementSettingsFile"/> round-trips every property and the
/// BurstDecay/Waterfall option panels edit it — so it lives apart from
/// <c>WaterfallSeries</c>, which is the OxyPlot series that happens to consume it.
/// </summary>
public sealed class WaterfallGenerateOptions
{
    public int SliceCount { get; set; } = 64;
    public int Step { get; set; } = 4;
    public int Window { get; set; } = 4096;
    public int LeftTukeyWindow { get; set; } = 8;
    public int RightTukeyWindow { get; set; } = 512;
    public int DbRange { get; set; } = -60;
    public double SmoothingInverseOctaves { get; set; } = 6;
    public int Offset { get; set; }
    public WaterfallMode WaterfallMode { get; set; } = WaterfallMode.Fourier;
    public double Periods { get; set; } = 30;
}
