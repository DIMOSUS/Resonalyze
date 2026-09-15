using System.Globalization;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverChannelControlTotalGainTests
{
    [Fact]
    public void WithoutAPeqPreamp_TheReadoutStaysBlank()
    {
        using var control = new VirtualCrossoverChannelControl();

        control.GainInput.Value = -6.0m;

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
        // Stored settings apply with change events suppressed, so the batch itself must refresh the readout.
        using var control = new VirtualCrossoverChannelControl();
        control.PeqPreampDb = -4.5;
        control.SettingsChanged += (_, _) => Assert.Fail(
            "Applying stored settings must not look like a user edit.");

        control.RunBatchUpdate(() => control.GainInput.Value = -1.5m);

        Assert.Equal(Expected(-6.0), control.TotalGainLabel.Text);
    }

    private static string Expected(double totalDb) =>
        "All " + totalDb.ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture);
}
