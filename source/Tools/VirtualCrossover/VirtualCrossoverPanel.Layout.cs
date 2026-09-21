namespace Resonalyze;

public partial class VirtualCrossoverPanel
{
    // Layout applies deltas on the designer baseline, never absolute coordinates (those would be stuck at 96 DPI; see AGENTS.md).
    private Size baselineClientSize;
    private Size baselineMainPlotSize;
    private Size baselineDspPlotSize;

    // Designer gaps below the plot, measured from its current bottom each pass so AutoScroll offsets cancel.
    private readonly List<(Control Control, int Offset)> plotFollowers = [];

    private bool layoutInProgress;

    private void CaptureLayoutBaseline()
    {
        baselineClientSize = ClientSize;
        baselineMainPlotSize = mainPlotView.Size;
        baselineDspPlotSize = dspPlotView.Size;
        int bottom = mainPlotView.Bottom;
        foreach (Control control in new Control[]
        {
            labelCurves,
            curvesPanel,
            labelCalibration,
            comboBoxCalibration,
            checkBoxHybrid,
            labelView,
            panel1,
            labelSmoothing,
            comboBoxSmoothing,
            labelGroupView,
            comboBoxGroupView,
            buttonPhaseGate,
            buttonDspProcessor,
            buttonAutoSetup,
            buttonTuneJunction,
            buttonAutoDelay,
            buttonAi,
            dspPlotView
        })
        {
            plotFollowers.Add((control, control.Top - bottom));
        }
    }

    // Scale the baseline only on the panel's own auto-scale pass (declared size still differs): the shell's cascade resizes the panel alone,
    // and scaling on both left it a whole factor ahead (1.5625 at 125%), cutting the plots off.
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        bool rearrangesChildren = AutoScaleDimensions != CurrentAutoScaleDimensions;
        Padding padding = Padding;

        base.ScaleControl(factor, specified);

        if (!rearrangesChildren)
        {
        // Padding is part of the arrangement, so the non-arranging pass must not scale it (it doubled to 24 at 192 DPI).
            Padding = padding;
            return;
        }

        if (baselineClientSize.IsEmpty)
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

    // Stretch before the base pass: base sizes the AutoScroll area from the children and must see the final plot sizes.
    protected override void OnLayout(LayoutEventArgs e)
    {
        // Resizing a child re-enters; the guard stops nesting while the base pass still runs each time.
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

    /// <summary>Extra width goes to both plots, extra height is split in the designer's proportion; below designer size the panel scrolls.</summary>
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
