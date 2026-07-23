using Resonalyze.Dsp;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The cabin curves are anchored to published or owner measurements, not
/// invented: the averaged-sedan table (Autozvuk 2000/08), a measured hatchback
/// (+23.5 dB at 20 Hz), a compact sedan with the enclosure coupled into the
/// cabin (+27 dB at 20 Hz) and the owner-measured BMW F30 with the ski hatch
/// open. The tests hold the presets to those anchors and hold the shape to the
/// model: rising toward low frequencies below the corner, nothing above it.
/// </summary>
public sealed class CabinTransferFunctionTests
{
    private const int Rate = 48_000;

    [Theory]
    [InlineData(CabinBodyStyle.Sedan, 20.0, 15.5, 17.5)]
    [InlineData(CabinBodyStyle.Sedan, 40.0, 7.0, 8.5)]
    [InlineData(CabinBodyStyle.Sedan, 63.0, 2.3, 3.5)]
    [InlineData(CabinBodyStyle.Sedan, 80.0, 0.8, 1.9)]
    [InlineData(CabinBodyStyle.Hatchback, 20.0, 23.0, 25.0)]
    [InlineData(CabinBodyStyle.CompactSedan, 20.0, 26.0, 28.0)]
    [InlineData(CabinBodyStyle.Suv, 20.0, 13.5, 15.5)]
    [InlineData(CabinBodyStyle.Wagon, 20.0, 19.0, 21.0)]
    [InlineData(CabinBodyStyle.BmwF30SkiHatch, 20.0, 33.5, 34.5)]
    [InlineData(CabinBodyStyle.BmwF30SkiHatch, 50.0, 19.0, 20.0)]
    [InlineData(CabinBodyStyle.BmwF30SkiHatch, 70.0, 6.0, 7.0)]
    [InlineData(CabinBodyStyle.BmwF30SkiHatch, 100.0, 0.0, 0.0)]
    public void Evaluate_MatchesTheMeasuredAnchors(
        CabinBodyStyle bodyStyle, double frequencyHz, double minDb, double maxDb)
    {
        CabinTransferFunction cabin = CabinTransferFunction.FromBodyStyle(bodyStyle);

        Assert.InRange(cabin.Evaluate(frequencyHz), minDb, maxDb);
    }

    [Theory]
    [InlineData(CabinBodyStyle.Sedan, 140.0)]
    [InlineData(CabinBodyStyle.CompactSedan, 160.0)]
    [InlineData(CabinBodyStyle.Hatchback, 160.0)]
    [InlineData(CabinBodyStyle.Wagon, 140.0)]
    [InlineData(CabinBodyStyle.Suv, 110.0)]
    [InlineData(CabinBodyStyle.BmwF30SkiHatch, 95.0)]
    public void Evaluate_IsFlatAboveTheCornerAndMonotonicBelow(
        CabinBodyStyle bodyStyle, double flatFromHz)
    {
        CabinTransferFunction cabin = CabinTransferFunction.FromBodyStyle(bodyStyle);

        // An octave above the corner the knee has decayed to a fraction of a
        // dB, and midband/treble carry nothing at all.
        Assert.InRange(cabin.Evaluate(flatFromHz), 0.0, 0.6);
        Assert.InRange(cabin.Evaluate(1_000.0), 0.0, 0.01);
        Assert.InRange(cabin.Evaluate(16_000.0), 0.0, 0.01);

        double previous = double.MaxValue;
        for (double frequency = 10; frequency <= 20_000; frequency *= 1.1)
        {
            double gain = cabin.Evaluate(frequency);
            Assert.True(
                gain <= previous + 1e-12,
                $"Gain rose with frequency at {frequency:0.0} Hz");
            previous = gain;
        }
    }

    [Fact]
    public void Evaluate_ReachesTheNominalSlopeBelowTheKnee()
    {
        // The hatchback preset is the theoretical fully-sealed cabin:
        // 12 dB/oct. Two octaves under the corner the softplus knee is spent,
        // so one more octave down must add almost exactly that.
        CabinTransferFunction cabin =
            CabinTransferFunction.FromBodyStyle(CabinBodyStyle.Hatchback);

        double perOctave = cabin.Evaluate(10.0) - cabin.Evaluate(20.0);
        Assert.InRange(perOctave, 11.8, 12.01);
    }

    [Fact]
    public void Design_SubtractsTheCabinRiseWhenUsedAsACorrection()
    {
        // The audition feeds Evaluate straight into the calibration FIR
        // designer, whose kernel gain is the NEGATED correction: the cabin's
        // +24 dB at 20 Hz must come out as −24 dB of attenuation, and the
        // midband must stay untouched.
        CabinTransferFunction cabin =
            CabinTransferFunction.FromBodyStyle(CabinBodyStyle.Hatchback);
        double[] kernel = CalibrationFirFilter.Design(cabin.Evaluate, Rate);

        Assert.InRange(MagnitudeDbAt(kernel, 20.0), -25.0, -23.0);
        Assert.InRange(MagnitudeDbAt(kernel, 40.0), -13.0, -11.0);
        Assert.InRange(MagnitudeDbAt(kernel, 1_000.0), -0.3, 0.3);
        Assert.InRange(MagnitudeDbAt(kernel, 10_000.0), -0.3, 0.3);
    }

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
}
