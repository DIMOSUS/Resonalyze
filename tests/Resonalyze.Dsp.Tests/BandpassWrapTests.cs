namespace Resonalyze.Dsp.Tests;

/// <summary>Filtering a cut of a longer record must not wrap its tail onto its head (the arrival search would read it as a
/// front). Power-of-two lengths need padding too: ChainValidRange hands out exact record-length crops.</summary>
public sealed class BandpassWrapTests
{
    private const int SampleRate = 48_000;

    // The kernel is longest where the band is lowest.
    private const double LowHz = 27.5;
    private const double HighHz = 110.0;

    [Theory]
    [InlineData(32_768)]
    [InlineData(65_536)]
    public void BandLimitedRead_DoesNotWrapTheTailOntoTheHead(int length)
    {
        var impulseResponse = new System.Numerics.Complex[length];
        impulseResponse[length - 64] = 1.0;

        TimeAlignmentAnalysisResult result =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                impulseResponse,
                SampleRate,
                LowHz,
                HighHz,
                new ValidSampleRange(0, length));

        double[] envelope = result.EnvelopeSamples;
        Assert.NotEmpty(envelope);
        double peak = envelope.Max();
        Assert.True(peak > 0, "the band carried no energy at all");
        int head = 40 * SampleRate / 1000;
        double headPeak = envelope.Take(head).Max();
        Assert.True(
            headPeak < peak * 0.01,
            $"the head holds {headPeak / peak * 100:0.0}% of the peak — the tail " +
            "wrapped around the transform");
    }
}
