using System.Numerics;
using System.Runtime.CompilerServices;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The shared "where does this transfer IR honestly start" answer behind every
/// Auto gate-offset control (Phase, Group Delay) and the plot builds that
/// consume it: <see cref="TransferIrDiagnostics.EstimateIrStart"/>, memoized
/// per IR array so the dialogs and the plot factory read one figure computed
/// once per measurement instead of re-running the band-limited analysis on
/// every control change. Falls back to the transfer peak (the old Fit figure)
/// when the estimator refuses the record, so Auto is never worse than Fit was.
/// </summary>
internal static class TransferIrStartCache
{
    private sealed record CachedStart(int SampleRate, double StartMs);

    private static readonly ConditionalWeakTable<Complex[], CachedStart> cache = new();

    /// <summary>
    /// The estimated IR start (ms from the record start) of the measurement's
    /// transfer IR; null when there is no transfer IR to read.
    /// </summary>
    public static double? ResolveStartMs(ExpSweepMeasurement measurement)
    {
        if (measurement.Transfer is not { ImpulseResponse.Length: > 0 } transfer ||
            measurement.SampleRate <= 0)
        {
            return null;
        }

        return ResolveStartMs(
            transfer.ImpulseResponse, measurement.SampleRate, transfer.PeakIndex);
    }

    /// <summary>
    /// The same estimate for an IR held directly as an array (the Virtual DSP
    /// processed channels); <paramref name="fallbackPeakIndex"/> answers when
    /// the estimator refuses the record.
    /// </summary>
    public static double ResolveStartMs(
        Complex[] impulseResponse,
        int sampleRate,
        int fallbackPeakIndex)
    {
        if (cache.TryGetValue(impulseResponse, out CachedStart? cached) &&
            cached.SampleRate == sampleRate)
        {
            return cached.StartMs;
        }

        double startMs = TransferIrDiagnostics.EstimateIrStart(
            impulseResponse, sampleRate) is { } estimate
                ? estimate.StartMs
                : fallbackPeakIndex * 1_000.0 / sampleRate;
        cache.AddOrUpdate(impulseResponse, new CachedStart(sampleRate, startMs));
        return startMs;
    }
}
