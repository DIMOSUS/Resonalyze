using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>Zone and Mono are separate fields; only a centre (derived from L and R) forces mono.</summary>
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
        // Releasing the lock must not uncheck Mono: a retyped centre-to-sub would silently split the shared driver.
        using var control = new VirtualCrossoverChannelControl();
        control.ZoneComboBox.SelectedItem = VirtualCrossoverZone.Center;

        control.ZoneComboBox.SelectedItem = VirtualCrossoverZone.Sub;

        Assert.True(control.MonoCheckBox.Checked);
        Assert.True(control.MonoCheckBox.Enabled);
    }

    [Fact]
    public void EveryOtherZone_LeavesMonoFree()
    {
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
        // The tooltip is set on two racing paths (constructor, later tooltip host); the second must write the text.
        using var control = new VirtualCrossoverChannelControl();
        using var toolTip = new WrappingToolTip();
        control.DelayInput.Value = 2.58m;

        control.ApplyTooltips(toolTip);

        string? text = toolTip.GetToolTip(control.DelayInput);
        // 2.58 ms of air is 885 mm, 34.8 in.
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

        string? text = toolTip.GetToolTip(control.DelayInput);
        Assert.Contains("3507", text);
        Assert.Contains("138", text);
    }

    [Theory]
    [InlineData(false, CrossoverKind.BandPass, VirtualCrossoverZone.Front)]
    [InlineData(false, CrossoverKind.HighPass, VirtualCrossoverZone.Front)]
    [InlineData(true, CrossoverKind.LowPass, VirtualCrossoverZone.Sub)]
    [InlineData(true, CrossoverKind.BandPass, VirtualCrossoverZone.Sub)]
    [InlineData(true, CrossoverKind.Off, VirtualCrossoverZone.Sub)]
    [InlineData(true, CrossoverKind.HighPass, VirtualCrossoverZone.Center)]
    public void LegacyBlocksAreClassifiedByWhatAPreZoneFileRecorded(
        bool mono,
        CrossoverKind kind,
        VirtualCrossoverZone expected) =>
        Assert.Equal(expected, VirtualCrossoverZones.GuessForLegacyPair(mono, kind));
}
