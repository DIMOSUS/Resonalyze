using System.Numerics;
using System.Text;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Auto delay's later stages: after the front chain is walked, each rear fill and centre group is settled on its own
/// junctions and then placed against the chain as one rigid body. See docs/tech/virtual-dsp-panel.md#staged-auto-delay.</summary>
internal sealed class StagedGroupPlacement
{
    // Allowed disagreement beyond the scene offset: envelope vs phase-extremum on two paths, narrower than a lobe.
    private const double CentreWitnessToleranceMs = 0.35;

    // Beyond this offset the groups no longer sum audibly, so polarity describes the measurement, not the listener.
    private const double HaasPolarityIrrelevantMs = 5.0;

    // Groups playing one band from different places never correlate like a crossover, so the bar is below a junction's.
    private const double StrongPlacementCoefficient = 0.6;

    private readonly AlignmentReprocessor reprocessor;
    private readonly Dictionary<IAlignmentChannel, AlignmentOverride> alignment;
    private readonly Dictionary<IAlignmentChannel, AlignmentDecision> decisions;
    private readonly StringBuilder log;
    private readonly List<IAlignmentChannel> fillCarriers = [];

    // Rebound by the inner walks, so later groups read settled responses.
    private Dictionary<IAlignmentChannel, AlignmentSnapshot> byChannel = [];

    private StagedGroupPlacement(
        AlignmentReprocessor reprocessor,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        Dictionary<IAlignmentChannel, AlignmentDecision> decisions,
        StringBuilder log)
    {
        this.reprocessor = reprocessor;
        this.alignment = alignment;
        this.decisions = decisions;
        this.log = log;
        Refresh();
    }

    /// <summary>One side: each later stage gets ONE delay for all its members.</summary>
    /// <returns>The channels carrying the rear-fill offset, for <see cref="NormalizeStagedDelays"/>.</returns>
    public static IReadOnlyCollection<IAlignmentChannel> PlaceSingleSide(
        IReadOnlyList<VirtualCrossoverChannel> chain,
        IReadOnlyList<VirtualCrossoverChannel> later,
        AlignmentReprocessor reprocessor,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        Dictionary<IAlignmentChannel, AlignmentDecision> decisions,
        double rearFillOffsetMs,
        StringBuilder log)
    {
        var placement = new StagedGroupPlacement(reprocessor, alignment, decisions, log);
        var front = new FrontReference<VirtualCrossoverChannel>(
            chain, placement.SumOf(chain), BandOf(chain), "the front stage");
        int sampleRate = chain[0].SampleRate;
        foreach (VirtualCrossoverAlignmentStage stage in
            VirtualCrossoverAlignmentStages.InOrder.Where(
                item => item != VirtualCrossoverAlignmentStage.FrontChain))
        {
            List<VirtualCrossoverChannel> members = [.. later.Where(channel =>
                VirtualCrossoverAlignmentStages.StageOf(channel.Pair.Zone) == stage)];
            if (members.Count == 0)
            {
                continue;
            }

            // A rear fill is wanted behind the front (precedence effect); a centre takes no offset.
            bool rear = stage == VirtualCrossoverAlignmentStage.Rear;
            placement.PlaceGroup(
                members,
                front,
                sampleRate,
                rear ? rearFillOffsetMs : 0.0,
                label: stage.ToString(),
                groupNoun: stage.ToString(),
                carriesFill: rear);
        }

        return placement.fillCarriers;
    }

