using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>The microphone calibration the curves are drawn through: the host's list, a curve the session carries, and the
/// notices an import raises. <see cref="VirtualCrossoverCalibrationPolicy"/> is what the rest of the tool reads.</summary>
public partial class VirtualCrossoverPanel
{
    private Func<string?, CalibrationFile?>? calibrationResolver;
    private IReadOnlyList<MicrophoneCalibrationEntry> calibrationEntries = [];

    private Func<VirtualCrossoverSessionCalibration, string?>? calibrationAdder;

    // A curve the bound project carries that no configured entry matches; offered as its own item.
    private VirtualCrossoverSessionCalibration? sessionCalibration;

    private VirtualCrossoverCalibrationNotice pendingCalibrationNotice;

    /// <summary>Called again whenever the configured calibrations change.</summary>
    internal void ConfigureCalibration(
        Func<string?, CalibrationFile?> resolver,
        IReadOnlyList<MicrophoneCalibrationEntry> entries,
        Func<VirtualCrossoverSessionCalibration, string?>? addToList = null)
    {
        calibrationResolver = resolver;
        calibrationEntries = entries;
        calibrationAdder = addToList ?? calibrationAdder;
        ReconcileCalibrationSelection();
    }

    // Selection stays (marked if gone); a session curve hands over to a configured entry holding the same
    // curve. The project's stored form follows.
    private void ReconcileCalibrationSelection()
    {
        string? selectedId = comboBoxCalibration.Items.Count > 0
            ? MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboBoxCalibration)
            : session.Project.CalibrationId;
        if (sessionCalibration is { } carried && calibrationResolver is { } resolve)
        {
            MicrophoneCalibrationEntry? same = calibrationEntries.FirstOrDefault(entry =>
                entry.Available &&
                CalibrationFile.SameCurve(resolve(entry.Id), carried.Curve));
            if (same != null)
            {
                if (VirtualCrossoverCalibrationSelection.IsSession(selectedId))
                {
                    selectedId = same.Id;
                }

                sessionCalibration = null;
            }
        }

