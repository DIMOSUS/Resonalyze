namespace Resonalyze;

public enum WaterfallMode
{
    Fourier,
    BurstDecay
}

/// <summary>Persisted schema (<see cref="MeasurementSettingsFile"/>), kept apart from the OxyPlot WaterfallSeries.</summary>
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

    internal WaterfallGenerateOptions Copy() => (WaterfallGenerateOptions)MemberwiseClone();

    /// <summary>Samples the first Fourier slice opens before the anchor; a backward step ends its plateau there.</summary>
    internal static int FirstSliceLead(int step, int window, int leftTukey, int rightTukey) =>
        step >= 0 ? leftTukey : window - rightTukey;
}