    /// <summary>Stereo: the rear fill placed PER SIDE against its own side's front stage, the centre midway between both.
    /// The chains arrive in engine ROLES: on right-hand drive the reference is the right side.</summary>
    /// <returns>The channels carrying the rear-fill offset, for <see cref="NormalizeStagedDelays"/>.</returns>
    public static IReadOnlyCollection<IAlignmentChannel> PlaceStereo(
        IReadOnlyList<VirtualCrossoverSideAlignmentChannel> chainReference,
        IReadOnlyList<VirtualCrossoverSideAlignmentChannel> chainFar,
        IReadOnlyList<VirtualCrossoverSideAlignmentChannel> later,
        AlignmentReprocessor reprocessor,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        Dictionary<IAlignmentChannel, AlignmentDecision> decisions,
        double sceneOffsetMs,
        double rearFillOffsetMs,
        bool rightHandDrive,
        StringBuilder log)
    {
        var placement = new StagedGroupPlacement(reprocessor, alignment, decisions, log);
        // Each side's own band: the two sides may carry different crossover corners.
        var reference = new FrontReference<VirtualCrossoverSideAlignmentChannel>(
            chainReference, placement.SumOf(chainReference), BandOf(chainReference),
            "this side's front stage");
        var far = new FrontReference<VirtualCrossoverSideAlignmentChannel>(
            chainFar, placement.SumOf(chainFar), BandOf(chainFar), "this side's front stage");
        int sampleRate = chainReference[0].SampleRate;

        // Grouped by cabin side so a two-way rear settles its own junction first.
        foreach (IGrouping<bool, VirtualCrossoverSideAlignmentChannel> sideGroup in
            later
                .Where(item =>
                    VirtualCrossoverAlignmentStages.StageOf(item.Runtime.Pair.Zone) ==
                        VirtualCrossoverAlignmentStage.Rear)
                .GroupBy(item => item.RightSide))
        {
            List<VirtualCrossoverSideAlignmentChannel> members = [.. sideGroup];
            placement.PlaceGroup(
                members,
                IsFarSide(sideGroup.Key, rightHandDrive) ? far : reference,
                sampleRate,
                rearFillOffsetMs,
                label: "rear " + string.Join("+", members.Select(item => item.Name)),
                groupNoun: "rear",
                carriesFill: true);
        }

        List<VirtualCrossoverSideAlignmentChannel> centre = [.. later.Where(item =>
            VirtualCrossoverAlignmentStages.StageOf(item.Runtime.Pair.Zone) ==
                VirtualCrossoverAlignmentStage.Center)];
        if (centre.Count > 0)
        {
            placement.PlaceCentre(centre, chainReference, chainFar, sampleRate, sceneOffsetMs);
        }

        return placement.fillCarriers;
    }

    /// <summary>The driver's side is the reference, so the far side is the other.</summary>
    internal static bool IsFarSide(bool rightSide, bool rightHandDrive) =>
        rightSide != rightHandDrive;

    private sealed record FrontReference<T>(
        IReadOnlyList<T> Chain,
        Complex[] Sum,
        (double LowHz, double HighHz) Band,
        string Name)
        where T : IVirtualCrossoverAlignmentChannel;

    // One front driver, not the stage summed: the sum's arrival belongs to its earliest player (a tweeter).
    private void PlaceGroup<T>(
        List<T> members,
        FrontReference<T> front,
        int sampleRate,
        double offsetMs,
        string label,
        string groupNoun,
        bool carriesFill)
        where T : IVirtualCrossoverAlignmentChannel
    {
        Dictionary<IAlignmentChannel, AlignmentOverride> inner = Settle(members);
        (double groupLow, double groupHigh) = BandOf(members);
        (T Channel, double LowHz, double HighHz)? pick =
            VirtualCrossoverGroupPlacement.ChooseReference(
                front.Chain,
                item => VirtualCrossoverJunctions.GetChannelBand(item.Settings),
                groupLow,
                groupHigh);
        Complex[] against = pick is { } chosen
            ? byChannel[chosen.Channel].ImpulseResponse
            : front.Sum;
        string againstName = pick is { } named ? named.Channel.Name : front.Name;
        double lowHz = pick?.LowHz ?? Math.Max(front.Band.LowHz, groupLow);
        double highHz = pick?.HighHz ?? Math.Min(front.Band.HighHz, groupHigh);
        GroupPlacement? placement = VirtualCrossoverGroupPlacement.Place(
            against, SumOf(members), sampleRate, lowHz, highHz);
        if (placement == null)
        {
            log.AppendLine(
                $"  {label}: not placed - no reliable arrival in {lowHz:0}-{highHz:0} Hz " +
                $"against {againstName}. Its current delay stands.");
            Lock(
                members,
                $"not placed: no reliable arrival in {lowHz:0}-{highHz:0} Hz " +
                $"against {againstName}, so the current delay stands");
            return;
        }

        if (carriesFill)
        {
            fillCarriers.AddRange(members.Cast<IAlignmentChannel>());
        }

        double delayMs = placement.CoArrivalDelayMs + offsetMs;
        bool invert = offsetMs < HaasPolarityIrrelevantMs && placement.Inverted;
        bool corroborated = !placement.EdgePinned;
        log.AppendLine(
            $"  {label}: {delayMs:+0.00;-0.00;0.00} ms (co-arrival " +
            $"{placement.CoArrivalDelayMs:+0.00;-0.00;0.00}" +
            (offsetMs != 0 ? $" plus {offsetMs:0.##} ms fill" : string.Empty) +
            $"), against {againstName} in {lowHz:0}-{highHz:0} Hz, " +
            $"r {placement.Coefficient:0.00}" +
            (invert ? ", inverted" : string.Empty) +
            (placement.EdgePinned
                ? ", pinned to the refinement edge (the arrival stands, " +
                    "no polarity claimed)"
                : string.Empty) +
            (Trusted(placement.Coefficient, corroborated) ? string.Empty : " - LOW CONFIDENCE"));
        Compose(
            members,
            inner,
            delayMs,
            invert,
            PlacementDecision(
                placement.Coefficient,
                corroborated,
                $"placed as a {groupNoun} group against {againstName} in " +
                $"{lowHz:0}-{highHz:0} Hz, r {placement.Coefficient:0.00}" +
                (offsetMs != 0 ? $", held back {offsetMs:0.##} ms" : string.Empty)));
    }

