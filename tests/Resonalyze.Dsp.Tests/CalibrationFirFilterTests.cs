using Resonalyze.Dsp;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The calibration FIR must do exactly two things: reproduce the calibration's
/// magnitude inverted (the correction the curves subtract, applied as a filter)
/// and stay strictly linear-phase, because the auralization runs it over BOTH
/// sides of the car and any phase behavior must cancel in the inter-side
/// comparison.
/// </summary>
public sealed class CalibrationFirFilterTests
{
    private const int Rate = 48_000;

    // The filter's frequency response probed directly: |Σ h[n]·e^(−j2πfn/fs)|.
    private static double MagnitudeDbAt(double[] kernel, double frequencyHz)
    {
        double real = 0;
        double imaginary = 0;
        for (int n = 0; n < kernel.Length; n++)
        {
            double phase = -2.0 * Math.PI * frequencyHz * n / Rate;
            real += kernel[n] * Math.Cos(phase);
            imaginary += kernel[n] * Math.Sin(phase);
        }

        return 20.0 * Math.Log10(Math.Sqrt(real * real + imaginary * imaginary));
    }

    [Fact]
    public void Design_InvertsAFlatCorrection()
    {
        // A microphone reading +6 dB hot everywhere: the filter must sit at
        // −6 dB everywhere, so the calibrated render matches the calibrated
        // on-screen curves.
        double[] kernel = CalibrationFirFilter.Design(_ => 6.0, Rate);

        foreach (double frequency in new[] { 40.0, 300.0, 1_000.0, 8_000.0, 16_000.0 })
        {
            Assert.InRange(MagnitudeDbAt(kernel, frequency), -6.2, -5.8);
        }
    }

    [Fact]
    public void Design_TracksAShelfAwayFromItsEdge()
    {
        // A +6 dB high shelf above 1 kHz. The frequency-sampling design smooths
        // the step over a couple of grid bins, so the assertion stays away from
        // the edge itself.
        double[] kernel = CalibrationFirFilter.Design(
            frequency => frequency >= 1_000.0 ? 6.0 : 0.0, Rate);

        Assert.InRange(MagnitudeDbAt(kernel, 100.0), -0.3, 0.3);
        Assert.InRange(MagnitudeDbAt(kernel, 250.0), -0.3, 0.3);
        Assert.InRange(MagnitudeDbAt(kernel, 4_000.0), -6.3, -5.7);
        Assert.InRange(MagnitudeDbAt(kernel, 12_000.0), -6.3, -5.7);
    }

    [Fact]
    public void Design_IsExactlyLinearPhase()
    {
        // Linear phase = symmetric kernel. Not required for the inter-side
        // scene (any shared filter cancels out of the phase difference), but
        // it is what this design promises — exact magnitude, constant delay —
        // so hold it to that.
        double[] kernel = CalibrationFirFilter.Design(
            frequency => 3.0 * Math.Sin(frequency / 700.0), Rate);

        int center = kernel.Length / 2;
        double peak = kernel.Max(Math.Abs);
        for (int offset = 1; offset < center; offset++)
        {
            Assert.True(
                Math.Abs(kernel[center + offset] - kernel[center - offset]) <
                    peak * 1e-9,
                $"Asymmetric at offset {offset}");
        }
    }

    [Fact]
    public void Design_DelaysByExactlyHalfItsLength()
    {
        // The peak of a near-flat filter sits at the centre tap: the constant
        // group delay both sides share.
        double[] kernel = CalibrationFirFilter.Design(_ => 0.0, Rate);

        int peakIndex = 0;
        for (int i = 1; i < kernel.Length; i++)
        {
            if (Math.Abs(kernel[i]) > Math.Abs(kernel[peakIndex]))
            {
                peakIndex = i;
            }
        }

        Assert.Equal(kernel.Length / 2, peakIndex);
    }
}
