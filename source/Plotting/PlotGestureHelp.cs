namespace Resonalyze;

/// <summary>One line of the graph help: a gesture, and what it does.</summary>
internal readonly record struct PlotGestureHelpEntry(string Gesture, string Effect);

/// <summary>A group of gestures under one heading.</summary>
internal sealed record PlotGestureHelpSection(
    string Title,
    IReadOnlyList<PlotGestureHelpEntry> Entries);

/// <summary>
/// The graph controls in the words the user reads them in — what F1 puts on screen.
///
/// It is a list rather than a hand-laid dialog because it is the SAME map twice
/// over: <see cref="PlotGestureController"/> binds it, and the "Graph Zoom and
/// Limits" table in REFERENCE.md tabulates it. A gesture that changes has three
/// places to change, and keeping this one in a form nobody has to lay out again is
/// what makes the third cheap enough to actually do.
///
/// The wording is deliberately shorter than the reference's: this is a card to
/// glance at with one hand on the mouse, not the place a behaviour is explained.
/// </summary>
internal static class PlotGestureHelp
{
    /// <summary>What the window is called, and the line under its title.</summary>
    public const string Title = "Graph controls";

    public const string Introduction =
        "The analysis plot, the Time Alignment previews, the EQ Wizard and the " +
        "Virtual DSP graphs all take these. Small previews inside settings panels " +
        "are fixed-scale by design and take none of them.";

    public static IReadOnlyList<PlotGestureHelpSection> Sections { get; } =
    [
        new PlotGestureHelpSection("Zoom",
        [
            new PlotGestureHelpEntry("Wheel", "Both axes, around the pointer"),
            new PlotGestureHelpEntry("Alt + wheel", "The same, in fine steps"),
            new PlotGestureHelpEntry("Shift + wheel", "The horizontal axis only"),
            new PlotGestureHelpEntry("Ctrl + wheel", "The vertical axis only"),
            new PlotGestureHelpEntry("Wheel over an axis", "That axis alone"),
            new PlotGestureHelpEntry(
                "Wheel over the end of an axis",
                "Moves that one limit, leaving the other end"),
            new PlotGestureHelpEntry(
                "Middle-button drag",
                "Variable zoom: sideways works the horizontal axis, up and down the vertical one"),
            new PlotGestureHelpEntry("X / Shift + X", "The horizontal axis out / in by about two"),
            new PlotGestureHelpEntry("Y / Shift + Y", "The vertical axis out / in by about two"),
            new PlotGestureHelpEntry(
                "The + / − buttons on the graph",
                "Zoom the axis they sit against, click after click"),
        ]),
        new PlotGestureHelpSection("Zoom box",
        [
            new PlotGestureHelpEntry(
                "Ctrl + right-button drag",
                "Draws a box and measures it in the units of the axes"),
            new PlotGestureHelpEntry(
                "Click inside the box",
                "Zooms to it — only the axes that allow zoom move"),
            new PlotGestureHelpEntry(
                "Click elsewhere, or Esc",
                "Lets the box go, leaving the scale where it is"),
        ]),
        new PlotGestureHelpSection("Pan, fit and limits",
        [
            new PlotGestureHelpEntry("Right-button drag", "Pans"),
            new PlotGestureHelpEntry("Ctrl + Alt + F", "Fits the view to the data"),
            new PlotGestureHelpEntry("Ctrl + Alt + Y", "Fits the vertical axis to the data"),
            new PlotGestureHelpEntry("Home, or A", "Back to the view's own default scale"),
            new PlotGestureHelpEntry("Double click", "Opens the graph limits dialog"),
        ]),
        new PlotGestureHelpSection("Undo and help",
        [
            new PlotGestureHelpEntry(
                "Ctrl + Z",
                "Steps back through the zoom moves (a wheel notch is its own undo — scroll it back)"),
            new PlotGestureHelpEntry("F1", "This window"),
        ]),
    ];
}