    // Read against one reference per side (peer drivers, else each side's own content) and placed at the midpoint; the
    // readings should differ by the scene offset, so a disagreement is reported, not averaged.
    private void PlaceCentre(
        List<VirtualCrossoverSideAlignmentChannel> members,
        IReadOnlyList<VirtualCrossoverSideAlignmentChannel> chainReference,
        IReadOnlyList<VirtualCrossoverSideAlignmentChannel> chainFar,
        int sampleRate,
        double sceneOffsetMs)
    {
        // A two-way centre settles its own junction first, then is placed as one.
        Dictionary<IAlignmentChannel, AlignmentOverride> inner = Settle(members);
        string name = string.Join("+", members.Select(item => item.Name));
        // Both readings over the SAME band: intersection of both references, narrowed to the centre.
        (double centreLow, double centreHigh) = BandOf(members);
        // See VirtualCrossoverGroupPlacement.ChooseCentreReferences (mirrors matching the centre to the voice-band driver).
        CentreReferenceChoice<VirtualCrossoverSideAlignmentChannel> choice =
            VirtualCrossoverGroupPlacement.ChooseCentreReferences(
                chainReference,
                chainFar,
                item => VirtualCrossoverJunctions.GetChannelBand(item.Settings),
                (near, far) => near.Runtime == far.Runtime && near != far,
                centreLow,
                centreHigh);
        if (choice.Plan is not { } plan)
        {
            // No plan is a refusal: a midpoint between readings that share no content is not a placement.
            log.AppendLine(
                $"  centre {name}: not placed - {choice.Refusal}. " +
                "Its current delay stands.");
            Lock(
                members,
                $"not placed: {choice.Refusal}, so there is no midpoint " +
                "to place the centre between and the current delay stands");
            return;
        }

        double lowHz = plan.LowHz;
        double highHz = plan.HighHz;
        string Describe(IReadOnlyList<VirtualCrossoverSideAlignmentChannel> side) =>
            (plan.Peers ? string.Empty : "the own content ") +
            string.Join("+", side.Select(item => item.Name));
        string nearName = Describe(plan.Near);
        string farName = Describe(plan.Far);
        Complex[] centreIr = SumOf(members);
        GroupPlacement? againstReference = VirtualCrossoverGroupPlacement.Place(
            SumOf(plan.Near), centreIr, sampleRate, lowHz, highHz);
        GroupPlacement? againstFar = VirtualCrossoverGroupPlacement.Place(
            SumOf(plan.Far), centreIr, sampleRate, lowHz, highHz);
        if (againstReference == null || againstFar == null)
        {
            log.AppendLine(
                $"  centre {name}: not placed - no reliable arrival " +
                $"in {lowHz:0}-{highHz:0} Hz against " +
                (againstReference == null ? nearName : farName) +
                ". Its current delay stands.");
            Lock(
                members,
                $"not placed: no reliable arrival in {lowHz:0}-{highHz:0} Hz " +
                $"against {nearName} and {farName}, so the current delay stands");
            return;
        }

        (double delayMs, bool inverted, CentreCorroboration corroboration) =
            VirtualCrossoverGroupPlacement.Midpoint(
                againstReference,
                againstFar,
                sceneOffsetMs,
                CentreWitnessToleranceMs);
        log.AppendLine(
            $"  centre {name}: {delayMs:+0.00;-0.00;0.00} ms - midway " +
            $"between {againstReference.CoArrivalDelayMs:+0.00;-0.00;0.00} " +
            $"(vs {nearName}, r {againstReference.Coefficient:0.00}) and " +
            $"{againstFar.CoArrivalDelayMs:+0.00;-0.00;0.00} " +
            $"(vs {farName}, r {againstFar.Coefficient:0.00}) in " +
            $"{lowHz:0}-{highHz:0} Hz" +
            (inverted ? ", inverted" : string.Empty) +
            $" - {corroboration.Describe()}" +
            (corroboration.Confident ? string.Empty : " - LOW CONFIDENCE"));
        Compose(
            members,
            inner,
            delayMs,
            inverted,
            PlacementDecision(
                Math.Min(againstReference.Coefficient, againstFar.Coefficient),
                corroboration.Confident,
                $"placed midway between {nearName} and {farName} in " +
                $"{lowHz:0}-{highHz:0} Hz - {corroboration.Describe()}"));
    }

