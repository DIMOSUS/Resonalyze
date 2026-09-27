using Resonalyze.Dsp;
using Xunit;

namespace Resonalyze.Dsp.Tests;

public class DspChainPeakTests
{
    // An RBJ peaking section reaches exactly its gain at its (prewarped) centre, so gain + band gain is the truth.
    [Theory]
    [InlineData(1_234.5, 12.0, 6.0, -2.0)]
    [InlineData(47.3, 20.0, 3.0, 0.0)]
    [InlineData(15_110.0, 8.0, 9.0, -9.5)]
    public void Find_ReadsANarrowBellAtItsCentre(double centreHz, double q, double bandDb, double gainDb)
    {
        var chain = new DspChannelChain(
            GainDb: gainDb,
            Peq: new EqualizationCurve(new[] { new PeqBand(centreHz, q, bandDb) }));

        (double frequencyHz, double peakDb) =
            DspChainPeak.Find(PreparedDspResponse.Create(chain, 48_000), 20, 20_000);

        Assert.Equal(gainDb + bandDb, peakDb, 3);
        Assert.InRange(frequencyHz / centreHz, 0.999, 1.001);
    }

    // Centres stepped across 1/12 octave: whatever the search's grid, some of them fall between its points.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Find_PrefersANarrowPeakThatTopsABroadOne(int step)
    {
        double narrowHz = 3_000 * Math.Pow(2, step / 96.0);
        var chain = new DspChannelChain(Peq: new EqualizationCurve(new[]
        {
            new PeqBand(90, 0.5, 4.8),
            new PeqBand(narrowHz, 60, 5.0)
        }));

        (double frequencyHz, double peakDb) =
            DspChainPeak.Find(PreparedDspResponse.Create(chain, 48_000), 20, 20_000);

        Assert.InRange(frequencyHz / narrowHz, 0.999, 1.001);
        Assert.True(peakDb >= 5.0, $"peak {peakDb:0.000} dB");
    }

    [Fact]
    public void Find_AMonotonicLowPassPeaksAtTheBandEdge()
    {
        var chain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            LowPassEdge: new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24)));

        (double frequencyHz, double peakDb) =
            DspChainPeak.Find(PreparedDspResponse.Create(chain, 48_000), 20, 20_000);

        // LR24: |H| = 1 / (1 + (f/fc)^4).
        Assert.Equal(-20 * Math.Log10(1 + Math.Pow(20.0 / 80.0, 4)), peakDb, 3);
        Assert.Equal(20, frequencyHz, 0.01);
    }
}
