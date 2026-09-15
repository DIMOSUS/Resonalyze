using Resonalyze.Dsp;

namespace Resonalyze.Dsp.Tests;

/// <summary>Inverted calibration magnitude, strictly linear-phase (the auralization runs it on both sides).</summary>
public sealed class CalibrationFirFilterTests
{
    private const int Rate = 48_000;

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
        double[] kernel = CalibrationFirFilter.Design(_ => 6.0, Rate);

        foreach (double frequency in new[] { 40.0, 300.0, 1_000.0, 8_000.0, 16_000.0 })
        {
            Assert.InRange(MagnitudeDbAt(kernel, frequency), -6.2, -5.8);
        }
    }

    [Fact]
    public void Design_TracksAShelfAwayFromItsEdge()
    {
        // Frequency sampling smooths the step over a few bins: assert away from the edge.
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
        // Type I symmetry h[n] == h[N−1−n] over an odd length.
        double[] kernel = CalibrationFirFilter.Design(
            frequency => 3.0 * Math.Sin(frequency / 700.0), Rate);

        Assert.True(kernel.Length % 2 == 1, "A Type-I FIR has odd length");
        double peak = kernel.Max(Math.Abs);
        for (int i = 0; i < kernel.Length / 2; i++)
        {
            Assert.True(
                Math.Abs(kernel[i] - kernel[kernel.Length - 1 - i]) <
                    peak * 1e-9,
                $"Asymmetric at tap {i}");
        }
    }

    [Fact]
    public void Design_DelaysByExactlyHalfItsLength()
    {
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