    private Dictionary<IAlignmentChannel, AlignmentOverride> Settle<T>(IReadOnlyList<T> members)
        where T : IVirtualCrossoverAlignmentChannel
    {
        Dictionary<IAlignmentChannel, AlignmentOverride> inner =
            SettleWithinGroup([.. members.Cast<IVirtualCrossoverAlignmentChannel>()], byChannel, reprocessor, log);
        if (inner.Count > 0)
        {
            ApplyInnerSettlement(members.Cast<IAlignmentChannel>(), inner, alignment);
            Refresh();
        }

        return inner;
    }

    // Written explicitly: an absent override means zero to the reprocessor.
    private void Lock<T>(IEnumerable<T> members, string reason)
        where T : IVirtualCrossoverAlignmentChannel
    {
        foreach (T member in members)
        {
            alignment[member] = new AlignmentOverride(
                member.Settings.DelayMs, member.Settings.InvertPolarity);
            decisions[member] = new AlignmentDecision(AlignmentDecisionKind.Locked, null, reason);
        }
    }

    // The group moves as one body on top of its own settled junctions.
    private void Compose<T>(
        IEnumerable<T> members,
        IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> inner,
        double delayMs,
        bool invert,
        AlignmentDecision decision)
        where T : IAlignmentChannel
    {
        foreach (T member in members)
        {
            AlignmentOverride own = inner.GetValueOrDefault(member);
            alignment[member] = new AlignmentOverride(
                own.DelayMs + delayMs, own.InvertPolarity ^ invert);
            decisions[member] = decision;
        }
    }

    private void Refresh() =>
        byChannel = reprocessor.Reprocess(alignment).ToDictionary(snapshot => snapshot.Channel);

    private Complex[] SumOf<T>(IEnumerable<T> group)
        where T : IAlignmentChannel =>
        VirtualCrossoverAnalysis.SumImpulseResponses(
            [.. group.Select(member => byChannel[member].ImpulseResponse)]);

    private static (double LowHz, double HighHz) BandOf<T>(IEnumerable<T> members)
        where T : IVirtualCrossoverAlignmentChannel
    {
        double low = double.MaxValue;
        double high = double.MinValue;
        foreach (T member in members)
        {
            (double memberLow, double memberHigh) =
                VirtualCrossoverJunctions.GetChannelBand(member.Settings);
            low = Math.Min(low, memberLow);
            high = Math.Max(high, memberHigh);
        }

        return (low, high);
    }

    private static bool Trusted(double coefficient, bool corroborated) =>
        corroborated && coefficient >= VirtualCrossoverGroupPlacement.MinimumTrustedCoefficient;

    // A placement the run does not trust must arrive in the report marked.
    private static AlignmentDecision PlacementDecision(
        double coefficient,
        bool corroborated,
        string detail)
    {
        AlignmentConfidence confidence =
            !Trusted(coefficient, corroborated)
                ? AlignmentConfidence.Low
                : coefficient >= StrongPlacementCoefficient
                    ? AlignmentConfidence.High
                    : AlignmentConfidence.Medium;
        return new AlignmentDecision(AlignmentDecisionKind.Search, confidence, detail);
    }

    /// <summary>Walks a later group's own junctions first, so it is placed as one settled body.</summary>
    /// <returns>The engine's SPARSE map (its reference has no entry); compose via <see cref="ApplyInnerSettlement"/>.</returns>
    internal static Dictionary<IAlignmentChannel, AlignmentOverride> SettleWithinGroup(
        IReadOnlyList<IVirtualCrossoverAlignmentChannel> members,
        IReadOnlyDictionary<IAlignmentChannel, AlignmentSnapshot> snapshots,
        AlignmentReprocessor reprocessor,
        StringBuilder log)
    {
        var inner = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        if (members.Count < 2)
        {
            return inner;
        }

        List<AlignmentSnapshot> byBand = [.. VirtualCrossoverAutoDelay.OrderByBand(members)
            .Select(member => snapshots[member])];
        List<AlignmentJunction> junctions = VirtualCrossoverAutoDelay.AdjacentJunctions(byBand);
        if (junctions.Count == 0)
        {
            return inner;
        }

        log.AppendLine(
            $"  settling {byBand.Count} drivers within the group " +
            $"({junctions.Count} junction(s)) before placing it:");
        AutoAlignmentEngine.Compute(
            byBand,
            junctions,
            reprocessor.Reprocess,
            inner,
            log);
        return inner;
    }

