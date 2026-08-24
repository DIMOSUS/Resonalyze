using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// Pins when a phase view may gate each curve on its own arrival rather than on
/// one shared window, and the placements the whole view is resolved from. Per-curve
/// placement is what keeps FDW's short high-frequency windows on the right channel's
/// first cycles; it is only comparable while each window opens before its own
/// channel's response, which is what the leading-edge loss measures.
/// <para>
/// The arithmetic is shared: the Virtual DSP panel resolves it per redraw, the EQ
/// Wizard's phase view resolves it from what the handoff froze. Both have to get the
/// same windows and the same τ out of the same channels, or a tune made in one view
/// would not hold in the other — which is what these pin.
/// </para>
/// </summary>
public sealed class VirtualCrossoverPhaseGatePlacementTests
{
    [Fact]
    public void APlacementThatKeepsItsLeadingEdgeIsAllowed()
    {
        // The field session's own-arrival placements, against the shared
        // window: both well under the ceiling.
        Assert.True(PhaseGatePlacement.AllowsPerCurveGate(-28.4, -31.5));
        Assert.True(PhaseGatePlacement.AllowsPerCurveGate(-44.9, -44.9));
        Assert.True(PhaseGatePlacement.AllowsPerCurveGate(-65.5, -72.2));
    }

    [Fact]
    public void APlacementOnTheArrivalPeakIsRefused()
    {
        // What this guard exists for: the peak placement that drew a summing
        // subwoofer/bass pair as antiphase. Over the ceiling AND far worse
        // than the shared window would be.
        Assert.False(PhaseGatePlacement.AllowsPerCurveGate(-3.5, -44.9));
        Assert.False(PhaseGatePlacement.AllowsPerCurveGate(-5.8, -31.5));
        Assert.False(PhaseGatePlacement.AllowsPerCurveGate(-10.8, -72.2));
    }

    [Fact]
    public void AGateTooShortForTheChannelDoesNotCostItThePerCurveWindow()
    {
        // The project default gate (0.5/4/1.5 ms) cannot hold one period of a
        // 55 Hz subwoofer wherever it is placed: on the field session it read
        // -19.4 dB at the channel's own arrival and -19.4 dB at the shared
        // one. Refusing there would buy no accuracy and would drop the whole
        // set onto a 6 ms window that a channel arriving 20 ms later falls
        // straight out of — the omission per-curve placement exists to
        // prevent. Over the ceiling is not enough; the shared window has to be
        // the better placement.
        Assert.True(PhaseGatePlacement.AllowsPerCurveGate(-19.4, -19.4));
        Assert.True(PhaseGatePlacement.AllowsPerCurveGate(-12.8, -12.7));
        // Equally bad is allowed; measurably worse is not.
        Assert.False(PhaseGatePlacement.AllowsPerCurveGate(-12.7, -12.8));
    }

    [Fact]
    public void ASharedWindowHoldingNoneOfTheChannelNeverTakesItsPlacement()
    {
        // The case that would put the late-channel omission back: channels at
        // 0 ms and 20 ms with the default 6 ms gate, the shared window on the
        // early one. The late channel is nowhere inside that window, so
        // falling back to it would delete the channel from the phase view and
        // from Sum - the exact defect per-curve placement exists to prevent.
        // GateLeadingEdgeLossDb reports such a window as infinite loss, so
        // however poorly the channel's own placement scores, it is still the
        // better of the two and is kept.
        Assert.True(PhaseGatePlacement.AllowsPerCurveGate(
            -10.0, double.PositiveInfinity));
        Assert.True(PhaseGatePlacement.AllowsPerCurveGate(
            0.0, double.PositiveInfinity));
    }

    [Fact]
    public void TheSharedWindowFollowsTheEarliestFrontUntilItIsPinned()
    {
        // Auto tracks the sources: the window opens on whichever channel arrives
        // first, so adding a delay or swapping a measurement moves it. A pinned
        // offset is an absolute time and is used exactly as given.
        IReadOnlyList<ProcessedChannel> channels = [Arriving(240), Arriving(480)];

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
        // The pin is the user saying "read every channel through THIS window";
        // per-curve placement would quietly undo that.
        IReadOnlyList<ProcessedChannel> channels = [Arriving(240), Arriving(480)];

        List<double> offsets = PhaseGatePlacement.ResolvePerCurveOffsets(
            channels, sharedOffsetMs: 4.0, SampleRate, pinnedOffsetMs: 4.0,
            leftMs: 0.5, plateauMs: 4.0, rightMs: 1.5);

        Assert.Equal([4.0, 4.0], offsets);
    }

    [Fact]
    public void AnUnpinnedGateGivesEachCurveItsOwnArrival()
    {
        // Two channels 5 ms apart with a window far too short to hold both from
        // one placement: each takes its own front, which is what keeps the later
        // one inside its window at all.
        IReadOnlyList<ProcessedChannel> channels = [Arriving(240), Arriving(480)];

        List<double> offsets = PhaseGatePlacement.ResolvePerCurveOffsets(
            channels, sharedOffsetMs: 5.0, SampleRate, pinnedOffsetMs: null,
            leftMs: 0.5, plateauMs: 4.0, rightMs: 1.5);

        Assert.Equal(5.0, offsets[0], 1);
        Assert.Equal(10.0, offsets[1], 1);
    }

    // The other half of the per-curve rule — one channel failing the guard takes the
    // WHOLE set back to the shared window — is pinned through AllowsPerCurveGate
    // above, on the field session's own numbers. It has no honest synthetic: the
    // front estimator is built to land on the arrival, so a hand-made impulse that
    // trips the guard would be pinning a quirk of the estimator rather than the rule.

    [Fact]
    public void TheDetrendIsOneValueForTheWholeSet()
    {
        // One τ for every curve is what makes their relative phase survive the
        // detrend. Off is no reference at all; a stated τ is used as given; and an
        // unstated one references the set's own earliest front — never each
        // channel's own, which would flatten every curve and erase the offsets a
        // crossover region is read for.
        IReadOnlyList<ProcessedChannel> channels = [Arriving(240), Arriving(480)];
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

    // A channel whose response starts at the given sample: a short decaying burst,
    // so the start estimate has a real front to find rather than one lone sample.
    private static ProcessedChannel Arriving(int startSample)
    {
        var ir = new Complex[8_192];
        for (int i = 0; i < 64; i++)
        {
            ir[startSample + i] =
                Math.Exp(-i / 12.0) * Math.Cos(2 * Math.PI * i / 16.0);
        }

        return Channel(ir, startSample);
    }

    private static ProcessedChannel Channel(Complex[] ir, int peakIndex) =>
        new(
            new VirtualCrossoverChannel("x") { SampleRate = SampleRate },
            ir,
            peakIndex,
            SampleRate,
            OxyColors.White);

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
