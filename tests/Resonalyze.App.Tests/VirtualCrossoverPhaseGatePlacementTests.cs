namespace Resonalyze.App.Tests;

/// <summary>
/// Pins when the Virtual DSP phase view may gate each curve on its own arrival
/// rather than on one shared window. Per-curve placement is what keeps FDW's
/// short high-frequency windows on the right channel's first cycles; it is only
/// comparable while each window opens before its own channel's response, which
/// is what the leading-edge loss measures.
/// </summary>
public sealed class VirtualCrossoverPhaseGatePlacementTests
{
    [Fact]
    public void APlacementThatKeepsItsLeadingEdgeIsAllowed()
    {
        // The field session's own-arrival placements, against the shared
        // window: both well under the ceiling.
        Assert.True(VirtualCrossoverPanel.AllowsPerCurvePhaseGate(-28.4, -31.5));
        Assert.True(VirtualCrossoverPanel.AllowsPerCurvePhaseGate(-44.9, -44.9));
        Assert.True(VirtualCrossoverPanel.AllowsPerCurvePhaseGate(-65.5, -72.2));
    }

    [Fact]
    public void APlacementOnTheArrivalPeakIsRefused()
    {
        // What this guard exists for: the peak placement that drew a summing
        // subwoofer/bass pair as antiphase. Over the ceiling AND far worse
        // than the shared window would be.
        Assert.False(VirtualCrossoverPanel.AllowsPerCurvePhaseGate(-3.5, -44.9));
        Assert.False(VirtualCrossoverPanel.AllowsPerCurvePhaseGate(-5.8, -31.5));
        Assert.False(VirtualCrossoverPanel.AllowsPerCurvePhaseGate(-10.8, -72.2));
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
        Assert.True(VirtualCrossoverPanel.AllowsPerCurvePhaseGate(-19.4, -19.4));
        Assert.True(VirtualCrossoverPanel.AllowsPerCurvePhaseGate(-12.8, -12.7));
        // Equally bad is allowed; measurably worse is not.
        Assert.False(VirtualCrossoverPanel.AllowsPerCurvePhaseGate(-12.7, -12.8));
    }
}
