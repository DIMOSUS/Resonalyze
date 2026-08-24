namespace Resonalyze;

public partial class EqWizardPanel
{
    // The designer's arrangement, captured before anything moves it: the panel's
    // client size and the plot's size. Everything this pass does is a DELTA on
    // these, never an absolute coordinate — the designer's numbers are the ones
    // the font autoscale has already put into the running DPI's units, and a
    // coordinate written here would be stuck at 96 DPI (see AGENTS.md).
    private Size baselineClientSize;
    private Size baselinePlotSize;

    // The two blocks that sit at the bottom-left of the panel and keep their own
    // size: the PEQ strip bank and the auto-tune box. Held as the designer's gap
    // below the plot's bottom edge, so a taller window moves them down by exactly
    // what the plot grew and the gaps are what survives every window size.
    // Measured from the plot's CURRENT bottom on each pass, which keeps them right
    // while the panel is scrolled (AutoScroll shifts the children out from under
    // their own coordinates, and both ends of the offset shift together).
    private readonly List<(Control Control, int Offset)> bottomRiders = [];

    private bool layoutInProgress;

    // Captured at the end of construction, when the controls still stand where the
    // designer put them.
    private void CaptureLayoutBaseline()
    {
        baselineClientSize = ClientSize;
        baselinePlotSize = plotWizard.Size;
        int bottom = plotWizard.Bottom;
        foreach (Control control in new Control[] { panelPEQ, panelAutoTune })
        {
            bottomRiders.Add((control, control.Top - bottom));
        }
    }

    // Container autoscaling (a font change, a DPI move) scales the controls; the
    // baseline is in the same units and has to follow, or the next layout pass
    // would add 96-DPI deltas to scaled controls.
    //
    // But only when the pass in question actually moves the children, and one of
    // the two this panel gets does not. Its OWN auto-scale rearranges everything
    // inside it; the shell's cascade afterwards resizes the panel ALONE, because
    // ContainerControl.ScaleChildren is false for a container that declares an
    // AutoScaleMode. Scaled on both, the baseline ends a whole factor ahead of the
    // arrangement it is supposed to measure: at 125% it read 1.5625, so the stretch
    // sized the plot for a panel a quarter wider than the one it sits in and the
    // panel came up with both scrollbars and the plot cut off at the right.
    // The panel's own pass is the one where its declared dimensions still differ
    // from the current ones — the auto-scale is what brings them into step.
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        bool rearrangesChildren = AutoScaleDimensions != CurrentAutoScaleDimensions;
        base.ScaleControl(factor, specified);
        if (baselineClientSize.IsEmpty || !rearrangesChildren)
        {
            return;
        }

        baselineClientSize = Scale(baselineClientSize, factor);
        baselinePlotSize = Scale(baselinePlotSize, factor);
        for (int index = 0; index < bottomRiders.Count; index++)
        {
            (Control control, int offset) = bottomRiders[index];
            bottomRiders[index] = (control, (int)Math.Round(offset * factor.Height));
        }
    }

    private static Size Scale(Size size, SizeF factor) => new(
        (int)Math.Round(size.Width * factor.Width),
        (int)Math.Round(size.Height * factor.Height));

    // BEFORE the base pass, not after: the base is what sizes the scrollable area
    // from the children (AutoScroll) and settles the anchored ones, and it has to
    // see the plot at its final size.
    protected override void OnLayout(LayoutEventArgs e)
    {
        // Resizing a child re-enters this. The guard keeps the stretch itself from
        // nesting, while the base pass below still runs for every one of those
        // calls, which is what keeps the scroll area in step.
        if (!layoutInProgress)
        {
            layoutInProgress = true;
            try
            {
                ApplyStretchLayout();
            }
            finally
            {
                layoutInProgress = false;
            }
        }

        base.OnLayout(e);
    }

    /// <summary>
    /// Spends the room a bigger window gives the panel on the plot alone: it takes
    /// the extra width out to the panel's right edge and the extra height down to
    /// the PEQ bank, which rides down with it at the designer's gap. The bank and
    /// the auto-tune box keep their size and stay in the bottom-left corner: the
    /// bank is a FIXED 16x2 grid of strips on percent styles, so room given to it
    /// would enlarge the strips rather than show more of them, and the curve being
    /// equalized is what a bigger window is opened for. Below the designer's size
    /// nothing shrinks: the panel scrolls instead (AutoScroll), the way it did
    /// before it could stretch at all.
    /// </summary>
    private void ApplyStretchLayout()
    {
        if (baselineClientSize.IsEmpty || bottomRiders.Count == 0)
        {
            return;
        }

        int extraWidth = Math.Max(0, ClientSize.Width - baselineClientSize.Width);
        int extraHeight = Math.Max(0, ClientSize.Height - baselineClientSize.Height);

        plotWizard.SetBounds(
            0,
            0,
            baselinePlotSize.Width + extraWidth,
            baselinePlotSize.Height + extraHeight,
            BoundsSpecified.Size);

        int bottom = plotWizard.Bottom;
        foreach ((Control control, int offset) in bottomRiders)
        {
            control.Top = bottom + offset;
        }
    }
}
