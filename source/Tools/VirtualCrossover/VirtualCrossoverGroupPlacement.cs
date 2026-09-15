using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Where one listening group sits against a settled reference. See docs/tech/junction-phase-and-group-placement.md#group-placement.</summary>
/// <param name="CoArrivalDelayMs">Delay to ADD to the group; negative when the group is already later (normalization then delays everything else).</param>
/// <param name="EdgePinned">Extremum sat on the refinement window edge: the placement is the coarse arrival and <see cref="Inverted"/> means "not measured".</param>
internal sealed record GroupPlacement(
    double CoArrivalDelayMs,
    bool Inverted,
    double Coefficient,
    bool EdgePinned = false);

/// <summary>What a centre is read against on each side over one shared band: a block's two side instances, or each side's own content summed. No plan means refuse.</summary>
internal sealed record CentreReferencePlan<T>(
    IReadOnlyList<T> Near,
    IReadOnlyList<T> Far,
    double LowHz,
    double HighHz,
    bool Peers);

/// <summary>A <see cref="Plan"/> or the <see cref="Refusal"/> sentence explaining why there is none; exactly one is set.</summary>
internal sealed record CentreReferenceChoice<T>(
    CentreReferencePlan<T>? Plan,
    string? Refusal);

/// <summary>Centre corroboration as four separate tests, so the report names the one that failed.</summary>
internal readonly record struct CentreCorroboration(
    bool PolarityAgrees,
    bool WithinSceneOffset,
    bool BothInterior,
    bool StrongEnough)
{
    public bool Confident =>
        PolarityAgrees && WithinSceneOffset && BothInterior && StrongEnough;

    /// <summary>Every failing reason, not just one: several can fail at once.</summary>
    public string Describe()
    {
        if (Confident)
        {
            return "the two sides corroborate each other";
        }

        var reasons = new List<string>();
        if (!PolarityAgrees)
        {
            reasons.Add("the two sides read OPPOSITE polarities");
        }
        if (!WithinSceneOffset)
        {
            reasons.Add("the two sides DISAGREE by more than the scene offset");
        }
        if (!BothInterior)
        {
            reasons.Add(
                "a reading was pinned to its refinement edge, so it is the " +
                "arrival rather than a phase measurement");
        }
        if (!StrongEnough)
        {
            reasons.Add("the correlation is too weak to trust");
        }

        return string.Join("; ", reasons) + " - this placement is a best guess";
    }
}

/// <summary>Places a whole group (rear fill, centre) against a settled reference (preferably one front-chain driver): coarse band-limited arrival, then whitened correlation refined around it.</summary>
/// <remarks>Not a junction search; one-way staging. See docs/tech/junction-phase-and-group-placement.md#group-placement.</remarks>
internal static class VirtualCrossoverGroupPlacement
{
    /// <summary>Cap on the refinement window: below ~125 Hz the arrival estimate's own error, not lobe spacing, binds.</summary>
    public const double MaximumRefineRangeMs = 2.0;

    /// <summary>Quarter period at the band centre, capped: the one correlation extremum the arrival anchor sits in.</summary>
    /// <remarks>See docs/tech/junction-phase-and-group-placement.md#refinement-window.</remarks>
    public static double RefineRangeMs(double lowHz, double highHz) =>
        Math.Min(MaximumRefineRangeMs, 250.0 / Math.Sqrt(lowHz * highHz));

    /// <summary>Below this |r| the placement is reported but not trusted.</summary>
    public const double MinimumTrustedCoefficient = 0.25;

    /// <summary>1-4 kHz: the band a centre is judged in, because a centre is there for the voice. See docs/tech/junction-phase-and-group-placement.md#reference-choice.</summary>
    public const double VoiceBandLowHz = 1_000.0;

    public const double VoiceBandHighHz = 4_000.0;

    /// <summary>The ONE settled channel a group is timed against: most voice-band overlap, else lowest overlap; ties break lower.
    /// Null when nothing overlaps widely enough (caller falls back to the stage sum). See docs/tech/junction-phase-and-group-placement.md#reference-choice.</summary>
    public static (T Channel, double LowHz, double HighHz)? ChooseReference<T>(
        IEnumerable<T> candidates,
        Func<T, (double LowHz, double HighHz)> bandOf,
        double groupLowHz,
        double groupHighHz)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(bandOf);
        var overlaps = new List<(T Channel, double LowHz, double HighHz)>();
        foreach (T candidate in candidates)
        {
            (double candidateLow, double candidateHigh) = bandOf(candidate);
            double lowHz = Math.Max(candidateLow, groupLowHz);
            double highHz = Math.Min(candidateHigh, groupHighHz);
            if (IsWideEnough(lowHz, highHz))
            {
                overlaps.Add((candidate, lowHz, highHz));
            }
        }

        if (overlaps.Count == 0)
        {
            return null;
        }

