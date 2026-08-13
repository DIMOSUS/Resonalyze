using System.Globalization;

namespace Resonalyze.App.Tests;

/// <summary>
/// The channel block's all-in gain readout: the level the two broadband stages of
/// the chain — the channel gain and the loaded PEQ's preamp — come to together,
/// which is what gets typed into a DSP whose equalizer has no preamp of its own.
/// </summary>
public sealed class VirtualCrossoverChannelControlTotalGainTests
{
    [Fact]
    public void WithoutAPeqPreamp_TheReadoutStaysBlank()
    {
        using var control = new VirtualCrossoverChannelControl();

        control.GainInput.Value = -6.0m;

        // Nothing to add to the gain: the readout would only repeat the field
        // standing next to it.
        Assert.Equal(string.Empty, control.TotalGainLabel.Text);
    }

    [Fact]
    public void WithAPeqPreamp_TheReadoutSumsItWithTheGain()
    {
        using var control = new VirtualCrossoverChannelControl();

        control.GainInput.Value = -3.5m;
        control.PeqPreampDb = -4.5;

        Assert.Equal(Expected(-8.0), control.TotalGainLabel.Text);
    }

    [Fact]
    public void ChangingTheGain_RefreshesTheReadout()
    {
        using var control = new VirtualCrossoverChannelControl();
        control.PeqPreampDb = -4.5;

        control.GainInput.Value = 2.0m;

        Assert.Equal(Expected(-2.5), control.TotalGainLabel.Text);
    }

    [Fact]
    public void ClearingThePeq_BlanksTheReadoutAgain()
    {
        using var control = new VirtualCrossoverChannelControl();
        control.GainInput.Value = -3.5m;
        control.PeqPreampDb = -4.5;

        control.PeqPreampDb = 0;

        Assert.Equal(string.Empty, control.TotalGainLabel.Text);
    }

    [Fact]
    public void ABatchUpdate_LeavesTheReadoutMatchingTheAppliedGain()
    {
        // The host applies stored settings with the change events suppressed, so the
        // readout has to be refreshed by the batch itself rather than by the
        // SettingsChanged the suppressed field never raises.
        using var control = new VirtualCrossoverChannelControl();
        control.PeqPreampDb = -4.5;
        control.SettingsChanged += (_, _) => Assert.Fail(
            "Applying stored settings must not look like a user edit.");

        control.RunBatchUpdate(() => control.GainInput.Value = -1.5m);

        Assert.Equal(Expected(-6.0), control.TotalGainLabel.Text);
    }

    // The block sits next to a numeric field that formats in the user's culture, so
    // the readout follows it rather than pinning an invariant separator.
    private static string Expected(double totalDb) =>
        "All " + totalDb.ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture);
}
