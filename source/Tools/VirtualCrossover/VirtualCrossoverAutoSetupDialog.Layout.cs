namespace Resonalyze;

internal sealed partial class VirtualCrossoverAutoSetupDialog
{
    private bool optionsPositioned;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        LayoutBelowChannelTable();
    }

    // Runs once after scaling, so every measurement is already in device pixels.
    private void LayoutBelowChannelTable()
    {
        if (optionsPositioned)
        {
            return;
        }

        optionsPositioned = true;

        // Runtime-added controls miss the form's font autoscale; size them in device units before measuring the row.
        Size comboSize = LogicalToDeviceUnits(new Size(110, 19));
        Size arrowSize = LogicalToDeviceUnits(new Size(22, 19));
        foreach (ChannelRow row in rows.Values)
        {
            row.TypeComboBox.Size = comboSize;
            row.Up.Size = arrowSize;
            row.Down.Size = arrowSize;
        }

        Size fieldSize = LogicalToDeviceUnits(new Size(62, 19));
        Size slopeSize = LogicalToDeviceUnits(new Size(58, 19));
        foreach (JunctionRow junction in junctions)
        {
            junction.MinHz.Size = fieldSize;
            junction.MaxHz.Size = fieldSize;
            junction.MinSlope.Size = slopeSize;
            junction.MaxSlope.Size = slopeSize;
        }

        tableChannels.PerformLayout();
        int outsideMargin = LogicalToDeviceUnits(12);
        int shift = tableChannels.Bottom + outsideMargin - labelJunctions.Top;
        foreach (Control control in new Control[] { labelJunctions, tableJunctions })
        {
            control.Top += shift;
        }

        tableJunctions.PerformLayout();
        shift = tableJunctions.Bottom + outsideMargin - labelFilters.Top;
        foreach (Control control in new Control[]
                 {
                     labelFilters, checkButterworth, checkLinkwitzRiley, checkBessel,
                     labelRange, minCrossover, labelDash, maxCrossover, labelHz,
                     independentSlopes, reorderBlocks, labelSubElevation, subElevation,
                     labelSubElevationUnit, panelPreview, progressPreview
                 })
        {
            control.Top += shift;
        }

        // The AutoSize tables can exceed the designed width even at 100% DPI.
        int clientWidth = Math.Max(
            ClientSize.Width,
            Math.Max(tableChannels.Right, tableJunctions.Right) + outsideMargin);
        SizePreviewCard(clientWidth, outsideMargin);
        ClientSize = new Size(
            clientWidth,
            progressPreview.Bottom + outsideMargin + buttonApply.Height + outsideMargin);
    }

    /// <summary>Sizes the result card to its text and parks the progress bar under it. Measured as laid out
    /// (summaries wrap), floored at the structural line count so the card does not jump about between refits.</summary>
    private void SizePreviewCard(int clientWidth, int outsideMargin)
    {
        panelPreview.Width = clientWidth - panelPreview.Left - outsideMargin;
        labelPreview.Width =
            panelPreview.Width - panelPreview.Padding.Left - panelPreview.Padding.Right;
        labelPreview.Height = Math.Max(
            (session is null ? 0 : AutoSetupWizardReport.PreviewLineCount(session)) * labelPreview.Font.Height,
            TextRenderer.MeasureText(
                labelPreview.Text,
                labelPreview.Font,
                new Size(labelPreview.Width, int.MaxValue),
                TextFormatFlags.WordBreak).Height);
        panelPreview.Height =
            labelPreview.Height + panelPreview.Padding.Top + panelPreview.Padding.Bottom;
        progressPreview.Top = panelPreview.Bottom + LogicalToDeviceUnits(6);
        progressPreview.Width = panelPreview.Width;
    }

    /// <summary>The verdict column and the amber notes arrive after the one-shot layout pass has sized the window,
    /// and both are AutoSize labels, so the dialog has to be allowed to grow around them. It only ever grows: a
    /// window that shrank back on every refit would twitch.</summary>
    private void GrowToFitContents()
    {
        if (!optionsPositioned)
        {
            return;
        }

        int margin = LogicalToDeviceUnits(12);
        tableJunctions.PerformLayout();
        int width = Math.Max(
            ClientSize.Width,
            Math.Max(tableChannels.Right, tableJunctions.Right) + margin);
        SizePreviewCard(width, margin);
        int height = progressPreview.Bottom + margin + buttonApply.Height + margin;
        if (width > ClientSize.Width || height > ClientSize.Height)
        {
            ClientSize = new Size(width, Math.Max(height, ClientSize.Height));
        }
    }
}
