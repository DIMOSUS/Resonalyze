namespace Resonalyze;

/// <summary>
/// What a file handed to the shell — dragged onto the window — turns out to hold.
/// </summary>
internal enum DroppedFileKind
{
    /// <summary>Not a document this application opens.</summary>
    Unknown,

    /// <summary>A Resonalyze impulse response.</summary>
    ImpulseResponse,

    /// <summary>A stored spatial average (the RTA analyzer's moving-mic capture).</summary>
    SpatialAverageCapture,

    /// <summary>A Virtual DSP session.</summary>
    VirtualDspSession,

    /// <summary>A recorded sweep to deconvolve (a WAV).</summary>
    RecordedSweep,

    /// <summary>A REW impulse response export (a text file).</summary>
    RewImpulseResponseExport,

    /// <summary>
    /// An overlay slot file. Recognized only to be refused by name: the overlay panel
    /// owns those files and addresses them by slot, so there is nothing for a drop to
    /// open — and "unsupported format" would be a lie about a file this very
    /// application wrote.
    /// </summary>
    OverlaySlot
}

/// <summary>
/// Decides which of the application's documents a file holds, so a drop can open it in
/// the mode it belongs to instead of asking the user which button it was meant for.
/// </summary>
/// <remarks>
/// The four JSON documents share an extension and are told apart by the <c>format</c>
/// marker each one writes, read out of the head of the file by
/// <see cref="JsonFormatMarker"/> rather than by deserializing candidate types in turn
/// to see which one does not throw.
/// </remarks>
internal static class DroppedFile
{
    /// <summary>
    /// Whether the extension is one the shell opens at all. This is what answers while
    /// a drag is still hovering — it decides the cursor, and it must not touch the disk
    /// for every mouse move over the window. What the file actually IS costs a read and
    /// is answered once, on the drop.
    /// </summary>
    internal static bool HasOpenableExtension(string path)
    {
        string extension = Path.GetExtension(path);
        return string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What <paramref name="path"/> holds, or <see cref="DroppedFileKind.Unknown"/>
    /// when nothing here recognizes it.
    /// </summary>
    /// <remarks>
    /// A WAV and a text file are taken at their extension, exactly as the Load dialog
    /// takes them: the sweep importer and the REW importer each read the file properly
    /// and report what is wrong with it, and a second opinion formed here out of a few
    /// kilobytes could only be a worse-informed one.
    /// </remarks>
    internal static DroppedFileKind Classify(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string extension = Path.GetExtension(path);
        if (string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase))
        {
            return DroppedFileKind.RecordedSweep;
        }

        if (string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase))
        {
            return DroppedFileKind.RewImpulseResponseExport;
        }

        if (!string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            return DroppedFileKind.Unknown;
        }

        return JsonFormatMarker.Read(path) switch
        {
            ImpulseResponseFile.CurrentFormat => DroppedFileKind.ImpulseResponse,
            LiveCaptureDocument.CurrentFormat => DroppedFileKind.SpatialAverageCapture,
            VirtualCrossoverProjectFile.CurrentFormat => DroppedFileKind.VirtualDspSession,
            OverlayFile.CurrentFormat => DroppedFileKind.OverlaySlot,
            _ => DroppedFileKind.Unknown
        };
    }
}