        ApplyCalibrationSelection(selectedId);
        if (PersistCalibrationSelection())
        {
            ScheduleSave();
        }
    }

    // The project's curve decides, its id is a hint; an unresolved legacy id keeps the panel's choice.
    private void BindCalibrationSelection(
        bool imported,
        string? previousSelectedId,
        VirtualCrossoverSessionCalibration? previousSession)
    {
        Func<string?, CalibrationFile?> resolve = calibrationResolver ?? (_ => null);
        VirtualCrossoverCalibrationDecision decision =
            VirtualCrossoverCalibrationSelection.Resolve(
                session.Project.CalibrationId,
                session.Project.Calibration,
                imported,
                calibrationEntries,
                resolve,
                previousSelectedId,
                previousSession);
        sessionCalibration = decision.Session;
        pendingCalibrationNotice = decision.Notice;
        ApplyCalibrationSelection(decision.SelectedId);
        PersistCalibrationSelection();
    }

    // An entry no longer configured stays, marked, so the stored preference survives the rebuild.
    private void ApplyCalibrationSelection(string? selectedId)
    {
        suppressProjectEvents = true;
        try
        {
            MicrophoneCalibrationComboHelper.Configure(
                comboBoxCalibration,
                selectedId,
                CalibrationEntriesWithSession());
        }
        finally
        {
            suppressProjectEvents = false;
        }

        ResolveCalibration();
        RedrawAll();
    }

    private IReadOnlyList<MicrophoneCalibrationEntry> CalibrationEntriesWithSession() =>
        VirtualCrossoverCalibrationSelection.EntriesWith(calibrationEntries, sessionCalibration);

    private CalibrationFile? ResolveSelectedCalibration(string? calibrationId) =>
        VirtualCrossoverCalibrationSelection.IsSession(calibrationId)
            ? sessionCalibration?.Curve
            : calibrationResolver?.Invoke(calibrationId);

    // The name is the entry's, not an id's: the session's own curve has no id the wizard could resolve.
    private void ResolveCalibration()
    {
        string? selected =
            MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboBoxCalibration);
        bool own = VirtualCrossoverCalibrationSelection.IsOwn(selected);
        session.Calibration = new VirtualCrossoverCalibrationPolicy(
            own,
            own ? null : ResolveSelectedCalibration(selected),
            selected == null
                ? null
                : CalibrationEntriesWithSession()
                    .FirstOrDefault(entry =>
                        string.Equals(entry.Id, selected, StringComparison.OrdinalIgnoreCase))
                    ?.Name);
    }

    // Resolved through the same path as the curves, so the file carries what the plot shows. True when changed.
    private bool PersistCalibrationSelection()
    {
        (string? id, VirtualCrossoverCalibrationSettings? calibration) =
            VirtualCrossoverCalibrationSelection.Persist(
                MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboBoxCalibration),
                sessionCalibration,
                calibrationEntries,
                calibrationResolver ?? (_ => null),
                session.Project.CalibrationId,
                session.Project.Calibration);
        bool changed =
            !string.Equals(id, session.Project.CalibrationId, StringComparison.OrdinalIgnoreCase) ||
            !VirtualCrossoverCalibrationSelection.SameStored(calibration, session.Project.Calibration);
        session.Project.CalibrationId = id;
        session.Project.Calibration = calibration;
        return changed;
    }

    private void OnCalibrationChanged()
    {
        if (suppressProjectEvents)
        {
            return;
        }

        PersistCalibrationSelection();
        ResolveCalibration();
        SaveAndRedraw();
    }

    // A session carrying its curve starts on it and offers to keep it; an id-only match is reported as such,
    // and a miss keeps the panel's selection.
    private void ShowCalibrationNotice()
    {
        VirtualCrossoverCalibrationNotice notice = pendingCalibrationNotice;
        pendingCalibrationNotice = VirtualCrossoverCalibrationNotice.None;
        if (IsDisposed)
        {
            return;
        }

        switch (notice)
        {
            case VirtualCrossoverCalibrationNotice.CarriedBySession
                when sessionCalibration is { } carried:
                OfferSessionCalibration(carried);
                break;

            case VirtualCrossoverCalibrationNotice.MatchedBySlotName:
                MessageBox.Show(
                    FindForm(),
                    "This session names its microphone calibration by a slot only " +
                    $"('{session.Calibration.SelectedName}'), without the curve itself — it " +
                    "was written by an older version. This computer's entry of the " +
                    "same name is selected, but nothing says the two files agree: " +
                    "check that it is the calibration of the microphone these " +
                    "measurements were taken with.",
                    "Virtual DSP",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                break;

            case VirtualCrossoverCalibrationNotice.KeptPrevious:
                string kept = session.Calibration.SelectedName is { } name
                    ? $"The '{name}' calibration this panel already had is kept"
                    : "The curves are drawn without any calibration, as before";
                MessageBox.Show(
                    FindForm(),
                    "This session was tuned with a microphone calibration that is not " +
                    "configured on this computer, and it was written by an older " +
                    $"version that did not store the curve itself. {kept}; the " +
                    "curves may not match the ones its author saw.",
                    "Virtual DSP",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                break;
        }
    }

    // Keeping it is right for the author's measurements, wrong for ones taken here with another microphone.
    private void OfferSessionCalibration(VirtualCrossoverSessionCalibration carried)
    {
        if (calibrationAdder == null)
        {
            MessageBox.Show(
                FindForm(),
                $"This session carries the microphone calibration {carried.Description} " +
                "it was tuned with, and it is selected, so the curves match the ones " +
                "its author saw.",
                "Virtual DSP",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        DialogResult answer = MessageBox.Show(
            FindForm(),
            $"This session carries the microphone calibration {carried.Description} " +
            "it was tuned with, and it is selected, so the curves match the ones its " +
            "author saw.\r\n\r\nAdd it to your calibrations (Record Settings → More " +
            "calibrations) so the other views can use it too? Say yes if these " +
            "measurements were taken with that microphone; a measurement you take " +
            "with your own microphone needs its own calibration.",
            "Virtual DSP",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        string? addedId = calibrationAdder(carried);
        if (addedId == null)
        {
            return;
        }

        // Only for a host that did not refresh consumers on adding.
        if (!string.Equals(
                MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboBoxCalibration),
                addedId,
                StringComparison.OrdinalIgnoreCase))
        {
            sessionCalibration = null;
            ApplyCalibrationSelection(addedId);
            PersistCalibrationSelection();
            ScheduleSave();
        }
    }
}
