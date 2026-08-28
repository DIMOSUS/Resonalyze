using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The calibration a LOADED measurement carries, offered to the selectors as an
/// entry of its own.
/// </summary>
/// <remarks>
/// An impulse response is stored raw — no calibration is ever baked into one — so
/// a measurement made on another machine draws a different curve here than it did
/// for its author unless their calibration comes with it. The file carries the
/// CURVE, and this is what puts it in front of the user: "ECM8000 0° (from file)".
/// <para>
/// The same shape as the Virtual DSP session calibration, and for the same reason
/// — two machines' calibration lists mint their own ids, so an id identifies
/// nothing across the gap and the points are what decide. It is deliberately NOT
/// the same code: a session is bound and rebound, distinguishes its own autosave
/// from an import, and carries notices about both; a loaded measurement is always
/// someone's file and needs none of that.
/// </para>
/// </remarks>
internal static class FileCalibrationSelection
{
    /// <summary>
    /// The selector id of the loaded measurement's own curve. Never persisted:
    /// it names a curve that belongs to whatever file is open, so a settings file
    /// carrying it would point at nothing on the next start.
    /// </summary>
    public const string FileId = "file-calibration";

    public static bool IsFile(string? calibrationId) =>
        string.Equals(calibrationId, FileId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The id to select for a measurement that has just been loaded, or null to
    /// leave the selection alone.
    /// </summary>
    /// <remarks>
    /// A local calibration holding the same curve wins over the file's copy of it.
    /// That is the ordinary case — your own file on your own machine — and it must
    /// not litter the selector with a duplicate entry, nor quietly move the
    /// selection off an entry that is already correct.
    /// </remarks>
    public static string? Choose(
        VirtualCrossoverCalibrationSettings? loaded,
        string? selectedId,
        IReadOnlyList<MicrophoneCalibrationEntry> entries,
        Func<string?, CalibrationFile?> resolve)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(resolve);
        if (loaded == null)
        {
            // Nothing to say. A measurement written before the format carried a
            // calibration is not a measurement that was taken without one, so the
            // view is left exactly as the user set it.
            return null;
        }

        CalibrationFile curve = loaded.ToCalibrationFile();
        if (!IsFile(selectedId) &&
            CalibrationFile.SameCurve(resolve(selectedId), curve))
        {
            return null;
        }

        string? matching = FindMatching(curve, entries, resolve);
        return matching ?? FileId;
    }

    /// <summary>
    /// The selector list with the loaded measurement's calibration appended, or
    /// the list unchanged when a local entry already holds that curve.
    /// </summary>
    public static IReadOnlyList<MicrophoneCalibrationEntry> EntriesWith(
        IReadOnlyList<MicrophoneCalibrationEntry> entries,
        VirtualCrossoverCalibrationSettings? loaded,
        Func<string?, CalibrationFile?> resolve)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(resolve);
        if (loaded == null)
        {
            return entries;
        }

        CalibrationFile curve = loaded.ToCalibrationFile();
        if (FindMatching(curve, entries, resolve) != null)
        {
            return entries;
        }

        var extended = new List<MicrophoneCalibrationEntry>(entries.Count + 1);
        extended.AddRange(entries);
        extended.Add(new MicrophoneCalibrationEntry(
            FileId,
            DisplayName(loaded),
            Available: true,
            loaded.FileName));
        return extended;
    }

    public static string DisplayName(VirtualCrossoverCalibrationSettings loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        string name = string.IsNullOrWhiteSpace(loaded.Name)
            ? "Measurement calibration"
            : loaded.Name;
        return $"{name} (from file)";
    }

    // The first local entry holding this very curve, or null when none does. An
    // entry whose file is missing right now cannot be compared and is skipped
    // rather than assumed to differ.
    private static string? FindMatching(
        CalibrationFile curve,
        IReadOnlyList<MicrophoneCalibrationEntry> entries,
        Func<string?, CalibrationFile?> resolve)
    {
        foreach (MicrophoneCalibrationEntry entry in entries)
        {
            if (entry.Available && CalibrationFile.SameCurve(resolve(entry.Id), curve))
            {
                return entry.Id;
            }
        }

        return null;
    }
}
