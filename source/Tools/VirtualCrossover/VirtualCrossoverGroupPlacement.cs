using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Where one listening group sits against a settled reference: the delay that
/// would make the two arrive together, whether the group reads inverted, and
/// how much the answer can be trusted.
/// </summary>
/// <param name="CoArrivalDelayMs">
/// The delay to ADD to the group so it arrives with the reference. Negative
/// when the group is already the later of the two — the group normalization
/// pass is what turns such an answer into something a processor can dial, by
/// delaying everything else instead.
/// </param>
/// <param name="Inverted">
/// True when the group's phase-whitened correlation against the reference peaks
/// NEGATIVE, which is a measurement of relative polarity rather than a guess.
/// </param>
/// <param name="Coefficient">
/// The extremum's height, |r| in 0..1. The confidence the caller reports.
/// </param>
/// <param name="EdgePinned">
/// True when the extremum sat on the refinement window's boundary, so the
/// correlation never found an interior one. The placement is then the coarse
/// ARRIVAL itself and <see cref="Inverted"/> is false — not "not inverted" but
/// "not measured": a polarity read off a window the search was clamped against
/// is a fact about the clamp. Reporting the boundary lag instead would put the
/// answer up to a whole refinement window from the arrival it claims to be
/// standing on (2 ms at a subwoofer band, where the cap governs) and could flip
/// a channel on it. Not an error — the arrival is the safer of the two readings
/// and this is how the placement degrades to it — but a weaker reading than an
/// interior extremum, and the caller says so rather than presenting the two
/// alike. The window is now sized by the band
/// (<see cref="VirtualCrossoverGroupPlacement.RefineRangeMs"/>), which makes
/// this common where the flat 2 ms made it rare.
/// </param>
internal sealed record GroupPlacement(
    double CoArrivalDelayMs,
    bool Inverted,
    double Coefficient,
    bool EdgePinned = false);

/// <summary>
/// What a centre is read against, on each side, over one shared band: either one
/// block's two side instances (<see cref="Peers"/>), or each side's own content
/// summed. Both lists are non-empty by construction — there is no third shape,
/// and no plan at all when neither is available.
/// </summary>
/// <remarks>
/// A plan rather than a pair of impulse responses because the CHOICE is the part
/// that has to be right, and the choice is what a caller inside a WinForms panel
/// cannot be tested on. Its absence is the refusal: a caller with no plan has no
/// midpoint to place, and must say so rather than reach for something else.
/// </remarks>
internal sealed record CentreReferencePlan<T>(
    IReadOnlyList<T> Near,
    IReadOnlyList<T> Far,
    double LowHz,
    double HighHz,
    bool Peers);

/// <summary>
/// The outcome of choosing a centre's references: a <see cref="Plan"/>, or the
/// <see cref="Refusal"/> that says why there is none. Exactly one is set.
/// </summary>
/// <remarks>
/// The refusal is a sentence rather than a flag because there is more than one
/// way to have no plan and they call for different things from the tuner — two
/// sides that share everything they play here is an installation fact, two sides
/// whose own content lies in different parts of the spectrum is a crossover
/// fact. A caller that owns neither number cannot tell them apart, so the
/// chooser, which owns both, writes the sentence.
/// </remarks>
internal sealed record CentreReferenceChoice<T>(
    CentreReferencePlan<T>? Plan,
    string? Refusal);

