using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Builds the RAW (unsmoothed) form of the live RTA trace so an overlay can store it and
/// re-apply its own smoothing later — the same contract the frequency-response primary
/// curve has, but for the reference-free microphone spectrum.
/// </summary>
/// <remarks>
/// Only the native (relative) RTA has a raw form. There, the trace is the FFT bins
/// converted to dB and smoothed by <see cref="DataHelper.LogarithmicResample"/> onto the
/// 20 Hz .. 20 kHz display grid, exactly like the swept primary curve, so the bins ARE
/// the reference and re-smoothing them reproduces the trace.
/// <para>
/// The dB SPL RTA has none. Its level is a band POWER integral
/// (<see cref="DataHelper.LogarithmicPowerBandResample"/>) evaluated on a grid the
/// integrator CLAMPS to the range where a whole band fits inside the resolved spectrum —
/// at a 2048-point FFT and 48 kHz that grid starts near 59 Hz, not 20 Hz. Re-gridding
/// such a curve onto the display range would hold the lowest band all the way down to
/// 20 Hz and invent a flat bass tail the analyzer never measured, which is worse than
/// having no raw form at all. An SPL capture therefore stores the drawn curve, matching
/// how the swept frequency response already declines to store a raw SPL reference.
/// </para>
/// </remarks>
internal static class LiveRtaRawCapture
{
    /// <summary>
    /// The raw samples of the relative (native dB) RTA: every FFT bin as (Hz, dB),
    /// uncalibrated. Mirrors the point construction inside the live magnitude resampler,
    /// so smoothing these with <see cref="RawCurveRenderer.Render"/> reproduces the trace.
    /// </summary>
    public static List<SignalPoint> BuildRelativeRaw(
        IReadOnlyList<double> amplitudeSpectrum,
        int fftLength,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(amplitudeSpectrum);

        int binCount = Math.Min(fftLength / 2, amplitudeSpectrum.Count);
        var points = new List<SignalPoint>(Math.Max(0, binCount - 1));
        if (fftLength <= 0 || sampleRate <= 0)
        {
            return points;
        }

        // Bin 0 (DC) is skipped: it has no place on a logarithmic frequency axis.
        for (int i = 1; i < binCount; i++)
        {
            double frequency = i * ((double)sampleRate / fftLength);
            points.Add(new SignalPoint(
                frequency,
                DataHelper.AmplitudeToDecibels(amplitudeSpectrum[i])));
        }

        return points;
    }
}
