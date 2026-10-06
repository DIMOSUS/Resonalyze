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

        VirtualCrossoverChannel channel = target.Channel;
        string? other = VirtualCrossoverDroppedFile.OtherDocument(target.Button, path);
        // The slot the file was dropped on, taken before the first await as a pick takes it: L/R, Mono or an import can
        // reroute the side meanwhile. See docs/tech/virtual-dsp-panel.md#source-loading.
        bool rightSide = channel.ActiveRight;
        VirtualCrossoverChannelState state = channel.SideState(rightSide);
        VirtualCrossoverChannelSettings settings = channel.SideSettings(rightSide);
        // Only a measurement that will load takes a revision: any other would refuse a source load in flight.
        int revision = other == null && target.Button == VirtualCrossoverDropButton.Source
            ? state.BeginSourceLoad()
            : 0;
        // Explorer waits inside the drop until it returns, and a response file stops to ask what it carries.
        await Task.Yield();
        if (IsDisposed || !channelControls.ContainsKey(channel))
        {
            return;
        }

        if (other != null)
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
                await LoadSourceFileAsync(channel, state, settings, revision, path);
                break;

            case VirtualCrossoverDropButton.SpatialAverage:
                AttachSpatialAverage(channel, state, settings, path);
                break;

            default:
                ImportFirFile(channel, settings, path);
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
