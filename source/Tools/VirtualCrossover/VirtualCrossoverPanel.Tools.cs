namespace Resonalyze;

public partial class VirtualCrossoverPanel
{
    private ContextMenuStrip? toolsMenu;

    /// <summary>
    /// The occasional tools, off the main tuning path: auditioning the tune through a music file and capturing the
    /// sum as an overlay. Both used to hold a row each in a column that has run out of them, and neither belongs in
    /// the sequence of buttons the tune is actually built with.
    /// </summary>
    private void ShowToolsMenu()
    {
        if (toolsMenu is { Visible: true })
        {
            toolsMenu.Close();
            return;
        }

        toolsMenu?.Dispose();
        toolsMenu = new ContextMenuStrip();
        toolsMenu.Items.Add(new ToolStripMenuItem(
            "Audition track…",
            null,
            async (_, _) => await AuditionTrackAsync().ConfigureAwait(true))
        {
            ToolTipText =
                "Render a music file through the tune into a stereo WAV: the\r\n" +
                "left sum on channel 1, the right on channel 2.\r\n" +
                "Listen through HEADPHONES only."
        });
        toolsMenu.Items.Add(new ToolStripMenuItem(
            "Capture to overlay",
            null,
            async (_, _) => await CaptureSumToOverlayAsync().ConfigureAwait(true))
        {
            ToolTipText =
                "Keep the current sum as an overlay curve, so a later tune can\r\n" +
                "be compared against this one on the same plot."
        });
        DropDownMenu.ShowUnder(buttonTools, toolsMenu);
    }
}
