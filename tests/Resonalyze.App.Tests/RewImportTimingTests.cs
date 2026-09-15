namespace Resonalyze.App.Tests;

// A REW text header is identical with or without a timing offset, so the import asks; these are the four outcomes.
public sealed class RewImportTimingTests
{
    private const int SampleRate = 96000;

    [Fact]
    public void AnUnknownOffsetImportsTheShapeAndClaimsNothingAboutTime()
    {
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

        Assert.Equal(-127.0, plan.ArrivalSamples);
    }

    [Fact]
    public void AStatedOffsetIsTakenBackOutAndTrusted()
    {
        // Real m-L export, 4 ms offset at 96 kHz: t=0 at 96127, peak 96000 (-1.32 ms); removing 384 samples gives +257.
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
        // Sound cannot reach the mic before the loopback, so a zero offset with the peak before t=0 is refused.
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
