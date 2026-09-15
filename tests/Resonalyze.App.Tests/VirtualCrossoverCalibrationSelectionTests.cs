using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>Ids deliberately collide: "90deg" exists on every machine that migrated a legacy slot, so an id match says nothing about the file.</summary>
public sealed class VirtualCrossoverCalibrationSelectionTests
{
    private static readonly CalibrationFile AuthorsCurve =
        CalibrationFile.Parse("20 0\n1000 1.5\n20000 -3\n");

    private static readonly CalibrationFile RecipientsCurve =
        CalibrationFile.Parse("20 0\n1000 -1.5\n20000 2\n");

    private static readonly CalibrationFile ZeroCurve =
        CalibrationFile.Parse("20 0\n20000 0\n");

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
        // The session names "90deg" and the recipient has a "90deg" from a different microphone.
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
        // The autosave's id is this machine's; its stored curve is the file's previous state, not a rival.
        VirtualCrossoverCalibrationDecision decision =
            Decide("90deg", Carried(AuthorsCurve), imported: false);

        Assert.Equal("90deg", decision.SelectedId);
        Assert.Null(decision.Session);
        Assert.Equal(VirtualCrossoverCalibrationNotice.None, decision.Notice);
    }

    [Fact]
    public void TheAutosave_OffersItsOwnCurve_WhenTheEntryIsGone()
    {
        VirtualCrossoverCalibrationDecision decision =
            Decide("cal-deleted", Carried(AuthorsCurve, name: "Seat", fileName: "seat.txt"), imported: false);

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
        // An unplugged entry stays selected (Persist keeps its curve), so it survives the file being edited meanwhile.
        VirtualCrossoverCalibrationDecision autosave =
            Decide("cal-gone", Carried(RecipientsCurve, name: "Unplugged"), imported: false);

        Assert.Equal("cal-gone", autosave.SelectedId);
        Assert.Null(autosave.Session);

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
        VirtualCrossoverCalibrationDecision decision =
            Decide("cal-authors-id", null, previousId: "cal-local");

        Assert.Equal("cal-local", decision.SelectedId);
        Assert.Equal(VirtualCrossoverCalibrationNotice.KeptPrevious, decision.Notice);

        var previousSession = new VirtualCrossoverSessionCalibration(AuthorsCurve, "Prev", null);
        VirtualCrossoverCalibrationDecision kept = Decide(
            "cal-gone", null,
            previousId: VirtualCrossoverCalibrationSelection.SessionId,
            previousSession: previousSession);
        Assert.Equal(VirtualCrossoverCalibrationSelection.SessionId, kept.SelectedId);
        Assert.Same(previousSession, kept.Session);

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
    public void EntriesWith_OffersOwnFirstAndTheSessionCurveLast()
    {
        var session = new VirtualCrossoverSessionCalibration(AuthorsCurve, "90°", "ECM8000_90deg.txt");

        IReadOnlyList<MicrophoneCalibrationEntry> entries =
            VirtualCrossoverCalibrationSelection.EntriesWith(Recipient, session);

        Assert.Equal(Recipient.Length + 2, entries.Count);
        Assert.Equal(VirtualCrossoverCalibrationSelection.OwnId, entries[0].Id);
        Assert.Equal("Own (as measured)", entries[0].Name);
        Assert.True(entries[0].Available);
        Assert.Null(entries[0].FileName);

        MicrophoneCalibrationEntry last = entries[^1];
        Assert.Equal(VirtualCrossoverCalibrationSelection.SessionId, last.Id);
        Assert.Equal("90° (from session)", last.Name);
        Assert.True(last.Available);
        Assert.Equal("ECM8000_90deg.txt", last.FileName);
    }

    [Fact]
    public void EntriesWith_OffersOwnEvenWithoutASessionCurve()
    {
        IReadOnlyList<MicrophoneCalibrationEntry> entries =
            VirtualCrossoverCalibrationSelection.EntriesWith(Recipient, null);

        Assert.Equal(Recipient.Length + 1, entries.Count);
        Assert.Equal(VirtualCrossoverCalibrationSelection.OwnId, entries[0].Id);
        Assert.DoesNotContain(
            entries,
            entry => entry.Id == VirtualCrossoverCalibrationSelection.SessionId);
    }

    [Fact]
    public void Own_SurvivesABindAndPersistsAsARuleWithNoCurve()
    {
        // Own names no curve, so no stored curve is written back (it would read every channel through one file).
        VirtualCrossoverCalibrationDecision decision =
            VirtualCrossoverCalibrationSelection.Resolve(
                VirtualCrossoverCalibrationSelection.OwnId,
                Carried(ForeignCurve),
                imported: true,
                Recipient,
                Resolve,
                previousSelectedId: "cal-local",
                previousSession: null);

        Assert.Equal(VirtualCrossoverCalibrationSelection.OwnId, decision.SelectedId);
        Assert.Null(decision.Session);
        Assert.Equal(VirtualCrossoverCalibrationNotice.None, decision.Notice);

        (string? id, VirtualCrossoverCalibrationSettings? curve) =
            VirtualCrossoverCalibrationSelection.Persist(
                VirtualCrossoverCalibrationSelection.OwnId,
                null,
                Recipient,
                Resolve,
                storedId: null,
                stored: null);
        Assert.Equal(VirtualCrossoverCalibrationSelection.OwnId, id);
        Assert.Null(curve);
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
        Assert.Null(id);
        Assert.Equal("Theirs", curve!.Name);
        Assert.True(CalibrationFile.SameCurve(RecipientsCurve, curve.ToCalibrationFile()));

        (id, curve) = VirtualCrossoverCalibrationSelection.Persist(
            "cal-gone", null, Recipient, Resolve, storedId: "cal-local", stored: Carried(AuthorsCurve));
        Assert.Equal("cal-gone", id);
        Assert.Null(curve);

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