/// <summary>
/// Why a centre placement is, or is not, corroborated. Four independent tests,
/// kept apart because the report has to name the one that failed: a run that
/// says "the two sides disagree by more than the scene offset" when what
/// actually happened was a weak correlation, or a reading pinned to its window,
/// sends the tuner to look at the wrong thing.
/// </summary>
internal readonly record struct CentreCorroboration(
    bool PolarityAgrees,
    bool WithinSceneOffset,
    bool BothInterior,
    bool StrongEnough)
{
    public bool Confident =>
        PolarityAgrees && WithinSceneOffset && BothInterior && StrongEnough;

    /// <summary>
    /// The clause the log line and the report's note both end with: the
    /// corroboration, or EVERY reason it is missing — two can fail at once, and
    /// picking one to show would be the guess this type exists to remove.
    /// </summary>
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

/// <summary>
/// Places a whole group (a rear fill, a centre) against a reference that is
/// already settled: ONE front-chain driver after the chain walk — see
/// <see cref="VirtualCrossoverGroupPlacement.ChooseReference"/> for which, and
/// why not the whole stage summed.
/// </summary>
/// <remarks>
/// Deliberately not a junction search. A rear fill shares no crossover with the
/// front stage and a centre shares none with anything, so there is no handover
/// to optimise: what these groups need is one number each, and one number is
/// what a correlation against a settled reference gives. That is also what makes
/// the staging one-way — a placement computed from a fixed reference cannot move
/// the thing it was computed from.
/// <para>
/// Two steps, the same shape the alignment engine uses at a junction: a coarse
/// band-limited ARRIVAL difference says roughly where the answer is, then the
/// phase-whitened correlation is searched in a short window centred there. The
/// coarse step matters because a rear fill can sit ten or twenty milliseconds
/// from the front stage — a correlation searched around zero would find a lobe
/// rather than the answer, and one searched across the whole range would have
/// many lobes to choose between. The arrival is the ANCHOR in the strong sense:
/// the refinement window is sized so it can only reach the extremum the anchor
/// already sits in (<see cref="RefineRangeMs"/>).
/// </para>
/// </remarks>
internal static class VirtualCrossoverGroupPlacement
{
    /// <summary>
    /// The widest the refinement window is ever opened, whatever the band. Below
    /// ~125 Hz a quarter period exceeds it, and there the binding constraint
    /// stops being lobe spacing and becomes the arrival estimate's own error:
    /// nothing is gained by opening a window wider than the estimate can be
    /// wrong by, and the value is the one every placement was made at before the
    /// width was sized by the band at all.
    /// </summary>
    public const double MaximumRefineRangeMs = 2.0;

    /// <summary>
    /// How far around the coarse arrival estimate the whitened correlation is
    /// refined: a QUARTER PERIOD at the band's centre, capped at
    /// <see cref="MaximumRefineRangeMs"/>. A correlation's extrema alternate
    /// every half period, so a quarter period each way is the one extremum the
    /// arrival anchor sits in — and nothing else.
    /// </summary>
    /// <remarks>
    /// This used to be the flat 2 ms, described as "narrow enough that the
    /// search cannot walk into the neighbouring lobe". That is true of a
    /// subwoofer junction and false of everything above ~125 Hz: on the
    /// reference car a centre is placed over 400 Hz–20 kHz, where 2 ms holds
    /// ELEVEN periods, and the four candidates inside it stood at |r| 0.20 to
    /// 0.29 — a spread that is noise, over 2.4 ms of delay. Which lobe won was a
    /// coin toss, and the centre landed half a millisecond off the midpoint its
    /// own arrivals agree on. Inside a quarter period the anchor, not the toss,
    /// decides the extremum, and the correlation still does what it is for:
    /// placing the answer to a fraction of a sample where an envelope arrival
    /// can only place it to a fraction of a period.
    /// <para>
    /// The centre's own witness is what settled the width. On the reference car
    /// the two side readings must differ by the scene offset; refined a quarter
    /// period they differ by 0.36 ms against a 0.20 ms offset and corroborate,
    /// refined a half period they differ by 0.67 ms and do not — the extra width
    /// buys nothing but the neighbouring extremum.
    /// </para>
    /// <para>
    /// Below 125 Hz the cap governs and this width is the old one exactly. That
    /// is the WIDTH only: the reference a group is read against changed at every
    /// frequency (see <see cref="ChooseReference"/>), so a low-frequency
    /// placement is not unchanged — only the window it refines in is.
    /// </para>
    /// </remarks>
    public static double RefineRangeMs(double lowHz, double highHz) =>
        Math.Min(MaximumRefineRangeMs, 250.0 / Math.Sqrt(lowHz * highHz));

    /// <summary>
    /// Below this |r| the placement is reported but not trusted: the groups play
    /// the same band from different places, so their correlation is never as
    /// clean as a crossover's, but a value this low means the band holds no
    /// common feature to time against at all.
    /// </summary>
    public const double MinimumTrustedCoefficient = 0.25;

    /// <summary>
    /// The band a placed group is judged in when there is a choice: 1–4 kHz,
    /// because a centre channel is there for the VOICE, and that is the band a
    /// voice is heard in — its upper formants and consonants, where the ear is
    /// most sensitive and where a misplaced centre is heard first.
    /// </summary>
    /// <remarks>
    /// This is the owner's criterion, and it is the criterion rather than a
    /// tie-break because "widest overlap" — the obvious rule — picks the WRONG
    /// driver on an ordinary front. A centre high-passed at 300 Hz beside a
    /// 3-way front of 60–500 / 500–3000 / 3000–20000 Hz overlaps the tweeter by
    /// 2.74 octaves and the midrange by 2.58, so widest-overlap hands the centre
    /// to a driver that starts at 3 kHz — above nearly everything a voice does.
    /// Both figures are part fiction anyway: a channel with only a high-pass is
    /// booked to 20 kHz whether or not it plays there, so the two "widest" bands
    /// being compared are the two unbounded ones.
    /// <para>
    /// The band is deliberately narrower than the voice's full range. What is
    /// wanted is not every frequency a voice contains but the one region where
    /// the centre and the front stage must agree for the voice to sit still, and
    /// below ~1 kHz a cabin's modes and boundary reflections dominate the early
    /// field enough that timing read there says more about the room than about
    /// the two sources.
    /// </para>
    /// </remarks>
    public const double VoiceBandLowHz = 1_000.0;

    /// <summary>The top of the voice band — see <see cref="VoiceBandLowHz"/>.</summary>
    public const double VoiceBandHighHz = 4_000.0;

    /// <summary>
    /// Which ONE settled channel a group is timed against, and over what band:
    /// the candidate whose overlap with the group covers most of the voice band
    /// (<see cref="VoiceBandLowHz"/>), and failing that — no candidate reaches it
    /// — the one whose overlap sits lowest.
    /// Null when nothing on offer overlaps the group widely enough to read an
    /// arrival in — the caller then falls back to the whole stage summed.
    /// </summary>
    /// <remarks>
    /// The front stage summed is the wrong reference, and it is wrong in the
    /// coarse leg before the correlation is reached. A band-limited arrival is
    /// the arrival of whatever plays EARLIEST inside the band being read, so a
    /// centre read against the front sum over the band they nominally share is
    /// timed against the TWEETER — which it does not overlap — rather than
    /// against the midrange it does. On the reference car those two arrive
    /// 0.2 ms apart. The correlation then inherits the error and adds its own:
    /// whitened across five octaves, most of which the centre does not play, it
    /// answers at |r| ≈ 0.27.
    /// <para>
    /// One driver, in the band the two genuinely share, is also what a tuner
    /// does by hand — mute the rest of the front, match the centre to what is
    /// left — and it is where this rule came from.
    /// </para>
    /// <para>
    /// Ties — including the all-zero tie of a group that never reaches the voice
    /// band at all — break toward the LOWER overlap. Its period is longer, so
    /// the envelope arrival, whose error is a roughly fixed number of
    /// milliseconds, lands a smaller fraction of a period from the extremum it
    /// has to select.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// The near and far references a CENTRE is read against, over the one band
    /// they share — or, when there is no such pair, the sentence saying why.
    /// </summary>
    /// <remarks>
    /// The centre's two readings are each other's witness — they should differ by
    /// the scene offset, because that is how far apart the sides are — and that
    /// only holds when they measure the SAME thing from the two sides. So the two
    /// picks must be one block's left and right instance. A near-side midrange
    /// witnessed by a far-side tweeter, which two sides carrying different
    /// corners can produce, is two different measurements averaged; a MONO block
    /// picked by both sides is one measurement counted twice, and it would
    /// satisfy the witness vacuously, at a difference of exactly zero. Both fall
    /// back to the summed stages rather than fabricate a corroboration.
    /// </remarks>
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
            // One band for both readings: a midpoint between two bands is not a
            // midpoint, and neither is a witness across them.
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

        // The fallback sums each side's OWN content, never the side entire. A
        // mono front block belongs to BOTH sides' lists, so summing them whole
        // would put one response into both references and leave the witness
        // comparing two copies of it — the very thing the peer test above
        // refuses, reintroduced by the thing it falls back to.
        IReadOnlyList<T> nearOwn =
            OwnContent(near, far, bandOf, groupLowHz, groupHighHz);
        IReadOnlyList<T> farOwn =
            OwnContent(far, near, bandOf, groupLowHz, groupHighHz);
        // No plan, and deliberately not a wider one. When a side plays nothing of
        // its own in the band there are not two views of the centre to average:
        // whatever is there belongs to both sides equally, so the "midpoint"
        // would be one reading reported twice. The caller refuses, and the centre
        // keeps the delay it had.
        if (nearOwn.Count == 0 || farOwn.Count == 0)
        {
            return new CentreReferenceChoice<T>(
                null,
                $"the two sides play no content of their own in {groupLowHz:0}-" +
                $"{groupHighHz:0} Hz - whatever either of them plays there, they " +
                "play from the same response");
        }

        // The band comes from what the references actually ARE, not from what
        // the sides were before the shared response was taken out of them. Those
        // are different questions and the wrong answer is catastrophically wide:
        // a near side left with a 60-200 Hz midbass and a far side left with a
        // 4-20 kHz tweeter would otherwise still share a nominal 20 Hz-20 kHz,
        // and two readings taken "in the same band" would have had no content in
        // common at all - while corroborating each other, because nothing
        // downstream asks what a band was computed from.
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

        // Only the members that play in the chosen band. The rest add nothing to
        // a band-limited read anyway, and leaving them in would put names in the
        // trace for drivers that contributed nothing to the number beside them.
        return new CentreReferenceChoice<T>(
            new CentreReferencePlan<T>(
                [.. nearOwn.Where(item => PlaysIn(bandOf(item), lowHz, highHz))],
                [.. farOwn.Where(item => PlaysIn(bandOf(item), lowHz, highHz))],
                lowHz,
                highHz,
                Peers: false),
            null);
    }

    /// <summary>
    /// The frequency intervals a set of channels covers between them, merged
    /// where they touch or overlap and sorted upward.
    /// </summary>
    /// <remarks>
    /// NOT the span from the lowest corner to the highest. A side left with a
    /// midbass and a tweeter and nothing between covers two intervals with a hole
    /// in the middle, and a band chosen from the span would sit in the hole: both
    /// sides would be "playing" a range one of them is two crossovers away from.
    /// What would then be measured is filter leakage against real content, which
    /// correlates about as well as anything else that quiet and can carry a
    /// confident midpoint out of the run. Adjacent intervals DO merge — a
    /// crossover is exactly two bands meeting at a corner, and a side covers
    /// straight through it.
    /// </remarks>
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

    /// <summary>
    /// The widest band, in octaves, that both coverages hold in one piece and the
    /// group plays in. Null when they share nothing worth timing in.
    /// </summary>
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

    // A coverage written out for the refusal: the intervals, not their span,
    // because the span is the thing that was wrong.
    private static string Describe(IReadOnlyList<(double LowHz, double HighHz)> coverage) =>
        string.Join(
            " and ",
            coverage.Select(band => $"{band.LowHz:0}-{band.HighHz:0} Hz"));

    // How much of the voice band an overlap covers, in octaves; zero when it
    // does not reach it.
    private static double VoiceBandOctaves(double lowHz, double highHz)
    {
        double low = Math.Max(lowHz, VoiceBandLowHz);
        double high = Math.Min(highHz, VoiceBandHighHz);
        return high > low ? Math.Log2(high / low) : 0.0;
    }

    // The admission rule of the arrival analysis, stated once. Place refuses a
    // band narrower than this, so a caller CHOOSING a band has to use the same
    // comparison — a hair-narrower one would hand Place a band it rejects and
    // report the group as unmeasurable while a usable reference went unused.
    private static bool IsWideEnough(double lowHz, double highHz) =>
        lowHz > 0 && highHz > lowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio;

    /// <summary>
    /// Places <paramref name="groupIr"/> against <paramref name="referenceIr"/>
    /// in the band the two share. Null when either side holds no reliable
    /// arrival there, which is the honest answer for a group that does not
    /// overlap the reference enough to be timed against it.
    /// </summary>
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

        // How much later the group arrives. The correlation's own convention is
        // the delay to ADD to the second response, so the window it searches is
        // centred on the negative of that.
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
        // Pinned: the search hit the window edge, so what came back is the
        // boundary, not an extremum. The arrival is what stands — literally, not
        // as a figure of speech — and no polarity is claimed from a clamped
        // search. The coefficient is still reported: it is a real correlation
        // height and the callers rank on it, they just may not trust it.
        return best.EdgePinned
            ? new GroupPlacement(-lateMs, false, Math.Abs(best.Coefficient), EdgePinned: true)
            : new GroupPlacement(
                best.DelayMs,
                best.InvertPolarity,
                Math.Abs(best.Coefficient));
    }

    /// <summary>
    /// The centre's placement from its two side readings: the midpoint. A centre
    /// plays a signal derived from L and R and sits between them, so the delay
    /// that puts it in the middle is the average of the two that would align it
    /// with each side alone.
    /// </summary>
    /// <remarks>
    /// The two readings are also each other's witness. They should differ by the
    /// scene offset — the same figure the stereo run applies and the metric panel
    /// verifies — because that is how far apart the sides themselves are. A
    /// larger disagreement means one of the readings landed on the wrong lobe,
    /// and the caller is told so rather than handed a confident midpoint between
    /// a right answer and a wrong one.
    /// <para>
    /// Polarity is only applied when both sides agree on it. A centre that reads
    /// inverted against one side and normal against the other is not a centre
    /// with a wiring fault; it is a measurement that has not settled, and
    /// flipping on half of it would be a coin toss dressed as a reading.
    /// </para>
    /// <para>
    /// A reading pinned to its window edge costs the confidence too. It is still
    /// used — it IS the arrival, which is the reading this whole step is anchored
    /// on — but it is not a corroborated phase measurement and must not arrive at
    /// the dialog looking like one.
    /// </para>
    /// <para>
    /// Whether the two readings are two MEASUREMENTS is not among these tests,
    /// because it is not left to a test: a run only reaches here with a
    /// <see cref="CentreReferencePlan{T}"/>, and a plan has no shape in which the
    /// two sides share a response. Every test that IS here is reported
    /// separately, because the note the tuner reads has to name the one that
    /// failed.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// What one side plays in the placement band that the other side does NOT:
    /// the members the fallback reference is summed from, and the answer to
    /// whether the two sides can witness each other at all (an empty list means
    /// they cannot).
    /// </summary>
    /// <remarks>
    /// The scene-offset witness assumes two independent views of the centre, and
    /// nothing else in the pipeline enforces that. A MONO front block is one
    /// object in both sides' lists, so when the caller falls back to the summed
    /// stages that response is inside BOTH sums — and if it dominates the band,
    /// the two "readings" are one measurement taken twice: they differ by about
    /// zero, and a witness asking whether they differ by about the scene offset
    /// passes on that whenever the offset is small. Refusing the pair in
    /// <see cref="ChooseCentreReferences"/> does nothing about it, because the
    /// fallback it sends the caller to is exactly where the shared response
    /// lives.
    /// <para>
    /// So the fallback references are built from THIS, not from the whole side:
    /// the shared response is removed rather than left in and argued about. An
    /// EMPTY answer is then a refusal, not a signal to widen — see
    /// <see cref="ChooseCentreReferences"/>. What a declared-band test could
    /// never settle is settled downstream instead: a side whose own channels are
    /// silent or noisy yields a silent or noisy reference, and
    /// <see cref="Place"/>'s arrival gate (validity and SNR) refuses THAT on the
    /// signal. A channel that is merely weak still reads, and shows up as the low
    /// correlation it is.
    /// </para>
    /// </remarks>
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

    // Whether a channel's band reaches into the placement band at all. A hair of
    // overlap counts: this asks whether the side has content of its own there,
    // not whether that content could be timed against on its own — that question
    // belongs to the arrival gate in Place, which reads the SIGNAL rather than
    // the declared corners.
    private static bool PlaysIn(
        (double LowHz, double HighHz) band, double lowHz, double highHz) =>
        band.HighHz > lowHz && band.LowHz < highHz;

    private static bool Reliable(TimeAlignmentAnalysisResult arrival) =>
        arrival.IsValid &&
        arrival.SignalToNoiseDecibels >= AutoAlignmentEngine.MinimumArrivalSnrDb;
}
