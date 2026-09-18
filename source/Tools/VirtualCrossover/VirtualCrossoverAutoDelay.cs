using System.Numerics;
using System.Text;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Why a run does not start. <see cref="Message"/> is what an interactive run shows (without one it beeps when
/// <see cref="Beep"/> says so, else stays quiet); <see cref="Summary"/> is the phrase an AI import quotes.</summary>
internal sealed record AutoDelayRefusal(
    string Summary,
    string? Message = null,
    string? Detail = null,
    bool Beep = false);

/// <summary>A prepared run: the button hands it to the dialog, an AI import runs it directly.</summary>
internal sealed record AutoDelayPlan(
    bool Stereo,
    Func<AutoDelayRunRequest, Task<AutoDelayRunResult>> Run,
    string? PolarityWarning,
    bool HasRearFill);

/// <summary>Auto delay over a session: which run the tune asks for, the run itself on a worker, and writing a confirmed
/// result back. Stages and tie-breaks live in <see cref="AutoAlignmentEngine"/> / AlignmentSelection; previous delays and
/// polarities are ignored, so each run is an absolute proposal. See docs/tech/virtual-dsp-panel.md#staged-auto-delay.</summary>
internal static class VirtualCrossoverAutoDelay
{
    /// <param name="consentToBroadWindow">Asked when no channel has a crossover; null (an import) refuses instead.</param>
    public static (AutoDelayPlan? Plan, AutoDelayRefusal? Refusal) Prepare(
        VirtualCrossoverSession session,
        GatePlacementVerdict? gatePlacement,
        Func<bool>? consentToBroadWindow)
    {
        // Stereo when some non-mono pair has both sides resolved (the highest becomes the L/R bridge).
        (List<VirtualCrossoverSideAlignmentChannel> leftSide, List<VirtualCrossoverSideAlignmentChannel> rightSide) =
            CollectStereoSides(session.Channels);
        VirtualCrossoverSideAlignmentChannel? bridgeRight = PickStereoBridge(leftSide, rightSide);
        return bridgeRight != null && leftSide.Count(InFrontChain) >= 2
            ? PrepareStereo(
                session, leftSide, rightSide, bridgeRight, gatePlacement, consentToBroadWindow)
            : PrepareSingleSide(session, gatePlacement, consentToBroadWindow);
    }

    // The dialog's modality keeps channel settings stable during the background compute; an import disables the panel.
    private static (AutoDelayPlan? Plan, AutoDelayRefusal? Refusal) PrepareSingleSide(
        VirtualCrossoverSession session,
        GatePlacementVerdict? gatePlacement,
        Func<bool>? consentToBroadWindow)
    {
        // No DSP here: crop and ApplyChain run later in ComputeAlignment's AlignmentReprocessor.
        List<VirtualCrossoverChannel> participants = session.Channels
            .Where(channel =>
                channel.Pair.Enabled && channel.TransferImpulseResponse != null)
            .ToList();
        if (participants.Count < 2)
        {
            return (null, new AutoDelayRefusal(
                "fewer than two enabled channels have a measurement", Beep: true));
        }

        if (RefuseToRun(
                [.. participants.Where(channel => channel.Pair.Bypass).Select(channel => channel.Name)],
                participants.Any(channel => channel.Settings.EffectiveCrossover.Kind != CrossoverKind.Off),
                gatePlacement,
                consentToBroadWindow) is { } refusal)
        {
            return (null, refusal);
        }

        (double minHz, double maxHz) = VirtualCrossoverJunctions.GetCrossoverWindow(
            participants.Select(channel => channel.Settings));
        return (
            new AutoDelayPlan(
                Stereo: false,
                request => RunSingleSideAsync(session, participants, minHz, maxHz, request),
                PolarityWarning: null,
                HasRearFill: participants.Any(channel =>
                    channel.Pair.Zone == VirtualCrossoverZone.Rear)),
            null);
    }

    // Driver's side first, then the L/R bridge at the top pair, then the far side (AutoAlignmentEngine.ComputeStereo).
    private static (AutoDelayPlan? Plan, AutoDelayRefusal? Refusal) PrepareStereo(
        VirtualCrossoverSession session,
        List<VirtualCrossoverSideAlignmentChannel> leftSide,
        List<VirtualCrossoverSideAlignmentChannel> rightSide,
        VirtualCrossoverSideAlignmentChannel bridgeRight,
        GatePlacementVerdict? gatePlacement,
        Func<bool>? consentToBroadWindow)
    {
        List<VirtualCrossoverSideAlignmentChannel> union = leftSide.Concat(rightSide)
            .Distinct()
            .ToList();

        // Bypass belongs to the block, so both sides are refused. The gate verdict covers only the side on screen.
        if (RefuseToRun(
                [.. union.Where(item => item.Runtime.Pair.Bypass).Select(item => item.Name)],
                union.Any(item => item.Settings.EffectiveCrossover.Kind != CrossoverKind.Off),
                gatePlacement,
                consentToBroadWindow) is { } refusal)
        {
            return (null, refusal);
        }

        VirtualCrossoverSideAlignmentChannel bridgeLeft = leftSide.First(
            item => item.Runtime == bridgeRight.Runtime && !item.RightSide);
        if (StereoBridgeBand(bridgeLeft, bridgeRight) is not
            (double bridgeBandLowHz, double bridgeBandHighHz))
        {
            (double leftBandLowHz, double leftBandHighHz) =
                VirtualCrossoverJunctions.GetChannelBand(bridgeLeft.Settings);
            (double rightBandLowHz, double rightBandHighHz) =
                VirtualCrossoverJunctions.GetChannelBand(bridgeRight.Settings);
            return (null, new AutoDelayRefusal(
                "the stereo bridge has no usable shared band",
                "The stereo bridge has no usable shared band.",
                $"The top pair's crossover bands barely overlap: " +
                $"{bridgeLeft.Name} plays {leftBandLowHz:0}-{leftBandHighHz:0} Hz, " +
                $"{bridgeRight.Name} plays {rightBandLowHz:0}-{rightBandHighHz:0} Hz. " +
                "Align the pair's crossover settings so the sides share at " +
                "least a third of an octave and run Auto delay again."));
        }

        return (
            new AutoDelayPlan(
                Stereo: true,
                request => RunStereoAsync(
                    session, leftSide, rightSide, union, bridgeLeft, bridgeRight,
                    bridgeBandLowHz, bridgeBandHighHz, request),
                DescribeLeftRightPolarityMismatch(leftSide, rightSide),
                union.Any(side => side.Runtime.Pair.Zone == VirtualCrossoverZone.Rear)),
            null);
    }

    // The checks both runs share, in the order the user meets them.
    private static AutoDelayRefusal? RefuseToRun(
        IReadOnlyList<string> bypassed,
        bool anyCrossover,
        GatePlacementVerdict? gatePlacement,
        Func<bool>? consentToBroadWindow)
    {
        // Overrides would not move a bypassed channel, yet it would join the walk and get a delay applied later.
        if (bypassed.Count > 0)
        {
            string names = string.Join(", ", bypassed);
            return new AutoDelayRefusal(
                "a participating channel is bypassed: " + names,
                "Auto delay cannot run with bypassed channels.",
                "Bypass feeds the raw measured signal, so the computed delays " +
                "and polarities would not apply to: " + names +
                ".\r\n\r\nDisable Bypass on every participating channel " +
                "(or mute the channel to exclude it) and run Auto delay again.");
        }

        if (gatePlacement is { CutsChannels: true } verdict)
        {
            return new AutoDelayRefusal(
                "the phase gate is misplaced",
                verdict.FormatRefusal("Auto delay"),
                verdict.FormatDetail());
        }

        // Without crossovers the search uses a broad midband window and the result shifts once filters are set.
        if (!anyCrossover)
        {
            if (consentToBroadWindow == null)
            {
                return new AutoDelayRefusal(
                    "no channel has a crossover configured; set the crossovers first");
            }

            if (!consentToBroadWindow())
            {
                return new AutoDelayRefusal("cancelled");
            }
        }

        return null;
    }

    // A mono pair contributes ONE instance (left), tuned in the left pass and fixed on the right.
    internal static (List<VirtualCrossoverSideAlignmentChannel> Left, List<VirtualCrossoverSideAlignmentChannel> Right)
        CollectStereoSides(IEnumerable<VirtualCrossoverChannel> channels)
    {
        var left = new List<VirtualCrossoverSideAlignmentChannel>();
        var right = new List<VirtualCrossoverSideAlignmentChannel>();
        foreach (VirtualCrossoverChannel channel in channels)
        {
            if (channel.Pair.Enabled &&
                channel.SideState(false).TransferImpulseResponse != null)
            {
                var side = new VirtualCrossoverSideAlignmentChannel(channel, false);
                left.Add(side);
                if (channel.Pair.Mono)
                {
                    right.Add(side);
                }
            }

            if (!channel.Pair.Mono &&
                channel.Pair.Enabled &&
                channel.SideState(true).TransferImpulseResponse != null)
            {
                right.Add(new VirtualCrossoverSideAlignmentChannel(channel, true));
            }
        }

        return (left, right);
    }

    // A mono block appears once (as its left instance), so a centre is found exactly once.
    /// <summary>Top front-chain pair resolved on both sides, or null where the run cannot be a stereo one.
    /// The bridge must be a front-chain pair, or the scene anchors to the rear fill.</summary>
    internal static VirtualCrossoverSideAlignmentChannel? PickStereoBridge(
        List<VirtualCrossoverSideAlignmentChannel> leftSide,
        List<VirtualCrossoverSideAlignmentChannel> rightSide) =>
        rightSide
            .Where(item => item.RightSide &&
                InFrontChain(item) &&
                leftSide.Any(left =>
                    left.Runtime == item.Runtime && !left.RightSide))
            .OrderBy(item => VirtualCrossoverJunctions.BandCenterHz(item.Settings))
            .LastOrDefault();

    /// <summary>Bridge band = INTERSECTION of both sides' playing bands; null where they barely overlap.</summary>
    internal static (double LowHz, double HighHz)? StereoBridgeBand(
        VirtualCrossoverSideAlignmentChannel bridgeLeft,
        VirtualCrossoverSideAlignmentChannel bridgeRight)
    {
        (double leftLowHz, double leftHighHz) =
            VirtualCrossoverJunctions.GetChannelBand(bridgeLeft.Settings);
        (double rightLowHz, double rightHighHz) =
            VirtualCrossoverJunctions.GetChannelBand(bridgeRight.Settings);
        double lowHz = Math.Max(leftLowHz, rightLowHz);
        double highHz = Math.Min(leftHighHz, rightHighHz);
        return highHz < lowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio
            ? null
            : (lowHz, highHz);
    }

    internal static bool InFrontChain(VirtualCrossoverSideAlignmentChannel side) =>
        VirtualCrossoverAlignmentStages.StageOf(side.Runtime.Pair.Zone) ==
            VirtualCrossoverAlignmentStage.FrontChain;

    // From the raw transfer IRs (the "IR:" badge), not the Invert switch: alignment can mask a swapped wire.
    private static string? DescribeLeftRightPolarityMismatch(
        IEnumerable<VirtualCrossoverSideAlignmentChannel> leftSide,
        IReadOnlyCollection<VirtualCrossoverSideAlignmentChannel> rightSide)
    {
        var names = new List<string>();
        foreach (VirtualCrossoverSideAlignmentChannel right in
            rightSide.Where(side => side.RightSide))
        {
            VirtualCrossoverSideAlignmentChannel? left = leftSide.FirstOrDefault(
                side => side.Runtime == right.Runtime && !side.RightSide);
            if (left == null ||
                left.State.TransferImpulseResponse is not { } leftIr ||
                right.State.TransferImpulseResponse is not { } rightIr)
            {
                continue;
            }

            PolarityEstimate leftPolarity = VirtualCrossoverAnalysis.EstimatePolarity(leftIr);
            PolarityEstimate rightPolarity = VirtualCrossoverAnalysis.EstimatePolarity(rightIr);
            if (leftPolarity != PolarityEstimate.Unknown &&
                rightPolarity != PolarityEstimate.Unknown &&
                leftPolarity != rightPolarity)
            {
                names.Add(right.Runtime.Name);
            }
        }

        return FormatPolarityMismatchWarning(names);
    }

    internal static string? FormatPolarityMismatchWarning(
        IReadOnlyList<string> mismatchedDrivers) =>
        mismatchedDrivers.Count == 0
            ? null
            : $"⚠ L/R polarity mismatch on {string.Join(", ", mismatchedDrivers)} — " +
              "one side measured inverted (check wiring).";

    private static async Task<AutoDelayRunResult> RunSingleSideAsync(
        VirtualCrossoverSession session,
        List<VirtualCrossoverChannel> participants,
        double windowMinHz,
        double windowMaxHz,
        AutoDelayRunRequest request)
    {
        int processorSampleRateHz = session.ProcessorSampleRateHz;
        double maxDelayMs = session.ProcessorMaxDelayMs;
        var log = new StringBuilder();
        log.AppendLine($"Auto delay {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        log.AppendLine($"Crossover window: {windowMinHz:0} - {windowMaxHz:0} Hz");
        log.AppendLine("Previous delay / polarity settings ignored for this run.");

        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        var decisions = new Dictionary<IAlignmentChannel, AlignmentDecision>();
        IReadOnlyList<GainBalanceResult>? gains = null;
        AutoDelaySumLossForecast? sumLoss = null;
        await Task.Run(() =>
        {
            // An unstaged project puts every participant in the chain, so this is the plain unstaged engine call.
            (List<VirtualCrossoverChannel> chain, List<VirtualCrossoverChannel> later) =
                VirtualCrossoverAlignmentStages.Split(participants);
            AlignmentReprocessor reprocessor = ComputeAlignment(
                participants,
                alignment,
                decisions,
                log,
                processorSampleRateHz,
                maxDelayMs,
                walkSet: later.Count > 0 ? chain : null);
            if (later.Count > 0)
            {
                IReadOnlyCollection<IAlignmentChannel> fillCarriers =
                    StagedGroupPlacement.PlaceSingleSide(
                        chain, later, reprocessor, alignment, decisions,
                        request.RearFillOffsetMs, log);
                StagedGroupPlacement.NormalizeStagedDelays(
                    [.. participants.Cast<IAlignmentChannel>()], alignment, log,
                    maxDelayMs, request.RearFillOffsetMs, fillCarriers);
            }

            // "Before" snapshots exist only for the report's before/after forecast.
            IReadOnlyList<AlignmentSnapshot> beforeSnapshots =
                reprocessor.Reprocess(participants.ToDictionary(
                    channel => (IAlignmentChannel)channel,
                    channel => new AlignmentOverride(
                        channel.Settings.DelayMs, channel.Settings.InvertPolarity)));
            if (request.AdjustGains)
            {
                gains = ComputeGainBalance(
                    participants.Select(channel => (
                        (IAlignmentChannel)channel,
                        channel.Settings,
                        channel.Pair.Mono,
                        RightSide: false,
                        (IAlignmentChannel?)null)),
                    reprocessor, alignment, levelDifferenceDb: 0, log);
            }

            IReadOnlyList<AlignmentSnapshot> afterSnapshots =
                reprocessor.Reprocess(alignment);
            sumLoss = ForecastSumLoss(
                participants.Select(channel =>
                    ((IAlignmentChannel)channel, channel.Settings)).ToList(),
                ToIrMap(beforeSnapshots), ToIrMap(afterSnapshots),
                AdjustedGainMap(gains), windowMinHz, windowMaxHz);
        });

        List<AutoDelayChannelOutcome> outcomes = BuildOutcomes(
            participants.Select(channel => (
                (IAlignmentChannel)channel,
                Runtime: channel,
                channel.Settings,
                channel.Name)),
            alignment, decisions, gains);
        string report = VirtualCrossoverAutoDelayReport.Format(
            outcomes, stereo: false, request, sumLoss);
        // Written at the proposal stage so a discarded run can still be shared.
        WriteLog(log.ToString());
        return new AutoDelayRunResult(outcomes, Stereo: false, request, report, log);
    }

    // Right channels' gains are judged against their left peers, tilted by the entered L-R level difference.
    private static async Task<AutoDelayRunResult> RunStereoAsync(
        VirtualCrossoverSession session,
        List<VirtualCrossoverSideAlignmentChannel> leftSide,
        List<VirtualCrossoverSideAlignmentChannel> rightSide,
        List<VirtualCrossoverSideAlignmentChannel> union,
        VirtualCrossoverSideAlignmentChannel bridgeLeft,
        VirtualCrossoverSideAlignmentChannel bridgeRight,
        double bridgeBandLowHz,
        double bridgeBandHighHz,
        AutoDelayRunRequest request)
    {
        int processorSampleRateHz = session.ProcessorSampleRateHz;
        double maxDelayMs = session.ProcessorMaxDelayMs;
        var log = new StringBuilder();
        log.AppendLine($"Auto delay (stereo) {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        log.AppendLine(
            $"Layout {(request.RightHandDrive ? "RHD" : "LHD")}: scene offset " +
            $"{request.SceneOffsetMs:0.00} ms, the " +
            $"{(request.RightHandDrive ? "left" : "right")} side leads; " +
            $"bridge {(request.RightHandDrive ? bridgeRight : bridgeLeft).Name} -> " +
            $"{(request.RightHandDrive ? bridgeLeft : bridgeRight).Name} " +
            $"in {bridgeBandLowHz:0}-{bridgeBandHighHz:0} Hz");
        if (request.RightHandDrive)
        {
            log.AppendLine(
                "RHD run: the engine trace below reads in mirrored " +
                "coordinates (ref = the right side, far = the left).");
        }
        log.AppendLine("Previous delay / polarity settings ignored for this run.");

        var engineAlignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        var decisions = new Dictionary<IAlignmentChannel, AlignmentDecision>();
        IReadOnlyList<GainBalanceResult>? gains = null;
        AutoDelaySumLossForecast? leftSumLoss = null;
        AutoDelaySumLossForecast? rightSumLoss = null;
        await Task.Run(() =>
        {
            // The reprocessor covers the union (later stages render from it); only the chain is walked.
            List<VirtualCrossoverSideAlignmentChannel> chainLeft =
                [.. leftSide.Where(InFrontChain)];
            List<VirtualCrossoverSideAlignmentChannel> chainRight =
                [.. rightSide.Where(InFrontChain)];
            List<VirtualCrossoverSideAlignmentChannel> later =
                [.. union.Where(side => !InFrontChain(side))];
            AlignmentReprocessor reprocessor = ComputeStereoAlignment(
                chainLeft, chainRight, union, bridgeLeft, bridgeRight,
                bridgeBandLowHz, bridgeBandHighHz, request.SceneOffsetMs,
                request.RightHandDrive, processorSampleRateHz, maxDelayMs,
                engineAlignment, decisions, log);
            if (later.Count > 0)
            {
                // Engine roles, so RHD places groups against the reference the walk settled.
                IReadOnlyCollection<IAlignmentChannel> fillCarriers =
                    StagedGroupPlacement.PlaceStereo(
                        request.RightHandDrive ? chainRight : chainLeft,
                        request.RightHandDrive ? chainLeft : chainRight,
                        later,
                        reprocessor,
                        engineAlignment,
                        decisions,
                        request.SceneOffsetMs,
                        request.RearFillOffsetMs,
                        request.RightHandDrive,
                        log);
                StagedGroupPlacement.NormalizeStagedDelays(
                    [.. union.Cast<IAlignmentChannel>()], engineAlignment, log,
                    maxDelayMs, request.RearFillOffsetMs, fillCarriers);
            }

            // "Before" snapshots exist only for the report's before/after forecast.
            IReadOnlyList<AlignmentSnapshot> beforeSnapshots =
                reprocessor.Reprocess(union.ToDictionary(
                    side => (IAlignmentChannel)side,
                    side => new AlignmentOverride(
                        side.Settings.DelayMs, side.Settings.InvertPolarity)));
            if (request.AdjustGains)
            {
                gains = ComputeGainBalance(
                    union.Select(side => (
                        (IAlignmentChannel)side,
                        side.Settings,
                        side.Runtime.Pair.Mono,
                        side.RightSide,
                        (IAlignmentChannel?)(side.RightSide
                            ? leftSide.FirstOrDefault(left =>
                                left.Runtime == side.Runtime && !left.RightSide)
                            : null))),
                    reprocessor, engineAlignment, request.LevelDifferenceDb, log);
            }

            IReadOnlyList<AlignmentSnapshot> afterSnapshots =
                reprocessor.Reprocess(engineAlignment);
            Dictionary<IAlignmentChannel, Complex[]> beforeIrs = ToIrMap(beforeSnapshots);
            Dictionary<IAlignmentChannel, Complex[]> afterIrs = ToIrMap(afterSnapshots);
            Dictionary<IAlignmentChannel, GainBalanceResult>? adjustedGains =
                AdjustedGainMap(gains);
            (double leftMinHz, double leftMaxHz) =
                VirtualCrossoverJunctions.GetCrossoverWindow(
                    leftSide.Select(side => side.Settings));
            leftSumLoss = ForecastSumLoss(
                leftSide.Select(side => ((IAlignmentChannel)side, side.Settings)).ToList(),
                beforeIrs, afterIrs, adjustedGains, leftMinHz, leftMaxHz);
            (double rightMinHz, double rightMaxHz) =
                VirtualCrossoverJunctions.GetCrossoverWindow(
                    rightSide.Select(side => side.Settings));
            rightSumLoss = ForecastSumLoss(
                rightSide.Select(side => ((IAlignmentChannel)side, side.Settings)).ToList(),
                beforeIrs, afterIrs, adjustedGains, rightMinHz, rightMaxHz);
        });

        // Report grouped per block (A L, A R, B L...).
        List<AutoDelayChannelOutcome> outcomes = BuildOutcomes(
            union
                .OrderBy(side => session.Channels.IndexOf(side.Runtime))
                .ThenBy(side => side.RightSide)
                .Select(side => (
                    (IAlignmentChannel)side,
                    side.Runtime,
                    side.Settings,
                    side.Name)),
            engineAlignment, decisions, gains);
        string report = VirtualCrossoverAutoDelayReport.Format(
            outcomes, stereo: true, request, leftSumLoss, rightSumLoss);
        WriteLog(log.ToString());
        return new AutoDelayRunResult(outcomes, Stereo: true, request, report, log);
    }

    // Bridges to AutoAlignmentEngine on a background thread; the reprocessor's run-scoped FFT cache re-FFTs only changed
    // channels and is returned for the gain stage.
    /// <param name="walkSet">Channels forming junctions when narrower than the participants (later stages still render from all); null walks all.</param>
    private static AlignmentReprocessor ComputeAlignment(
        List<VirtualCrossoverChannel> participants,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        Dictionary<IAlignmentChannel, AlignmentDecision> decisions,
        StringBuilder log,
        int processorSampleRateHz,
        double maxDelayMs,
        IReadOnlyList<VirtualCrossoverChannel>? walkSet = null)
    {
        // Adjacent drivers by band centre form the junctions (same as VirtualCrossoverJunctions).
        List<VirtualCrossoverChannel> ordered = OrderByBand(participants);

        // Shared direct-sound crop: identical delays at a fraction of the FFT cost.
        var reprocessor = new AlignmentReprocessor(
            CleanCrosstalkHeads(
                ordered.Select(channel => new AlignmentReprocessInput(
                    channel,
                    channel.TransferImpulseResponse!,
                    channel.SampleRate,
                    processorSampleRateHz,
                    channel.Settings.ToChain(channel.Pair.Zone))).ToList(),
                log));

        IReadOnlyList<AlignmentSnapshot> initial = reprocessor.Reprocess(
            new Dictionary<IAlignmentChannel, AlignmentOverride>());
        var snapshots = ordered
            .Select((channel, i) => (channel, snapshot: initial[i]))
            .ToDictionary(item => item.channel, item => item.snapshot);
        // A narrowed walk keeps the whole set's band order.
        List<AlignmentSnapshot> walked =
            [.. ordered.Where(channel => walkSet?.Contains(channel) ?? true)
                .Select(channel => snapshots[channel])];
        AutoAlignmentEngine.Compute(
            walked,
            AdjacentJunctions(walked),
            reprocessor.Reprocess,
            alignment,
            log,
            decisions,
            maxDelayMs: maxDelayMs);
        return reprocessor;
    }

    internal static AlignmentReprocessor ComputeStereoAlignment(
        List<VirtualCrossoverSideAlignmentChannel> leftSide,
        List<VirtualCrossoverSideAlignmentChannel> rightSide,
        List<VirtualCrossoverSideAlignmentChannel> union,
        VirtualCrossoverSideAlignmentChannel bridgeLeft,
        VirtualCrossoverSideAlignmentChannel bridgeRight,
        double bridgeBandLowHz,
        double bridgeBandHighHz,
        double sceneOffsetMs,
        bool rightHandDrive,
        int processorSampleRateHz,
        double maxDelayMs,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        Dictionary<IAlignmentChannel, AlignmentDecision> decisions,
        StringBuilder log)
    {
        // Crop to the direct sound: final delays identical to a full-length run (validated), FFTs much shorter.
        var reprocessor = new AlignmentReprocessor(
            CleanCrosstalkHeads(
                union.Select(side => new AlignmentReprocessInput(
                    side,
                    side.State.TransferImpulseResponse!,
                    side.State.SampleRate,
                    processorSampleRateHz,
                    side.Settings.ToChain(side.Runtime.Pair.Zone))).ToList(),
                log));

        IReadOnlyList<AlignmentSnapshot> initialSnapshots = reprocessor.Reprocess(
            new Dictionary<IAlignmentChannel, AlignmentOverride>());
        Dictionary<VirtualCrossoverSideAlignmentChannel, AlignmentSnapshot> initial = union
            .Select((side, i) => (side, snapshot: initialSnapshots[i]))
            .ToDictionary(item => item.side, item => item.snapshot);
        List<AlignmentSnapshot> ByBand(List<VirtualCrossoverSideAlignmentChannel> sides) =>
            [.. OrderByBand(sides).Select(side => initial[side])];

        // The plan is in reference/far ROLES; RHD hands it mirrored, so a positive offset makes the left side lead.
        // Pair links aim the descent's prior at the cross-side-consistent delay.
        var pairLinks = new List<StereoPairLink>();
        foreach (VirtualCrossoverSideAlignmentChannel right in rightSide.Where(side => side.RightSide))
        {
            VirtualCrossoverSideAlignmentChannel? left = leftSide.FirstOrDefault(
                side => side.Runtime == right.Runtime && !side.RightSide);
            if (left == null)
            {
                continue;
            }

            (double leftLow, double leftHigh) =
                VirtualCrossoverJunctions.GetChannelBand(left.Settings);
            (double rightLow, double rightHigh) =
                VirtualCrossoverJunctions.GetChannelBand(right.Settings);
            double lowHz = Math.Max(leftLow, rightLow);
            double highHz = Math.Min(leftHigh, rightHigh);
            // Must satisfy the arrival analysis' admission rule, or the link could never measure.
            if (highHz >= lowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio)
            {
                pairLinks.Add(rightHandDrive
                    ? new StereoPairLink(right, left, lowHz, highHz)
                    : new StereoPairLink(left, right, lowHz, highHz));
            }
        }

        List<AlignmentSnapshot> referenceByBand =
            ByBand(rightHandDrive ? rightSide : leftSide);
        List<AlignmentSnapshot> farByBand =
            ByBand(rightHandDrive ? leftSide : rightSide);
        AutoAlignmentEngine.ComputeStereo(
            new StereoAlignmentPlan(
                referenceByBand,
                AdjacentJunctions(referenceByBand),
                farByBand,
                AdjacentJunctions(farByBand),
                // Monos from the walked left side, NOT the union: a staged union keeps a mono centre/rear that trips the engine's
                // "mono must be in the left walk" guard.
                leftSide.Where(side => side.Runtime.Pair.Mono)
                    .Cast<IAlignmentChannel>()
                    .ToList(),
                rightHandDrive ? bridgeRight : bridgeLeft,
                rightHandDrive ? bridgeLeft : bridgeRight,
                bridgeBandLowHz,
                bridgeBandHighHz,
                sceneOffsetMs,
                pairLinks),
            reprocessor.Reprocess,
            alignment,
            log,
            decisions,
            maxDelayMs);
        return reprocessor;
    }

    internal static List<T> OrderByBand<T>(IEnumerable<T> channels)
        where T : IVirtualCrossoverAlignmentChannel =>
        [.. channels.OrderBy(channel => VirtualCrossoverJunctions.BandCenterHz(channel.Settings))];

    /// <summary>The junctions between neighbours of a band-ordered walk, each searched in its crossover's overlap band.</summary>
    internal static List<AlignmentJunction> AdjacentJunctions(IReadOnlyList<AlignmentSnapshot> byBand)
    {
        var junctions = new List<AlignmentJunction>();
        for (int i = 0; i < byBand.Count - 1; i++)
        {
            double pairHz = VirtualCrossoverJunctions.GetPairCrossoverHz(
                SettingsOf(byBand[i]), SettingsOf(byBand[i + 1]));
            (double lowHz, double highHz) = VirtualCrossoverJunctions.OverlapBand(pairHz);
            junctions.Add(new AlignmentJunction(byBand[i], byBand[i + 1], pairHz, lowHz, highHz));
        }

        return junctions;

        static VirtualCrossoverChannelSettings SettingsOf(AlignmentSnapshot snapshot) =>
            ((IVirtualCrossoverAlignmentChannel)snapshot.Channel).Settings;
    }

    // Records may carry a playback-crosstalk click at a fixed early sample (biases GCC-PHAT, wrong branch on gentle
    // slopes): head-gate convicted records and log them.
    private static List<AlignmentReprocessInput> CleanCrosstalkHeads(
        List<AlignmentReprocessInput> inputs,
        StringBuilder log) =>
        inputs.Select(input =>
        {
            double[] real = Array.ConvertAll(
                input.MeasuredImpulseResponse, sample => sample.Real);
            CrosstalkHeadGate? gate = TransferIrDiagnostics.DetectCrosstalkHead(
                real, input.SampleRate);
            if (gate is not { } convicted)
            {
                return input;
            }

            log.AppendLine(
                $"{input.Channel.Name}: playback-crosstalk click at " +
                $"{convicted.BurstTimeMs:0.00} ms ({convicted.BurstPeakDbReMax:0.0} dB " +
                "re max) removed from the record's head before the search");
            return input with
            {
                MeasuredImpulseResponse = TransferIrDiagnostics.CleanCrosstalkHead(
                    input.MeasuredImpulseResponse, input.SampleRate, convicted)
            };
        }).ToList();

    // Levels from the FINAL snapshots; the engine subtracts the baked-in gain, so the proposal is absolute.
    private static IReadOnlyList<GainBalanceResult> ComputeGainBalance(
        IEnumerable<(IAlignmentChannel Channel, VirtualCrossoverChannelSettings Settings,
            bool Mono, bool RightSide, IAlignmentChannel? LeftPeer)> channels,
        AlignmentReprocessor reprocessor,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        double levelDifferenceDb,
        StringBuilder log)
    {
        IReadOnlyList<AlignmentSnapshot> snapshots = reprocessor.Reprocess(alignment);
        Dictionary<IAlignmentChannel, AlignmentSnapshot> byChannel =
            snapshots.ToDictionary(snapshot => snapshot.Channel);
        List<GainBalanceInput> inputs = channels
            .Select(item =>
            {
                (double lowHz, double highHz) =
                    VirtualCrossoverJunctions.GetChannelBand(item.Settings);
                return new GainBalanceInput(
                    item.Channel,
                    byChannel[item.Channel].ImpulseResponse,
                    item.Channel.SampleRate,
                    item.Settings.GainDb,
                    lowHz,
                    highHz,
                    item.Settings.EffectiveCrossover.Kind != CrossoverKind.Off,
                    item.Mono,
                    item.RightSide,
                    item.LeftPeer);
            })
            .ToList();
        return GainBalanceEngine.Compute(inputs, levelDifferenceDb, log);
    }

    private static Dictionary<IAlignmentChannel, Complex[]> ToIrMap(
        IReadOnlyList<AlignmentSnapshot> snapshots) =>
        snapshots.ToDictionary(
            snapshot => snapshot.Channel,
            snapshot => snapshot.ImpulseResponse);

    private static Dictionary<IAlignmentChannel, GainBalanceResult>? AdjustedGainMap(
        IReadOnlyList<GainBalanceResult>? gains) =>
        gains?.Where(result => result.Adjusted)
            .ToDictionary(result => result.Channel);

    // Proposed gains enter as spectrum scales (the reprocessor's chains carry current gains). Null below two channels.
    private static AutoDelaySumLossForecast? ForecastSumLoss(
        IReadOnlyList<(IAlignmentChannel Channel, VirtualCrossoverChannelSettings Settings)> sideChannels,
        IReadOnlyDictionary<IAlignmentChannel, Complex[]> beforeIrs,
        IReadOnlyDictionary<IAlignmentChannel, Complex[]> afterIrs,
        IReadOnlyDictionary<IAlignmentChannel, GainBalanceResult>? adjustedGains,
        double windowMinHz,
        double windowMaxHz)
    {
        if (sideChannels.Count < 2)
        {
            return null;
        }

        int sampleRate = sideChannels[0].Channel.SampleRate;
        double? before = VirtualCrossoverAnalysis.PredictedAverageSumLossDb(
            sideChannels.Select(item => beforeIrs[item.Channel]).ToList(),
            sampleRate, windowMinHz, windowMaxHz);
        List<double> scales = sideChannels
            .Select(item =>
                adjustedGains != null &&
                adjustedGains.TryGetValue(item.Channel, out GainBalanceResult? gain)
                    ? Math.Pow(10.0, (gain.ProposedGainDb - item.Settings.GainDb) / 20.0)
                    : 1.0)
            .ToList();
        double? after = VirtualCrossoverAnalysis.PredictedAverageSumLossDb(
            sideChannels.Select(item => afterIrs[item.Channel]).ToList(),
            sampleRate, windowMinHz, windowMaxHz, scales);
        return before.HasValue && after.HasValue
            ? new AutoDelaySumLossForecast(before.Value, after.Value)
            : null;
    }

    private static List<AutoDelayChannelOutcome> BuildOutcomes(
        IEnumerable<(IAlignmentChannel Channel, VirtualCrossoverChannel Runtime,
            VirtualCrossoverChannelSettings Settings, string Name)> channels,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        Dictionary<IAlignmentChannel, AlignmentDecision> decisions,
        IReadOnlyList<GainBalanceResult>? gains)
    {
        Dictionary<IAlignmentChannel, GainBalanceResult>? gainByChannel =
            gains?.ToDictionary(result => result.Channel);
        var outcomes = new List<AutoDelayChannelOutcome>();
        foreach ((IAlignmentChannel channel, VirtualCrossoverChannel runtime,
            VirtualCrossoverChannelSettings settings, string name) in channels)
        {
            AlignmentOverride over = alignment.GetValueOrDefault(channel);
            AlignmentDecision? decision = decisions.GetValueOrDefault(channel);
            GainBalanceResult? gain = gainByChannel?.GetValueOrDefault(channel);
            bool gainAdjusted = gain?.Adjusted == true;
            outcomes.Add(new AutoDelayChannelOutcome(
                runtime,
                settings,
                name,
                settings.DelayMs,
                settings.InvertPolarity,
                settings.GainDb,
                Math.Round(over.DelayMs, 2),
                over.InvertPolarity,
                gainAdjusted ? gain!.ProposedGainDb : settings.GainDb,
                gainAdjusted,
                decision?.Kind,
                decision?.Confidence,
                decision?.Detail ?? string.Empty,
                gain?.Confidence,
                gain?.Detail ?? string.Empty));
        }

        return outcomes;
    }

    /// <summary>Writes a confirmed run into the channel settings and the project and appends the result to its log; the
    /// caller refreshes what shows them. Synchronous, so Apply fully lands or fails before anything is half-written.</summary>
    public static void Commit(AutoDelayRunResult result, VirtualCrossoverProjectFile project)
    {
        // Stored so the next run on this car starts from the fill it settled on.
        project.RearFillOffsetMs = result.Request.RearFillOffsetMs;
        foreach (AutoDelayChannelOutcome outcome in result.Outcomes)
        {
            outcome.Settings.DelayMs = outcome.AfterDelayMs;
            outcome.Settings.InvertPolarity = outcome.AfterInvert;
            if (outcome.GainAdjusted)
            {
                outcome.Settings.GainDb = outcome.AfterGainDb;
            }

            result.Log.AppendLine(
                $"Result {outcome.Name}: " +
                $"delay {outcome.AfterDelayMs:0.00} ms, " +
                $"invert {(outcome.AfterInvert ? "yes" : "no")}" +
                (outcome.GainAdjusted
                    ? $", gain {outcome.AfterGainDb:0.0} dB"
                    : ""));
        }

        if (result.Stereo)
        {
            // Persisted only on Apply, layout-signed so older builds read and resave the file.
            project.SetStereoScene(
                result.Request.SceneOffsetMs, result.Request.RightHandDrive);
            project.StereoLevelDifferenceDb = result.Request.LevelDifferenceDb;
        }
    }

    // Best effort: a failed write must never break the alignment.
    internal static void WriteLog(string text)
    {
        try
        {
            AtomicFile.WriteAllText(
                ApplicationDataPaths.Current.VirtualDspAlignmentLogFile, text);
        }
        catch
        {
        }
    }
}
