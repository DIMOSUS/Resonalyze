using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// How the Virtual DSP selector reads the calibration a project stores — the four
/// ways a session can arrive (its own curve; a curve this machine has under some
/// name; a legacy id that resolves; a legacy id that does not) and what each
/// persists as. The ids are deliberately the colliding ones: "90deg" exists on
/// every machine that migrated a legacy 90° slot, and an id agreeing says nothing
/// about the files.
/// </summary>
public sealed class VirtualCrossoverCalibrationSelectionTests
{
    private static readonly CalibrationFile AuthorsCurve =
        CalibrationFile.Parse("20 0\n1000 1.5\n20000 -3\n");

    private static readonly CalibrationFile RecipientsCurve =
        CalibrationFile.Parse("20 0\n1000 -1.5\n20000 2\n");

    private static readonly CalibrationFile ZeroCurve =
        CalibrationFile.Parse("20 0\n20000 0\n");

    // A curve no entry of the recipient's list resolves to.
    private static readonly CalibrationFile ForeignCurve =
        CalibrationFile.Parse("20 0\n1000 0.5\n20000 -6\n");

    private static readonly MicrophoneCalibrationEntry[] Recipient =
    [
        new(MicrophoneCalibrationIds.ZeroDegrees, "0°", Available: true, "ECM8000_0deg.txt"),
        new("90deg", "90°", Available: true, "other-mic-90.txt"),
        new("cal-local", "Seat", Available: true, "seat.txt"),
        new("cal-gone", "Unplugged", Available: false, "unplugged.txt")
    ];

    private static CalibrationFile? Resolve(string? id) => id switch
    {
        MicrophoneCalibrationIds.ZeroDegrees => ZeroCurve,
        "90deg" => RecipientsCurve,
        "cal-local" => AuthorsCurve,
        _ => null
    };

    private static VirtualCrossoverCalibrationSettings Carried(
        CalibrationFile curve, string name = "90°", string? fileName = "ECM8000_90deg.txt") =>
        VirtualCrossoverCalibrationSettings.From(curve, name, fileName);

    private static VirtualCrossoverCalibrationDecision Decide(
        string? calibrationId,
        VirtualCrossoverCalibrationSettings? calibration,
        bool imported = true,
        string? previousId = "cal-local",
        VirtualCrossoverSessionCalibration? previousSession = null) =>
        VirtualCrossoverCalibrationSelection.Resolve(
            calibrationId, calibration, imported, Recipient, Resolve, previousId, previousSession);

    [Fact]
    public void ACarriedCurveNobodyHas_IsOfferedAndSelected_AndTheIdCollisionIsIgnored()
    {
        // The loud branch of #108 and the silent one at once: the session names
        // "90deg", the recipient HAS a "90deg" — a different microphone's file.
        VirtualCrossoverCalibrationDecision decision =
            Decide("90deg", Carried(ForeignCurve));

        Assert.Equal(VirtualCrossoverCalibrationSelection.SessionId, decision.SelectedId);
        Assert.NotNull(decision.Session);
        Assert.True(CalibrationFile.SameCurve(ForeignCurve, decision.Session.Curve));
        Assert.Equal("90° (from session)", decision.Session.DisplayName);
        Assert.Equal("'90°' (ECM8000_90deg.txt)", decision.Session.Description);
        Assert.Equal(VirtualCrossoverCalibrationNotice.CarriedBySession, decision.Notice);
    }

    [Fact]
    public void ACarriedCurveThisMachineHasUnderAnotherName_SelectsThatEntry()
    {
        // The author's own round trip, and a recipient who already kept the file:
        // the curve is found under whatever id and name this machine gave it.
        VirtualCrossoverCalibrationDecision decision =
            Decide("cal-authors-id", Carried(AuthorsCurve));

        Assert.Equal("cal-local", decision.SelectedId);
        Assert.Null(decision.Session);
        Assert.Equal(VirtualCrossoverCalibrationNotice.None, decision.Notice);
    }

    [Fact]
    public void ACarriedCurveMatchingTheNamedEntry_WinsOverAnotherEntryWithTheSameCurve()
    {
        MicrophoneCalibrationEntry[] twoAlike =
        [
            new("cal-a", "A", Available: true),
            new("cal-b", "B", Available: true)
        ];

        VirtualCrossoverCalibrationDecision decision =
            VirtualCrossoverCalibrationSelection.Resolve(
                "cal-b", Carried(AuthorsCurve), imported: true, twoAlike,
                _ => AuthorsCurve, previousSelectedId: null, previousSession: null);

        Assert.Equal("cal-b", decision.SelectedId);
    }

    [Fact]
    public void TheAutosave_FollowsItsOwnEntryEvenWhenTheFileWasEditedSince()
    {
        // The autosave's id is this machine's by construction, and an entry exists
        // so that editing the file updates every view reading it — the stored
        // curve is the previous state of that file, not a rival to it.
        VirtualCrossoverCalibrationDecision decision =
            Decide("90deg", Carried(AuthorsCurve), imported: false);

        Assert.Equal("90deg", decision.SelectedId);
        Assert.Null(decision.Session);
        Assert.Equal(VirtualCrossoverCalibrationNotice.None, decision.Notice);
    }

