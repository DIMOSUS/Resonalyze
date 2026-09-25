namespace Resonalyze.App.Tests;

public sealed class LiveSequenceContinuityTests
{
    [Fact]
    public void ConsecutiveSequencesJoin()
    {
        Assert.False(LiveSequenceContinuity.Breaks(null, 1, 0, 0));
        Assert.False(LiveSequenceContinuity.Breaks(4, 5, 2, 2));
    }

    // B read, C dropped while the reader was between B and its next check, D next: the gap is after B, not before it.
    [Fact]
    public void ADroppedSequenceBreaksAtTheSequenceAfterIt()
    {
        Assert.False(LiveSequenceContinuity.Breaks(1, 2, 0, 0));
        Assert.True(LiveSequenceContinuity.Breaks(2, 4, 0, 0));
    }

    [Fact]
    public void ADeviceDiscontinuityBreaksAtTheFirstSequenceStampedAfterIt()
    {
        Assert.False(LiveSequenceContinuity.Breaks(6, 7, 3, 3));
        Assert.True(LiveSequenceContinuity.Breaks(7, 8, 3, 4));
    }
}
