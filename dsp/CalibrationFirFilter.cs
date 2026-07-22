using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// Designs a linear-phase FIR filter that APPLIES a microphone calibration to
/// audio: its magnitude at every frequency is the inverse of the calibration
/// correction (calibrated curve = raw − correction, so the filter's gain is
/// 10^(−correction/20)), and its phase is exactly linear.
/// <para>
/// Linear phase is a CHOICE here, not a requirement. Any filter applied
/// identically to both sides cancels out of the inter-side phase difference —
/// arg(H_L·C) − arg(H_R·C) = arg(H_L) − arg(H_R) — so a minimum-phase design
/// would preserve the auditioned scene just as well. Linear phase is kept
/// because the designed magnitude is realized exactly and the kernel's
/// symmetry is trivially verifiable; its price — a shared constant delay of
/// half the kernel and a little pre-ringing — is invisible in an offline
/// render of a smooth calibration curve.
/// </para>
/// <para>
/// Frequency-sampling design: the correction is sampled onto an FFT grid as a
/// real zero-phase spectrum, inverted to time, rotated to centre, and shaped by
/// a periodic Hann window. The window trades the truncation seam for roughly a
/// two-bin smoothing of the response — calibration curves are smooth by nature,
/// so nothing real is lost.
/// </para>
/// </summary>
public static class CalibrationFirFilter
{
    // The FFT grid is sized for this resolution: fine enough that a mic's
    // low-frequency deviation (the region car-audio tuning cares most about)
    // is reproduced, coarse enough that the kernel stays a modest fraction of
    // a second.
    private const double TargetResolutionHz = 3.0;

    private const int MinimumLength = 4_096;
    private const int MaximumLength = 32_768;

    /// <summary>
    /// Designs the correction filter for the given sample rate.
    /// <paramref name="correctionDb"/> is the calibration's correction in dB at
    /// a frequency in Hz — pass <c>CalibrationFile.GetDecibelCorrection</c>.
    /// The returned kernel is symmetric (linear-phase) with its peak at
    /// <c>length / 2</c>, so it delays the signal by exactly that many samples.
    /// </summary>
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

        // A real, even spectrum (positive gains mirrored onto the negative
        // bins) inverts to a real, zero-phase impulse response.
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

        // Rotate the zero-phase response so its peak sits at the centre tap
        // (zero phase → linear phase), then apply a PERIODIC Hann window —
        // symmetric about the same centre, so the kernel stays exactly
        // symmetric and the phase exactly linear.
        int center = length / 2;
        var kernel = new double[length];
        for (int i = 0; i < length; i++)
        {
            double window = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / length));
            kernel[i] = spectrum[(i + center) % length].Real * window;
        }

        return kernel;
    }
}
