using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Raw (unsmoothed) live RTA for overlays; only the relative RTA has one. See docs/tech/live-spectrum.md#raw-rta-capture.</summary>
internal static class LiveRtaRawCapture
{
    /// <summary>Every FFT bin as (Hz, dB), uncalibrated, with any active noise-tilt compensation baked in as displayed.</summary>
    public static List<SignalPoint> BuildRelativeRaw(
        IReadOnlyList<double> amplitudeSpectrum,
        int fftLength,
        int sampleRate,
        NoiseSpectralModel? tiltCompensationModel = null)
    {
        List<SignalPoint> bins =
            DataHelper.MagnitudeBinsToDecibels(amplitudeSpectrum, fftLength, sampleRate);
        if (tiltCompensationModel is { } model && sampleRate > 0)
        {
            for (int i = 0; i < bins.Count; i++)
            {
                bins[i] = new SignalPoint(
                    bins[i].X,
                    bins[i].Y + NoiseTiltCompensation.BinCompensationDb(
                        model, bins[i].X, sampleRate));
            }
        }

        return bins;
    }
}
