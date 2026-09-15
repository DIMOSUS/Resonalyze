namespace Resonalyze.Screenshots;

/// <summary>Auto delay figure: its regions are text blocks whose size changes with the engine, so boxes are measured via
/// <see cref="TextBoxBase.GetPositionFromCharIndex"/> while the dialog is alive and drawn over the file afterwards.</summary>
internal static class AutoDelayFigure
{
    private static readonly string[] BlockStarts =
        ["Auto delay proposal", "Channel", "Notes:", "Table —"];

    internal sealed record Layout(
        Rectangle Controls, IReadOnlyList<Rectangle> Blocks, Rectangle? Confidence);

    public static Layout PoseAndMeasure(ShotSession session, Form dialog)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(dialog);

        var apply = Reflect.Field<Button>(dialog, "buttonApply");
        Reflect.Field<Button>(dialog, "buttonRun").PerformClick();
        for (int attempt = 0; attempt < 240 && !apply.Enabled; attempt++)
        {
            session.Pump(500);
        }

        if (!apply.Enabled)
        {
            throw new InvalidOperationException(
                "Auto delay did not finish within two minutes.");
        }

        // Left at its own size: a long report scrolls, and a resized window would show a dialog no user sees.
        var report = Reflect.Field<TextBox>(dialog, "textBoxReport");
        report.SelectionStart = 0;
        report.SelectionLength = 0;
        report.ScrollToCaret();
        session.Pump(300);

        return Measure(dialog, report);
    }

    public static void Draw(Layout layout, string path)
    {
        ArgumentNullException.ThrowIfNull(layout);
        using Annotate figure = Annotate.Open(path);
        figure.Region(layout.Controls, "1",
            new Point(layout.Controls.Right - 46, layout.Controls.Top + 22));
        for (int block = 0; block < layout.Blocks.Count; block++)
        {
            Rectangle box = layout.Blocks[block];
            figure.Region(box, (block + 2).ToString(),
                new Point(box.Right - 40, box.Top + 26));
        }

        if (layout.Confidence is { } confidence)
        {
            figure.Detail(confidence);
        }

        figure.Save(path);
    }

    private static Layout Measure(Form dialog, TextBox report)
    {
        Point screen = report.PointToScreen(Point.Empty);
        var origin = new Point(screen.X - dialog.Left, screen.Y - dialog.Top);

        // The control's own text: GetPositionFromCharIndex counts its line endings, and normalizing shifted every box.
        string text = report.Text;
        int[] starts = [.. BlockStarts.Select(marker => IndexOfLine(text, marker))];
        if (starts.Any(start => start < 0))
        {
            throw new InvalidOperationException(
                "The Auto delay report no longer opens the blocks this figure marks: " +
                string.Join(", ", BlockStarts));
        }

        int left = origin.X - 6;
        int right = origin.X + report.Width + 6;

        int floor = origin.Y + report.Height - 4;
        var blocks = new List<Rectangle>();
        for (int block = 0; block < starts.Length; block++)
        {
            int top = origin.Y + report.GetPositionFromCharIndex(starts[block]).Y;
            if (top >= floor)
            {
                break;
            }

            int bottom = block + 1 < starts.Length
                ? origin.Y + report.GetPositionFromCharIndex(starts[block + 1]).Y - 6
                : floor;
            blocks.Add(new Rectangle(
                left, top - 6, right - left, Math.Min(bottom, floor) - top + 10));
        }

        Rectangle? confidence = null;
        const string Column = "Delay conf";
        int header = text.IndexOf(Column, starts[1], StringComparison.Ordinal);
        if (header > 0)
        {
            Point start = report.GetPositionFromCharIndex(header);
            Point end = report.GetPositionFromCharIndex(header + Column.Length);
            int top = origin.Y + report.GetPositionFromCharIndex(starts[1]).Y;
            int bottom = origin.Y + report.GetPositionFromCharIndex(starts[2]).Y - 12;
            confidence = new Rectangle(
                origin.X + start.X - 6, top - 4, end.X - start.X + 12, bottom - top);
        }

        var controls = new Rectangle(left - 4, 34, right - left + 8, origin.Y - 44);
        return new Layout(controls, blocks, confidence);
    }

    // "Channel" also occurs inside the notes, so the marker must open a line.
    private static int IndexOfLine(string text, string marker)
    {
        for (int at = text.IndexOf(marker, StringComparison.Ordinal);
             at >= 0;
             at = text.IndexOf(marker, at + 1, StringComparison.Ordinal))
        {
            if (at == 0 || text[at - 1] == '\n')
            {
                return at;
            }
        }

        return -1;
    }
}
