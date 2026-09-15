using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>"Audition track": sums both sides (and, when possible, a spatial-average-corrected pair) for <see cref="VirtualCrossoverAuditionDialog"/>. Headphone-only auralization at the mic position.</summary>
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

    // Null when the tune cannot be auditioned; the refusal has already been shown.
    private async Task<VirtualCrossoverAuditionContext?> PrepareAuditionAsync()
    {
        // Both sides from one coordinator revision, so both ears share one tune state.
        long revision = processingCoordinator.CurrentRevision;
        VirtualCrossoverSideSum? leftSide = await metrics.ComputeSideSumAsync(
            channels, rightSide: false, revision, minimumChannels: 1);
        VirtualCrossoverSideSum? rightSide = await metrics.ComputeSideSumAsync(
            channels, rightSide: true, revision, minimumChannels: 1);
        // Staleness first: a mid-flight change nulls the second sum, which must not read as a missing side.
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

        // A missing side renders from the other; the dialog report warns.
        string? borrowedSide = leftSide == null ? "left" : rightSide == null ? "right" : null;
        // Captured before borrowing, so a borrowed ear does not enter the spatial-average set twice.
        List<bool> measuredSides =
            MeasuredSides(leftSide != null, rightSide != null);
        leftSide ??= rightSide;
        rightSide ??= leftSide;
        VirtualCrossoverSideSum left = leftSide!;
        VirtualCrossoverSideSum right = rightSide!;
        if (left.SampleRate != right.SampleRate)
        {
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
        // A coherent set that produced nothing is refused by the measurements (e.g. band mismatch), not by a missing file.
        // Re-check the revision: the panel stays live during this await.
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

    /// <summary>"Own (as measured)" for a render: the one curve every channel used, or why there is none.</summary>
    /// <remarks>One filter is baked into both summed sides, so mixed microphones are refused. See docs/tech/spatial-average.md#audition-render.</remarks>
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
                // A mono pair feeds both ears; count it once.
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
                "), and a render carries a single correction for both sides");
    }

    /// <summary>Side flags (<c>false</c> = left) the ears render from; half a tune names only its measured side.</summary>
    internal static List<bool> MeasuredSides(bool hasLeft, bool hasRight) =>
        hasLeft && hasRight ? [false, true] : [!hasLeft];

    /// <summary>Whether the rendered sides may be corrected onto spatial averages: the recipe decides, and both ears are judged as one set.</summary>
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

    /// <summary>The tune corrected onto spatial averages, or null. Snapshot on the UI thread, compute on a worker, eagerly so the dialog can describe it.</summary>
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
                        // Cropped source, as for the EQ Wizard handoff: the full record's noise tail lifts quiet bands.
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

    // Rebuilt from corrected channels: one filter over the sum would compromise overlapping channels.
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
