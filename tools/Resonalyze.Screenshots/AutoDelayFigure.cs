namespace Resonalyze.Screenshots;

/// <summary>
/// The Auto delay figure, whose annotation cannot be written down as coordinates.
/// </summary>
/// <remarks>
/// Its five regions are blocks of TEXT inside one report box, and the report grows
/// and shrinks with the tune and with the engine: a change to the alignment engine
/// added two "far-side polish" notes and two more changed delays, which pushed the
/// last block past a hard-coded box and out of the dialog altogether. So the dialog
/// is first grown until the whole report is visible, then every box is MEASURED from
/// the control through <see cref="TextBoxBase.GetPositionFromCharIndex"/> — the call
/// the caret uses, so a box lands where the text really is.
///
/// Measuring and drawing are separate because they happen at different times: the
/// dialog is alive only while the shot is being taken, and the annotation is drawn
/// over the file afterwards.
/// </remarks>
internal static class AutoDelayFigure
{
    /// <summary>Where each numbered region starts, in the report's own words.</summary>
    private static readonly string[] BlockStarts =
        ["Auto delay proposal", "Channel", "Notes:", "Table —"];

    /// <summary>What the drawing pass needs, in window coordinates.</summary>
    internal sealed record Layout(
        Rectangle Controls, IReadOnlyList<Rectangle> Blocks, Rectangle? Confidence);

    /// <summary>
    /// Runs the proposal, grows the dialog until the whole report shows, and measures
    /// the regions. Called from inside the shot, with the dialog on screen.
    /// </summary>
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

        // The dialog is left at its own size. A long report simply scrolls — that is
        // what the scrollbar is for, and resizing the window to swallow it would make
        // the figure show a dialog nobody's screen shows.
        var report = Reflect.Field<TextBox>(dialog, "textBoxReport");
        report.SelectionStart = 0;
        report.SelectionLength = 0;
        report.ScrollToCaret();
        session.Pump(300);

        return Measure(dialog, report);
    }

    /// <summary>Draws the measured regions over the saved shot.</summary>
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
        // The shot is of the whole window, so the control's position has to be in
        // WINDOW coordinates: its screen position minus the window's own.
        Point screen = report.PointToScreen(Point.Empty);
        var origin = new Point(screen.X - dialog.Left, screen.Y - dialog.Top);

        // The control's OWN text, line endings and all: GetPositionFromCharIndex
        // counts in these characters, so normalizing first shifts every index by one
        // per preceding line — which drifted each box a line up and put the column
        // box thirteen characters to the left of the column it marks.
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

        // A block that scrolls off the bottom is simply cut there.
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

        // The detail the prose singles out: the confidence column, located by the
        // header's own characters rather than by a remembered pixel.
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

        // Everything above the report is the run's own controls.
        var controls = new Rectangle(left - 4, 34, right - left + 8, origin.Y - 44);
        return new Layout(controls, blocks, confidence);
    }

    // The marker has to OPEN a line: "Channel" also occurs inside the notes. Written
    // against whatever line ending the control holds rather than assuming one.
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
