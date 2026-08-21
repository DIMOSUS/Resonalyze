using System.Numerics;
using System.Runtime.CompilerServices;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Where a transfer IR actually carries the driver's energy
/// (<see cref="TransferIrDiagnostics.DetectDominantBand"/>), memoized per IR array the
/// same way <see cref="TransferIrStartCache"/> memoizes the record's start — the
/// estimate costs a transform, and a view that asks it on every redraw would pay for it
/// on every settings change.
/// </summary>
internal static class TransferIrDominantBandCache
{
    private sealed record CachedBand(int SampleRate, DominantBand Band);

    private static readonly ConditionalWeakTable<Complex[], CachedBand> cache = new();

    /// <summary>
    /// The record's dominant band; null when there is nothing to read it from.
    /// </summary>
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

    /// <summary>
    /// Whether the record carries this driver's energy at <paramref name="centerHz"/>.
    /// False when the band cannot be read at all: a figure derived from a band the
    /// driver does not play is not a reading, and refusing it is the safe direction.
    /// </summary>
    public static bool Covers(IImpulseMeasurement measurement, double centerHz) =>
        Resolve(measurement) is { } band &&
        centerHz >= band.LowHz &&
        centerHz <= band.HighHz;
}
