using System.Globalization;
using System.Reflection;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>An OS shutdown persists settings with the caret still in a field, so typed text must commit on teardown.</summary>
public sealed class PeqSlotControlCommitTests
{
    [Fact]
    public void CommitPendingText_LandsTypedTextInEveryFieldWithoutLeavingIt()
    {
        using var slot = new PeqSlotControl();
        Editor(slot.FrequencyInput).Text = Local(315m);
        Editor(slot.QInput).Text = Local(2.5m);
        Editor(slot.GainInput).Text = Local(-4.5m);

        Assert.NotEqual(315m, slot.FrequencyInput.Value);

        slot.CommitPendingText();

        Assert.Equal(315m, slot.FrequencyInput.Value);
        Assert.Equal(2.5m, slot.QInput.Value);
        Assert.Equal(-4.5m, slot.GainInput.Value);
    }

    [Fact]
    public void CommitPendingText_LeavesAnUntouchedStripAlone()
    {
        using var slot = new PeqSlotControl();
        slot.FrequencyInput.Value = 1_000m;
        slot.QInput.Value = 5m;
        slot.GainInput.Value = 0m;

        slot.CommitPendingText();

        Assert.Equal(1_000m, slot.FrequencyInput.Value);
        Assert.Equal(5m, slot.QInput.Value);
        Assert.Equal(0m, slot.GainInput.Value);
    }

    [Fact]
    public void CommitPendingText_OnUnparseableTextKeepsTheCommittedValue()
    {
        using var slot = new PeqSlotControl();
        slot.GainInput.Value = -3m;
        Editor(slot.GainInput).Text = "not a number";

        slot.CommitPendingText();

        Assert.Equal(-3m, slot.GainInput.Value);
    }

    private static TextBox Editor(DarkNumericUpDown control) =>
        (TextBox)typeof(DarkNumericUpDown)
            .GetField("editor", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(control)!;

    private static string Local(decimal value) =>
        value.ToString(CultureInfo.CurrentCulture);
}