        return overlaps
            .OrderByDescending(item => VoiceBandOctaves(item.LowHz, item.HighHz))
            .ThenBy(item => item.LowHz * item.HighHz)
            .First();
    }

    /// <summary>Near and far references for a centre over one shared band, or the refusal sentence.</summary>
    /// <remarks>Prefers one block's left and right instance; otherwise each side's own content (shared blocks removed), else refuses. See docs/tech/junction-phase-and-group-placement.md#centre-references-and-witness.</remarks>
    public static CentreReferenceChoice<T> ChooseCentreReferences<T>(
        IReadOnlyCollection<T> near,
        IReadOnlyCollection<T> far,
        Func<T, (double LowHz, double HighHz)> bandOf,
        Func<T, T, bool> arePeers,
        double groupLowHz,
        double groupHighHz)
    {
        ArgumentNullException.ThrowIfNull(near);
        ArgumentNullException.ThrowIfNull(far);
        ArgumentNullException.ThrowIfNull(bandOf);
        ArgumentNullException.ThrowIfNull(arePeers);
        (T Channel, double LowHz, double HighHz)? nearPick =
            ChooseReference(near, bandOf, groupLowHz, groupHighHz);
        (T Channel, double LowHz, double HighHz)? farPick =
            ChooseReference(far, bandOf, groupLowHz, groupHighHz);
        if (nearPick is { } nearChoice && farPick is { } farChoice &&
            arePeers(nearChoice.Channel, farChoice.Channel))
        {
            // One band for both readings: a midpoint across two bands is not a midpoint.
            double peerLowHz = Math.Max(nearChoice.LowHz, farChoice.LowHz);
            double peerHighHz = Math.Min(nearChoice.HighHz, farChoice.HighHz);
            if (IsWideEnough(peerLowHz, peerHighHz))
            {
                return new CentreReferenceChoice<T>(
                    new CentreReferencePlan<T>(
                        [nearChoice.Channel], [farChoice.Channel],
                        peerLowHz, peerHighHz, Peers: true),
                    null);
            }
        }

        // Each side's OWN content: a mono block in both sums would make the witness compare two copies.
        IReadOnlyList<T> nearOwn =
            OwnContent(near, far, bandOf, groupLowHz, groupHighHz);
        IReadOnlyList<T> farOwn =
            OwnContent(far, near, bandOf, groupLowHz, groupHighHz);
        // No own content on a side means no second view of the centre: refuse, keep the old delay.
        if (nearOwn.Count == 0 || farOwn.Count == 0)
        {
            return new CentreReferenceChoice<T>(
                null,
                $"the two sides play no content of their own in {groupLowHz:0}-" +
                $"{groupHighHz:0} Hz - whatever either of them plays there, they " +
                "play from the same response");
        }

        // Band from the references as they ARE (shared response removed), not from the original sides.
        IReadOnlyList<(double LowHz, double HighHz)> nearCoverage =
            Coverage(nearOwn, bandOf);
        IReadOnlyList<(double LowHz, double HighHz)> farCoverage =
            Coverage(farOwn, bandOf);
        if (WidestShared(nearCoverage, farCoverage, groupLowHz, groupHighHz)
            is not (double lowHz, double highHz))
        {
            return new CentreReferenceChoice<T>(
                null,
                "the two sides share no band of their own wide enough to time in: " +
                "the reference side's own content covers " +
                $"{Describe(nearCoverage)} and the far side's {Describe(farCoverage)}");
        }

        return new CentreReferenceChoice<T>(
            new CentreReferencePlan<T>(
                [.. nearOwn.Where(item => PlaysIn(bandOf(item), lowHz, highHz))],
                [.. farOwn.Where(item => PlaysIn(bandOf(item), lowHz, highHz))],
                lowHz,
                highHz,
                Peers: false),
            null);
    }

    /// <summary>Merged, sorted frequency intervals the channels cover; not the lowest-to-highest span (a band in a hole would time leakage).</summary>
    internal static IReadOnlyList<(double LowHz, double HighHz)> Coverage<T>(
        IReadOnlyList<T> members,
        Func<T, (double LowHz, double HighHz)> bandOf)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(bandOf);
        var merged = new List<(double LowHz, double HighHz)>();
        foreach ((double lowHz, double highHz) in
            members.Select(bandOf).OrderBy(band => band.LowHz))
        {
            if (merged.Count > 0 && lowHz <= merged[^1].HighHz)
            {
                merged[^1] = (merged[^1].LowHz, Math.Max(merged[^1].HighHz, highHz));
            }
            else
            {
                merged.Add((lowHz, highHz));
            }
        }

        return merged;
    }

    internal static (double LowHz, double HighHz)? WidestShared(
        IReadOnlyList<(double LowHz, double HighHz)> near,
        IReadOnlyList<(double LowHz, double HighHz)> far,
        double groupLowHz,
        double groupHighHz)
    {
        ArgumentNullException.ThrowIfNull(near);
        ArgumentNullException.ThrowIfNull(far);
        (double LowHz, double HighHz)? best = null;
        foreach ((double nearLowHz, double nearHighHz) in near)
        {
            foreach ((double farLowHz, double farHighHz) in far)
            {
                double lowHz = Math.Max(Math.Max(nearLowHz, farLowHz), groupLowHz);
                double highHz = Math.Min(Math.Min(nearHighHz, farHighHz), groupHighHz);
                if (IsWideEnough(lowHz, highHz) &&
                    (best is not { } widest ||
                        highHz / lowHz > widest.HighHz / widest.LowHz))
                {
                    best = (lowHz, highHz);
                }
            }
        }

        return best;
    }

    private static string Describe(IReadOnlyList<(double LowHz, double HighHz)> coverage) =>
        string.Join(
            " and ",
            coverage.Select(band => $"{band.LowHz:0}-{band.HighHz:0} Hz"));

    private static double VoiceBandOctaves(double lowHz, double highHz)
    {
        double low = Math.Max(lowHz, VoiceBandLowHz);
        double high = Math.Min(highHz, VoiceBandHighHz);
        return high > low ? Math.Log2(high / low) : 0.0;
    }

    // Same admission rule as the arrival analysis, so a chosen band is never one Place rejects.
    private static bool IsWideEnough(double lowHz, double highHz) =>
        lowHz > 0 && highHz > lowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio;

    /// <summary>Null when either side holds no reliable arrival in the shared band.</summary>
    public static GroupPlacement? Place(
        Complex[] referenceIr,
        Complex[] groupIr,
        int sampleRate,
        double lowHz,
        double highHz)
    {
        ArgumentNullException.ThrowIfNull(referenceIr);
        ArgumentNullException.ThrowIfNull(groupIr);
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }
        if (!(lowHz > 0) || !(highHz > lowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio))
        {
            return null;
        }

        TimeAlignmentAnalysisResult reference =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                referenceIr, sampleRate, lowHz, highHz);
        TimeAlignmentAnalysisResult group =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                groupIr, sampleRate, lowHz, highHz);
        if (!Reliable(reference) || !Reliable(group))
        {
            return null;
        }

        // Correlation convention is the delay to ADD to the second response, hence the negated centre.
        double lateMs = group.FirstArrivalDelayMilliseconds -
            reference.FirstArrivalDelayMilliseconds;
        double centerHz = Math.Sqrt(lowHz * highHz);
        double octaves = Math.Log2(highHz / lowHz);
        CorrelationAlignmentResult correlation =
            VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                referenceIr,
                groupIr,
                sampleRate,
                centerHz,
                octaves,
                RefineRangeMs(lowHz, highHz),
                centerLagMs: -lateMs,
                phaseTransform: true);
        CorrelationDelayCandidate best = correlation.BestByMagnitude;
        // Pinned: boundary, not an extremum. Keep the arrival, claim no polarity; still report |r|.
        return best.EdgePinned
            ? new GroupPlacement(-lateMs, false, Math.Abs(best.Coefficient), EdgePinned: true)
            : new GroupPlacement(
                best.DelayMs,
                best.InvertPolarity,
                Math.Abs(best.Coefficient));
    }

    /// <summary>Centre delay = midpoint of the two side readings; polarity only when both agree.</summary>
    /// <remarks>The readings must differ by about the scene offset. See docs/tech/junction-phase-and-group-placement.md#centre-references-and-witness.</remarks>
    public static (double DelayMs, bool Inverted, CentreCorroboration Corroboration) Midpoint(
        GroupPlacement reference,
        GroupPlacement far,
        double sceneOffsetMs,
        double toleranceMs)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(far);
        bool agree = reference.Inverted == far.Inverted;
        var corroboration = new CentreCorroboration(
            PolarityAgrees: agree,
            WithinSceneOffset:
                Math.Abs(Math.Abs(reference.CoArrivalDelayMs - far.CoArrivalDelayMs) -
                    Math.Abs(sceneOffsetMs)) <= toleranceMs,
            BothInterior: !reference.EdgePinned && !far.EdgePinned,
            StrongEnough:
                Math.Min(reference.Coefficient, far.Coefficient) >=
                    MinimumTrustedCoefficient);
        return (
            (reference.CoArrivalDelayMs + far.CoArrivalDelayMs) / 2.0,
            agree && reference.Inverted,
            corroboration);
    }

    /// <summary>What one side plays in the band that the other does not; empty means the sides cannot witness each other.</summary>
    public static IReadOnlyList<T> OwnContent<T>(
        IReadOnlyCollection<T> side,
        IReadOnlyCollection<T> other,
        Func<T, (double LowHz, double HighHz)> bandOf,
        double lowHz,
        double highHz)
    {
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(bandOf);
        return [.. side.Where(item =>
            !other.Contains(item) && PlaysIn(bandOf(item), lowHz, highHz))];
    }

    // Any overlap counts; whether it is timeable is decided by Place's arrival gate on the signal.
    private static bool PlaysIn(
        (double LowHz, double HighHz) band, double lowHz, double highHz) =>
        band.HighHz > lowHz && band.LowHz < highHz;

    private static bool Reliable(TimeAlignmentAnalysisResult arrival) =>
        arrival.IsValid &&
        arrival.SignalToNoiseDecibels >= AutoAlignmentEngine.MinimumArrivalSnrDb;
}
