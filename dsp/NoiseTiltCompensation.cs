namespace Resonalyze.Dsp;

/// <summary>
/// Display-side compensation for the spectral tilt a noise excitation itself prints
/// onto a reference-free RTA: measured through a perfectly flat system, pink noise
/// still draws −3 dB/octave on a per-bin dB axis, because the tilt belongs to the
/// signal, not the system. Subtracting the noise's own rendered shape — pinned to
/// 0 dB at <see cref="PivotFrequency"/> so the level does not jump — makes a flat
/// system read flat whatever the excitation colour.
/// </summary>
/// <remarks>
/// The shape being subtracted depends on the DISPLAY PATH, not just the noise:
/// <list type="bullet">
/// <item>The per-bin dB display (constant absolute bin width) renders a noise of
/// PSD slope α dB/octave as a straight α dB/octave line —
/// <see cref="BinCompensationDb"/> is its mirror.</item>
/// <item>The band-power display (<see cref="DataHelper.LogarithmicPowerBandResample"/>)
/// integrates power over bands of constant RELATIVE width, where pink renders flat
/// and white renders +3 dB/octave — and switches to constant ABSOLUTE width where
/// the window main lobe is wider than the reference band, restoring the per-bin
/// slopes below that corner. <see cref="BandCompensationDb"/> follows every clamp
/// and kink exactly by rendering the analytic noise spectrum through the very same
/// resampler instead of restating its band law.</item>
/// </list>
/// </remarks>
public static class NoiseTiltCompensation
{
    /// <summary>
    /// The frequency the compensation is pinned to zero at, so switching it on
    /// rotates the curve around a familiar anchor instead of shifting its level.
    /// </summary>
    public const double PivotFrequency = 1000.0;

    /// <summary>
    /// The compensation for one point of the per-bin dB display: the mirrored
    /// straight line of the noise's PSD slope, zero at the pivot.
    /// </summary>
    public static double BinCompensationDb(double psdSlopeDbPerOctave, double frequency) =>
        frequency > 0.0
            ? -psdSlopeDbPerOctave * Math.Log2(frequency / PivotFrequency)
            : 0.0;

    /// <summary>
    /// The per-point compensation for a band-power display curve produced by
    /// <see cref="DataHelper.LogarithmicPowerBandResample"/> with these same
    /// parameters: the analytic spectrum of the noise is rendered through that
    /// resampler and mirrored, so the result aligns index-for-index with the
    /// displayed curve (same grid, same clamps) and is exact across the
    /// relative-to-absolute bandwidth corner. Zero at the grid point nearest the
    /// pivot. A slope of zero (white noise) still compensates — the band law
    /// itself tilts a flat PSD by +3 dB/octave.
    /// </summary>
    public static double[] BandCompensationDb(
        double psdSlopeDbPerOctave,
        int binCount,
        int fftLength,
        int sampleRate,
        double windowEnbwBins,
        double windowMainLobeBins,
        double start,
        double stop,
        int steps,
        double smoothingOctaves,
        bool psychoacoustic)
    {
        double[] reference = ReferenceAmplitudeSpectrum(
            psdSlopeDbPerOctave, binCount, fftLength, sampleRate);
        List<SignalPoint> shape = DataHelper.LogarithmicPowerBandResample(
            reference,
            fftLength,
            sampleRate,
            windowEnbwBins,
            windowMainLobeBins,
            start,
            stop,
            steps,
            smoothingOctaves,
            psychoacoustic);

        var compensation = new double[shape.Count];
        if (shape.Count == 0)
        {
            return compensation;
        }

        double pivotDb = shape[NearestIndex(shape, PivotFrequency)].Y;
        for (int i = 0; i < shape.Count; i++)
        {
            compensation[i] = pivotDb - shape[i].Y;
        }

        return compensation;
    }

    /// <summary>
    /// The amplitude spectrum a noise of the given PSD slope has, per FFT bin, with
    /// unit amplitude at the pivot: PSD ∝ f^(α/(10·log10 2)) means amplitude
    /// ∝ f^(α/(20·log10 2)) — exactly <c>1/√f</c> for pink (α = −3.01), which is
    /// also bin-for-bin how the periodic pink excitation is synthesised.
    /// </summary>
    private static double[] ReferenceAmplitudeSpectrum(
        double psdSlopeDbPerOctave,
        int binCount,
        int fftLength,
        int sampleRate)
    {
        double exponent = psdSlopeDbPerOctave / (20.0 * Math.Log10(2.0));
        double binWidth = fftLength > 0 ? (double)sampleRate / fftLength : 0.0;
        var amplitude = new double[Math.Max(0, binCount)];
        if (binWidth <= 0.0)
        {
            return amplitude;
        }

        // Bin 0 is DC: a sloped noise has no defined density there, and the band
        // resampler never reads it (it integrates from bin 1), so it stays zero.
        for (int bin = 1; bin < amplitude.Length; bin++)
        {
            amplitude[bin] = Math.Pow(bin * binWidth / PivotFrequency, exponent);
        }

        return amplitude;
    }

    private static int NearestIndex(List<SignalPoint> points, double frequency)
    {
        int nearest = 0;
        double best = double.PositiveInfinity;
        for (int i = 0; i < points.Count; i++)
        {
            // The grid is logarithmic, so compare in octaves, not hertz.
            double distance = Math.Abs(Math.Log2(points[i].X / frequency));
            if (distance < best)
            {
                best = distance;
                nearest = i;
            }
        }

        return nearest;
    }
}
