using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class JunctionPhaseLobeTests
{
    private const double HalfPeriodMs = 0.3;
    private const double StepMs = 0.0125;

    // Triangular lobes a quarter period wide at the foot: positive heights are in-phase lobes, negative ones inverted.
    private static List<SignalPoint> Lobes(params (double CentreMs, double Height)[] lobes)
    {
        var curve = new List<SignalPoint>();
        for (double delayMs = -1.5; delayMs <= 1.5 + 1e-9; delayMs += StepMs)
        {
            double score = 0;
            foreach ((double centreMs, double height) in lobes)
            {
                score += height * Math.Max(0, 1 - (Math.Abs(delayMs - centreMs) / (0.5 * HalfPeriodMs)));
            }

            curve.Add(new SignalPoint(Math.Round(delayMs, 4), score));
        }

        return curve;
    }

    private static AlignmentCandidate At(double delayMs, bool invert = false) => new(delayMs, invert, ScoreDb: 0);

    [Fact]
    public void Read_NamesTheLobeTheBandComesIntoPhaseAt_AndWhereItPeaks()
    {
        // The sum stands a period late on a weaker lobe and offers an optimum 0.05 ms off the strong lobe's peak.
        List<SignalPoint> curve = Lobes((-0.2, 0.93), (0.4, 0.78));
        AlignmentCandidate chosen = At(0.36);
        AlignmentCandidate near = At(-0.15);

        PhaseLobeReading reading = JunctionPhaseLobe.Read(curve, [near], chosen, HalfPeriodMs, expectedInvert: false)!;

        Assert.True(reading.NamesALobe);
        Assert.Same(near, reading.Pick);
        Assert.Equal(-0.2, reading.PeakDelayMs, 3);
    }

    [Fact]
    public void Read_AnotherOptimumOfThePicksOwnLobe_MovesOnlyTheDelay()
    {
        List<SignalPoint> curve = Lobes((0.0, 0.9));
        AlignmentCandidate chosen = At(0.05);

        PhaseLobeReading reading = JunctionPhaseLobe.Read(curve, [At(-0.03)], chosen, HalfPeriodMs, expectedInvert: false)!;

        Assert.Same(chosen, reading.Pick);
        Assert.Equal(0.0, reading.PeakDelayMs, 3);
    }

    [Theory]
    // The pick stands in phase, the half-period twin scores higher: the cells of the far sides in the field.
    [InlineData(false, 0.86, 0.90, false)]
    [InlineData(false, 0.84, 0.91, false)]
    // The pick stands inverted and the phase does not plainly back it: back to the expected relation.
    [InlineData(true, 0.94, 0.93, false)]
    [InlineData(true, 0.90, 0.94, false)]
    // The pick stands inverted and the phase plainly backs it.
    [InlineData(true, 0.86, 0.95, true)]
    public void Read_KeepsTheExpectedRelation_UnlessThePickLeftItAndThePhasePlainlyAgrees(
        bool chosenInverted, double inPhaseScore, double invertedScore, bool expectInverted)
    {
        List<SignalPoint> curve = Lobes((0.0, inPhaseScore), (HalfPeriodMs, -invertedScore));
        AlignmentCandidate inPhase = At(0.0);
        AlignmentCandidate inverted = At(HalfPeriodMs, invert: true);

        PhaseLobeReading reading = JunctionPhaseLobe.Read(
            curve, [inPhase, inverted], chosenInverted ? inverted : inPhase, HalfPeriodMs, expectedInvert: false)!;

        Assert.Equal(expectInverted, reading.Pick.InvertPolarity);
    }

    [Fact]
    public void Read_WhereTheFiltersExpectInversion_TheInvertedRelationIsTheOneKept()
    {
        List<SignalPoint> curve = Lobes((0.0, 0.92), (HalfPeriodMs, -0.86));
        AlignmentCandidate inverted = At(HalfPeriodMs, invert: true);

        PhaseLobeReading reading = JunctionPhaseLobe.Read(
            curve, [At(0.0)], inverted, HalfPeriodMs, expectedInvert: true)!;

        Assert.Same(inverted, reading.Pick);
    }

    [Fact]
    public void Read_WhereNoLagBringsTheBandIntoPhase_NamesNoLobe()
    {
        List<SignalPoint> curve = Lobes((-0.5, 0.68), (0.1, 0.53));

        PhaseLobeReading reading = JunctionPhaseLobe.Read(curve, [At(-0.5)], At(0.1), HalfPeriodMs, expectedInvert: false)!;

        Assert.False(reading.NamesALobe);
    }

    [Fact]
    public void PeakOf_ASlopeAcrossTheReach_KeepsTheCandidatesOwnDelayAndTheScoreThere()
    {
        // The lobe peaks beyond the half period the candidate may claim; only its foot reaches in.
        List<SignalPoint> curve = Lobes((0.1, 0.2), (0.45, 0.9));

        (double delayMs, double score) = JunctionPhaseLobe.PeakOf(curve, At(0.1), HalfPeriodMs);

        Assert.Equal(0.1, delayMs);
        Assert.Equal(0.2, score, 3);
    }

    [Theory]
    [InlineData(true, 0.5)]
    [InlineData(false, -0.5)]
    public void Curve_PeaksAtTheDelayThatBringsTheSearchedChannelToItsNeighbour(
        bool searchedIsLower, double expectedMs)
    {
        // The upper channel arrives 0.5 ms after the lower one.
        const int SampleRate = 48_000;
        PlacementChannel lower = Impulse(480);
        PlacementChannel upper = Impulse(504);

        List<SignalPoint> curve = JunctionPhaseLobe.Curve(
            searchedIsLower ? lower : upper,
            searchedIsLower ? upper : lower,
            searchedIsLower, SampleRate, SampleRate,
            crossoverHz: 1_500, bandLowHz: 750, bandHighHz: 3_000, rangeMs: 1.0)!;

        SignalPoint peak = curve.MaxBy(point => point.Y);
        Assert.Equal(expectedMs, peak.X, 2);
        Assert.InRange(peak.Y, 0.99, 1.0);
    }

    private static PlacementChannel Impulse(int position)
    {
        var response = new Complex[8_192];
        response[position] = Complex.One;
        return new PlacementChannel(response, position, default);
    }
}