    /// <summary>Never index <c>inner[member]</c>: the engine's map is sparse and omits its reference channel.</summary>
    internal static void ApplyInnerSettlement(
        IEnumerable<IAlignmentChannel> members,
        IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> inner,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment)
    {
        foreach (IAlignmentChannel member in members)
        {
            alignment[member] = inner.GetValueOrDefault(member);
        }
    }

    // Slides every participant (absent = zero; the map omits the engine's reference) until the earliest is at zero.
    // See docs/tech/virtual-dsp-panel.md#delay-normalization.
    internal static void NormalizeStagedDelays(
        IReadOnlyList<IAlignmentChannel> scope,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        StringBuilder log,
        double maxDelayMs = AutoAlignmentEngine.DefaultMaxDelayMs,
        double rearFillOffsetMs = 0,
        IReadOnlyCollection<IAlignmentChannel>? rearFillCarriers = null)
    {
        if (scope.Count == 0)
        {
            return;
        }

        // Raw placements before the shift: the fill scan folds min(0, minimum) in itself.
        Dictionary<IAlignmentChannel, double> raw = scope.Distinct().ToDictionary(
            channel => channel,
            channel => alignment.GetValueOrDefault(channel).DelayMs);

        double minimum = raw.Values.Min();
        if (minimum < 0)
        {
            log.AppendLine(
                $"  normalization: every channel shifted +{-minimum:0.00} ms so the " +
                "earliest sits at zero.");
            foreach (IAlignmentChannel channel in raw.Keys)
            {
                AlignmentOverride over = alignment.GetValueOrDefault(channel);
                alignment[channel] = over with { DelayMs = over.DelayMs - minimum };
            }
        }

        IAlignmentChannel widest = raw.Keys.MaxBy(
            channel => alignment.GetValueOrDefault(channel).DelayMs)!;
        double widestDelayMs = alignment.GetValueOrDefault(widest).DelayMs;
        if (widestDelayMs <= maxDelayMs + 0.005)
        {
            return;
        }

        string message =
            "The staged alignment does not fit the DSP delay range: " +
            $"{widest.Name} needs {widestDelayMs:0.00} ms with the earliest " +
            $"channel at 0, but the limit is {maxDelayMs:0.##} ms.";
        message += LargestFittingRearFill(
                raw, rearFillCarriers, rearFillOffsetMs, maxDelayMs)
            is double fittingMs
            ? $" The {rearFillOffsetMs:0.##} ms rear fill is what pushes it past " +
                $"the range: up to {fittingMs:0.##} ms of fill fits — lower " +
                "Rear fill in the dialog and rerun."
            : " The spread between the earliest and latest channels is wider " +
                "than the DSP can realize.";
        throw new InvalidOperationException(message);
    }

    // Walks down on the 0.01 ms grid: the dialable span is not monotone in the fill. Null when no fill is in play or a zero fill does not fit either.
    private static double? LargestFittingRearFill(
        IReadOnlyDictionary<IAlignmentChannel, double> raw,
        IReadOnlyCollection<IAlignmentChannel>? carriers,
        double requestedFillMs,
        double maxDelayMs)
    {
        if (carriers == null || carriers.Count == 0 || requestedFillMs <= 0)
        {
            return null;
        }

        for (double fillMs = Math.Floor(requestedFillMs * 100) / 100;
            fillMs >= 0;
            fillMs = Math.Round(fillMs - 0.01, 2))
        {
            double minMs = double.MaxValue;
            double maxMs = double.MinValue;
            foreach ((IAlignmentChannel channel, double delayMs) in raw)
            {
                double trialMs = carriers.Contains(channel)
                    ? delayMs - requestedFillMs + fillMs
                    : delayMs;
                minMs = Math.Min(minMs, trialMs);
                maxMs = Math.Max(maxMs, trialMs);
            }

            if (maxMs - Math.Min(0, minMs) <= maxDelayMs + 0.005)
            {
                return fillMs;
            }
        }

        return null;
    }
}
