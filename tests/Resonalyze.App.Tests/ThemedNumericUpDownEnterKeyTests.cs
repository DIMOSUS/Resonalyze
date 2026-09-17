using System.Globalization;
using System.Reflection;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>Enter that lands an edit is kept, so it does not also fire the dialog's AcceptButton.</summary>
public sealed class ThemedNumericUpDownEnterKeyTests
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

    private static ThemedNumericUpDown NewControl() => new()
    {
        DecimalPlaces = 1,
        Minimum = 0,
        Maximum = 60,
        Increment = 1,
        Value = 0
    };

    private static TextBox Editor(ThemedNumericUpDown control) =>
        control.Controls.OfType<TextBox>().Single();

    private static string FormatLocal(decimal value) =>
        value.ToString("F1", CultureInfo.CurrentCulture);

    private static bool PressEnter(ThemedNumericUpDown control)
    {
        MethodInfo method = typeof(ThemedNumericUpDown).GetMethod(
            "ProcessCmdKey",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ProcessCmdKey is missing.");
        var message = new Message
        {
            Msg = 0x0100,
            WParam = (IntPtr)Keys.Enter
        };
        object[] arguments = [message, Keys.Enter];
        return (bool)method.Invoke(control, arguments)!;
    }
}
