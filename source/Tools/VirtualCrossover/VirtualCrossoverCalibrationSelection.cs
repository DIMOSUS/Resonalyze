using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// A calibration curve a session carries inside itself and no configured entry
/// matches: the Virtual DSP selector offers it as its own item, so a session
/// that travels with its measurements is drawn with the correction its author
/// saw on a machine that never configured that file.
/// </summary>
internal sealed record VirtualCrossoverSessionCalibration(
    CalibrationFile Curve,
    string Name,
    string? FileName)
{
    public string DisplayName => $"{Name} (from session)";

    /// <summary>What the author would recognize it by: the file, failing that the name.</summary>
    public string Description =>
        FileName is { } fileName && !string.Equals(
            fileName, Name, StringComparison.OrdinalIgnoreCase)
            ? $"'{Name}' ({fileName})"
            : $"'{Name}'";

    public VirtualCrossoverCalibrationSettings ToSettings() =>
        VirtualCrossoverCalibrationSettings.From(Curve, Name, FileName);
}

/// <summary>What the panel tells the user, once, after a session was bound.</summary>
internal enum VirtualCrossoverCalibrationNotice
{
    None,

    /// <summary>
    /// The session's own curve is selected because no configured entry has it;
    /// the user may want it in their list.
    /// </summary>
    CarriedBySession,

    /// <summary>
    /// A session written before the curve travelled names a slot-style id
    /// ("90deg") that this machine also minted. The ids agree; nothing says the
    /// files do.
    /// </summary>
    MatchedBySlotName,

    /// <summary>
    /// A session written before the curve travelled names an entry this machine
    /// does not have; the selection the panel already had was kept.
    /// </summary>
    KeptPrevious
}

/// <summary>
/// The selector state a bound project starts on: which item is selected (a
/// configured entry's id, <see cref="VirtualCrossoverCalibrationSelection.SessionId"/>
/// for the session's own curve, or null for Off), the session curve to offer
/// when there is one, and the notice to show.
/// </summary>
internal sealed record VirtualCrossoverCalibrationDecision(
    string? SelectedId,
    VirtualCrossoverSessionCalibration? Session,
    VirtualCrossoverCalibrationNotice Notice);

/// <summary>
/// Decides how the Virtual DSP selector reads the calibration a project stores.
/// The project carries the CURVE (and, as a hint, the id of the entry it mapped
/// to where it was saved); ids are local to a machine — the migrated "90deg"
/// id in particular exists on every machine that had a legacy 90° slot — so the
/// curve, not the id, says whether this machine already has that calibration.
/// </summary>
internal static class VirtualCrossoverCalibrationSelection
{
    /// <summary>
    /// The selector id of the curve the session carries. Never written to a
    /// project: the persisted form of that selection is the curve with no id.
    /// </summary>
    public const string SessionId = "session-calibration";

