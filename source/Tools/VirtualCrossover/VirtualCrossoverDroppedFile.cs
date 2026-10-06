namespace Resonalyze;

/// <summary>The block buttons a file from Explorer can be dropped on; each takes what its own menu's file pick opens.</summary>
internal enum VirtualCrossoverDropButton
{
    Source,

    SpatialAverage,

    Fir
}

internal static class VirtualCrossoverDroppedFile
{
    /// <summary>By name only: answered on every drag move.</summary>
    internal static bool Takes(VirtualCrossoverDropButton button, string path) =>
        button switch
        {
            VirtualCrossoverDropButton.Source => HasExtension(path, ".json"),
            VirtualCrossoverDropButton.SpatialAverage => HasExtension(path, ".json", ".txt"),
            _ => HasExtension(path, ".wav", ".fir", ".txt")
        };

    /// <summary>What another of the application's own documents is, read from its marker before a loader misreads it;
    /// null when the button's own loader is the one to judge the file.</summary>
    internal static string? OtherDocument(VirtualCrossoverDropButton button, string path)
    {
        if (button == VirtualCrossoverDropButton.Fir || !DroppedFile.HasJsonExtension(path))
        {
            return null;
        }

        DroppedFileKind own = button == VirtualCrossoverDropButton.Source
            ? DroppedFileKind.ImpulseResponse
            : DroppedFileKind.SpatialAverageCapture;
        DroppedFileKind kind = DroppedFile.Classify(path);
        return kind == own
            ? null
            : kind switch
            {
                DroppedFileKind.ImpulseResponse =>
                    "an impulse response: drop it on the block's Source button to tune on it",
                DroppedFileKind.SpatialAverageCapture =>
                    "a moving-mic capture: drop it on the block's MMM button to attach it",
                DroppedFileKind.VirtualDspSession =>
                    "a Virtual DSP session: drop it anywhere off the block buttons to open it",
                DroppedFileKind.OverlaySlot =>
                    "an overlay slot file, this application's own storage for one slot of one mode",
                _ => null
            };
    }

    private static bool HasExtension(string path, params string[] extensions)
    {
        string extension = Path.GetExtension(path);
        return extensions.Any(candidate => string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase));
    }
}
