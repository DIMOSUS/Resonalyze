using System.Numerics;
using System.Runtime.CompilerServices;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Memoized <see cref="TransferIrDiagnostics.DetectDominantBand"/> per IR array; the estimate costs a transform per redraw otherwise.</summary>
internal static class TransferIrDominantBandCache
{
    private sealed record CachedBand(int SampleRate, DominantBand Band);

    private static readonly ConditionalWeakTable<Complex[], CachedBand> cache = new();

    public static DominantBand? Resolve(IImpulseMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        if (measurement.ImpulseResponse is not { Length: > 0 } impulseResponse ||
            measurement.SampleRate <= 0)
        {
            return null;
        }

        if (cache.TryGetValue(impulseResponse, out CachedBand? cached) &&
            cached.SampleRate == measurement.SampleRate)
        {
            return cached.Band;
        }

        var real = new double[impulseResponse.Length];
        for (int i = 0; i < real.Length; i++)
        {
            real[i] = impulseResponse[i].Real;
        }

        DominantBand band = TransferIrDiagnostics.DetectDominantBand(
            real, measurement.SampleRate);
        cache.AddOrUpdate(impulseResponse, new CachedBand(measurement.SampleRate, band));
        return band;
    }

    /// <summary>False when the band cannot be read: refusing is the safe direction.</summary>
    public static bool Covers(IImpulseMeasurement measurement, double centerHz) =>
        Resolve(measurement) is { } band &&
        centerHz >= band.LowHz &&
        centerHz <= band.HighHz;
}
