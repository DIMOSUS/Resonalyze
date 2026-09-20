namespace Resonalyze.Dsp.Tests;

public sealed class MagnitudeSlopeFitTests
{
    [Fact]
    public void AKnownFall_ReadsBackAsItsOwnSlope()
    {
        // Twelve decibels per octave, sampled across three octaves.
        var fit = new MagnitudeSlopeFit();
        for (double hz = 100; hz <= 800; hz *= Math.Pow(2, 1.0 / 12.0))
        {
            fit.Add(hz, -12 * Math.Log2(hz / 100));
        }

        Assert.Equal(-12, fit.DbPerOctave!.Value, 1e-9);
        Assert.Equal(3, fit.SpanOctaves, 0.1);
    }

    [Fact]
    public void TooFewPointsOrTooNarrowASpan_AnswerNothing_RatherThanFlat()
    {
        // A line through two bins is not a measurement, and one fitted across a twelfth of an octave reads the room.
        var sparse = new MagnitudeSlopeFit();
        sparse.Add(100, 0);
        sparse.Add(200, -12);
        Assert.Null(sparse.DbPerOctave);
        Assert.Equal(2, sparse.Count);

        var narrow = new MagnitudeSlopeFit();
        for (double hz = 1_000; hz <= 1_050; hz += 10)
        {
            narrow.Add(hz, -0.5);
        }

        Assert.True(narrow.Count >= MagnitudeSlopeFit.MinimumPoints);
        Assert.True(narrow.SpanOctaves < MagnitudeSlopeFit.MinimumSpanOctaves);
        Assert.Null(narrow.DbPerOctave);
    }

    [Fact]
    public void AnUnusablePoint_IsSkipped_AndWeightDecidesWhichBandTheLineFollows()
    {
        var fit = new MagnitudeSlopeFit();
        fit.Add(double.NaN, -3);
        fit.Add(0, -3);
        fit.Add(1_000, double.NaN);
        fit.Add(1_000, -3, weight: 0);
        Assert.Equal(0, fit.Count);
        Assert.Null(fit.DbPerOctave);

        // Two straight segments of different slope: the heavily weighted one is the answer.
        var weighted = new MagnitudeSlopeFit();
        for (double hz = 100; hz <= 400; hz *= Math.Pow(2, 1.0 / 12.0))
        {
            weighted.Add(hz, -6 * Math.Log2(hz / 100), weight: 100);
        }
        for (double hz = 400; hz <= 1_600; hz *= Math.Pow(2, 1.0 / 12.0))
        {
            weighted.Add(hz, -12 - 24 * Math.Log2(hz / 400), weight: 0.01);
        }

        Assert.InRange(weighted.DbPerOctave!.Value, -8, -6);
    }
}
