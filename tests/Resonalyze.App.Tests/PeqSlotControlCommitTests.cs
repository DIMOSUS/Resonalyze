using System.Globalization;
using System.Reflection;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>
/// A strip's fields commit typed text when they lose focus or take Enter, which is
/// enough while the application is running. It is not enough while it is being torn
/// down: an OS shutdown persists the settings with the caret still in the box, and
/// the value read there would be the one from before the number was typed.
/// </summary>
public sealed class PeqSlotControlCommitTests
{
    [Fact]
    public void CommitPendingText_LandsTypedTextInEveryFieldWithoutLeavingIt()
    {
        using var slot = new PeqSlotControl();
        Editor(slot.FrequencyInput).Text = Local(315m);
        Editor(slot.QInput).Text = Local(2.5m);
        Editor(slot.GainInput).Text = Local(-4.5m);

        // Nothing has left the fields, so nothing has committed yet.
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

    // The editor is the control's own inner TextBox; the tests reach it the same way
    // DarkNumericUpDownEnterKeyTests does, since typing is what they simulate.
    private static TextBox Editor(DarkNumericUpDown control) =>
        (TextBox)typeof(DarkNumericUpDown)
            .GetField("editor", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(control)!;

    private static string Local(decimal value) =>
        value.ToString(CultureInfo.CurrentCulture);
}
