using System.Drawing;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>A plain-key shortcut yields to whatever holds the caret: a text box, a numeric field's editor, an editable combo.</summary>
public sealed class KeyboardFocusTests
{
    [Fact]
    public void OnlyAFieldBeingTypedIn_CountsAsTyping() => StaTest.Run(() =>
    {
        var text = new TextBox();
        var numeric = new ThemedNumericUpDown { Top = 30 };
        var editable = new ComboBox { Top = 60, DropDownStyle = ComboBoxStyle.DropDown };
        var list = new ComboBox { Top = 90, DropDownStyle = ComboBoxStyle.DropDownList };
        var button = new Button { Top = 120 };
        using var form = new Form
        {
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-4000, -4000)
        };
        form.Controls.AddRange([text, numeric, editable, list, button]);
        form.Show();

        Assert.True(Typing(form, text));
        Assert.True(Typing(form, numeric));
        Assert.True(Typing(form, editable));
        Assert.False(Typing(form, list));
        Assert.False(Typing(form, button));
        Assert.Same(button, KeyboardFocus.Leaf(form));
    });

    private static bool Typing(Form form, Control control)
    {
        control.Focus();
        StaTest.Pump();
        Assert.True(control.ContainsFocus, $"{control.GetType().Name} did not take focus.");
        return KeyboardFocus.IsTyping(form);
    }
}