    [Fact]
    public void TheAutosave_OffersItsOwnCurve_WhenTheEntryIsGone()
    {
        // An entry deleted after the session was tuned: the curve is still the
        // session's, and it carries on drawing with it, silently — the user did
        // this themselves.
        VirtualCrossoverCalibrationDecision decision =
            Decide("cal-deleted", Carried(AuthorsCurve, name: "Seat", fileName: "seat.txt"), imported: false);

        // ...except that this machine still has the curve under "cal-local".
        Assert.Equal("cal-local", decision.SelectedId);

        VirtualCrossoverCalibrationDecision unmatched =
            Decide("cal-deleted", Carried(ForeignCurve, name: "Old"), imported: false);
        Assert.Equal(VirtualCrossoverCalibrationSelection.SessionId, unmatched.SelectedId);
        Assert.Equal(VirtualCrossoverCalibrationNotice.None, unmatched.Notice);
        Assert.Equal("Old", unmatched.Session!.Name);
    }

    [Fact]
    public void TheAutosave_KeepsItsUnavailableEntrySelected_AndAnImportDoesNot()
    {
        // The user's own entry with its file unplugged: it stays selected and marked,
        // as every view's selection does (Persist keeps the stored curve for it), so
        // it is still the same selection when the file returns — even if the file
        // was edited meanwhile and the curve alone would no longer find it.
        VirtualCrossoverCalibrationDecision autosave =
            Decide("cal-gone", Carried(RecipientsCurve, name: "Unplugged"), imported: false);

        Assert.Equal("cal-gone", autosave.SelectedId);
        Assert.Null(autosave.Session);

        // An import has no such claim on an unavailable entry of the same id: the
        // curve decides, and RecipientsCurve IS configured here, as "90deg".
        VirtualCrossoverCalibrationDecision import =
            Decide("cal-gone", Carried(RecipientsCurve, name: "Unplugged"), imported: true);

        Assert.Equal("90deg", import.SelectedId);
    }

    [Fact]
    public void Off_IsOff()
    {
        VirtualCrossoverCalibrationDecision decision = Decide(null, null);

        Assert.Null(decision.SelectedId);
        Assert.Null(decision.Session);
        Assert.Equal(VirtualCrossoverCalibrationNotice.None, decision.Notice);
    }

    [Fact]
    public void ALegacySlotId_ThatResolvesHere_IsMatchedByNameAndSaysSo()
    {
        // A session written before the curve travelled, naming "90deg": the match is
        // by slot name only, and the user is told rather than left to assume.
        VirtualCrossoverCalibrationDecision decision = Decide("90deg", null);

        Assert.Equal("90deg", decision.SelectedId);
        Assert.Equal(VirtualCrossoverCalibrationNotice.MatchedBySlotName, decision.Notice);

        VirtualCrossoverCalibrationDecision zero =
            Decide(MicrophoneCalibrationIds.ZeroDegrees, null);
        Assert.Equal(VirtualCrossoverCalibrationNotice.MatchedBySlotName, zero.Notice);
    }

    [Fact]
    public void ALegacyGeneratedId_ThatResolvesHere_IsThisMachinesOwn()
    {
        VirtualCrossoverCalibrationDecision decision = Decide("cal-local", null);

        Assert.Equal("cal-local", decision.SelectedId);
        Assert.Equal(VirtualCrossoverCalibrationNotice.None, decision.Notice);
    }

    [Fact]
    public void ALegacyId_ThatDoesNotResolve_KeepsWhatThePanelHad()
    {
        // The issue's first ask: a valid selection must not be lost to an invalid one.
        VirtualCrossoverCalibrationDecision decision =
            Decide("cal-authors-id", null, previousId: "cal-local");

        Assert.Equal("cal-local", decision.SelectedId);
        Assert.Equal(VirtualCrossoverCalibrationNotice.KeptPrevious, decision.Notice);

        // ...including when what it had was a curve carried by the previous session.
        var previousSession = new VirtualCrossoverSessionCalibration(AuthorsCurve, "Prev", null);
        VirtualCrossoverCalibrationDecision kept = Decide(
            "cal-gone", null,
            previousId: VirtualCrossoverCalibrationSelection.SessionId,
            previousSession: previousSession);
        Assert.Equal(VirtualCrossoverCalibrationSelection.SessionId, kept.SelectedId);
        Assert.Same(previousSession, kept.Session);

        // ...and when it had nothing, nothing: the notice still says so.
        VirtualCrossoverCalibrationDecision off = Decide("cal-authors-id", null, previousId: null);
        Assert.Null(off.SelectedId);
        Assert.Equal(VirtualCrossoverCalibrationNotice.KeptPrevious, off.Notice);
    }

