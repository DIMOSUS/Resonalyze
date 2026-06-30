namespace Resonalyze.Dsp.Tests;

public sealed class EqualizationCurveTests
{
    [Theory]
    [InlineData(6.0)]
    [InlineData(-6.0)]
    [InlineData(12.0)]
    public void PeqBand_AtCentreFrequency_EqualsBandGain(double gainDb)
    {
        var band = new PeqBand(1_000, 1.0, gainDb);

        Assert.Equal(gainDb, band.MagnitudeDbAt(1_000), 9);
    }

    [Fact]
    public void PeqBand_FarFromCentre_ApproachesZero()
    {
        var band = new PeqBand(1_000, 4.0, 12.0);

        Assert.Equal(0.0, band.MagnitudeDbAt(20), 2);
        Assert.Equal(0.0, band.MagnitudeDbAt(20_000), 2);
    }

    [Fact]
    public void PeqBand_IsSymmetricInLogFrequency()
    {
        // A peaking band is symmetric about its centre on a log frequency axis:
        // an octave below reads the same as an octave above.
        var band = new PeqBand(1_000, 2.0, 8.0);

        Assert.Equal(
            band.MagnitudeDbAt(500),
            band.MagnitudeDbAt(2_000),
            9);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.0, 0.0)]
    public void PeqBand_TransparentOrDegenerate_ReturnsZero(
        double gainDb,
        double q = 1.0)
    {
        var band = new PeqBand(1_000, q, gainDb);

        Assert.Equal(0.0, band.MagnitudeDbAt(1_000), 12);
    }

    [Fact]
    public void PeqBand_NonPositiveQ_ReturnsZero()
    {
        var band = new PeqBand(1_000, 0.0, 6.0);

        Assert.Equal(0.0, band.MagnitudeDbAt(1_000), 12);
    }

    [Fact]
    public void Curve_SumsBandsAndPreampInDecibels()
    {
        var bands = new[]
        {
            new PeqBand(1_000, 1.0, 6.0),
            new PeqBand(1_000, 1.0, -2.0)
        };
        var curve = new EqualizationCurve(bands, preampDb: 1.5);

        // At 1 kHz both bands hit their centre gain, so the total is
        // preamp + 6 + (-2) = 5.5 dB.
        Assert.Equal(5.5, curve.MagnitudeDbAt(1_000), 9);
    }

    [Fact]
    public void Curve_NoBands_ReturnsPreamp()
    {
        var curve = new EqualizationCurve(Array.Empty<PeqBand>(), preampDb: -3.0);

        Assert.Equal(-3.0, curve.MagnitudeDbAt(500), 12);
        Assert.Equal(-3.0, curve.MagnitudeDbAt(5_000), 12);
    }

    [Fact]
    public void Curve_Sample_ReturnsPointPerFrequency()
    {
        var curve = new EqualizationCurve(new[] { new PeqBand(1_000, 1.0, 6.0) });
        double[] grid = { 100, 1_000, 10_000 };

        IReadOnlyList<SignalPoint> points = curve.Sample(grid);

        Assert.Equal(3, points.Count);
        Assert.Equal(1_000, points[1].X, 9);
        Assert.Equal(6.0, points[1].Y, 9);
    }

    [Fact]
    public void Curve_RejectsTooManyBands()
    {
        PeqBand[] bands = Enumerable
            .Range(0, EqualizationCurve.MaxBandCount + 1)
            .Select(_ => new PeqBand(1_000, 1.0, 1.0))
            .ToArray();

        Assert.Throws<ArgumentException>(() => new EqualizationCurve(bands));
    }

    [Fact]
    public void LogFrequencyGrid_SpansRangeLogarithmically()
    {
        IReadOnlyList<double> grid = EqualizationCurve.LogFrequencyGrid(20, 20_000, 5);

        Assert.Equal(5, grid.Count);
        Assert.Equal(20, grid[0], 6);
        Assert.Equal(20_000, grid[^1], 6);
        // Midpoint of a log grid from 20 to 20000 is the geometric mean = 632.45 Hz.
        Assert.Equal(Math.Sqrt(20.0 * 20_000.0), grid[2], 6);
    }

    [Theory]
    [InlineData(0, 100, 5)]
    [InlineData(100, 50, 5)]
    [InlineData(20, 20_000, 1)]
    public void LogFrequencyGrid_RejectsInvalidArguments(
        double minHz,
        double maxHz,
        int count)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => EqualizationCurve.LogFrequencyGrid(minHz, maxHz, count));
    }
}
