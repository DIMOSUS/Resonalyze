namespace Resonalyze;

/// <summary>The Frequency Response overlay slots for a tool that writes one without the overlay panel.</summary>
internal interface IFrequencyResponseOverlaySlots
{
    List<OverlaySlotOccupant> ReadFrequencyResponseOverlaySlots();

    /// <param name="smoothingCode">The smoothing already in <paramref name="points"/>.</param>
    void SaveFrequencyResponseOverlay(int slot, string title, OverlayPoint[] points, int smoothingCode);
}
