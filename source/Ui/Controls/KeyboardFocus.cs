namespace Resonalyze;

/// <summary>Where the caret is, for plain-key shortcuts that must never take a keystroke meant for a field.</summary>
internal static class KeyboardFocus
{
    /// <summary>The control under <paramref name="root"/> holding focus; null when focus is outside it.</summary>
    public static Control? Leaf(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Control? focused = root.ContainsFocus ? root : null;
        while (focused is { Focused: false })
        {
            focused = focused.Controls.Cast<Control>().FirstOrDefault(child => child.ContainsFocus);
        }

        return focused;
    }

    /// <summary>A field's own editor (a numeric field's text box too) or an editable combo holds the caret.</summary>
    public static bool IsTyping(Control root) =>
        Leaf(root) is TextBoxBase or ComboBox { DropDownStyle: not ComboBoxStyle.DropDownList };
}
