namespace Resonalyze;

/// <summary>Strips only this slot's generated "Overlay {slot}:" prefix (or a legacy bare "{slot}:"); user-typed titles stay intact.</summary>
internal static class OverlaySlotName
{
    /// <summary>An occupied slot keeps its (possibly renamed) name; an empty one gets the automatic form.</summary>
    public static string ForSave(
        bool slotOccupied,
        string currentTitle,
        int slot,
        string sourceName) =>
        slotOccupied ? currentTitle : $"Overlay {slot}: {sourceName}";

    public static string Shorten(string title, int slot)
    {
        ArgumentNullException.ThrowIfNull(title);

        string number = slot.ToString();
        foreach (string prefix in (string[])[$"Overlay {number}:", $"{number}:"])
        {
            if (!title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string rest = title[prefix.Length..].TrimStart();
            // An empty label would read as an empty slot.
            return rest.Length > 0 ? rest : title;
        }

        return title;
    }
}
