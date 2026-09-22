namespace Resonalyze;

internal sealed partial class VirtualCrossoverAutoSetupDialog
{
    private bool optionsPositioned;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        LayoutBelowChannelTable();
    }

    // Runs once after scaling, so every measurement is already in device pixels; later changes refit in FitToContents.
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

        FitToContents(growOnly: false);
    }

    // A reorder rebuilds the junction rows, so their fields are sized on every fit, not once.
    private void SizeJunctionFields()
    {
        Size fieldSize = LogicalToDeviceUnits(new Size(62, 19));
        Size slopeSize = LogicalToDeviceUnits(new Size(58, 19));
        foreach (JunctionRow junction in junctions)
        {
            junction.MinHz.Size = fieldSize;
            junction.MaxHz.Size = fieldSize;
            junction.MinSlope.Size = slopeSize;
            junction.MaxSlope.Size = slopeSize;
        }
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

    /// <summary>Restacks everything under the two AutoSize tables, whose height changes as junction notes come and
    /// go. Width only grows (the verdicts change on every refit and would make it twitch); height follows the content
    /// except on a refit.</summary>
    private void FitToContents(bool growOnly)
    {
        if (!optionsPositioned)
        {
            return;
        }

        SizeJunctionFields();
        int margin = LogicalToDeviceUnits(12);
        tableChannels.PerformLayout();
        ShiftTo(tableChannels.Bottom + margin, labelJunctions, tableJunctions);
        tableJunctions.PerformLayout();
        ShiftTo(
            tableJunctions.Bottom + margin,
            labelFilters, checkButterworth, checkLinkwitzRiley, checkBessel,
            labelRange, minCrossover, labelDash, maxCrossover, labelHz,
            independentSlopes, reorderBlocks, labelSubElevation, subElevation,
            labelSubElevationUnit, panelPreview, progressPreview);

        // The AutoSize tables can exceed the designed width even at 100% DPI.
        int width = Math.Max(
            ClientSize.Width,
            Math.Max(tableChannels.Right, tableJunctions.Right) + margin);
        SizePreviewCard(width, margin);
        int height = progressPreview.Bottom + margin + buttonApply.Height + margin;
        if (growOnly)
        {
            height = Math.Max(height, ClientSize.Height);
        }

        if (width != ClientSize.Width || height != ClientSize.Height)
        {
            ClientSize = new Size(width, height);
        }
    }

    // Moves a block as one, keeping its internal offsets, so its first control starts at `top`.
    private static void ShiftTo(int top, Control first, params Control[] rest)
    {
        int shift = top - first.Top;
        if (shift == 0)
        {
            return;
        }

        first.Top += shift;
        foreach (Control control in rest)
        {
            control.Top += shift;
        }
    }
}