    [Fact]
    public void TheAutosave_KeepsAnUnresolvableId_ForTheSelectorToMark()
    {
        VirtualCrossoverCalibrationDecision decision =
            Decide("cal-deleted", null, imported: false);

        Assert.Equal("cal-deleted", decision.SelectedId);
        Assert.Equal(VirtualCrossoverCalibrationNotice.None, decision.Notice);
    }

    [Fact]
    public void EntriesWith_AppendsTheSessionCurveAsItsOwnItem()
    {
        var session = new VirtualCrossoverSessionCalibration(AuthorsCurve, "90°", "ECM8000_90deg.txt");

        IReadOnlyList<MicrophoneCalibrationEntry> entries =
            VirtualCrossoverCalibrationSelection.EntriesWith(Recipient, session);

        Assert.Equal(Recipient.Length + 1, entries.Count);
        MicrophoneCalibrationEntry last = entries[^1];
        Assert.Equal(VirtualCrossoverCalibrationSelection.SessionId, last.Id);
        Assert.Equal("90° (from session)", last.Name);
        Assert.True(last.Available);
        Assert.Equal("ECM8000_90deg.txt", last.FileName);
        Assert.Same(Recipient, VirtualCrossoverCalibrationSelection.EntriesWith(Recipient, null));
    }

    [Fact]
    public void Persist_WritesTheCurveForAnEntry_TheCurveAloneForTheSession_AndNothingForOff()
    {
        (string? id, VirtualCrossoverCalibrationSettings? curve) =
            VirtualCrossoverCalibrationSelection.Persist(
                "cal-local", null, Recipient, Resolve, storedId: null, stored: null);
        Assert.Equal("cal-local", id);
        Assert.NotNull(curve);
        Assert.Equal("Seat", curve.Name);
        Assert.Equal("seat.txt", curve.FileName);
        Assert.True(CalibrationFile.SameCurve(AuthorsCurve, curve.ToCalibrationFile()));

        var session = new VirtualCrossoverSessionCalibration(RecipientsCurve, "Theirs", "theirs.txt");
        (id, curve) = VirtualCrossoverCalibrationSelection.Persist(
            VirtualCrossoverCalibrationSelection.SessionId, session, Recipient, Resolve,
            storedId: null, stored: null);
        // No id: the persisted form of "the curve the session carries" is the curve
        // itself, so an autosave re-read on this machine cannot be captured by an
        // entry that merely shares a slot name.
        Assert.Null(id);
        Assert.Equal("Theirs", curve!.Name);
        Assert.True(CalibrationFile.SameCurve(RecipientsCurve, curve.ToCalibrationFile()));

        // An entry with no usable file: the id alone says what was meant...
        (id, curve) = VirtualCrossoverCalibrationSelection.Persist(
            "cal-gone", null, Recipient, Resolve, storedId: "cal-local", stored: Carried(AuthorsCurve));
        Assert.Equal("cal-gone", id);
        Assert.Null(curve);

        // ...unless the project already held that entry's curve: an unplugged file
        // must not erase the record of what the session was tuned with.
        VirtualCrossoverCalibrationSettings held = Carried(RecipientsCurve, name: "Unplugged");
        (id, curve) = VirtualCrossoverCalibrationSelection.Persist(
            "cal-gone", null, Recipient, Resolve, storedId: "cal-gone", stored: held);
        Assert.Equal("cal-gone", id);
        Assert.Same(held, curve);

        (id, curve) = VirtualCrossoverCalibrationSelection.Persist(
            null, null, Recipient, Resolve, storedId: "cal-gone", stored: held);
        Assert.Null(id);
        Assert.Null(curve);
    }

    [Fact]
    public void SessionCalibrationFiles_NameFilesAndEntriesWithoutCollisions()
    {
        string directory = Path.Combine("C:", "data");
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(directory, "ECM8000_90deg.txt"),
            Path.Combine(directory, "ECM8000_90deg (2).txt")
        };

        Assert.Equal(
            Path.Combine(directory, "ECM8000_90deg (3).txt"),
            SessionCalibrationFiles.UniquePath(directory, "ECM8000_90deg.txt", taken.Contains));
        Assert.Equal(
            Path.Combine(directory, "mic.cal"),
            SessionCalibrationFiles.UniquePath(directory, "mic.cal", taken.Contains));
        // A free-form entry name: file-system-safe, with an extension every reader takes.
        Assert.Equal(
            Path.Combine(directory, "90° seat_.txt"),
            SessionCalibrationFiles.UniquePath(directory, "90° seat?", taken.Contains));
        Assert.Equal(
            Path.Combine(directory, "calibration.txt"),
            SessionCalibrationFiles.UniquePath(directory, " ... ", taken.Contains));

        Assert.Equal("90°", SessionCalibrationFiles.UniqueName("90°", ["0°", "Seat"]));
        Assert.Equal("90° (2)", SessionCalibrationFiles.UniqueName("90°", ["90°"]));
        Assert.Equal("90° (3)", SessionCalibrationFiles.UniqueName(" 90° ", ["90°", "90° (2)"]));
        Assert.Equal("calibration", SessionCalibrationFiles.UniqueName("", []));
    }
}
