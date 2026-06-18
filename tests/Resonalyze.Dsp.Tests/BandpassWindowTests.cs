namespace Resonalyze.Dsp.Tests;

public sealed class BandpassWindowTests
{
    [Fact]
    public void BandAround_ComputesExpectedEdges()
    {
        (double f1, double f2, double f3, double f4) =
            BandpassWindow.BandAround(
                centerHz: 1_000,
                passOctaves: 1,
                fadeOctaves: 0.5);

        Assert.Equal(500.0, f1, precision: 9);
        Assert.Equal(707.1067811865476, f2, precision: 9);
        Assert.Equal(1414.213562373095, f3, precision: 9);
        Assert.Equal(2000.0, f4, precision: 9);
    }

    [Fact]
    public void Weight_ReturnsExpectedValuesAcrossBand()
    {
        const double f1 = 500;
        const double f2 = 1000;
        const double f3 = 2000;
        const double f4 = 4000;

        Assert.Equal(0.0, BandpassWindow.Weight(400, f1, f2, f3, f4), precision: 12);
        Assert.Equal(0.0, BandpassWindow.Weight(500, f1, f2, f3, f4), precision: 12);
        Assert.Equal(0.5, BandpassWindow.Weight(750, f1, f2, f3, f4), precision: 12);
        Assert.Equal(1.0, BandpassWindow.Weight(1500, f1, f2, f3, f4), precision: 12);
        Assert.Equal(0.5, BandpassWindow.Weight(3000, f1, f2, f3, f4), precision: 12);
        Assert.Equal(0.0, BandpassWindow.Weight(4000, f1, f2, f3, f4), precision: 12);
        Assert.Equal(0.0, BandpassWindow.Weight(4500, f1, f2, f3, f4), precision: 12);
    }

    [Fact]
    public void Create_IsSymmetricForPositiveAndNegativeFrequencies()
    {
        const int fftSize = 1024;
        const double sampleRate = 48_000;
        double[] window = BandpassWindow.Create(
            fftSize,
            sampleRate,
            f1: 800,
            f2: 1_000,
            f3: 2_000,
            f4: 2_400);

        for (int bin = 1; bin < fftSize / 2; bin++)
        {
            Assert.Equal(window[bin], window[fftSize - bin], precision: 12);
        }
    }

    [Fact]
    public void Create_PlateauBinsRemainAtUnity()
    {
        const int fftSize = 2048;
        const double sampleRate = 48_000;
        double[] window = BandpassWindow.Create(
            fftSize,
            sampleRate,
            f1: 900,
            f2: 1_000,
            f3: 2_000,
            f4: 2_200);

        int oneKilohertzBin = (int)Math.Round(1_000 * fftSize / sampleRate);
        int fifteenHundredBin = (int)Math.Round(1_500 * fftSize / sampleRate);
        int twoKilohertzBin = (int)Math.Round(2_000 * fftSize / sampleRate);

        Assert.Equal(1.0, window[oneKilohertzBin], precision: 12);
        Assert.Equal(1.0, window[fifteenHundredBin], precision: 12);
        Assert.Equal(1.0, window[twoKilohertzBin], precision: 12);
    }

    [Fact]
    public void Create_RejectsInvalidBandOrdering()
    {
        Assert.Throws<ArgumentException>(() =>
            BandpassWindow.Create(
                fftSize: 1024,
                sampleRate: 48_000,
                f1: 1000,
                f2: 900,
                f3: 2000,
                f4: 2500));
    }
}
