namespace Resonalyze;

internal static class OverlayDialogControls
{
    public static void ApplyRuntimeDpiScale(Form form)
    {
        float factor = GetRuntimeDpiScale(form);
        if (factor <= 1.01f)
        {
            return;
        }

        form.ClientSize = ScaleSize(form.ClientSize, factor);
        form.Padding = ScalePadding(form.Padding, factor);
        foreach (Control child in form.Controls)
        {
            ScaleControlTree(child, factor);
        }
    }

    public static Button CreateDialogButton(
        string text,
        DialogResult result,
        bool accent)
    {
        return UiStyle.CreateDialogButton(text, result, accent);
    }

    public static Label CreateLabel(
        string text,
        Point location,
        Color color,
        Font font)
    {
        return UiStyle.CreateLabel(text, location, color, font);
    }

    private static void ScaleControlTree(Control control, float factor)
    {
        control.Bounds = new Rectangle(
            Scale(control.Left, factor),
            Scale(control.Top, factor),
            Math.Max(1, Scale(control.Width, factor)),
            Math.Max(1, Scale(control.Height, factor)));
        control.Margin = ScalePadding(control.Margin, factor);
        control.Padding = ScalePadding(control.Padding, factor);

        foreach (Control child in control.Controls)
        {
            ScaleControlTree(child, factor);
        }
    }

    private static float GetRuntimeDpiScale(Form form)
    {
        using Graphics graphics = form.CreateGraphics();
        return Math.Max(form.DeviceDpi / 96.0f, graphics.DpiX / 96.0f);
    }

    private static Size ScaleSize(Size size, float factor) =>
        new(Scale(size.Width, factor), Scale(size.Height, factor));

    private static Padding ScalePadding(Padding padding, float factor) =>
        new(
            Scale(padding.Left, factor),
            Scale(padding.Top, factor),
            Scale(padding.Right, factor),
            Scale(padding.Bottom, factor));

    private static int Scale(int value, float factor) =>
        (int)Math.Round(value * factor);
}
