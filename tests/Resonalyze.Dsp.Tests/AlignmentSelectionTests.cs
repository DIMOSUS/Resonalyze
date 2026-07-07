namespace Resonalyze.Dsp.Tests;

public sealed class AlignmentSelectionTests
{
    [Fact]
    public void Select_SingleCandidateIsReturned()
    {
        var candidate = new AlignmentCandidate(1.5, false, -0.4);

        AlignmentCandidate chosen = AlignmentSelection.Select([candidate], 0.0);

        Assert.Equal(candidate, chosen);
    }

    [Fact]
    public void Select_RejectsEmptyCandidateList()
    {
        Assert.Throws<ArgumentException>(() =>
            AlignmentSelection.Select(Array.Empty<AlignmentCandidate>(), 0.0));
    }

    [Fact]
    public void Select_PrefersNonInvertedWithinMargin()
    {
        // The inverted impostor wins by less than the invert margin, so the
        // non-inverted candidate must be chosen.
        var inverted = new AlignmentCandidate(2.0, true, -0.50);
        var normal = new AlignmentCandidate(1.4, false, -0.65);

        AlignmentCandidate chosen = AlignmentSelection.Select(
            [inverted, normal], baseDeltaMs: 1.4);

        Assert.Equal(normal, chosen);
    }

    [Fact]
    public void Select_KeepsInvertedWinnerBeyondMargin()
    {
        // A genuinely flipped driver wins by more than the margin and stays.
        var inverted = new AlignmentCandidate(2.0, true, -0.50);
        var normal = new AlignmentCandidate(1.4, false, -0.90);

        AlignmentCandidate chosen = AlignmentSelection.Select(
            [inverted, normal], baseDeltaMs: 1.4);

        Assert.Equal(inverted, chosen);
    }

    [Fact]
    public void Select_BreaksNearTiesByClosenessToBase()
    {
        // Two non-inverted candidates within the tie margin: the one closer to
        // the arrival-based base delta wins even though it scores lower.
        var farBetter = new AlignmentCandidate(3.0, false, -0.50);
        var nearSlightlyWorse = new AlignmentCandidate(1.1, false, -0.55);

        AlignmentCandidate chosen = AlignmentSelection.Select(
            [farBetter, nearSlightlyWorse], baseDeltaMs: 1.0);

        Assert.Equal(nearSlightlyWorse, chosen);
    }

    [Fact]
    public void Select_DoesNotTieBreakAcrossTheMargin()
    {
        // Outside the tie margin the score decides, regardless of distance.
        var farBetter = new AlignmentCandidate(3.0, false, -0.50);
        var nearMuchWorse = new AlignmentCandidate(1.1, false, -0.75);

        AlignmentCandidate chosen = AlignmentSelection.Select(
            [farBetter, nearMuchWorse], baseDeltaMs: 1.0);

        Assert.Equal(farBetter, chosen);
    }

    [Fact]
    public void Select_TieBreakStaysWithinChosenPolarity()
    {
        // The inverted winner keeps its polarity group for the delay tie-break:
        // a near-tied non-inverted candidate beyond the invert margin must not
        // participate.
        var inverted = new AlignmentCandidate(2.0, true, -0.50);
        var invertedFar = new AlignmentCandidate(4.0, true, -0.58);
        var normal = new AlignmentCandidate(1.0, false, -0.90);

        AlignmentCandidate chosen = AlignmentSelection.Select(
            [inverted, invertedFar, normal], baseDeltaMs: 3.8);

        Assert.Equal(invertedFar, chosen);
    }
}
