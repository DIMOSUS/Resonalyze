using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Why the tune cannot be auditioned, and what to do about it.</summary>
internal sealed record AuditionRefusal(string Message, string Detail);

/// <summary>"Audition track" off the session: both sides summed at one revision and, when the recipe allows, a pair corrected
/// onto spatial averages, for <see cref="VirtualCrossoverAuditionDialog"/>. Headphone-only auralization at the mic position.</summary>
internal sealed class VirtualCrossoverAudition(
    VirtualCrossoverSession session,
    VirtualCrossoverProcessingCoordinator coordinator,
    VirtualCrossoverMetrics metrics,
    VirtualCrossoverHybrid hybrid)
{
    /// <returns>The render's context without the calibration choices, which are the caller's; or the refusal.</returns>
    public async Task<(VirtualCrossoverAuditionContext? Context, AuditionRefusal? Refusal)> PrepareAsync()
    {
        // Both sides from one coordinator revision, so both ears share one tune state.
        long revision = coordinator.CurrentRevision;
        VirtualCrossoverSideSum? leftSide = await metrics.ComputeSideSumAsync(
            session.Channels, rightSide: false, revision, minimumChannels: 1);
        VirtualCrossoverSideSum? rightSide = await metrics.ComputeSideSumAsync(
            session.Channels, rightSide: true, revision, minimumChannels: 1);
        // Staleness first: a mid-flight change nulls the second sum, which must not read as a missing side.
        if (!coordinator.IsCurrent(revision))
        {
            return (null, new AuditionRefusal(
                "The tune changed while the sides were being summed.",
                "Nothing was rendered; press Audition track again."));
        }

        // A mono block sums into both sides, so a side holding only mono blocks is missing its own drivers.
        if (!HasOwnSource(session.Channels, rightSide: false) && HasOwnSource(session.Channels, rightSide: true))
        {
            leftSide = null;
        }
        else if (!HasOwnSource(session.Channels, rightSide: true) && HasOwnSource(session.Channels, rightSide: false))
        {
            rightSide = null;
        }

        if (leftSide == null && rightSide == null)
        {
            return (null, new AuditionRefusal(
                "No channel has a source on either side.",
                "Pick measurements for at least one channel (Source...) before " +
                "auditioning a track."));
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
            return (null, new AuditionRefusal(
                $"The two sides were measured at different rates " +
                $"({left.SampleRate} Hz and {right.SampleRate} Hz).",
                "All channels in a Virtual DSP project must share one sample rate."));
        }

        LiveCaptureSetVerdict spatialVerdict = JudgeSpatialAverages(measuredSides);
        VirtualCrossoverAuditionSpatialAverage? spatialAverage = spatialVerdict.Coherent
            ? await BuildSpatialAverageAsync(left, right, measuredSides)
            : null;
        // A coherent set that produced nothing is refused by the measurements (e.g. band mismatch), not by a missing file.
        // Re-check the revision: the panel stays live during this await.
        if (!coordinator.IsCurrent(revision))
        {
            return (null, new AuditionRefusal(
                "The tune changed while the audition was being prepared.",
                "Nothing was rendered; press Audition track again."));
        }

        string? spatialRefusal = spatialAverage != null
            ? null
            : spatialVerdict.Coherent
                ? "the captures and the impulse responses have nothing to compare."
                : spatialVerdict.Reason ?? "this tune has none.";

        return (
            new VirtualCrossoverAuditionContext(
                left.ImpulseResponse,
                right.ImpulseResponse,
                left.SampleRate,
                left.ChannelCount,
                right.ChannelCount,
                borrowedSide,
                CalibrationResolver: null,
                CalibrationEntries: [],
                InitialCalibrationId: null,
                ResolveOwnCalibration(left, right, measuredSides),
                spatialAverage,
                spatialRefusal),
            null);
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
                string channelName = EarLabel(processed.Channel, rightSide, bothSides);
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

    /// <summary>Whether an enabled stereo block has a source on this side; mono blocks feed both and prove neither.</summary>
    internal static bool HasOwnSource(IReadOnlyList<VirtualCrossoverChannel> channels, bool rightSide) =>
        channels.Any(channel =>
            channel.Pair.Enabled &&
            !channel.Pair.Mono &&
            channel.SideState(rightSide).ProcessingSource != null);

    /// <summary>Side flags (<c>false</c> = left) the ears render from; half a tune names only its measured side.</summary>
    internal static List<bool> MeasuredSides(bool hasLeft, bool hasRight) =>
        hasLeft && hasRight ? [false, true] : [!hasLeft];

    // A block's side is named only when both ears render and the block has two.
    private static string EarLabel(VirtualCrossoverChannel channel, bool rightSide, bool bothSides) =>
        bothSides && !channel.Pair.Mono
            ? $"{channel.Name} {(rightSide ? "R" : "L")}"
            : channel.Name;

    /// <summary>Whether the rendered sides may be corrected onto spatial averages: the recipe decides, and both ears are judged as one set.</summary>
    private LiveCaptureSetVerdict JudgeSpatialAverages(IReadOnlyList<bool> sides)
    {
        if (session.SpatialAverageMode == VirtualCrossoverSpatialAverageMode.Off)
        {
            return LiveCaptureSetVerdict.No(
                "This project is set to use no spatial average (MMM button).");
        }

        var collected = new List<List<LiveCaptureDocument>>(sides.Count);
        foreach (bool rightSide in sides)
        {
            LiveCaptureSetVerdict gathered =
                hybrid.CollectSideCaptures(rightSide, out List<LiveCaptureDocument> captures);
            if (!gathered.Coherent)
            {
                return gathered;
            }

            collected.Add(captures);
        }

        return collected.Count > 1
            ? VirtualCrossoverHybrid.JudgeSidesShareAnOffset(collected[0], collected[1])
            : collected.Count == 1
                ? LiveCaptureDocument.JudgeSet(collected[0])
                : LiveCaptureSetVerdict.No("No side has a spatial average.");
    }

    /// <summary>The tune corrected onto spatial averages, or null. Snapshot on the UI thread, compute on a worker, eagerly so the dialog can describe it.</summary>
    private async Task<VirtualCrossoverAuditionSpatialAverage?> BuildSpatialAverageAsync(
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
                        state.SpatialAverageFor(session.SpatialAverageMode)));
                    names.Add(EarLabel(processed.Channel, rightSide, bothSides));
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
