namespace Resonalyze;

public partial class EqWizardPanel
{
    // Layout is DELTAS on the designer's (already DPI-scaled) sizes; absolute coordinates would stick at 96 DPI (see AGENTS.md).
    private Size baselineClientSize;
    private Size baselinePlotSize;

    // Designer gap below the plot, measured from its CURRENT bottom each pass so it holds while scrolled.
    private readonly List<(Control Control, int Offset)> bottomRiders = [];

    private bool layoutInProgress;

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

    // Scale the baseline only on the panel's OWN autoscale (declared size still differs from current); the shell's
    // cascade resizes the panel alone, and scaling on both left the baseline a whole factor ahead (1.5625 at 125%).
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        bool rearrangesChildren = AutoScaleDimensions != CurrentAutoScaleDimensions;
        Padding padding = Padding;

        base.ScaleControl(factor, specified);

        if (!rearrangesChildren)
        {
        // Padding is part of the arrangement: scaling it on both passes doubles it (6 -> 12 -> 24 at 192 DPI).
            Padding = padding;
            return;
        }

        if (baselineClientSize.IsEmpty)
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

    // Before the base pass, which sizes the AutoScroll area and anchors from the plot's final size.
    protected override void OnLayout(LayoutEventArgs e)
    {
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

    /// <summary>Extra room goes to the plot only (the bank is a fixed 16x2 percent grid); below designer size the panel scrolls.</summary>
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
