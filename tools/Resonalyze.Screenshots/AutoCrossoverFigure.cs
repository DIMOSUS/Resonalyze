namespace Resonalyze.Screenshots;

/// <summary>Auto crossover figure. Its regions are MEASURED off the live dialog rather than read from the rendered
/// file: the dialog lays itself out at runtime — both tables grow with the channel count and the result card is sized
/// to its own text — so hand-read coordinates would be stale the first time a car with a different chain is shot.</summary>
internal static class AutoCrossoverFigure
{
    internal sealed record Layout(
        Rectangle Drivers,
        Rectangle Junctions,
        Rectangle Options,
        Rectangle Proposal,
        Rectangle Verdicts,
        Point Background);

    public static Layout Measure(Form dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        var drivers = Reflect.Field<Control>(dialog, "tableChannels");
        var junctionsHeading = Reflect.Field<Control>(dialog, "labelJunctions");
        var junctions = Reflect.Field<Control>(dialog, "tableJunctions");
        var filters = Reflect.Field<Control>(dialog, "labelFilters");
        var elevationUnit = Reflect.Field<Control>(dialog, "labelSubElevationUnit");
        var proposal = Reflect.Field<Control>(dialog, "panelPreview");

        Rectangle Frame(Control control, int pad)
        {
            Point screen = control.PointToScreen(Point.Empty);
            return new Rectangle(
                screen.X - dialog.Left - pad,
                screen.Y - dialog.Top - pad,
                control.Width + (2 * pad),
                control.Height + (2 * pad));
        }

        Rectangle driverBox = Frame(drivers, 6);
        // The heading goes INSIDE the box rather than under its top edge: a box drawn through a label is exactly
        // what the figure convention forbids, and the line is what the zone is for anyway.
        Rectangle junctionBox = Rectangle.Union(Frame(junctionsHeading, 5), Frame(junctions, 6));
        Rectangle filterBox = Frame(filters, 6);
        Rectangle elevationBox = Frame(elevationUnit, 6);
        Rectangle proposalBox = Frame(proposal, 4);

        // One box over the whole options block: they are read as a group, and boxing each row would bury the figure.
        var options = new Rectangle(
            filterBox.X,
            filterBox.Y,
            Math.Max(junctionBox.Right, proposalBox.Right) - filterBox.X,
            elevationBox.Bottom - filterBox.Y);

        // The verdict column, which is the only part of a junction row the wizard writes rather than the user.
        Rectangle verdicts = VerdictColumn(dialog, junctionBox);

        // Between the last option row and the result card: dialog background, whatever the chain looks like.
        var background = new Point(
            Math.Max(junctionBox.Right, proposalBox.Right) - 8,
            (options.Bottom + proposalBox.Top) / 2);
        return new Layout(driverBox, junctionBox, options, proposalBox, verdicts, background);
    }

    public static void Draw(Layout layout, string path)
    {
        ArgumentNullException.ThrowIfNull(layout);

        using Annotate figure = Annotate.Open(path);
        figure.Gutter(46, onLeft: true, sample: layout.Background)
              .Region(layout.Drivers, "1", Badge(layout.Drivers), leader: true, badgeRadius: 13)
              .Region(layout.Junctions, "2", Badge(layout.Junctions), leader: true, badgeRadius: 13)
              .Region(layout.Options, "3", Badge(layout.Options), leader: true, badgeRadius: 13)
              .Region(layout.Proposal, "4", Badge(layout.Proposal), leader: true, badgeRadius: 13)
              .Detail(layout.Verdicts)
              .Save(path);
    }

    // In the gutter, level with the middle of its box.
    private static Point Badge(Rectangle box) => new(23, box.Top + (box.Height / 2));

    private static Rectangle VerdictColumn(Form dialog, Rectangle junctionBox)
    {
        var rows = (System.Collections.IEnumerable)Reflect.Field<object>(dialog, "junctions");
        int left = int.MaxValue;
        int top = int.MaxValue;
        int right = 0;
        int bottom = 0;
        foreach (object? row in rows)
        {
            if (row is null)
            {
                continue;
            }

            var verdict = (Control)row.GetType().GetProperty("Verdict")!.GetValue(row)!;
            Point screen = verdict.PointToScreen(Point.Empty);
            int x = screen.X - dialog.Left;
            int y = screen.Y - dialog.Top;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x + verdict.Width);
            bottom = Math.Max(bottom, y + verdict.Height);
        }

        return left == int.MaxValue
            ? junctionBox
            : new Rectangle(left - 5, top - 4, right - left + 10, bottom - top + 8);
    }
}
