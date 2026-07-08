namespace Resonalyze;

/// <summary>
/// Remembers which overlay slots are checked per overlay mode across tab
/// switches, replacing the raw dictionary on <c>Form1</c>: the shell captures
/// the active set when leaving a mode, restores it when entering one, and the
/// Virtual DSP overlay capture arms its freshly saved slot so it arrives
/// already checked. UI-thread only.
/// </summary>
internal sealed class ActiveOverlaySlotTracker
{
    private readonly Dictionary<Mode, List<int>> slotsByMode = new();

    public void Store(Mode overlayMode, List<int> activeSlots)
    {
        slotsByMode[overlayMode] = activeSlots;
    }

    public bool TryGet(Mode overlayMode, out List<int> activeSlots)
    {
        if (slotsByMode.TryGetValue(overlayMode, out List<int>? stored))
        {
            activeSlots = stored;
            return true;
        }

        activeSlots = new List<int>();
        return false;
    }

    public void MarkActive(Mode overlayMode, int slot)
    {
        if (!slotsByMode.TryGetValue(overlayMode, out List<int>? active))
        {
            active = new List<int>();
            slotsByMode[overlayMode] = active;
        }

        if (!active.Contains(slot))
        {
            active.Add(slot);
        }
    }
}
