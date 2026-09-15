using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>A curve the session carries that no configured entry matches; offered as its own selector item.</summary>
internal sealed record VirtualCrossoverSessionCalibration(
    CalibrationFile Curve,
    string Name,
    string? FileName)
{
    public string DisplayName => $"{Name} (from session)";

    public string Description =>
        FileName is { } fileName && !string.Equals(
            fileName, Name, StringComparison.OrdinalIgnoreCase)
            ? $"'{Name}' ({fileName})"
            : $"'{Name}'";

    public VirtualCrossoverCalibrationSettings ToSettings() =>
        VirtualCrossoverCalibrationSettings.From(Curve, Name, FileName);
}

internal enum VirtualCrossoverCalibrationNotice
{
    None,

    CarriedBySession,

    /// <summary>A pre-curve session names a slot id ("90deg") minted on every machine: the ids agree, the files may not.</summary>
    MatchedBySlotName,

    KeptPrevious
}

internal sealed record VirtualCrossoverCalibrationDecision(
    string? SelectedId,
    VirtualCrossoverSessionCalibration? Session,
    VirtualCrossoverCalibrationNotice Notice);

/// <summary>Maps a project's stored calibration to the selector. The curve, not the machine-local id, decides identity. See docs/tech/virtual-dsp-session-file.md#calibration.</summary>
internal static class VirtualCrossoverCalibrationSelection
{
    /// <summary>Never written to a project: this selection persists as the curve with no id.</summary>
    public const string SessionId = "session-calibration";

    public static bool IsSession(string? calibrationId) =>
        string.Equals(calibrationId, SessionId, StringComparison.OrdinalIgnoreCase);

    /// <summary>Each measurement through the calibration it recorded (e.g. per-capsule array files); a rule, so persisted as the id alone.</summary>
    public const string OwnId = "own-calibration";

    public static bool IsOwn(string? calibrationId) =>
        string.Equals(calibrationId, OwnId, StringComparison.OrdinalIgnoreCase);

    /// <param name="imported">True for a loaded file (possibly foreign); false for the autosave, whose ids are this machine's.</param>
    public static VirtualCrossoverCalibrationDecision Resolve(
        string? calibrationId,
        VirtualCrossoverCalibrationSettings? calibration,
        bool imported,
        IReadOnlyList<MicrophoneCalibrationEntry> entries,
        Func<string?, CalibrationFile?> resolve,
        string? previousSelectedId,
        VirtualCrossoverSessionCalibration? previousSession)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(resolve);

        string? id = MicrophoneCalibrationIds.Normalize(calibrationId);
        // A rule names no curve, so a stored curve beside it has nothing to say.
        if (IsOwn(id))
        {
            return new VirtualCrossoverCalibrationDecision(
                OwnId, null, VirtualCrossoverCalibrationNotice.None);
        }

        MicrophoneCalibrationEntry? named = Find(entries, id);

        if (calibration is { } embedded)
        {
            CalibrationFile curve = embedded.ToCalibrationFile();
            if (named is { Available: true } &&
                CalibrationFile.SameCurve(resolve(named.Id), curve))
            {
                return new VirtualCrossoverCalibrationDecision(
                    named.Id, null, VirtualCrossoverCalibrationNotice.None);
            }

            // Autosave: the entry follows its file even if the curve changed; a missing file stays selected and marked.
            if (!imported && named != null)
            {
                return new VirtualCrossoverCalibrationDecision(
                    named.Id, null, VirtualCrossoverCalibrationNotice.None);
            }

            MicrophoneCalibrationEntry? same = entries.FirstOrDefault(entry =>
                entry.Available && CalibrationFile.SameCurve(resolve(entry.Id), curve));
            if (same != null)
            {
                return new VirtualCrossoverCalibrationDecision(
                    same.Id, null, VirtualCrossoverCalibrationNotice.None);
            }

            return new VirtualCrossoverCalibrationDecision(
                SessionId,
                new VirtualCrossoverSessionCalibration(
                    curve, embedded.Name, embedded.FileName),
                imported
                    ? VirtualCrossoverCalibrationNotice.CarriedBySession
                    : VirtualCrossoverCalibrationNotice.None);
        }

        if (id == null)
        {
            return new VirtualCrossoverCalibrationDecision(
                null, null, VirtualCrossoverCalibrationNotice.None);
        }

        if (!imported)
        {
            return new VirtualCrossoverCalibrationDecision(
                id, null, VirtualCrossoverCalibrationNotice.None);
        }

        if (named is { Available: true })
        {
            // A generated id cannot be minted twice, so it proves the same machine; a slot id matches by name only.
            return new VirtualCrossoverCalibrationDecision(
                id,
                null,
                MicrophoneCalibrationDefinition.IsGeneratedId(id)
                    ? VirtualCrossoverCalibrationNotice.None
                    : VirtualCrossoverCalibrationNotice.MatchedBySlotName);
        }

        // Entry missing: keep the working previous selection rather than replacing it with nothing.
        return new VirtualCrossoverCalibrationDecision(
            previousSelectedId,
            IsSession(previousSelectedId) ? previousSession : null,
            VirtualCrossoverCalibrationNotice.KeptPrevious);
    }

    public static IReadOnlyList<MicrophoneCalibrationEntry> EntriesWith(
        IReadOnlyList<MicrophoneCalibrationEntry> entries,
        VirtualCrossoverSessionCalibration? session)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var all = new List<MicrophoneCalibrationEntry>(entries.Count + 2)
        {
            new(OwnId, "Own (as measured)", Available: true, FileName: null)
        };
        all.AddRange(entries);
        if (session != null)
        {
            all.Add(new MicrophoneCalibrationEntry(
                SessionId, session.DisplayName, Available: true, session.FileName));
        }

        return all;
    }

    /// <summary>What a selection persists as; an entry whose file is missing keeps the curve the project already held.</summary>
    public static (string? CalibrationId, VirtualCrossoverCalibrationSettings? Calibration)
        Persist(
            string? selectedId,
            VirtualCrossoverSessionCalibration? session,
            IReadOnlyList<MicrophoneCalibrationEntry> entries,
            Func<string?, CalibrationFile?> resolve,
            string? storedId,
            VirtualCrossoverCalibrationSettings? stored)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(resolve);

        if (IsSession(selectedId))
        {
            return (null, session?.ToSettings());
        }

        // Id alone: writing one measurement's curve would read every channel through it elsewhere.
        if (IsOwn(selectedId))
        {
            return (OwnId, null);
        }

        string? id = MicrophoneCalibrationIds.Normalize(selectedId);
        if (id == null)
        {
            return (null, null);
        }

        CalibrationFile? curve = resolve(id);
        if (curve is not { HasData: true })
        {
            return (
                id,
                string.Equals(id, storedId, StringComparison.OrdinalIgnoreCase)
                    ? stored
                    : null);
        }

        MicrophoneCalibrationEntry? entry = Find(entries, id);
        return (
            id,
            VirtualCrossoverCalibrationSettings.From(
                curve, entry?.Name ?? id, entry?.FileName));
    }

    private static MicrophoneCalibrationEntry? Find(
        IReadOnlyList<MicrophoneCalibrationEntry> entries,
        string? id) =>
        id == null
            ? null
            : entries.FirstOrDefault(entry =>
                string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));
}