    public static bool IsSession(string? calibrationId) =>
        string.Equals(calibrationId, SessionId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The selector id that means "each measurement through the calibration IT
    /// recorded" rather than one curve for the whole project.
    /// </summary>
    /// <remarks>
    /// A rule and not a curve, which is why it persists as an id with nothing beside
    /// it — there is no single correction to store, and storing one of them would be
    /// storing the wrong answer for the other channels. The panel's other selections
    /// answer "what should everything be read through"; this one answers "what was
    /// each of them actually read through", and only the files can say.
    /// <para>
    /// It exists because the project's one calibration stopped being able to describe
    /// the measurements: a microphone array records several capsules, each corrected
    /// by its own file before the positions are averaged, and no single curve names
    /// that. The same is true, less visibly, of a project whose channels were measured
    /// on different days with different microphones.
    /// </para>
    /// </remarks>
    public const string OwnId = "own-calibration";

    public static bool IsOwn(string? calibrationId) =>
        string.Equals(calibrationId, OwnId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the selection for a project being bound.
    /// </summary>
    /// <param name="imported">
    /// True for a session the user loaded from a file — possibly written on
    /// another machine — as opposed to the tool's own autosave, whose ids are
    /// this machine's by construction.
    /// </param>
    /// <param name="previousSelectedId">The selector's selection before the bind.</param>
    /// <param name="previousSession">The session curve the selector offered before the bind.</param>
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
        // Before anything is matched against a curve: this selection names no curve,
        // so a stored one beside it (from a project saved under a different rule and
        // migrated) has nothing to say about it.
        if (IsOwn(id))
        {
            return new VirtualCrossoverCalibrationDecision(
                OwnId, null, VirtualCrossoverCalibrationNotice.None);
        }

        MicrophoneCalibrationEntry? named = Find(entries, id);

        if (calibration is { } embedded)
        {
            CalibrationFile curve = embedded.ToCalibrationFile();
            // The entry the session names, when it still holds the same curve.
            if (named is { Available: true } &&
                CalibrationFile.SameCurve(resolve(named.Id), curve))
            {
                return new VirtualCrossoverCalibrationDecision(
                    named.Id, null, VirtualCrossoverCalibrationNotice.None);
            }

            // The tool's own autosave: the entry is the user's, and a curve that
            // differs means they edited the file since — the entry follows the
            // file, which is the point of an entry. An entry whose file is missing
            // right now stays selected and marked, like every other view's
            // selection does; Persist keeps the stored curve for it, so the record
            // of what the session was tuned with survives the unplugged drive.
            if (!imported && named != null)
            {
                return new VirtualCrossoverCalibrationDecision(
                    named.Id, null, VirtualCrossoverCalibrationNotice.None);
            }

            // Any configured entry with that curve, under whatever id and name
            // this machine gave it.
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

        // An id with no curve: a session written before the curve travelled, or
        // one saved while its entry had no usable file. The autosave keeps it as
        // it is — the selector marks a missing or unavailable entry rather than
        // rewriting the choice.
        if (!imported)
        {
            return new VirtualCrossoverCalibrationDecision(
                id, null, VirtualCrossoverCalibrationNotice.None);
        }

        if (named is { Available: true })
        {
            // A generated id cannot be minted twice, so a foreign session naming
            // one of this machine's means it came from this machine. A slot id
            // ("90deg", "0deg") is minted everywhere, so the match is by name.
            return new VirtualCrossoverCalibrationDecision(
                id,
                null,
                MicrophoneCalibrationDefinition.IsGeneratedId(id)
                    ? VirtualCrossoverCalibrationNotice.None
                    : VirtualCrossoverCalibrationNotice.MatchedBySlotName);
        }

        // The entry is not here (or has no file): the one thing the session can
        // tell us is which correction NOT to use, and replacing a working choice
        // with nothing would cost the user the very selection they had.
        return new VirtualCrossoverCalibrationDecision(
            previousSelectedId,
            IsSession(previousSelectedId) ? previousSession : null,
            VirtualCrossoverCalibrationNotice.KeptPrevious);
    }

    /// <summary>
    /// The selector's items: the configured entries plus, when there is one,
    /// the session's own curve.
    /// </summary>
    public static IReadOnlyList<MicrophoneCalibrationEntry> EntriesWith(
        IReadOnlyList<MicrophoneCalibrationEntry> entries,
        VirtualCrossoverSessionCalibration? session)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var all = new List<MicrophoneCalibrationEntry>(entries.Count + 2)
        {
            // First, straight after Off: it is the answer that needs no configuring
            // and cannot be wrong about what was measured. The curves below it are
            // this machine's opinions about what to read the measurements through.
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

    /// <summary>
    /// What a selection persists as. A configured entry stores its id and its
    /// curve; the session's own curve stores the curve and no id; Off stores
    /// neither. An entry with no usable file right now (unplugged, deleted)
    /// keeps the curve the project already held for that same entry — the
    /// session was tuned with it, and a missing file must not erase the record
    /// of what that was — and stores the id alone when there was none.
    /// </summary>
    /// <param name="storedId">The project's current stored id.</param>
    /// <param name="stored">The project's current stored curve.</param>
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

        // A rule, so the id alone. There is no curve that describes it, and writing
        // one of the measurements' would make a project reopened elsewhere read every
        // channel through whichever file happened to be first.
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
