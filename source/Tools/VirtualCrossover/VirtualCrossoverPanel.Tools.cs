namespace Resonalyze;

public partial class VirtualCrossoverPanel
{
    private ContextMenuStrip? toolsMenu;

    /// <summary>The occasional tools, off the main tuning path: audition and overlay capture.</summary>
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
            "Capture to overlay…",
            null,
            async (_, _) => await CaptureToOverlayAsync().ConfigureAwait(true))
        {
            ToolTipText =
                "Save a block, a side's Sum or L+R as drawn into a Frequency\r\n" +
                "Response overlay slot, to compare with a measurement of the\r\n" +
                "real system or with a later tune."
        });
        ShowMenu(buttonTools, toolsMenu);
    }
}
