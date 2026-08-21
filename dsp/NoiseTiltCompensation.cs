using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>
/// The coefficients of Paul Kellett's economical pink-noise filter bank — the one
/// source of truth shared by the noise synthesis (the app's <c>NoiseSignal</c>
/// drives white noise through this recurrence) and by
/// <see cref="NoiseSpectralModel.KellettPink"/>, which must model the very filter
/// the excitation was made with: the bank only approximates −3 dB/octave between
/// its poles, and below the lowest pole's corner (~8 Hz at 44.1 kHz but ~35 Hz at
/// 192 kHz — the poles live in normalized frequency) the response flattens.
/// </summary>
public static class KellettPinkFilter
{
    /// <summary>Per-pole (feedback A, input gain G): state' = A·state + G·white.</summary>
    public static readonly IReadOnlyList<(double A, double G)> Poles =
    [
        (0.99886, 0.0555179),
        (0.99332, 0.0750759),
        (0.96900, 0.1538520),
        (0.86650, 0.3104856),
        (0.55000, 0.5329522),
        (-0.7616, -0.0168980)
    ];

    /// <summary>The direct white-noise term added to the pole sum.</summary>
    public const double DirectGain = 0.5362;

    /// <summary>The one-sample-delayed white-noise term (the classic b6 state).</summary>
    public const double DelayedGain = 0.115926;

    /// <summary>
    /// The exact magnitude response of the bank at a frequency, for the sample rate
    /// the noise is generated at: <c>|Σ G/(1−A·z⁻¹) + direct + delayed·z⁻¹|</c>.
    /// </summary>
    public static double MagnitudeAt(double frequency, int sampleRate)
    {
        double omega = 2.0 * Math.PI * frequency / sampleRate;
        Complex z1 = Complex.FromPolarCoordinates(1.0, -omega);
        Complex response = DirectGain + DelayedGain * z1;
        foreach ((double a, double g) in Poles)
        {
            response += g / (Complex.One - a * z1);
        }

        return response.Magnitude;
    }
}

public enum NoiseSpectralModelKind
{
    PowerLaw,
    LeakyIntegrator,
    KellettPink
}

