namespace Resonalyze;

internal enum DroppedFileKind
{
    Unknown,

    ImpulseResponse,

    SpatialAverageCapture,

    VirtualDspSession,

    RecordedSweep,

    RewImpulseResponseExport,

    /// <summary>Recognized only to be refused by name: the overlay panel owns slot files.</summary>
    OverlaySlot
}

/// <summary>JSON documents are told apart by their <c>format</c> marker (<see cref="JsonFormatMarker"/>), not by trial deserialization.</summary>
internal static class DroppedFile
{
    /// <summary>By name only: answers during drag hover and must not touch the disk.</summary>
    internal static bool HasOpenableExtension(string path)
    {
        string extension = Path.GetExtension(path);
        return HasJsonExtension(path) ||
            string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool HasJsonExtension(string path) =>
        string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);

    /// <remarks>WAV and text go by extension; their importers report problems better than a sniff here could.</remarks>
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
