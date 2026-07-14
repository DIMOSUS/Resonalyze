namespace Resonalyze.Dsp.Tests;

public sealed class DigitalEqualizationResponseTests
{
    [Fact]
    public void MagnitudeDbAt_MatchesVirtualDspChannelChain()
    {
        var curve = new EqualizationCurve(
            [
                new PeqBand(1_000, 1.4, 5),
                new PeqBand(17_000, 3.0, -7)
            ],
            preampDb: -2);
        var chain = new DspChannelChain(Peq: curve);

        foreach (double frequency in new[] { 100.0, 1_000.0, 10_000.0, 17_000.0, 20_000.0 })
        {
            double expected = 20 * Math.Log10(chain.Response(frequency, 48_000).Magnitude);
            double actual = DigitalEqualizationResponse.MagnitudeDbAt(
                curve, frequency, 48_000);
            Assert.Equal(expected, actual, 9);
        }
    }

    [Fact]
    public void ResponseNearNyquist_DependsOnSampleRate()
    {
        var curve = new EqualizationCurve([new PeqBand(18_000, 2.0, 6)]);

        double at44K = DigitalEqualizationResponse.MagnitudeDbAt(
            curve, 15_000, 44_100);
        double at96K = DigitalEqualizationResponse.MagnitudeDbAt(
            curve, 15_000, 96_000);

        Assert.True(Math.Abs(at44K - at96K) > 0.5);
    }
}
