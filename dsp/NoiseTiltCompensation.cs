using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>Kellett pink-noise bank shared by synthesis and <see cref="NoiseSpectralModel.KellettPink"/>. Poles are in normalized
/// frequency, so the low-end flattening moves with rate (~8 Hz at 44.1 kHz, ~35 Hz at 192 kHz).</summary>
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

    public const double DirectGain = 0.5362;

    public const double DelayedGain = 0.115926;

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

/// <summary>The shape the excitation was actually synthesised with; compensating a nominal slope where a filter flattens
/// would print an artificial bass roll-off.</summary>
/// <param name="Parameter">PowerLaw: PSD slope in dB/octave. LeakyIntegrator: corner in Hz. Unused for KellettPink.</param>
public readonly record struct NoiseSpectralModel(
    NoiseSpectralModelKind Kind,
    double Parameter)
{
    public static NoiseSpectralModel PowerLaw(double psdSlopeDbPerOctave) =>
        new(NoiseSpectralModelKind.PowerLaw, psdSlopeDbPerOctave);

    public static NoiseSpectralModel LeakyIntegrator(double cornerHz) =>
        new(NoiseSpectralModelKind.LeakyIntegrator, cornerHz);

    public static NoiseSpectralModel KellettPink { get; } =
        new(NoiseSpectralModelKind.KellettPink, 0.0);

    /// <summary>Amplitude at an arbitrary overall gain (consumers normalize at the pivot).</summary>
    public double AmplitudeAt(double frequency, int sampleRate)
    {
        if (frequency <= 0.0)
        {
            return 0.0;
        }

        switch (Kind)
        {
            case NoiseSpectralModelKind.PowerLaw:
                return Math.Pow(frequency, Parameter / (20.0 * Math.Log10(2.0)));

            case NoiseSpectralModelKind.LeakyIntegrator:
            {
                // Mirrors the synthesis: leak = 1 − 2π·fc/fs, |H| = (1−leak)/|1 − leak·e^(−jω)|.
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

/// <summary>Subtracts the excitation's own rendered tilt from a reference-free RTA, pinned to 0 dB at <see cref="PivotFrequency"/>.</summary>
/// <remarks>The shape depends on the display path: per-bin dB mirrors the amplitude spectrum; band-power display is rendered through
/// the same resampler so every clamp and bandwidth kink matches.</remarks>
public static class NoiseTiltCompensation
{
    public const double PivotFrequency = 1000.0;

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

    /// <summary>Aligns index-for-index with <see cref="DataHelper.LogarithmicPowerBandResample"/> output; white still compensates (+3 dB/oct band law).</summary>
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

        // DC stays zero; the band resampler integrates from bin 1.
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
