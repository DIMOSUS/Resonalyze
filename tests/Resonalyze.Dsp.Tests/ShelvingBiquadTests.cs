namespace Resonalyze.Dsp.Tests;

// The defining properties of an RBJ shelf, pinned against the realized biquad
// rather than against a second copy of the same formula: full gain on its own
// side, unity on the other, and exactly half the gain at the stated frequency —
// which is what makes that frequency the middle of the transition.
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
        // Not Nyquist itself: the bilinear transform pins the response there, and a
        // shelf is judged where music lives.
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
        // The reason a shelf's Q is a knee and not a bandwidth: past 1/sqrt(2) the
        // response rises past the shelf before settling on it. A user typing a
        // bell's Q into a shelf has to be able to see that in the curve.
        var band = new PeqBand(500, 3.0, 8, PeqBandType.LowShelf);

        double peak = EqualizationCurve.LogFrequencyGrid(20, 20_000, 400)
            .Max(frequency => MagnitudeDb(band, frequency));

        Assert.True(peak > 9.0, $"a Q of 3 should overshoot 8 dB; it peaked at {peak:0.0}.");
    }

    [Fact]
    public void AnalogPrototypeAgreesWithTheRealizedBiquad()
    {
        // The prototype is what plots and synthetic sources use; it has to be the
        // same filter as the biquad, not merely a similar shape. They part company
        // as the bilinear transform warps toward Nyquist, so this holds where a
        // shelf is judged, not at the very top.
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
                // An absolute tolerance rather than a rounded comparison: the two
                // differ by hundredths of a dB, which a decimal-place assert can
                // still fail on either side of a rounding boundary.
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
        // The conventions restate a bandwidth between half-gain points. A shelf has
        // none, so rescaling its Q by the gain would print a shelf that overshoots
        // where the designed one does not.
        var shelf = new PeqBand(100, 0.7, 12, PeqBandType.LowShelf);

        Assert.Equal(
            shelf.Q,
            PeqQConventions.ToConvention(shelf, PeqQConvention.Symmetric).Q,
            10);
        Assert.Equal(
            shelf.Q,
            PeqQConventions.ToConvention(shelf, PeqQConvention.Classic).Q,
            10);
        // A bell of the same depth is restated, so the test is really testing the type.
        var bell = new PeqBand(100, 0.7, 12);
        Assert.True(PeqQConventions.ToConvention(bell, PeqQConvention.Symmetric).Q > 1.3);
    }
}
