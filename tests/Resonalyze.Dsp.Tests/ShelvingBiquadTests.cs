namespace Resonalyze.Dsp.Tests;

// RBJ shelf properties against the realized biquad: full gain one side, unity the other, half gain at the stated frequency.
public sealed class ShelvingBiquadTests
{
    private const double SampleRate = 48_000;

    private static double MagnitudeDb(PeqBand band, double frequencyHz) =>
        DigitalEqualizationResponse.MagnitudeDbAt(band, frequencyHz, SampleRate);

    [Theory]
    [InlineData(6.0)]
    [InlineData(-9.0)]
    public void LowShelf_LiftsBelowAndLeavesAboveAlone(double gainDb)
    {
        var band = new PeqBand(200, 0.7, gainDb, PeqBandType.LowShelf);

        Assert.Equal(gainDb, MagnitudeDb(band, 5), 1);
        Assert.Equal(gainDb / 2, MagnitudeDb(band, 200), 2);
        Assert.Equal(0, MagnitudeDb(band, 10_000), 1);
    }

    [Theory]
    [InlineData(6.0)]
    [InlineData(-9.0)]
    public void HighShelf_IsTheMirrorImage(double gainDb)
    {
        var band = new PeqBand(4_000, 0.7, gainDb, PeqBandType.HighShelf);

        Assert.Equal(0, MagnitudeDb(band, 40), 1);
        Assert.Equal(gainDb / 2, MagnitudeDb(band, 4_000), 2);
        // Not Nyquist: the bilinear transform pins the response there.
        Assert.Equal(gainDb, MagnitudeDb(band, 18_000), 1);
    }

    [Fact]
    public void ShelfQ_BelowTheMonotonicLimitDoesNotOvershoot()
    {
        var band = new PeqBand(500, 0.7071, 8, PeqBandType.LowShelf);

        double previous = MagnitudeDb(band, 20_000);
        foreach (double frequency in EqualizationCurve.LogFrequencyGrid(20, 20_000, 400).Reverse())
        {
            double current = MagnitudeDb(band, frequency);
            Assert.True(
                current >= previous - 1e-9,
                $"the shelf dips at {frequency:0} Hz: {current:0.000} after {previous:0.000} dB");
            previous = current;
        }

        Assert.True(previous <= 8 + 1e-6, "the shelf overshot its own gain.");
    }

    [Fact]
    public void ShelfQ_AboveTheMonotonicLimitOvershoots()
    {
        // Past Q 1/sqrt(2) a shelf overshoots before settling; a user must see that.
        var band = new PeqBand(500, 3.0, 8, PeqBandType.LowShelf);

        double peak = EqualizationCurve.LogFrequencyGrid(20, 20_000, 400)
            .Max(frequency => MagnitudeDb(band, frequency));

        Assert.True(peak > 9.0, $"a Q of 3 should overshoot 8 dB; it peaked at {peak:0.0}.");
    }

    [Fact]
    public void AnalogPrototypeAgreesWithTheRealizedBiquad()
    {
        // The analog prototype must equal the biquad where a shelf is judged, not near Nyquist.
        var shelves = new[]
        {
            new PeqBand(120, 0.7, 6, PeqBandType.LowShelf),
            new PeqBand(3_000, 1.2, -8, PeqBandType.HighShelf)
        };

        foreach (PeqBand band in shelves)
        {
            foreach (double frequency in EqualizationCurve.LogFrequencyGrid(20, 8_000, 60))
            {
                double analog = band.MagnitudeDbAt(frequency);
                double digital = MagnitudeDb(band, frequency);
                Assert.True(
                    Math.Abs(analog - digital) < 0.15,
                    $"{band.Type} at {frequency:0} Hz: prototype {analog:0.000} dB, " +
                    $"biquad {digital:0.000} dB");
            }
        }
    }

    [Fact]
    public void ATransparentShelfIsFlat()
    {
        var band = new PeqBand(1_000, 0.7, 0, PeqBandType.LowShelf);

        Assert.True(band.IsTransparent);
        Assert.Equal(0, MagnitudeDb(band, 100), 6);
        Assert.Equal(0, MagnitudeDb(band, 10_000), 6);
    }

    [Fact]
    public void ComputeRejectsAPeakingBand()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ShelvingBiquad.Compute(new PeqBand(1_000, 1, 6), SampleRate));
    }

    [Fact]
    public void QConventions_LeaveAShelfAlone()
    {
        // A shelf has no half-gain bandwidth: rescaling its Q would print an overshoot.
        var shelf = new PeqBand(100, 0.7, 12, PeqBandType.LowShelf);

        Assert.Equal(
            shelf.Q,
            PeqQConventions.ToConvention(shelf, PeqQConvention.Symmetric).Q,
            10);
        Assert.Equal(
            shelf.Q,
            PeqQConventions.ToConvention(shelf, PeqQConvention.Classic).Q,
            10);
        var bell = new PeqBand(100, 0.7, 12);
        Assert.True(PeqQConventions.ToConvention(bell, PeqQConvention.Symmetric).Q > 1.3);
    }
}
