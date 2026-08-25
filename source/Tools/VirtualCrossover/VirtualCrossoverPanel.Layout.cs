namespace Resonalyze;

public partial class VirtualCrossoverPanel
{
    // The designer's arrangement, captured before anything moves it: the panel's
    // client size and the two plots' sizes. Everything the layout pass does is a
    // DELTA on these, never an absolute coordinate — the designer's numbers are
    // the ones the font autoscale has already put into the running DPI's units,
    // and a coordinate written here would be stuck at 96 DPI (see AGENTS.md).
    private Size baselineClientSize;
    private Size baselineMainPlotSize;
    private Size baselineDspPlotSize;

    // Everything that rides at a fixed distance UNDER the main plot: the Curves
    // and View rows, the two buttons that line up with the DSP plot's top, and
    // the DSP plot itself. Held as the designer's gap below the plot's bottom
    // edge, so those gaps are what survives every window size. Measured from the
    // plot's CURRENT bottom on each pass, which keeps them right while the panel
    // is scrolled (AutoScroll shifts the children out from under their own
    // coordinates, and both ends of the offset shift together).
    private readonly List<(Control Control, int Offset)> plotFollowers = [];

    // The bottom row (the button stack, the DSP mode selector) is bottom-anchored
    // in the designer instead, so WinForms carries it and this pass never touches
    // it. The channel column and its buttons on the left were already anchored.
    private bool layoutInProgress;

    // Captured at the end of construction, when the controls still stand where
    // the designer put them.
    private void CaptureLayoutBaseline()
    {
        baselineClientSize = ClientSize;
        baselineMainPlotSize = mainPlotView.Size;
        baselineDspPlotSize = dspPlotView.Size;
        int bottom = mainPlotView.Bottom;
        foreach (Control control in new Control[]
        {
            labelCurves,
            checkBoxShowSum,
            checkBoxShowLoss,
            checkBoxShowTarget,
            numericTargetLevel,
            buttonTargetSettings,
            labelCalibration,
            comboBoxCalibration,
            checkBoxHybrid,
            labelView,
            panel1,
            labelSmoothing,
            comboBoxSmoothing,
            buttonPhaseGate,
            buttonAutoSetup,
            buttonAutoDelay,
            dspPlotView
        })
        {
            plotFollowers.Add((control, control.Top - bottom));
        }
    }

    // Container autoscaling (a font change, a DPI move) scales the controls; the
    // baseline is in the same units and has to follow, or the next layout pass
    // would add 96-DPI deltas to scaled controls. The same reason
    // VirtualCrossoverChannelControl scales its parked height.
    //
    // But only when the pass in question actually moves the children, and one of
    // the two this panel gets does not. Its OWN auto-scale rearranges everything
    // inside it; the shell's cascade afterwards resizes the panel ALONE, because
    // ContainerControl.ScaleChildren is false for a container that declares an
    // AutoScaleMode. Scaled on both, the baseline ends a whole factor ahead of the
    // arrangement it is supposed to measure: at 125% it read 1.5625, so the stretch
    // sized the plots for a panel a quarter wider than the one they sit in and the
    // panel came up with both scrollbars and the plots cut off at the right.
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
        baselineMainPlotSize = Scale(baselineMainPlotSize, factor);
        baselineDspPlotSize = Scale(baselineDspPlotSize, factor);
        for (int index = 0; index < plotFollowers.Count; index++)
        {
            (Control control, int offset) = plotFollowers[index];
            plotFollowers[index] = (control, (int)Math.Round(offset * factor.Height));
        }
    }

    private static Size Scale(Size size, SizeF factor) => new(
        (int)Math.Round(size.Width * factor.Width),
        (int)Math.Round(size.Height * factor.Height));

    // BEFORE the base pass, not after: the base is what sizes the scrollable
    // area from the children (AutoScroll) and settles the anchored ones, and it
    // has to see the plots at their final size. Run afterwards, it measured the
    // scroll area against plots this pass was about to shrink, and the panel came
    // back from a maximized window scrolled sideways with both bars stuck on —
    // the children were right, the viewport around them was not.
    protected override void OnLayout(LayoutEventArgs e)
    {
        // Resizing a child re-enters this. The guard keeps the stretch itself
        // from nesting, while the base pass below still runs for every one of
        // those calls, which is what keeps the scroll area in step.
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
    /// Spends the room a bigger window gives the panel on the two plots: the
    /// extra width on both (they share a right edge, so they keep it), the extra
    /// height SPLIT BETWEEN THEM IN THE DESIGNER'S PROPORTION — the acoustic plot
    /// is the one being read, the DSP plot is its companion, and a split that
    /// grew one of them alone would end up hiding that relation. The rows between
    /// them ride down with the acoustic plot's bottom edge. Below the designer's
    /// size nothing shrinks: the panel scrolls instead (AutoScroll), the way it
    /// did before it could stretch at all.
    /// </summary>
    private void ApplyStretchLayout()
    {
        if (baselineClientSize.IsEmpty || plotFollowers.Count == 0)
        {
            return;
        }

        int extraWidth = Math.Max(0, ClientSize.Width - baselineClientSize.Width);
        int extraHeight = Math.Max(0, ClientSize.Height - baselineClientSize.Height);
        int plotHeights = baselineMainPlotSize.Height + baselineDspPlotSize.Height;
        int mainGrowth = plotHeights > 0
            ? (int)Math.Round(
                extraHeight * (double)baselineMainPlotSize.Height / plotHeights)
            : 0;

        mainPlotView.SetBounds(
            0,
            0,
            baselineMainPlotSize.Width + extraWidth,
            baselineMainPlotSize.Height + mainGrowth,
            BoundsSpecified.Size);

        int bottom = mainPlotView.Bottom;
        foreach ((Control control, int offset) in plotFollowers)
        {
            control.Top = bottom + offset;
        }

        dspPlotView.SetBounds(
            0,
            0,
            baselineDspPlotSize.Width + extraWidth,
            baselineDspPlotSize.Height + (extraHeight - mainGrowth),
            BoundsSpecified.Size);
    }
}
