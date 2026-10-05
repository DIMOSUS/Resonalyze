namespace Resonalyze;

/// <summary>What a slot's file holds, read without loading it into the panel: a tool that writes a slot lists them.</summary>
/// <param name="Title">Null for a free slot and for an unreadable file.</param>
internal sealed record OverlaySlotOccupant(int Slot, string? Title, OverlayKind? Kind, bool Unreadable = false)
{
    public bool IsFree => Title == null && !Unreadable;

    public static List<OverlaySlotOccupant> ReadAll(Mode mode, string? storageRoot = null)
    {
        var occupants = new List<OverlaySlotOccupant>(OverlayFile.MaximumSlotCount);
        for (int slot = 1; slot <= OverlayFile.MaximumSlotCount; slot++)
        {
            try
            {
                occupants.Add(OverlayFile.Load(mode, slot, storageRoot) is { } file
                    ? new OverlaySlotOccupant(slot, file.Title, file.Kind)
                    : new OverlaySlotOccupant(slot, null, null));
            }
            catch (Exception)
            {
                occupants.Add(new OverlaySlotOccupant(slot, null, null, Unreadable: true));
            }
        }

        return occupants;
    }
}
