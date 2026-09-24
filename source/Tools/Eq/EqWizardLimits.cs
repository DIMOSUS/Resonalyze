using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Ranges and decimals of every value the EQ Wizard holds. The session keeps each value as its field would show it and
/// the panel gives the fields these ranges, so the bank the fit, the plot and an export read is the one on screen.
/// </summary>
internal static class EqWizardLimits
{
    public const int MaxBands = 32;
    public const int MinAutoTuneBandLimit = 4;

    public const decimal MinFrequencyGapHz = 1m;
    public const decimal MinGainGapDb = 1m;

    public static readonly NumericFieldRange BandFrequency = new(10m, 20_000m, 0);
    public static readonly NumericFieldRange BandQ = new(0.1m, 20m, 1);

    private const int BandGainDecimals = 1;

    public static readonly NumericFieldRange Preamp = new(-80m, 80m, 1);
    public static readonly NumericFieldRange GainMinimum = new(-60m, 0m, 0);
    public static readonly NumericFieldRange GainMaximum = new(0m, 24m, 0);
    public static readonly NumericFieldRange AutoTuneMaxQ = new(0.5m, 20m, 1);
    public static readonly NumericFieldRange WindowFrequency = new(20m, 20_000m, 0);
    public static readonly NumericFieldRange TargetOffset = new(-180m, 180m, 1);

    /// <summary>A band's gain field: the user's Max Cut to Max Boost.</summary>
    public static NumericFieldRange BandGain(decimal minimumDb, decimal maximumDb) =>
        new(minimumDb, maximumDb, BandGainDecimals);

    /// <summary>A band as a strip holds it: each field clamped and rounded (<see cref="NumericFieldRange.Clamp"/>).</summary>
    public static PeqBand Normalize(PeqBand band, NumericFieldRange gain) => band with
    {
        FrequencyHz = (double)BandFrequency.Clamp(band.FrequencyHz),
        Q = (double)BandQ.Clamp(band.Q),
        GainDb = (double)gain.Clamp(band.GainDb)
    };
}
