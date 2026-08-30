using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The channel block's ZONE — which part of the installation it is. The zone and
/// the Mono flag are deliberately separate fields (a sub pair can be stereo, a
/// two-way centre is two mono blocks), with one rule tying them together: a
/// centre plays a signal derived from L and R, so it has no side.
/// </summary>
public sealed class VirtualCrossoverZoneTests
{
    [Fact]
    public void PickingCenter_ForcesMonoOnAndLocksIt()
    {
        using var control = new VirtualCrossoverChannelControl();
        control.ZoneComboBox.SelectedItem = VirtualCrossoverZone.Front;

        control.ZoneComboBox.SelectedItem = VirtualCrossoverZone.Center;

        Assert.True(control.MonoCheckBox.Checked);
        Assert.False(control.MonoCheckBox.Enabled);
    }

    [Fact]
    public void LeavingCenter_ReleasesMonoWithoutClearingIt()
    {
        // Releasing the lock must not also UNCHECK the box: a mono sub is the
        // ordinary case, and a user retyping a mis-guessed centre as a sub would
        // silently get their shared driver split back into two sides.
        using var control = new VirtualCrossoverChannelControl();
        control.ZoneComboBox.SelectedItem = VirtualCrossoverZone.Center;

        control.ZoneComboBox.SelectedItem = VirtualCrossoverZone.Sub;

        Assert.True(control.MonoCheckBox.Checked);
        Assert.True(control.MonoCheckBox.Enabled);
    }

    [Fact]
    public void EveryOtherZone_LeavesMonoFree()
    {
        // Only the centre is mono by nature. A subwoofer usually is and legitimately
        // is not (a stereo pair in the kick panels), and a rear pair is stereo — so
        // neither zone may decide the flag.
        foreach (VirtualCrossoverZone zone in new[]
        {
            VirtualCrossoverZone.Front,
            VirtualCrossoverZone.Rear,
            VirtualCrossoverZone.Sub
        })
        {
            using var control = new VirtualCrossoverChannelControl();

            control.ZoneComboBox.SelectedItem = zone;

            Assert.False(VirtualCrossoverZones.RequiresMono(zone));
            Assert.True(control.MonoCheckBox.Enabled);
            Assert.False(control.MonoCheckBox.Checked);
        }
    }

    [Fact]
    public void TheComboOffersEveryZoneUnderItsOwnName()
    {
        using var control = new VirtualCrossoverChannelControl();

        Assert.Equal(
            VirtualCrossoverZones.All,
            control.ZoneComboBox.Items.Cast<VirtualCrossoverZone>().ToList());
        Assert.Equal("Front", VirtualCrossoverZones.DisplayName(VirtualCrossoverZone.Front));
        Assert.Equal("Rear", VirtualCrossoverZones.DisplayName(VirtualCrossoverZone.Rear));
        Assert.Equal("Center", VirtualCrossoverZones.DisplayName(VirtualCrossoverZone.Center));
        Assert.Equal("Sub", VirtualCrossoverZones.DisplayName(VirtualCrossoverZone.Sub));
    }

    [Fact]
    public void TheDelayTooltipCarriesTheDistanceFromTheMomentItIsInstalled()
    {
        // The distance readout lost its own label and lives in this tooltip, so the
        // tooltip IS the feature — and it is installed on two paths that race: the
        // constructor computes the distance before any tooltip host exists, and the
        // host arrives later. Whichever runs second has to write the text. This
        // pins the second one, which is the path a real block always takes and the
        // one that silently kept a stale string describing a control that is gone.
        using var control = new VirtualCrossoverChannelControl();
        using var toolTip = new WrappingToolTip();
        control.DelayInput.Value = 2.58m;

        control.ApplyTooltips(toolTip);

        string? text = toolTip.GetToolTip(control.DelayInput);
        // 2.58 ms of air is 885 mm, which is 34.8 inches.
        Assert.Contains("885", text);
        Assert.Contains("34", text);
        Assert.Contains("mm", text);
        Assert.Contains("in)", text);
        Assert.DoesNotContain("readout", text);
    }

    [Fact]
    public void TheDelayTooltipFollowsTheValueAfterTheHostIsInstalled()
    {
        using var control = new VirtualCrossoverChannelControl();
        using var toolTip = new WrappingToolTip();
        control.ApplyTooltips(toolTip);

        control.DelayInput.Value = 10.22m;

        // 10.22 ms is 3507 mm — 138.1 inches.
        string? text = toolTip.GetToolTip(control.DelayInput);
        Assert.Contains("3507", text);
        Assert.Contains("138", text);
    }

    [Theory]
    // A stereo pair is the front stage: a v8 file cannot tell a rear pair from it.
    [InlineData(false, CrossoverKind.BandPass, VirtualCrossoverZone.Front)]
    [InlineData(false, CrossoverKind.HighPass, VirtualCrossoverZone.Front)]
    // Mono meant "shared subwoofer" for the tool's whole history…
    [InlineData(true, CrossoverKind.LowPass, VirtualCrossoverZone.Sub)]
    [InlineData(true, CrossoverKind.BandPass, VirtualCrossoverZone.Sub)]
    [InlineData(true, CrossoverKind.Off, VirtualCrossoverZone.Sub)]
    // …except when it high-passes, which no subwoofer does.
    [InlineData(true, CrossoverKind.HighPass, VirtualCrossoverZone.Center)]
    public void LegacyBlocksAreClassifiedByWhatAPreZoneFileRecorded(
        bool mono,
        CrossoverKind kind,
        VirtualCrossoverZone expected) =>
        Assert.Equal(expected, VirtualCrossoverZones.GuessForLegacyPair(mono, kind));
}
