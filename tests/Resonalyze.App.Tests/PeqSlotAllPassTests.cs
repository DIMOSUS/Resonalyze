using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class PeqSlotAllPassTests
{
    [Fact]
    public void AllPassStrip_SwapsTheGainControlsForTheGroupDelayReadout()
    {
        using var slot = new PeqSlotControl();
        slot.BandType = PeqBandType.AllPassSecondOrder;

        Assert.False(slot.GainInput.Visible);
        Assert.True(slot.GroupDelayReadout.Visible);
        Assert.True(slot.QInput.Enabled);

        slot.BandType = PeqBandType.Peaking;

        Assert.True(slot.GainInput.Visible);
        Assert.False(slot.GroupDelayReadout.Visible);
    }

    [Fact]
    public void FirstOrderStrip_GreysTheQItDoesNotRead()
    {
        using var slot = new PeqSlotControl();
        slot.BandType = PeqBandType.AllPassFirstOrder;

        Assert.False(slot.QInput.Enabled);

        slot.BandType = PeqBandType.AllPassSecondOrder;

        Assert.True(slot.QInput.Enabled);
    }

    [Fact]
    public void GroupDelayReadout_TracksTheCornerAndTheRate()
    {
        using var slot = new PeqSlotControl();
        slot.BandType = PeqBandType.AllPassSecondOrder;
        slot.FrequencyInput.Value = 63m;
        slot.QInput.Value = 2m;

        double ms = AllPassFilter.CornerGroupDelaySeconds(
            new AllPassSpec(AllPassType.SecondOrder, 63, 2), slot.SampleRateHz) * 1_000.0;
        Assert.Equal($"= {ms:0.00} ms", slot.GroupDelayReadout.Text);

        // Near Nyquist the corner GD depends on the rate the wizard realizes biquads at.
        slot.FrequencyInput.Value = 20_000m;
        string at48k = slot.GroupDelayReadout.Text;
        slot.SampleRateHz = 192_000;
        Assert.NotEqual(at48k, slot.GroupDelayReadout.Text);
    }

    [Fact]
    public void HiddenGainSurvivesTheRoundTripThroughAnAllPass()
    {
        using var slot = new PeqSlotControl();
        slot.GainInput.Value = -4.5m;

        slot.BandType = PeqBandType.AllPassSecondOrder;
        slot.BandType = PeqBandType.Peaking;

        Assert.Equal(-4.5m, slot.GainInput.Value);
    }

    [Theory]
    [InlineData(PeqBandType.AllPassFirstOrder, "AP1")]
    [InlineData(PeqBandType.AllPassSecondOrder, "AP2")]
    public void Header_NamesTheAllPassWithTheDeviceToken(PeqBandType type, string token)
    {
        // Tokens match Audiotec PC-Tool slot names.
        Assert.Equal(token, PeqBandToken.Of(type));
    }
}
