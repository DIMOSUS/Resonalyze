using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>Shared by the Virtual DSP panel and the EQ Wizard phase view; both must resolve the same windows and tau.</summary>
public sealed class VirtualCrossoverPhaseGatePlacementTests
{
    [Fact]
    public void APlacementThatKeepsItsLeadingEdgeIsAllowed()
    {
        Assert.True(PhaseGatePlacement.AllowsPerCurveGate(-28.4, -31.5));
        Assert.True(PhaseGatePlacement.AllowsPerCurveGate(-44.9, -44.9));
        Assert.True(PhaseGatePlacement.AllowsPerCurveGate(-65.5, -72.2));
    }

    [Fact]
    public void APlacementOnTheArrivalPeakIsRefused()
    {
        // The peak placement that drew a summing sub/bass pair as antiphase.
        Assert.False(PhaseGatePlacement.AllowsPerCurveGate(-3.5, -44.9));
        Assert.False(PhaseGatePlacement.AllowsPerCurveGate(-5.8, -31.5));
        Assert.False(PhaseGatePlacement.AllowsPerCurveGate(-10.8, -72.2));
    }

    [Fact]
    public void AGateTooShortForTheChannelDoesNotCostItThePerCurveWindow()
    {
        // A 55 Hz period never fits the default gate (-19.4 dB either way); refusing would drop late channels from a 6 ms window.
        Assert.True(PhaseGatePlacement.AllowsPerCurveGate(-19.4, -19.4));
        Assert.True(PhaseGatePlacement.AllowsPerCurveGate(-12.8, -12.7));
        Assert.False(PhaseGatePlacement.AllowsPerCurveGate(-12.7, -12.8));
    }

    [Fact]
    public void ASharedWindowHoldingNoneOfTheChannelNeverTakesItsPlacement()
    {
        // A late channel outside the shared window reads infinite loss, so its own placement is kept.
        Assert.True(PhaseGatePlacement.AllowsPerCurveGate(
            -10.0, double.PositiveInfinity));
        Assert.True(PhaseGatePlacement.AllowsPerCurveGate(
            0.0, double.PositiveInfinity));
    }

    [Fact]
    public void TheSharedWindowFollowsTheEarliestFrontUntilItIsPinned()
    {
        IReadOnlyList<PlacementChannel> channels = [Arriving(240), Arriving(480)];

        Assert.Equal(
            5.0,
            PhaseGatePlacement.ResolveSharedOffsetMs(channels, SampleRate, null),
            1);
        Assert.Equal(
            12.5,
            PhaseGatePlacement.ResolveSharedOffsetMs(channels, SampleRate, 12.5));
    }

    [Fact]
    public void APinnedGateGivesEveryCurveTheSameWindow()
    {
        IReadOnlyList<PlacementChannel> channels = [Arriving(240), Arriving(480)];

        List<double> offsets = PhaseGatePlacement.ResolvePerCurveOffsets(
            channels, sharedOffsetMs: 4.0, SampleRate, pinnedOffsetMs: 4.0,
            leftMs: 0.5, plateauMs: 4.0, rightMs: 1.5);

        Assert.Equal([4.0, 4.0], offsets);
    }

    [Fact]
    public void AnUnpinnedGateGivesEachCurveItsOwnArrival()
    {
        IReadOnlyList<PlacementChannel> channels = [Arriving(240), Arriving(480)];

        List<double> offsets = PhaseGatePlacement.ResolvePerCurveOffsets(
            channels, sharedOffsetMs: 5.0, SampleRate, pinnedOffsetMs: null,
            leftMs: 0.5, plateauMs: 4.0, rightMs: 1.5);

        Assert.Equal(5.0, offsets[0], 1);
        Assert.Equal(10.0, offsets[1], 1);
    }

    // The whole-set fallback has no honest synthetic (the front estimator lands on the arrival); pinned via AllowsPerCurveGate above.

    [Fact]
    public void TheDetrendIsOneValueForTheWholeSet()
    {
        // One tau for the set: per-channel tau would flatten each curve and erase the crossover offsets.
        IReadOnlyList<PlacementChannel> channels = [Arriving(240), Arriving(480)];
        PhaseAnalysisSettings template = Template(gateOffsetMs: 5.0);

        Assert.Equal(0.0, PhaseGatePlacement.ResolveCommonDetrendMs(
            channels, SampleRate, template, PhaseDetrendMode.Off, 7.5));
        Assert.Equal(7.5, PhaseGatePlacement.ResolveCommonDetrendMs(
            channels, SampleRate, template, PhaseDetrendMode.Manual, 7.5));
        Assert.Equal(
            5.0,
            PhaseGatePlacement.ResolveCommonDetrendMs(
                channels, SampleRate, template, PhaseDetrendMode.Manual, null),
            1);
    }

    private const int SampleRate = 48_000;

    private static PlacementChannel Arriving(int startSample)
    {
        var ir = new Complex[8_192];
        for (int i = 0; i < 64; i++)
        {
            ir[startSample + i] =
                Math.Exp(-i / 12.0) * Math.Cos(2 * Math.PI * i / 16.0);
        }

        return Channel(ir, startSample);
    }

    private static PlacementChannel Channel(Complex[] ir, int peakIndex) =>
        new(ir, peakIndex, default);

    private static PhaseAnalysisSettings Template(double gateOffsetMs) => new(
        PhaseWindowMode.Fixed,
        PhaseAnalysisSettings.DefaultFdwCycles,
        PhaseDetrendMode.Auto,
        ManualDetrendMilliseconds: 0.0,
        gateOffsetMs,
        LeftMs: 0.5,
        PlateauMs: 4.0,
        RightMs: 1.5,
        Unwrap: false,
        SmoothingInverseOctaves: 0.0);
}
