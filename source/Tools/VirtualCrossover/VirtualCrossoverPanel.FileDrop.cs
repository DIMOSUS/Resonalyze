namespace Resonalyze;

/// <summary>A file dropped on a block's Source, MMM or FIR button lands where that button's own file pick would put it,
/// on the side shown when it was dropped.</summary>
public partial class VirtualCrossoverPanel
{
    /// <summary>A block button answers for a drop on it, the window's routing included: it refuses what it does not take.</summary>
    internal bool OwnsFileDrop(Control over) => DropButtonAt(over) != null;

    /// <summary>By name only: asked on every drag move.</summary>
    internal bool TakesFileDrop(Control over, string path) =>
        DropButtonAt(over) is { } target && VirtualCrossoverDroppedFile.Takes(target.Button, path);

    internal async Task DropFileAsync(Control over, string path)
    {
        if (DropButtonAt(over) is not { } target || !VirtualCrossoverDroppedFile.Takes(target.Button, path))
        {
            return;
        }

        // The side the file was dropped on, whatever the user switches to before the load starts.
        bool rightSide = target.Channel.ActiveRight;
        // Explorer waits inside the drop until it returns, and a response file stops to ask what it carries.
        await Task.Yield();
        if (IsDisposed || !channelControls.ContainsKey(target.Channel))
        {
            return;
        }

        if (VirtualCrossoverDroppedFile.OtherDocument(target.Button, path) is { } other)
        {
            ShowMessage(
                $"'{Path.GetFileName(path)}' is {other}.",
                "Virtual DSP",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        switch (target.Button)
        {
            case VirtualCrossoverDropButton.Source:
                await LoadSourceFileAsync(target.Channel, rightSide, path);
                break;

            case VirtualCrossoverDropButton.SpatialAverage:
                AttachSpatialAverage(target.Channel, rightSide, path);
                break;

            default:
                ImportFirFile(target.Channel, rightSide, path);
                break;
        }
    }

    private (VirtualCrossoverChannel Channel, VirtualCrossoverDropButton Button)? DropButtonAt(Control over)
    {
        foreach ((VirtualCrossoverChannel channel, VirtualCrossoverChannelControl card) in channelControls)
        {
            if (over == card.SourceButton)
            {
                return (channel, VirtualCrossoverDropButton.Source);
            }

            if (over == card.SpatialAverageButton)
            {
                return (channel, VirtualCrossoverDropButton.SpatialAverage);
            }

            if (over == card.FirButton)
            {
                return (channel, VirtualCrossoverDropButton.Fir);
            }
        }

        return null;
    }
}
