using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>Field case: L pinned at 10 ms, R at 14 ms, viewing L - the dashed Sum R must be gated at 14 ms.</summary>
public sealed class VirtualCrossoverMagnitudeGateTests
{
    private static VirtualCrossoverPanel.MagnitudeGateSnapshot Snapshot(
        double? pinnedOffsetMs,
        double? oppositePinnedOffsetMs) => new(
            new PhaseAnalysisSettings(
                PhaseWindowMode.Fixed,
                PhaseAnalysisSettings.DefaultFdwCycles,
                PhaseDetrendMode.Off,
                ManualDetrendMilliseconds: 0.0,
                GateOffsetMs: 0.0,
                LeftMs: 0.5,
                PlateauMs: 4.0,
                RightMs: 1.5,
                Unwrap: false,
                SmoothingInverseOctaves: 0.0),
            pinnedOffsetMs,
            oppositePinnedOffsetMs,
            SmoothingInverseOctaves: 12);

    [Fact]
    public void EachSideUsesItsOwnPinnedOffset()
    {
        VirtualCrossoverPanel.MagnitudeGateSnapshot snapshot = Snapshot(
            pinnedOffsetMs: 10.0, oppositePinnedOffsetMs: 14.0);

        Assert.Equal(10.0, snapshot.ResolveGateOffsetMs(
            oppositeSide: false, anchorPeakIndex: 960, sampleRate: 48_000));
        Assert.Equal(14.0, snapshot.ResolveGateOffsetMs(
            oppositeSide: true, anchorPeakIndex: 960, sampleRate: 48_000));
    }

    [Fact]
    public void AnUnpinnedSideAnchorsAtTheGivenPeak_EvenWhenTheOtherIsPinned()
    {
        VirtualCrossoverPanel.MagnitudeGateSnapshot snapshot = Snapshot(
            pinnedOffsetMs: 10.0, oppositePinnedOffsetMs: null);

        Assert.Equal(10.0, snapshot.ResolveGateOffsetMs(
            oppositeSide: false, anchorPeakIndex: 960, sampleRate: 48_000));
        Assert.Equal(20.0, snapshot.ResolveGateOffsetMs(
            oppositeSide: true, anchorPeakIndex: 960, sampleRate: 48_000));
    }
}
