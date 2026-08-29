using System.Numerics;
using Resonalyze.Dsp;

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
/// <para>
/// When the tune's spatial averages allow it, a SECOND pair of sums is
/// prepared beside it: the same tune with each channel's magnitude corrected
/// onto its average over the listening volume instead of the one microphone
/// position (<see cref="SpatialAverageAudition"/>). The dialog offers the
/// choice; both pairs are built here because the correction reads the panel's
/// own state, which the dialog has no business holding.
/// </para>
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
        // The wait cursor covers the PREPARATION only, and the dialog is opened
        // outside it: summing both sides and levelling the captures against them is
        // a second or so of work with nothing on screen, while the dialog that
        // follows is the user's own to sit in.
        VirtualCrossoverAuditionContext? prepared;
        UseWaitCursor = true;
        try
        {
            prepared = await PrepareAuditionAsync();
        }
        finally
        {
            UseWaitCursor = false;
        }

        if (prepared == null)
        {
            return;
        }

        using var dialog = new VirtualCrossoverAuditionDialog(prepared);
        dialog.ShowDialog(FindForm());
    }

    // Everything the dialog needs, or null when the tune cannot be auditioned at all
    // — in which case the refusal has already been shown.
    private async Task<VirtualCrossoverAuditionContext?> PrepareAuditionAsync()
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
            return null;
        }

        if (leftSide == null && rightSide == null)
        {
            ShowError(
                "No channel has a source on either side.",
                "Pick measurements for at least one channel (Source...) before " +
                "auditioning a track.");
            return null;
        }

        // Half a tune is the normal mid-session state, so a missing side renders
        // from the other one instead of refusing; the dialog's report carries
        // the warning, in view the whole time the user sets the render up.
        string? borrowedSide = leftSide == null ? "left" : rightSide == null ? "right" : null;
        // Which measured side each ear ends up on, BEFORE the borrowing collapses
        // the two references into one. The spatial-average set is a set of
        // measurements, and a borrowed ear must not enter it twice.
        List<bool> measuredSides =
            MeasuredSides(leftSide != null, rightSide != null);
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
            return null;
        }

        LiveCaptureSetVerdict spatialVerdict = JudgeAuditionSpatialAverages(measuredSides);
        VirtualCrossoverAuditionSpatialAverage? spatialAverage = spatialVerdict.Coherent
            ? await BuildAuditionSpatialAverageAsync(left, right, measuredSides)
            : null;
        // A coherent set that still produced nothing is its own answer: the recipes
        // agreed, so the refusal came from the measurements themselves — a capture
        // whose band never meets its channel's, typically — and saying "no spatial
        // average" there would send the user looking for a file that is attached.
        // The preparation above is the flow's second await, and the panel stays live
        // behind the wait cursor — nothing disables it. A channel changed while it ran
        // would leave these sums describing the tune the user has just moved on from,
        // and neither the dialog nor the render it writes has any way to know.
        if (!processingCoordinator.IsCurrent(revision))
        {
            ShowError(
                "The tune changed while the audition was being prepared.",
                "Nothing was rendered; press Audition track again.");
            return null;
        }

        string? spatialRefusal = spatialAverage != null
            ? null
            : spatialVerdict.Coherent
                ? "the captures and the impulse responses have nothing to compare."
                : spatialVerdict.Reason ?? "this tune has none.";

        return new VirtualCrossoverAuditionContext(
            left.ImpulseResponse,
            right.ImpulseResponse,
            left.SampleRate,
            left.ChannelCount,
            right.ChannelCount,
            borrowedSide,
            ResolveSelectedCalibration,
            CalibrationEntriesWithSession(),
            Options.MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(
                comboBoxCalibration),
            ResolveOwnCalibration(left, right, measuredSides),
            spatialAverage,
            spatialRefusal);
    }

    /// <summary>
    /// What the panel's "Own (as measured)" means for a RENDER: the one curve every
    /// channel was read through, or the reason there is no such curve.
    /// </summary>
    /// <remarks>
    /// The panel can hold that selection because it corrects each channel's curve
    /// separately; a render cannot, because it bakes one filter into a side that
    /// several channels have already been summed into. Where the channels agree —
    /// one microphone measured the car, which is the ordinary case — the rule names a
    /// curve after all and the render carries it.
    /// <para>
    /// A microphone ARRAY is that ordinary case, not the exception: the array shares
    /// the sweep with the measurement microphone and stores level curves for its other
    /// positions, so the impulse response has one microphone behind it however many
    /// were listening, and the positions' individual calibrations never enter this
    /// question. What does reach it is a project assembled from measurements taken
    /// through DIFFERENT microphones — channels measured on separate days, or files
    /// written before a measurement recorded what it was read through mixed with newer
    /// ones. There the honest answer is to refuse and let the user name one curve,
    /// rather than pick a channel's and label the result as though it answered for all
    /// of them.
    /// </para>
    /// <para>
    /// It used to resolve through the app's calibration list, which has never heard of
    /// this id: Own came back as no curve at all, so the render was UNCALIBRATED and
    /// the report blamed a file it could not read.
    /// </para>
    /// </remarks>
    internal static VirtualCrossoverAuditionOwnCalibration ResolveOwnCalibration(
        VirtualCrossoverSideSum left,
        VirtualCrossoverSideSum right,
        IReadOnlyList<bool> measuredSides)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(measuredSides);
        var groups = new List<(CalibrationFile? Curve, string Label, List<string> Channels)>();
        var seen = new HashSet<VirtualCrossoverChannelState>();
        bool bothSides = measuredSides.Count > 1;
        for (int i = 0; i < measuredSides.Count; i++)
        {
            VirtualCrossoverSideSum side = i == 0 ? left : right;
            bool rightSide = measuredSides[i];
            foreach (ProcessedChannel processed in side.Channels)
            {
                VirtualCrossoverChannelState state =
                    processed.Channel.SideState(rightSide);
                // A mono pair hands the same measurement to both ears; counting it
                // twice would say two channels agree when there is only one.
                if (!seen.Add(state))
                {
                    continue;
                }

                CalibrationFile? curve = state.MicrophoneCalibrationCurve;
                string channelName = bothSides && !processed.Channel.Pair.Mono
                    ? $"{processed.Channel.Name} {(rightSide ? "R" : "L")}"
                    : processed.Channel.Name;
                int group = groups.FindIndex(
                    entry => CalibrationFile.SameCurve(entry.Curve, curve));
                if (group < 0)
                {
                    groups.Add((
                        curve,
                        state.MicrophoneCalibration?.Name ?? "none recorded",
                        [channelName]));
                }
                else
                {
                    groups[group].Channels.Add(channelName);
                }
            }
        }

        if (groups.Count == 0)
        {
            return new VirtualCrossoverAuditionOwnCalibration(null, null, null);
        }

        if (groups.Count == 1)
        {
            return new VirtualCrossoverAuditionOwnCalibration(
                groups[0].Curve, groups[0].Curve == null ? null : groups[0].Label, null);
        }

        return new VirtualCrossoverAuditionOwnCalibration(
            null,
            null,
            "the channels were not measured through one calibration (" +
                string.Join(
                    "; ",
                    groups.Select(group =>
                        $"{group.Label}: {string.Join(", ", group.Channels)}")) +
                "), and a render carries one correction for a whole side");
    }

    /// <summary>
    /// Which measured sides the two ears will render from, as the side flags every
    /// per-side lookup takes — <c>false</c> is the left one.
    /// </summary>
    /// <remarks>
    /// Half a tune renders both ears from the ONE side that has sources, so the list
    /// names that side rather than both. Its flag is the side's own: a left-only tune
    /// is <c>[false]</c>. Getting that backwards is invisible in the ordinary case and
    /// silent in the half one — every per-side lookup would read the EMPTY side, so a
    /// tune with averages on the side it renders from would report having none.
    /// A caller with neither side has already refused.
    /// </remarks>
    internal static List<bool> MeasuredSides(bool hasLeft, bool hasRight) =>
        hasLeft && hasRight ? [false, true] : [!hasLeft];

    /// <summary>
    /// Whether the sides being rendered may be corrected onto their spatial averages
    /// at all.
    /// </summary>
    /// <remarks>
    /// The RECIPE decides, exactly as it does for the hybrid view: coverage alone
    /// would admit captures taken at three frame lengths and two scales, which cannot
    /// share the one offset that levels them. Both ears are judged as ONE set when
    /// both are real, because a render levelled per side would put an L/R imbalance in
    /// the track that the car does not have — the same objection that gates the plot's
    /// dashed opposite sum.
    /// </remarks>
    private LiveCaptureSetVerdict JudgeAuditionSpatialAverages(IReadOnlyList<bool> sides)
    {
        if (SpatialAverageMode == VirtualCrossoverSpatialAverageMode.Off)
        {
            return LiveCaptureSetVerdict.No(
                "This project is set to use no spatial average (MMM button).");
        }

        var collected = new List<List<LiveCaptureDocument>>(sides.Count);
        foreach (bool rightSide in sides)
        {
            LiveCaptureSetVerdict gathered =
                TryCollectSideCaptures(rightSide, out List<LiveCaptureDocument> captures);
            if (!gathered.Coherent)
            {
                return gathered;
            }

            collected.Add(captures);
        }

        return collected.Count > 1
            ? JudgeSidesShareAnOffset(collected[0], collected[1])
            : collected.Count == 1
                ? LiveCaptureDocument.JudgeSet(collected[0])
                : LiveCaptureSetVerdict.No("No side has a spatial average.");
    }

    /// <summary>
    /// The same tune with every channel's magnitude moved from the microphone position
    /// onto its spatial average, or null when nothing could be corrected.
    /// </summary>
    /// <remarks>
    /// Snapshot first, compute after: what the correction needs off the panel is
    /// gathered on the UI thread (references only), and the transforms — a band
    /// spectrum per channel, a filter design and a convolution each — run on a worker.
    /// They are cheap next to the render that follows, and eager rather than lazy
    /// because the dialog's toggle has to be able to say what it would do BEFORE the
    /// user commits to a render.
    /// </remarks>
    private async Task<VirtualCrossoverAuditionSpatialAverage?>
        BuildAuditionSpatialAverageAsync(
            VirtualCrossoverSideSum left,
            VirtualCrossoverSideSum right,
            IReadOnlyList<bool> measuredSides)
    {
        var entries = new List<SpatialAverageAuditionChannel>();
        var names = new List<string>();
        var seen = new Dictionary<VirtualCrossoverChannelState, int>();
        bool bothSides = measuredSides.Count > 1;

        int[] EntriesFor(VirtualCrossoverSideSum side, bool rightSide)
        {
            var indices = new int[side.Channels.Count];
            for (int i = 0; i < side.Channels.Count; i++)
            {
                ProcessedChannel processed = side.Channels[i];
                VirtualCrossoverChannelState state =
                    processed.Channel.SideState(rightSide);
                if (!seen.TryGetValue(state, out int entry))
                {
                    entry = entries.Count;
                    entries.Add(new SpatialAverageAuditionChannel(
                        // The CROPPED source, which is the array the processed response
                        // was made from — the same reason the EQ Wizard handoff reads it.
                        // The measurement's own record runs on for seconds past the
                        // arrival at the noise floor, and a curve read over all of it
                        // describes a longer record than the kernel being filtered: the
                        // extra noise lifts the bands where a channel is quiet, which is
                        // where the difference is least able to bear it. An unresolved
                        // source cannot reach a side sum, so the fallback is a guard —
                        // an empty response yields no curve and the channel keeps its
                        // point measurement.
                        state.ProcessingSource?.CroppedImpulseResponse ?? [],
                        processed.SampleRate,
                        processed.MeasuredBand,
                        state.MicrophoneCalibrationCurve,
                        state.SpatialAverageFor(SpatialAverageMode)));
                    names.Add(
                        bothSides && !processed.Channel.Pair.Mono
                            ? $"{processed.Channel.Name} {(rightSide ? "R" : "L")}"
                            : processed.Channel.Name);
                    seen[state] = entry;
                }

                indices[i] = entry;
            }

            return indices;
        }

        bool borrowed = ReferenceEquals(left, right);
        int[] leftEntries = EntriesFor(left, measuredSides[0]);
        int[] rightEntries = borrowed
            ? leftEntries
            : EntriesFor(right, measuredSides[1]);
        int sampleRate = left.SampleRate;

        return await Task.Run(() =>
        {
            SpatialAverageAuditionPlan plan = SpatialAverageAudition.Build(entries);
            if (!plan.Corrects)
            {
                return null;
            }

            Complex[] leftSum = CorrectedSum(left, leftEntries, plan, sampleRate);
            Complex[] rightSum = borrowed
                ? leftSum
                : CorrectedSum(right, rightEntries, plan, sampleRate);
            return new VirtualCrossoverAuditionSpatialAverage(
                leftSum,
                rightSum,
                DescribeSpatialAverage(plan, entries, names));
        });
    }

    // One side, every channel filtered onto its average and summed again. The sum is
    // rebuilt from the corrected parts rather than corrected as a whole: a side's
    // channels each have their own average, and one filter over their sum could only
    // be a compromise between them wherever two of them overlap.
    private static Complex[] CorrectedSum(
        VirtualCrossoverSideSum side,
        IReadOnlyList<int> entries,
        SpatialAverageAuditionPlan plan,
        int sampleRate)
    {
        var corrected = new List<Complex[]>(side.Channels.Count);
        for (int i = 0; i < side.Channels.Count; i++)
        {
            corrected.Add(SpatialAverageAudition.Apply(
                side.Channels[i].ImpulseResponse,
                plan.Corrections[entries[i]],
                sampleRate));
        }

        return VirtualCrossoverAnalysis.SumImpulseResponses(corrected);
    }

    // What the dialog's report says about the correction it is offering.
    private static IReadOnlyList<string> DescribeSpatialAverage(
        SpatialAverageAuditionPlan plan,
        IReadOnlyList<SpatialAverageAuditionChannel> entries,
        IReadOnlyList<string> names)
    {
        var lines = new List<string>
        {
            $"Set offset {plan.SetOffsetDb:+0.0;-0.0} dB, channels disagree by " +
                $"{plan.SpreadDb:0.0} dB."
        };
        int limited = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            SpatialAverageAuditionCorrection correction = plan.Corrections[i];
            limited += correction.LimitedPoints;
            lines.Add(correction.Corrects
                ? $"  {names[i]}: {correction.LowestDb:+0.0;-0.0} … " +
                    $"{correction.HighestDb:+0.0;-0.0} dB"
                : $"  {names[i]}: point measurement (no average to correct with)");
        }

        if (limited > 0)
        {
            lines.Add(
                $"{limited} band(s) reached the ±{SpatialAverageAudition.LimitDb:0} dB " +
                "limit — the two measurements disagree there by more than a " +
                "correction is allowed to fix.");
        }

        return lines;
    }
}
