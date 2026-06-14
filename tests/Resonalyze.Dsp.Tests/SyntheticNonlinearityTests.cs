using System.Numerics;
using OxyPlot;

namespace Resonalyze.Dsp.Tests;

public sealed class SyntheticNonlinearityTests
{
    private const int SampleRate = 40_960;
    private const int TransformLength = 4096;
    private const int FundamentalBin = 100;

    [Fact]
    public void QuadraticNonlinearity_ProducesExpectedSecondHarmonic()
    {
        const double amplitude = 0.5;
        const double quadraticCoefficient = 0.4;
        var response = new Complex[TransformLength];

        for (int sample = 0; sample < response.Length; sample++)
        {
            double phase = Math.Tau * FundamentalBin * sample / TransformLength;
            double input = amplitude * Math.Sin(phase);
            double output = input + quadraticCoefficient * input * input;
            response[sample] = new Complex(output, 0);
        }

        var measurement = new SyntheticMeasurement(response, SampleRate, maxMagnitudeIndex: 0);
        List<DataPoint> spectrum = DataHelper.GetSpectrumData(
            measurement,
            start: 0,
            length: TransformLength);

        double fundamentalFrequency = FundamentalBin * SampleRate / (double)TransformLength;
        double secondHarmonicFrequency = 2 * fundamentalFrequency;
        double fundamentalDb = FindBin(spectrum, fundamentalFrequency).Y;
        double secondHarmonicDb = FindBin(spectrum, secondHarmonicFrequency).Y;
        double measuredSecondHarmonicDbc = secondHarmonicDb - fundamentalDb;
        double expectedRatio = quadraticCoefficient * amplitude / 2.0;
        double expectedSecondHarmonicDbc = DataHelper.AmplitudeToDecibels(expectedRatio);

        Assert.InRange(
            measuredSecondHarmonicDbc,
            expectedSecondHarmonicDbc - 1e-9,
            expectedSecondHarmonicDbc + 1e-9);
    }

    private static DataPoint FindBin(IEnumerable<DataPoint> spectrum, double frequency) =>
        spectrum.Single(point => Math.Abs(point.X - frequency) < 1e-9);
}
