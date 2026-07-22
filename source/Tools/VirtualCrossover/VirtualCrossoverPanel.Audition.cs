namespace Resonalyze;

/// <summary>
/// The Virtual DSP "Audition track" command: sums both sides of the tune and
/// hands them to <see cref="VirtualCrossoverAuditionDialog"/>, where the user
/// picks a music file, a destination and a microphone calibration, and renders
/// the track through the tune. The result is a headphone-only stereo
/// auralization of the measured left and right acoustic paths at the
/// microphone position — drivers, cabin and capsule included, but not a
/// binaural head simulation; played back through the same system it would
/// convolve the car twice.
/// </summary>
public partial class VirtualCrossoverPanel
{
    // Reentrancy guard: the side summing below awaits with the button still
    // enabled, so a double-click would otherwise start two interleaved flows.
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
        // Both sides are summed from the SAME coordinator revision, so the two
        // ears are rendered from one consistent state of the tune.
        long revision = processingCoordinator.CurrentRevision;
        VirtualCrossoverSideSum? leftSide = await metrics.ComputeSideSumAsync(
            channels, rightSide: false, revision, minimumChannels: 1);
        VirtualCrossoverSideSum? rightSide = await metrics.ComputeSideSumAsync(
            channels, rightSide: true, revision, minimumChannels: 1);
        // Staleness first: a mid-flight settings change nulls whichever sum ran
        // second, and that null must not masquerade as a missing side below.
        if (!processingCoordinator.IsCurrent(revision))
        {
            ShowError(
                "The tune changed while the sides were being summed.",
                "Nothing was rendered; press Audition track again.");
            return;
        }

        if (leftSide == null && rightSide == null)
        {
            ShowError(
                "No channel has a source on either side.",
                "Pick measurements for at least one channel (Source...) before " +
                "auditioning a track.");
            return;
        }

        // Half a tune is the normal mid-session state, so a missing side renders
        // from the other one instead of refusing; the dialog's report carries
        // the warning, in view the whole time the user sets the render up.
        string? borrowedSide = leftSide == null ? "left" : rightSide == null ? "right" : null;
        leftSide ??= rightSide;
        rightSide ??= leftSide;
        VirtualCrossoverSideSum left = leftSide!;
        VirtualCrossoverSideSum right = rightSide!;
        if (left.SampleRate != right.SampleRate)
        {
            // The project format admits one rate, so this is a guard rather than
            // a case: a mixed-rate pair would render the two ears on different
            // time bases.
            ShowError(
                $"The two sides were measured at different rates " +
                $"({left.SampleRate} Hz and {right.SampleRate} Hz).",
                "All channels in a Virtual DSP project must share one sample rate.");
            return;
        }

        using var dialog = new VirtualCrossoverAuditionDialog(
            new VirtualCrossoverAuditionContext(
                left.ImpulseResponse,
                right.ImpulseResponse,
                left.SampleRate,
                left.ChannelCount,
                right.ChannelCount,
                borrowedSide,
                calibrationResolver,
                hasZeroDegreeCalibration,
                hasNinetyDegreeCalibration,
                project.CalibrationMode));
        dialog.ShowDialog(FindForm());
    }
}
