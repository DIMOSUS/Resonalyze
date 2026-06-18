using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// Estimates relative impulse responses between two captured channels.
/// </summary>
public static class TransferFunction
{
    public static double[] ComputeRelativeIr(
        IReadOnlyList<double> referenceMic,
        IReadOnlyList<double> targetMic,
        double epsilon = 1e-12,
        IReadOnlyList<double>? filter = null)
    {
        ArgumentNullException.ThrowIfNull(referenceMic);
        ArgumentNullException.ThrowIfNull(targetMic);
        if (referenceMic.Count != targetMic.Count)
        {
            throw new ArgumentException("Input arrays must have same length.");
        }
        if (referenceMic.Count == 0)
        {
            throw new ArgumentException("Input arrays must not be empty.");
        }
        if (!double.IsFinite(epsilon) || epsilon < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(epsilon));
        }

        int fftLength = DspMath.NextPowerOfTwo(checked(referenceMic.Count * 2));
        var reference = new Complex[fftLength];
        var target = new Complex[fftLength];

        for (int i = 0; i < referenceMic.Count; i++)
        {
            reference[i] = new Complex(referenceMic[i], 0.0);
            target[i] = new Complex(targetMic[i], 0.0);
        }

        Fourier.Forward(reference, FourierOptions.Matlab);
        Fourier.Forward(target, FourierOptions.Matlab);

        var relative = new Complex[fftLength];
        for (int bin = 0; bin < fftLength; bin++)
        {
            Complex numerator = target[bin] * Complex.Conjugate(reference[bin]);
            double denominator =
                reference[bin].Magnitude * reference[bin].Magnitude + epsilon;
            relative[bin] = numerator / denominator;
        }

        if (filter != null && filter.Count == fftLength)
        {
            for (int bin = 0; bin < fftLength; bin++)
            {
                relative[bin] *= filter[bin];
            }
        }

        Fourier.Inverse(relative, FourierOptions.Matlab);

        var impulseResponse = new double[fftLength];
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            impulseResponse[i] = relative[i].Real;
        }

        return impulseResponse;
    }
}
