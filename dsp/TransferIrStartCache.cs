using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Resonalyze.Dsp;

/// <summary>
/// The shared "where does this transfer IR honestly start" answer behind every
/// Auto gate-offset control (Phase, Group Delay), the plot builds that consume
/// it and the plain magnitude extraction
/// (<see cref="DataHelper.GetOversampledPrimarySpectrum"/>):
/// <see cref="TransferIrDiagnostics.EstimateIrStart(Complex[], int, ValidSampleRange)"/>,
/// memoized per IR array so the dialogs, the plot factory and the spectrum
/// path read one figure computed once per measurement instead of re-running
/// the band-limited analysis on every control change or redraw. Falls back to
/// the transfer peak — the Fit figure — when the estimator refuses the
/// record, so Auto is never worse than Fit.
/// </summary>
public static class TransferIrStartCache
{
    private sealed record CachedStart(
        int SampleRate, ValidSampleRange ValidRange, double StartMs);

    private static readonly ConditionalWeakTable<Complex[], CachedStart> cache = new();

    /// <summary>
    /// The same estimate for any analysis-layer measurement view (the Compare
    /// overlay); null without an impulse response.
    /// </summary>
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

    /// <summary>
    /// The estimated IR start (ms from the record start) for an IR held
    /// directly as an array; <paramref name="fallbackPeakIndex"/> answers when
    /// the estimator refuses the record.
    /// </summary>
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

    /// <summary>
    /// <see cref="ResolveStartMs(Complex[], int, int, ValidSampleRange)"/> as a
    /// sample index into the record — the ONE ms-to-index conversion every
    /// window that anchors on the response start shares, so the Virtual DSP
    /// plot, the handoffs and the plain spectrum extraction cannot round the
    /// same figure to different samples.
    /// </summary>
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
