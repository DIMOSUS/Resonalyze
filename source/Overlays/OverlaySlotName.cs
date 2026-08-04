namespace Resonalyze;

/// <summary>
/// Shortens a slot title for the narrow name label in the overlay panel.
/// </summary>
/// <remarks>
/// A captured or imported slot is titled "Overlay {slot}: …", and the capture button
/// right beside the label already shows the number, so that prefix is pure repetition in
/// a label this narrow. Only that exact generated form is removed (plus the bare
/// "{slot}: " an older file may carry): a title the user typed is shown as it is, so
/// "Overlayed response", "Overlay correction", "4 Ω response" and "2 way" survive intact.
/// A prefix naming a different slot also survives — there it is information, not noise.
/// </remarks>
internal static class OverlaySlotName
{
    /// <summary>
    /// The name a slot takes when a curve is saved into it. An occupied slot keeps
    /// its current name — the user may have renamed it, and a re-capture updates the
    /// curve, not the label — while an empty (never used or cleared) slot gets the
    /// automatic "Overlay {slot}: {source}" form.
    /// </summary>
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
            // A title that is nothing but the prefix keeps it: an empty label would
            // read as an empty slot.
            return rest.Length > 0 ? rest : title;
        }

        return title;
    }
}
