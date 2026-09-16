namespace Resonalyze.Dsp.Tests;

public sealed class DirectLobeWitnessTests
{
    private const double HalfPeriodMs = 2.5;

    /// <summary>A correlation comb: crests of <paramref name="peaks"/> at their lags, alternating sign every half period.</summary>
    private static List<SignalPoint> Comb(params (double LagMs, double R)[] peaks)
    {
        var curve = new List<SignalPoint>();
        for (double lag = -12; lag <= 12.0001; lag += 0.05)
        {
            double value = 0;
            foreach ((double peakLag, double r) in peaks)
            {
                double distance = Math.Abs(lag - peakLag);
                if (distance < HalfPeriodMs)
                {
                    value += r * (1 - distance / HalfPeriodMs);
                }
            }

            curve.Add(new SignalPoint(lag, value));
        }

        return curve;
    }

    [Fact]
    public void CoherenceOf_ReadsACandidatesOwnLobeAndItsPolarity()
    {
        List<SignalPoint> curve = Comb((0.0, 0.9), (2.5, -0.8));
        var upright = new AlignmentCandidate(0.0, false, -0.2);
        var inverted = new AlignmentCandidate(2.5, true, -0.3);

        Assert.Equal(0.9, DirectLobeWitness.CoherenceOf(curve, upright, HalfPeriodMs), 2);
        Assert.Equal(0.8, DirectLobeWitness.CoherenceOf(curve, inverted, HalfPeriodMs), 2);
        // Standing on the inverted crest without inverting attains nothing of it.
        Assert.True(
            DirectLobeWitness.CoherenceOf(curve, upright with { DelayMs = 2.5 }, HalfPeriodMs) < 0.2);
    }

    [Fact]
    public void Overturns_MovesThePickToTheLobeTheWavefrontsWant()
    {
        List<SignalPoint> curve = Comb((0.0, 0.2), (5.0, 0.9));
        var chosen = new AlignmentCandidate(0.0, false, -0.2, LossDb: -0.2);
        var rival = new AlignmentCandidate(5.0, false, -0.9, LossDb: -0.9);

        DirectLobeReading reading =
            DirectLobeWitness.Read(curve, [chosen, rival], chosen, HalfPeriodMs)!;

        Assert.True(DirectLobeWitness.Overturns(reading, chosen));
        Assert.Equal(rival, reading.Candidate);
    }

    [Fact]
    public void Overturns_LeavesNeighbouringLobesTheCombCannotSeparate()
    {
        // 0.12 apart: above the tie arbitration's resolution, far below a gulf.
        List<SignalPoint> curve = Comb((0.0, 0.78), (5.0, 0.90));
        var chosen = new AlignmentCandidate(0.0, false, -0.2, LossDb: -0.2);
        var rival = new AlignmentCandidate(5.0, false, -0.9, LossDb: -0.9);

        DirectLobeReading reading =
            DirectLobeWitness.Read(curve, [chosen, rival], chosen, HalfPeriodMs)!;

        Assert.False(DirectLobeWitness.Overturns(reading, chosen));
    }

    [Fact]
    public void Overturns_NeedsTheRivalToBeCoherentAtAll()
    {
        // The rival wins by a gulf but never reaches the coherence floor.
        List<SignalPoint> curve = Comb((0.0, 0.05), (5.0, 0.45));
        var chosen = new AlignmentCandidate(0.0, false, -0.2, LossDb: -0.2);
        var rival = new AlignmentCandidate(5.0, false, -0.9, LossDb: -0.9);

        DirectLobeReading reading =
            DirectLobeWitness.Read(curve, [chosen, rival], chosen, HalfPeriodMs)!;

        Assert.False(DirectLobeWitness.Overturns(reading, chosen));
    }

    [Fact]
    public void NamesAnUnreachedLobe_ReportsACrestNoCandidateStandsOn()
    {
        List<SignalPoint> curve = Comb((0.0, 0.5), (7.5, 0.95));
        var chosen = new AlignmentCandidate(0.0, false, -0.2, LossDb: -0.2);

        DirectLobeReading reading =
            DirectLobeWitness.Read(curve, [chosen], chosen, HalfPeriodMs)!;

        Assert.True(DirectLobeWitness.NamesAnUnreachedLobe(reading, chosen, HalfPeriodMs));
        Assert.Equal(7.5, reading.BestLagMs, 1);
        Assert.Equal(0.95, reading.BestR, 2);
    }

    [Fact]
    public void NamesAnUnreachedLobe_SilentWhereTheCrestIsTheChosenLobe()
    {
        List<SignalPoint> curve = Comb((0.0, 0.95));
        var chosen = new AlignmentCandidate(0.0, false, -0.2, LossDb: -0.2);

        DirectLobeReading reading =
            DirectLobeWitness.Read(curve, [chosen], chosen, HalfPeriodMs)!;

        Assert.False(DirectLobeWitness.NamesAnUnreachedLobe(reading, chosen, HalfPeriodMs));
    }

    [Fact]
    public void NamesAnUnreachedLobe_SilentWhereACandidateAlreadyHoldsTheCrest()
    {
        List<SignalPoint> curve = Comb((0.0, 0.5), (7.5, 0.95));
        var chosen = new AlignmentCandidate(0.0, false, -0.2, LossDb: -0.2);
        var reachable = new AlignmentCandidate(7.5, false, -0.9, LossDb: -0.9);

        DirectLobeReading reading =
            DirectLobeWitness.Read(curve, [chosen, reachable], chosen, HalfPeriodMs)!;

        Assert.False(DirectLobeWitness.NamesAnUnreachedLobe(reading, chosen, HalfPeriodMs));
        Assert.True(DirectLobeWitness.Overturns(reading, chosen));
    }

    [Fact]
    public void Read_NullOnAnEmptyCurve()
    {
        var chosen = new AlignmentCandidate(0.0, false, -0.2);

        Assert.Null(DirectLobeWitness.Read([], [chosen], chosen, HalfPeriodMs));
    }
}