/// <summary>
/// The spectral shape a noise excitation was actually SYNTHESISED with, as the tilt
/// compensation must model it. An idealised per-octave slope is only honest for
/// signals built that way (white; the periodic pink whose bins are exactly 1/√f);
/// the filtered noises deviate from their nominal slope where their filters do —
/// brown's leaky integrator flattens below its corner, Kellett pink below its
/// lowest pole — and compensating the nominal slope there would print an artificial
/// bass roll-off onto a correct measurement.
/// </summary>
/// <param name="Parameter">
/// <see cref="NoiseSpectralModelKind.PowerLaw"/>: the PSD slope in dB per octave.
/// <see cref="NoiseSpectralModelKind.LeakyIntegrator"/>: the corner frequency in Hz
/// (the synthesis derives its leak from this and the sample rate; so does the model).
/// Unused for <see cref="NoiseSpectralModelKind.KellettPink"/>.
/// </param>
public readonly record struct NoiseSpectralModel(
    NoiseSpectralModelKind Kind,
    double Parameter)
{
    public static NoiseSpectralModel PowerLaw(double psdSlopeDbPerOctave) =>
        new(NoiseSpectralModelKind.PowerLaw, psdSlopeDbPerOctave);

    /// <summary>Brown noise: white through a one-pole leaky integrator.</summary>
    public static NoiseSpectralModel LeakyIntegrator(double cornerHz) =>
        new(NoiseSpectralModelKind.LeakyIntegrator, cornerHz);

    /// <summary>Random pink noise: white through the Kellett filter bank.</summary>
    public static NoiseSpectralModel KellettPink { get; } =
        new(NoiseSpectralModelKind.KellettPink, 0.0);

    /// <summary>
    /// The amplitude spectrum of the noise at a frequency (arbitrary overall gain —
    /// every consumer normalizes at the pivot). The digital filter magnitudes use
    /// the same leak/pole formulas as the synthesis, so the model stays exact from
    /// the flattened low corners up to the near-Nyquist digital deviation.
    /// </summary>
    public double AmplitudeAt(double frequency, int sampleRate)
    {
        if (frequency <= 0.0)
        {
            return 0.0;
        }

        switch (Kind)
        {
            case NoiseSpectralModelKind.PowerLaw:
                // PSD ∝ f^(α/(10·log10 2)) means amplitude ∝ f^(α/(20·log10 2)) —
                // exactly 1/√f for pink (α = −3.01).
                return Math.Pow(frequency, Parameter / (20.0 * Math.Log10(2.0)));

            case NoiseSpectralModelKind.LeakyIntegrator:
            {
                // Mirrors the synthesis: value' = leak·value + (1−leak)·white with
                // leak = 1 − 2π·fc/fs, so |H| = (1−leak)/|1 − leak·e^(−jω)|.
                double leak = Math.Clamp(
                    1.0 - 2.0 * Math.PI * Parameter / Math.Max(1, sampleRate),
                    0.0,
                    0.99999);
                double omega = 2.0 * Math.PI * frequency / sampleRate;
                return (1.0 - leak) / Math.Sqrt(
                    1.0 - 2.0 * leak * Math.Cos(omega) + leak * leak);
            }

            default:
                return KellettPinkFilter.MagnitudeAt(frequency, sampleRate);
        }
    }
}

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
/// <item>The per-bin dB display (constant absolute bin width) renders the noise's
/// amplitude spectrum directly — <see cref="BinCompensationDb"/> is its mirror.</item>
/// <item>The band-power display (<see cref="DataHelper.LogarithmicPowerBandResample"/>)
/// integrates power over bands of constant RELATIVE width, where pink renders flat
/// and white renders +3 dB/octave — and switches to constant ABSOLUTE width where
/// the window main lobe is wider than the reference band, restoring the per-bin
/// slopes below that corner. <see cref="BandCompensationDb"/> follows every clamp
/// and kink exactly by rendering the modelled noise spectrum through the very same
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
    /// modelled amplitude of the noise, zero at the pivot.
    /// </summary>
    public static double BinCompensationDb(
        NoiseSpectralModel model,
        double frequency,
        int sampleRate)
    {
        if (frequency <= 0.0 || sampleRate <= 0)
        {
            return 0.0;
        }

        double amplitude = model.AmplitudeAt(frequency, sampleRate);
        double pivot = model.AmplitudeAt(PivotFrequency, sampleRate);
        return amplitude > 0.0 && pivot > 0.0
            ? -20.0 * Math.Log10(amplitude / pivot)
            : 0.0;
    }

    /// <summary>
    /// The per-point compensation for a band-power display curve produced by
    /// <see cref="DataHelper.LogarithmicPowerBandResample"/> with these same
    /// parameters: the modelled spectrum of the noise is rendered through that
    /// resampler and mirrored, so the result aligns index-for-index with the
    /// displayed curve (same grid, same clamps) and is exact across the
    /// relative-to-absolute bandwidth corner. Zero at the grid point nearest the
    /// pivot. A flat model (white noise) still compensates — the band law itself
    /// tilts a flat PSD by +3 dB/octave.
    /// </summary>
    public static double[] BandCompensationDb(
        NoiseSpectralModel model,
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
            model, binCount, fftLength, sampleRate);
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
    /// The modelled amplitude spectrum of the noise, per FFT bin, at an arbitrary
    /// overall gain (the compensation is pivot-normalized either way).
    /// </summary>
    private static double[] ReferenceAmplitudeSpectrum(
        NoiseSpectralModel model,
        int binCount,
        int fftLength,
        int sampleRate)
    {
        double binWidth = fftLength > 0 ? (double)sampleRate / fftLength : 0.0;
        var amplitude = new double[Math.Max(0, binCount)];
        if (binWidth <= 0.0)
        {
            return amplitude;
        }

        // Bin 0 is DC: the sloped noises have no defined density there, and the band
        // resampler never reads it (it integrates from bin 1), so it stays zero.
        for (int bin = 1; bin < amplitude.Length; bin++)
        {
            amplitude[bin] = model.AmplitudeAt(bin * binWidth, sampleRate);
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
