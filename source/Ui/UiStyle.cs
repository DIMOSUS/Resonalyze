using System.Runtime.CompilerServices;

namespace Resonalyze.Ui;

internal static class UiStyle
{
    private static readonly ConditionalWeakTable<Control, object> enabledForeColors = new();

    // WinForms paints disabled Label/CheckBox/RadioButton text near-black on the dark theme, so they stay Enabled with muted text.
    // interactive:true also stops toggling and focus.
    public static void SetTextEnabledLook(Control control, bool enabled, bool interactive = false)
    {
        if (enabled)
        {
            if (enabledForeColors.TryGetValue(control, out object? stored))
            {
                control.ForeColor = (Color)stored;
            }
        }
        else
        {
            if (!enabledForeColors.TryGetValue(control, out _))
            {
                enabledForeColors.Add(control, control.ForeColor);
            }

            control.ForeColor = UiPalette.TextDisabled;
        }

        if (!interactive)
        {
            return;
        }

        switch (control)
        {
            case RadioButton radioButton:
                radioButton.AutoCheck = enabled;
                radioButton.TabStop = enabled;
                break;
            case CheckBox checkBox:
                checkBox.AutoCheck = enabled;
                checkBox.TabStop = enabled;
                break;
        }
    }

    public static void ApplyDarkDialog(
        Form form,
        Size clientSize,
        string? title = null,
        bool showInTaskbar = false,
        bool fixedDialog = true,
        Padding? padding = null)
    {
        // Declared 96-DPI dimensions give AutoScale something to scale from (otherwise factor 1, clipped at 125%).
        // This must be the only scaling pass; a DeviceDpi/96 walk on top squares the factor.
        form.AutoScaleDimensions = new SizeF(96F, 96F);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.BackColor = UiPalette.DialogBackground;
        form.ClientSize = clientSize;
        form.Font = new Font("Segoe UI", 9F);
        form.ForeColor = UiPalette.TextBright;
        form.FormBorderStyle = fixedDialog
            ? FormBorderStyle.FixedDialog
            : FormBorderStyle.None;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.Padding = padding ?? new Padding(20);
        form.ShowIcon = false;
        form.ShowInTaskbar = showInTaskbar;
        form.StartPosition = FormStartPosition.CenterParent;
        if (title != null)
        {
            form.Text = title;
        }
    }

    public static Label CreateLabel(
        string text,
        Point location,
        Color color,
        Font font,
        bool autoSize = true)
    {
        return new Label
        {
            AutoSize = autoSize,
            Font = font,
            ForeColor = color,
            Location = location,
            Text = text
        };
    }

    public static Button CreateDialogButton(
        string text,
        DialogResult result,
        bool accent,
        Size? size = null)
    {
        var button = new ReleaseClickButton
        {
            BackColor = accent
                ? UiPalette.AccentFill
                : UiPalette.DialogSurfaceMuted,
            DialogResult = result,
            FlatStyle = FlatStyle.Flat,
            ForeColor = UiPalette.TextPrimary,
            Size = size ?? new Size(94, 30),
            Text = text,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private static void ApplySurfaceInput(Control control, Point location, Size size)
    {
        control.BackColor = UiPalette.InputSurface;
        control.ForeColor = UiPalette.TextPrimary;
        control.Location = location;
        control.Size = size;
    }

    public static void ApplySurfaceButton(
        Button button,
        Color background,
        Color? foreground = null,
        bool borderless = true)
    {
        button.BackColor = background;
        button.FlatStyle = FlatStyle.Flat;
        button.ForeColor = foreground ?? UiPalette.TextPrimary;
        button.UseVisualStyleBackColor = false;
        if (borderless)
        {
            button.FlatAppearance.BorderSize = 0;
        }
    }

    public static void ApplyBorderedSwatch(Button button, Color borderColor)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderColor = borderColor;
        button.FlatAppearance.BorderSize = 1;
    }

    public static void ApplyTextBox(TextBoxBase textBox, Point location, Size size)
    {
        ApplySurfaceInput(textBox, location, size);
        textBox.BorderStyle = BorderStyle.FixedSingle;
    }

    public static void ApplyNumericUpDown(DarkNumericUpDown input, Point location, Size size)
    {
        ApplySurfaceInput(input, location, size);
    }
}
