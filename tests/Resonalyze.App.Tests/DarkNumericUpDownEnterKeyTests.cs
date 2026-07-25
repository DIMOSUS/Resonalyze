using System.Globalization;
using System.Reflection;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>
/// Enter inside one of these fields used to reach the host dialog's AcceptButton in the
/// same keystroke: in the Virtual DSP auto-setup that ran the whole crossover proposal
/// and closed the window while a value was still being typed. The control now keeps the
/// Enter that lands an edit and passes on the one that has nothing to commit.
/// </summary>
public sealed class DarkNumericUpDownEnterKeyTests
{
    [Fact]
    public void Enter_CommitsTypedTextAndIsNotPassedOnToTheAcceptButton()
    {
        using var control = NewControl();
        Editor(control).Text = FormatLocal(12.5m);

        bool handled = PressEnter(control);

        Assert.True(handled);
        Assert.Equal(12.5m, control.Value);
    }

    [Fact]
    public void Enter_WithNothingPending_ReachesTheAcceptButton()
    {
        using var control = NewControl();
        control.Value = 12.5m;

        bool handled = PressEnter(control);

        Assert.False(handled);
        Assert.Equal(12.5m, control.Value);
    }

    [Fact]
    public void Enter_AfterCommittingAnEdit_ReachesTheAcceptButtonOnTheSecondPress()
    {
        using var control = NewControl();
        Editor(control).Text = FormatLocal(30m);

        Assert.True(PressEnter(control));
        Assert.False(PressEnter(control));
        Assert.Equal(30m, control.Value);
    }

    [Fact]
    public void Enter_OnUnparseableText_RestoresTheValueAndStaysInTheField()
    {
        using var control = NewControl();
        control.Value = 6m;
        Editor(control).Text = "not a number";

        bool handled = PressEnter(control);

        Assert.True(handled);
        Assert.Equal(6m, control.Value);
        Assert.Equal(FormatLocal(6m), Editor(control).Text);
    }

    private static DarkNumericUpDown NewControl() => new()
    {
        DecimalPlaces = 1,
        Minimum = 0,
        Maximum = 60,
        Increment = 1,
        Value = 0
    };

    private static TextBox Editor(DarkNumericUpDown control) =>
        control.Controls.OfType<TextBox>().Single();

    private static string FormatLocal(decimal value) =>
        value.ToString("F1", CultureInfo.CurrentCulture);

    // ProcessCmdKey is where a dialog key is offered to the control before the form's
    // default button sees it; true means the control kept the key.
    private static bool PressEnter(DarkNumericUpDown control)
    {
        MethodInfo method = typeof(DarkNumericUpDown).GetMethod(
            "ProcessCmdKey",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ProcessCmdKey is missing.");
        var message = new Message
        {
            Msg = 0x0100, // WM_KEYDOWN
            WParam = (IntPtr)Keys.Enter
        };
        object[] arguments = [message, Keys.Enter];
        return (bool)method.Invoke(control, arguments)!;
    }
}
