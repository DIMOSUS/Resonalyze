using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class LowJunctionPolarityTests
{
    private static double Score(AlignmentCandidate candidate) => candidate.LossDb;

    private const int SampleRate = 48_000;

    /// <summary>A crest of <paramref name="amplitude"/> at <paramref name="atMs"/>, plus a weaker one of the
    /// opposite sign, so both branches have something to read.</summary>
    private static Complex[] Crest(double atMs, double amplitude, double oppositeAtMs, double oppositeAmplitude)
    {
        var response = new Complex[4096];
        response[(int)Math.Round(atMs * SampleRate / 1000.0)] = amplitude;
        response[(int)Math.Round(oppositeAtMs * SampleRate / 1000.0)] = oppositeAmplitude;
        return response;
    }

    [Fact]
    public void Read_TheNearerCrestNamesThePolarity()
    {
        Complex[] neighbor = Crest(10.0, 1.0, 30.0, -0.2);
        // Same-sign crest 4 ms away, opposite-sign crest 0.5 ms away: the split sums inverted.
        Complex[] variable = Crest(14.0, 0.9, 10.5, -0.8);

        LowJunctionPolarityVote vote = LowJunctionPolarity.Read(neighbor, variable, SampleRate)!;

        Assert.True(vote.ExpectsRelativeInversion);
        Assert.Equal(-4.0, vote.SameSignShiftMs, 3);
        Assert.Equal(-0.5, vote.OppositeSignShiftMs, 3);
        Assert.Equal(-0.5, vote.ShiftMs, 3);
    }

    [Fact]
    public void Read_ShiftIsTheDelayThatLinesTheVariableUp()
    {
        Complex[] neighbor = Crest(12.0, 1.0, 30.0, -0.2);
        Complex[] variable = Crest(9.0, 0.9, 25.0, -0.3);

        LowJunctionPolarityVote vote = LowJunctionPolarity.Read(neighbor, variable, SampleRate)!;

        Assert.False(vote.ExpectsRelativeInversion);
        Assert.Equal(3.0, vote.ShiftMs, 3);
    }

    [Fact]
    public void Read_JudgesEachMeetingFromWhereTheFrontsPutTheChannel()
    {
        // The channel must move 8 ms: its same-sign crest meets there, the opposite one at 3 ms. Judged from the
        // undelayed position, the 3 ms meeting won and named an inversion the waveforms never asked for.
        Complex[] neighbor = Crest(20.0, 1.0, 40.0, -0.2);
        Complex[] variable = Crest(12.0, 0.9, 17.0, -0.8);

        LowJunctionPolarityVote vote = LowJunctionPolarity.Read(neighbor, variable, SampleRate, anchorMs: 8.0)!;

        Assert.False(vote.ExpectsRelativeInversion);
        Assert.Equal(8.0, vote.ShiftMs, 3);
        Assert.Equal(5.0, vote.SeparationMs, 3);
        Assert.True(LowJunctionPolarity.Read(neighbor, variable, SampleRate)!.ExpectsRelativeInversion);
    }

    [Fact]
    public void Read_ReadsOnlyInsideTheMeasuredRange()
    {
        Complex[] neighbor = Crest(10.0, 1.0, 30.0, -0.2);
        // The tallest same-sign sample sits in the padding; inside the range the crest is at 13 ms.
        Complex[] variable = Crest(13.0, 0.5, 10.4, -0.6);
        variable[3900] = 5.0;

        LowJunctionPolarityVote vote = LowJunctionPolarity.Read(
            neighbor, variable, SampleRate,
            variableRange: new ValidSampleRange(0, 3000))!;

        Assert.Equal(-3.0, vote.SameSignShiftMs, 3);
    }

    [Fact]
    public void Read_NullWhenAChannelHasNoCrest()
    {
        Complex[] neighbor = Crest(10.0, 1.0, 30.0, -0.2);

        Assert.Null(LowJunctionPolarity.Read(neighbor, new Complex[4096], SampleRate));
    }

    [Fact]
    public void IsDecisive_NeedsTheLosingSignAQuarterPeriodFarther()
    {
        // 120 Hz: a quarter period is 2.083 ms.
        var clear = new LowJunctionPolarityVote(false, -0.5, -3.0);
        var tied = new LowJunctionPolarityVote(false, -10.813, -10.833);

        Assert.True(clear.IsDecisive(120));
        Assert.Equal(2.5, clear.SeparationMs, 3);
        Assert.False(tied.IsDecisive(120));
        // The same pair of crests at a lower crossover, where a quarter period is longer still.
        Assert.False(clear.IsDecisive(70));
    }

    [Fact]
    public void Decide_TakesTheExpectedBranchWhenTheScoresTie()
    {
        var chosen = new AlignmentCandidate(0.2, false, -0.2, LossDb: -0.2);
        var expectedBranch = new AlignmentCandidate(-6.1, true, -0.5, LossDb: -0.5);

        AlignmentCandidate decided = LowJunctionPolarity.Decide(
            [chosen, expectedBranch], chosen, Score,
            expectedRelativeInversion: true, anchorMs: 0, neighborInverted: false);

        Assert.Equal(expectedBranch, decided);
    }

    [Fact]
    public void Decide_LeavesABranchTheSummationSeparates()
    {
        var chosen = new AlignmentCandidate(0.2, false, -0.2, LossDb: -0.2);
        var expectedBranch = new AlignmentCandidate(-6.1, true, -1.4, LossDb: -1.4);

        AlignmentCandidate decided = LowJunctionPolarity.Decide(
            [chosen, expectedBranch], chosen, Score,
            expectedRelativeInversion: true, anchorMs: 0, neighborInverted: false);

        Assert.Equal(chosen, decided);
    }

    [Fact]
    public void Decide_PolarityIsRelativeToTheSettledNeighbour()
    {
        // The neighbour is inverted, so "in phase" means the variable is inverted too.
        var chosen = new AlignmentCandidate(0.2, false, -0.2, LossDb: -0.2);
        var inPhaseWithNeighbor = new AlignmentCandidate(-5.9, true, -0.4, LossDb: -0.4);

        AlignmentCandidate decided = LowJunctionPolarity.Decide(
            [chosen, inPhaseWithNeighbor], chosen, Score,
            expectedRelativeInversion: false, anchorMs: 0, neighborInverted: true);

        Assert.Equal(inPhaseWithNeighbor, decided);
    }

    [Fact]
    public void Decide_InertWhenTheChosenAlreadyCarriesTheExpectedPolarity()
    {
        var chosen = new AlignmentCandidate(0.2, false, -0.2, LossDb: -0.2);
        var rival = new AlignmentCandidate(-6.1, true, -0.1, LossDb: -0.1);

        AlignmentCandidate decided = LowJunctionPolarity.Decide(
            [chosen, rival], chosen, Score,
            expectedRelativeInversion: false, anchorMs: 0, neighborInverted: false);

        Assert.Equal(chosen, decided);
    }

    [Fact]
    public void Decide_KeepsThePickWhenTheExpectedBranchHoldsNoCandidate()
    {
        var chosen = new AlignmentCandidate(0.2, false, -0.2, LossDb: -0.2);

        AlignmentCandidate decided = LowJunctionPolarity.Decide(
            [chosen], chosen, Score,
            expectedRelativeInversion: true, anchorMs: 0, neighborInverted: false);

        Assert.Equal(chosen, decided);
    }

    [Fact]
    public void Decide_PicksTheAnchorNearestLobeOfTheExpectedBranch()
    {
        var chosen = new AlignmentCandidate(0.2, false, -0.2, LossDb: -0.2);
        var near = new AlignmentCandidate(-6.1, true, -0.45, LossDb: -0.45);
        var far = new AlignmentCandidate(7.4, true, -0.4, LossDb: -0.4);

        AlignmentCandidate decided = LowJunctionPolarity.Decide(
            [chosen, far, near], chosen, Score,
            expectedRelativeInversion: true, anchorMs: -6.0, neighborInverted: false);

        Assert.Equal(near, decided);
    }

    [Fact]
    public void Decide_OnlyHandsTheVoteToALobeTheSummationTied()
    {
        // Two lobes of the voted branch: one the acoustics tie with the pick, one they separate by 1.3 dB but whose
        // prior-laden score ranks first. The tie-breaks read that score, so the separated lobe must never reach them.
        var chosen = new AlignmentCandidate(0.2, false, -0.2, LossDb: -0.2);
        var tied = new AlignmentCandidate(-6.1, true, -0.6, LossDb: -0.4);
        var separated = new AlignmentCandidate(-5.9, true, -0.3, LossDb: -1.5);

        AlignmentCandidate decided = LowJunctionPolarity.Decide(
            [chosen, separated, tied], chosen, Score,
            expectedRelativeInversion: true, anchorMs: -6.0, neighborInverted: false);

        Assert.Equal(tied, decided);
    }
}
