namespace Resonalyze.App.Tests;

// The trust boundary of the REW text import. The format cannot state the timing offset
// REW was running with — the header of a measurement taken with one is word for word
// the header of one taken without — so the import asks, and the answer decides whether
// the arrival is a delay or merely a number. These are the four outcomes of that answer.
public sealed class RewImportTimingTests
{
    private const int SampleRate = 96000;

    [Fact]
    public void AnUnknownOffsetImportsTheShapeAndClaimsNothingAboutTime()
    {
        // "I do not know" is an answer, not a failure. Nothing is compensated, the
        // export keeps its own reference, and the result is stamped RecordedSweep —
        // where the position is explicitly not comparable with anything else.
        Assert.True(RewImportTiming.TryResolve(
            statedOffsetSeconds: null,
            timeZeroIndex: 96127.0,
            peakIndex: 96000,
            sampleCount: 262144,
            sampleRate: SampleRate,
            out RewImportTimingPlan? plan,
            out string? problem));

        Assert.Null(problem);
        Assert.NotNull(plan);
        Assert.Equal(TimingReference.RecordedSweep, plan!.Reference);
        Assert.Equal(96127.0, plan.ReferenceIndex);
        Assert.Equal(0, plan.OffsetSeconds);

        // The arrival is reported as the file implies it — negative here, which is the
        // shadow of the offset. Under RecordedSweep that is not a lie about the world,
        // only a number nobody is allowed to compare.
        Assert.Equal(-127.0, plan.ArrivalSamples);
    }

    [Fact]
    public void AStatedOffsetIsTakenBackOutAndTrusted()
    {
        // The real measurement behind this: m-L exported with a 4 ms offset at 96 kHz.
        // 4 ms is 384 whole samples, t = 0 sits at 96127 and the peak at 96000, so the
        // file alone reads as an arrival of -1.32 ms — REW's own API says the same.
        // Taking the offset out moves the reference to 95743 and the arrival to +257.
        Assert.True(RewImportTiming.TryResolve(
            statedOffsetSeconds: 0.004,
            timeZeroIndex: 96127.0,
            peakIndex: 96000,
            sampleCount: 262144,
            sampleRate: SampleRate,
            out RewImportTimingPlan? plan,
            out string? problem));

        Assert.Null(problem);
        Assert.Equal(TimingReference.SynchronizedLoopback, plan!.Reference);
        Assert.Equal(95743.0, plan.ReferenceIndex);
        Assert.Equal(257.0, plan.ArrivalSamples);
        Assert.Equal(0.004, plan.OffsetSeconds);
    }

    [Fact]
    public void ZeroIsAnAssertionLikeAnyOtherValue()
    {
        // The common answer, and the one the old design tried to prove from the file.
        // It is trusted for the same reason 4 ms is: the user said so.
        Assert.True(RewImportTiming.TryResolve(
            statedOffsetSeconds: 0,
            timeZeroIndex: 95743.0,
            peakIndex: 96000,
            sampleCount: 262144,
            sampleRate: SampleRate,
            out RewImportTimingPlan? plan,
            out _));

        Assert.Equal(TimingReference.SynchronizedLoopback, plan!.Reference);
        Assert.Equal(95743.0, plan.ReferenceIndex);
        Assert.Equal(257.0, plan.ArrivalSamples);
    }

    [Fact]
    public void AStatedOffsetTheFileContradictsIsRefusedWithTheOneThatWouldWork()
    {
        // Claiming no offset for a file whose peak precedes its reference cannot be
        // true: sound does not reach the microphone before it reaches the loopback.
        // The refusal names the offset that would put the arrival after t = 0, so the
        // next attempt is informed rather than a guess.
        Assert.False(RewImportTiming.TryResolve(
            statedOffsetSeconds: 0,
            timeZeroIndex: 96127.0,
            peakIndex: 96000,
            sampleCount: 262144,
            sampleRate: SampleRate,
            out RewImportTimingPlan? plan,
            out string? problem));

        Assert.Null(plan);
        Assert.Contains("cannot produce", problem);
        Assert.Contains("1.3229", problem);
    }

    [Fact]
    public void AnOffsetThatMovesTheReferenceOutOfTheBufferIsRefused()
    {
        // A mistyped offset — seconds where milliseconds were meant — would otherwise
        // ask for a rotation by more than the buffer holds.
        Assert.False(RewImportTiming.TryResolve(
            statedOffsetSeconds: 4.0,
            timeZeroIndex: 96127.0,
            peakIndex: 96000,
            sampleCount: 262144,
            sampleRate: SampleRate,
            out RewImportTimingPlan? plan,
            out string? problem));

        Assert.Null(plan);
        Assert.Contains("outside the buffer", problem);
    }
}
