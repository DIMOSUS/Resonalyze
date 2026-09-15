using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>Linear-phase frequency-sampling FIR that applies a calibration (gain 10^(−correction/20)), Hann-windowed.
/// Linear phase is a choice: a filter shared by both sides cancels out of the inter-side phase anyway.</summary>
public static class CalibrationFirFilter
{
    private const double TargetResolutionHz = 3.0;

    private const int MinimumLength = 4_096;
    private const int MaximumLength = 32_768;

    /// <summary>Returns an odd-length Type-I kernel, exactly symmetric, peak (and delay) at <c>(N−1)/2</c>.</summary>
    public static double[] Design(Func<double, double> correctionDb, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(correctionDb);
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        int length = Math.Clamp(
            DspMath.NextPowerOfTwo(
                (int)Math.Ceiling(sampleRate / TargetResolutionHz)),
            MinimumLength,
            MaximumLength);

        var spectrum = new Complex[length];
        for (int bin = 0; bin <= length / 2; bin++)
        {
            double frequency = bin * (double)sampleRate / length;
            double gain = Math.Pow(10.0, -correctionDb(frequency) / 20.0);
            spectrum[bin] = gain;
            if (bin > 0 && bin < length / 2)
            {
                spectrum[length - bin] = gain;
            }
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);

        // One extra tap makes the length odd so tap i pairs exactly with tap length − i (formal Type-I, not just circularly symmetric).
        int center = length / 2;
        var kernel = new double[length + 1];
        for (int i = 0; i <= length; i++)
        {
            double window = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / length));
            kernel[i] = spectrum[(i + center) % length].Real * window;
        }

        return kernel;
    }
}
