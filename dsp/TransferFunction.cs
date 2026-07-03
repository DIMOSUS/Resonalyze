using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// Estimates relative impulse responses between two captured channels.
/// </summary>
public static class TransferFunction
{
    public static TransferEstimateResult ComputeAveragedRelativeIr(
        IReadOnlyList<TransferFunctionFrame> frames,
        double epsilon = 1e-12)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            throw new ArgumentException("At least one transfer frame is required.", nameof(frames));
        }
        if (!double.IsFinite(epsilon) || epsilon < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(epsilon));
        }

        int sampleCount = frames.Min(frame => Math.Min(frame.Reference.Count, frame.Target.Count));
        if (sampleCount == 0)
        {
            throw new ArgumentException("Transfer frames must not be empty.", nameof(frames));
        }

        int fftLength = DspMath.NextPowerOfTwo(checked(sampleCount * 2));
        var crossSpectrum = new Complex[fftLength];
        var referencePowerSpectrum = new double[fftLength];
        var targetPowerSpectrum = new double[fftLength];

        foreach (TransferFunctionFrame frame in frames)
        {
            var reference = new Complex[fftLength];
            var target = new Complex[fftLength];
            for (int i = 0; i < sampleCount; i++)
            {
                reference[i] = new Complex(frame.Reference[i], 0.0);
                target[i] = new Complex(frame.Target[i], 0.0);
            }

            Fourier.Forward(reference, FourierOptions.Matlab);
            Fourier.Forward(target, FourierOptions.Matlab);

            for (int bin = 0; bin < fftLength; bin++)
            {
                Complex referenceBin = reference[bin];
                Complex targetBin = target[bin];
                crossSpectrum[bin] += targetBin * Complex.Conjugate(referenceBin);
                referencePowerSpectrum[bin] += MagnitudeSquared(referenceBin);
                targetPowerSpectrum[bin] += MagnitudeSquared(targetBin);
            }
        }

        var relative = new Complex[fftLength];
        var coherence = new double[fftLength / 2 + 1];
        for (int bin = 0; bin < fftLength; bin++)
        {
            relative[bin] = crossSpectrum[bin] / (referencePowerSpectrum[bin] + epsilon);
            if (bin < coherence.Length)
            {
                double denominator = referencePowerSpectrum[bin] * targetPowerSpectrum[bin];
                coherence[bin] = denominator > 0
                    ? Math.Clamp(MagnitudeSquared(crossSpectrum[bin]) / denominator, 0.0, 1.0)
                    : 0.0;
            }
        }

        Fourier.Inverse(relative, FourierOptions.Matlab);

        var impulseResponse = new double[fftLength];
        double peakMagnitude = 0;
        int peakIndex = 0;
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            double value = relative[i].Real;
            impulseResponse[i] = value;
            double magnitude = Math.Abs(value);
            if (magnitude > peakMagnitude)
            {
                peakMagnitude = magnitude;
                peakIndex = i;
            }
        }

        return new TransferEstimateResult(
            impulseResponse,
            peakIndex,
            frames.Count >= 2 ? coherence : null);
    }

    private static double MagnitudeSquared(Complex value) =>
        value.Real * value.Real + value.Imaginary * value.Imaginary;

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

public readonly record struct TransferFunctionFrame(
    IReadOnlyList<double> Reference,
    IReadOnlyList<double> Target);

public readonly record struct TransferEstimateResult(
    double[] ImpulseResponse,
    int PeakIndex,
    double[]? Coherence);
