namespace Resonalyze;

/// <summary>Checked overlay slots per mode across tab switches. UI-thread only.</summary>
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

    public void Clear() => slotsByMode.Clear();

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
