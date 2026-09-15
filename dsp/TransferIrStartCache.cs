using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Resonalyze.Dsp;

/// <summary>Memoized per IR array: <see cref="TransferIrDiagnostics.EstimateIrStart(Complex[], int, ValidSampleRange)"/> shared by every Auto gate
/// and the spectrum path. Falls back to the transfer peak, so Auto is never worse than Fit.</summary>
public static class TransferIrStartCache
{
    private sealed record CachedStart(
        int SampleRate, ValidSampleRange ValidRange, double StartMs);

    private static readonly ConditionalWeakTable<Complex[], CachedStart> cache = new();

    public static double? ResolveStartMs(IImpulseMeasurement measurement)
    {
        if (measurement.ImpulseResponse is not { Length: > 0 } impulseResponse ||
            measurement.SampleRate <= 0)
        {
            return null;
        }

        return ResolveStartMs(
            impulseResponse, measurement.SampleRate, measurement.PeakIndex);
    }

    public static double ResolveStartMs(
        Complex[] impulseResponse,
        int sampleRate,
        int fallbackPeakIndex,
        ValidSampleRange validRange = default)
    {
        if (cache.TryGetValue(impulseResponse, out CachedStart? cached) &&
            cached.SampleRate == sampleRate &&
            cached.ValidRange == validRange)
        {
            return cached.StartMs;
        }

        double startMs = TransferIrDiagnostics.EstimateIrStart(
            impulseResponse, sampleRate, validRange) is { } estimate
                ? estimate.StartMs
                : fallbackPeakIndex * 1_000.0 / sampleRate;
        cache.AddOrUpdate(
            impulseResponse, new CachedStart(sampleRate, validRange, startMs));
        return startMs;
    }

    /// <summary>The one ms-to-index conversion, so all start-anchored windows round to the same sample.</summary>
    public static int ResolveStartIndex(
        Complex[] impulseResponse,
        int sampleRate,
        int fallbackPeakIndex,
        ValidSampleRange validRange = default) =>
        Math.Clamp(
            (int)Math.Floor(
                ResolveStartMs(
                    impulseResponse, sampleRate, fallbackPeakIndex, validRange)
                / 1_000.0 * sampleRate),
            0,
            Math.Max(0, impulseResponse.Length - 1));
}
