namespace Resonalyze;

/// <summary>The Audition track button: what is rendered is prepared by <see cref="VirtualCrossoverAudition"/>; the panel adds
/// the calibration choices and opens <see cref="VirtualCrossoverAuditionDialog"/>.</summary>
public partial class VirtualCrossoverPanel
{
    // The side summing awaits with the button enabled; a double-click would start two flows.
    private bool auditionInFlight;

    private async Task AuditionTrackAsync()
    {
        if (auditionInFlight)
        {
            return;
        }

        auditionInFlight = true;
        try
        {
            await RunAuditionFlowAsync();
        }
        finally
        {
            auditionInFlight = false;
        }
    }

    private async Task RunAuditionFlowAsync()
    {
        VirtualCrossoverAuditionContext? prepared;
        AuditionRefusal? refusal;
        UseWaitCursor = true;
        try
        {
            (prepared, refusal) = await audition.PrepareAsync();
        }
        finally
        {
            UseWaitCursor = false;
        }

        if (prepared == null)
        {
            ShowError(refusal!.Message, refusal.Detail);
            return;
        }

        using var dialog = new VirtualCrossoverAuditionDialog(prepared with
        {
            CalibrationResolver = ResolveSelectedCalibration,
            CalibrationEntries = CalibrationEntriesWithSession(),
            InitialCalibrationId = Options.MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(
                comboBoxCalibration)
        });
        dialog.ShowDialog(FindForm());
    }
}
