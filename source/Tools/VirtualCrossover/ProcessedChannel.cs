using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// A channel's processed response ready for the metric, the complex sum and the
/// plot: the applied-chain impulse response, its peak index, the rate it was
/// processed at, the channel's plot color and where the MEASURED content sits
/// inside the processed record (<see cref="ValidRange"/> — the coordinator
/// computes it with every render; front detections read it so a chain delay's
/// silent prefix cannot pose as arrival SNR). Shared by the redraw, the metric
/// read-out and the Auto delay search. The range defaults to unknown for
/// callers without one — the analyses then fall back to their
/// padding-signature heuristic.
/// <para>
/// <see cref="SampleRate"/> is CAPTURED with the response rather than read
/// back through <see cref="Channel"/>: the record is a snapshot while the
/// channel is live, and importing a session rebinds every channel's runtime
/// state on the UI thread while renders and metric rebuilds are still in
/// flight — a consumer that dereferenced the live channel mid-rebind read a
/// ZERO rate against a real response (the ArgumentOutOfRangeException crash
/// on opening a session over a loaded one). Everything derived from
/// <see cref="ImpulseResponse"/> must take the rate from this snapshot; the
/// live channel stays for its identity, settings and visibility flags.
/// </para>
/// </summary>
internal sealed record ProcessedChannel(
    VirtualCrossoverChannel Channel,
    Complex[] ImpulseResponse,
    int PeakIndex,
    int SampleRate,
    OxyColor Color,
    ValidSampleRange ValidRange = default,
    // Snapshotted from the SIDE this response came from, like the rate above: the
    // list can carry the opposite side's responses, and reading it back off the
    // channel would answer for whichever side happens to be active.
    MeasuredBand MeasuredBand = default,
    // The calibration this side's MEASUREMENT was read through, snapshotted for the
    // same reason. Null when its file named none, and null on every path that does
    // not care — the panel's own selection is what those get.
    CalibrationFile? MicrophoneCalibration = null);

/// <summary>
/// One gated magnitude curve at the two widths the tool needs, from a single gate
/// and FFT. <see cref="Display"/> carries the chosen smoothing and is what the plot
/// draws; <see cref="Unsmoothed"/> is what the summation loss is divided out of,
/// because smoothing the operands before the division inflates a steep crossover
/// skirt far more than the (flat) sum and invents a dip at every corner — see
/// <see cref="VirtualCrossoverAnalysis.SumLossCurve"/>. The two are the same
/// instance when no smoothing is selected.
/// </summary>
internal sealed record GatedMagnitude(AnalysisCurve Display, AnalysisCurve Unsmoothed)
{
    /// <summary>
    /// The same pair with every frequency no channel measured broken.
    /// </summary>
    /// <remarks>
    /// Both widths, because the unsmoothed half is what the summation loss divides:
    /// a loss measured against a window's leakage is not a loss.
    /// </remarks>
    public GatedMagnitude MeasuredBySomeChannel(
        IReadOnlyList<ProcessedChannel> channels) =>
        new(
            Display with
            {
                Points = ProcessedChannels.MeasuredBySomeChannel(Display.Points, channels)
            },
            ReferenceEquals(Display, Unsmoothed)
                ? Display with
                {
                    Points = ProcessedChannels.MeasuredBySomeChannel(Display.Points, channels)
                }
                : Unsmoothed with
                {
                    Points = ProcessedChannels.MeasuredBySomeChannel(
                        Unsmoothed.Points, channels)
                });
}

/// <summary>
/// One side of the car summed: the complex sum of its participating channels'
/// processed responses, the earliest arrival among them (the window anchor every
/// curve built from this sum must share), the project rate they were measured at,
/// and the channels themselves in the order they went in.
/// </summary>
/// <remarks>
/// <see cref="Channels"/> is what lets that sum be rebuilt from its parts rather
/// than only drawn: the hybrid sum is the channels' magnitudes plus the summation
/// loss, so the side that used to hand back one summed response could not take part
/// in it. The list carries the OPPOSITE side's responses, so anything reading a
/// channel's own state through <see cref="ProcessedChannel.Channel"/> must ask for
/// the side this sum was computed for rather than the channel's active one.
/// </remarks>
internal sealed record VirtualCrossoverSideSum(
    Complex[] ImpulseResponse,
    int AnchorIndex,
    int SampleRate,
    IReadOnlyList<ProcessedChannel> Channels)
{
    public int ChannelCount => Channels.Count;
}

/// <summary>
/// Adjacent channels along the spectrum with their shared junction: the pair
/// crossover frequency and the band (an octave to each side) where the two
/// drivers genuinely overlap. This band is where coarse arrivals are compared,
/// where the fine delay search correlates, and where the per-pair sum-loss metric
/// is read.
/// </summary>
internal sealed record AdjacentPair(
    ProcessedChannel Lower,
    ProcessedChannel Upper,
    double CrossoverHz,
    double BandLowHz,
    double BandHighHz);

/// <summary>
/// Ordering and junction helpers over a processed-channel set, shared by the
/// metric read-out and the Auto delay search so the two never disagree on which
/// drivers are adjacent or which band a junction spans.
/// </summary>
internal static class ProcessedChannels
{
    /// <summary>
    /// The outer edges of what a SUM of these channels measured.
    /// </summary>
    /// <remarks>
    /// A sum plays wherever any of its channels does. Taking one channel's band — or
    /// the narrowest — would blank a range a woofer measured perfectly well because a
    /// tweeter beside it was filtered, or swept, out of that range.
    /// <para>
    /// The HULL, not the union: two channels whose sweeps do not overlap — a woofer
    /// swept to 500 Hz beside a tweeter swept from 1 kHz — leave a hole between them
    /// that this interval cannot express. <see cref="MeasuredBySomeChannel"/> answers
    /// that per frequency, and is what a sum curve is masked by; this stays for the
    /// callers that only need the ends.
    /// </para>
    /// </remarks>
    public static MeasuredBand UnionOfMeasuredBands(
        IReadOnlyList<ProcessedChannel> channels)
    {
        if (channels.Count == 0)
        {
            return MeasuredBand.Everything;
        }

        double lowest = double.PositiveInfinity;
        double highest = 0.0;
        foreach (ProcessedChannel channel in channels)
        {
            lowest = Math.Min(lowest, channel.MeasuredBand.LowEdgeHz);
            highest = Math.Max(highest, channel.MeasuredBand.HighEdgeHz);
        }

        return new MeasuredBand(lowest, highest);
    }

    /// <summary>
    /// Breaks a summed curve at every frequency NO channel measured — including one
    /// that falls between two channels' bands rather than outside both.
    /// </summary>
    /// <remarks>
    /// The interval above cannot hold a hole, and a hole is exactly what disjoint
    /// sweeps leave: between them the summed impulse response is zero from every
    /// contributor at once, so the curve there is the analysis window and nothing
    /// else. Applied to the finished curve, like every other break.
    /// </remarks>
    public static IReadOnlyList<SignalPoint> MeasuredBySomeChannel(
        IReadOnlyList<SignalPoint> curve,
        IReadOnlyList<ProcessedChannel> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        return channels.Count == 0
            ? curve
            : MeasuredBand.MaskUnmeasured(
                curve,
                channels.Select(channel => channel.MeasuredBand).ToList());
    }

    // The frequency window the metric and Auto delay operate in: around the
    // corner frequencies the channels actually use (one octave to each side),
    // or a broad midband default when no crossover is configured yet.
    public static (double MinHz, double MaxHz) GetCrossoverWindow(
        IReadOnlyList<ProcessedChannel> processed) =>
        VirtualCrossoverJunctions.GetCrossoverWindow(
            processed.Select(item => item.Channel.Settings));

    /// <summary>
    /// Where one channel lets a shared window open: its estimated response
    /// START, as a sample index, falling back to its peak when the estimator
    /// refuses the record (see <see cref="TransferIrStartCache"/>, which
    /// memoizes the estimate per IR — the gate-placement guard reads the same
    /// figure for the same channels on the same redraw).
    /// <para>
    /// A peak would be the wrong figure to share: a crossover's group delay
    /// puts a filtered channel's peak milliseconds behind its own front — on
    /// the archived Passat right side the subwoofer peaks 5.6 ms after its
    /// band's arrival — so a window anchored on the earliest PEAK can still
    /// open after an earlier channel's front. The phase view's Auto placement
    /// has always used the start; this is the same rule for the magnitude
    /// window, and for the junction gate the Auto delay search places
    /// (VirtualCrossoverAnalysis.FindGateAnchor).
    /// </para>
    /// </summary>
    public static int StartAnchorIndex(
        Complex[] impulseResponse,
        int peakIndex,
        int sampleRate,
        ValidSampleRange validRange = default) =>
        TransferIrStartCache.ResolveStartIndex(
            impulseResponse, sampleRate, peakIndex, validRange);

    /// <summary>
    /// The shared window's anchor for a channel set: the earliest
    /// <see cref="StartAnchorIndex"/> among them, each read within its own
    /// <see cref="ProcessedChannel.ValidRange"/> — a chain delay's silent
    /// prefix must not certify a front here any more than in the junction
    /// gates.
    /// </summary>
    public static int SharedStartAnchorIndex(
        IReadOnlyList<ProcessedChannel> processed) =>
        processed.Min(item => StartAnchorIndex(
            item.ImpulseResponse, item.PeakIndex, item.SampleRate,
            item.ValidRange));

    public static List<ProcessedChannel> OrderByBand(IReadOnlyList<ProcessedChannel> processed) =>
        processed
            .OrderBy(item => VirtualCrossoverJunctions.BandCenterHz(item.Channel.Settings))
            .ToList();

    /// <summary>
    /// The junctions in a band-ordered set: each neighbouring pair that actually
    /// hands one band to the other, with the frequency it happens at and the
    /// octave to each side where the two genuinely sum.
    /// </summary>
    /// <remarks>
    /// Adjacency along the spectrum is necessary and not sufficient. Two channels
    /// can be neighbours in the ordering with a hole between them — a subwoofer
    /// stopping at 110 Hz beside a rear fill starting at 290 — and no filter
    /// hands anything across that gap. Reported as a junction it produced a
    /// summation loss and a phase recommendation for a crossover that is not in
    /// the car, which is the same defect grouped views exist to remove, one level
    /// down.
    /// <para>
    /// The test is the pair's own overlap band: both channels have to PLAY inside
    /// the octave-each-way window the junction would be measured over. That reuses
    /// the window the measurement itself uses rather than inventing a gap
    /// tolerance, and it keeps every real handover — drivers meeting at a shared
    /// corner both reach well into it, and so do ones deliberately crossed a
    /// little apart.
    /// </para>
    /// </remarks>
    public static List<AdjacentPair> GetAdjacentPairs(IReadOnlyList<ProcessedChannel> byBand)
    {
        var pairs = new List<AdjacentPair>();
        for (int i = 0; i < byBand.Count - 1; i++)
        {
            double pairHz = VirtualCrossoverJunctions.GetPairCrossoverHz(
                byBand[i].Channel.Settings, byBand[i + 1].Channel.Settings);
            (double bandLowHz, double bandHighHz) = VirtualCrossoverJunctions.OverlapBand(pairHz);
            if (!PlaysWithin(byBand[i], bandLowHz, bandHighHz) ||
                !PlaysWithin(byBand[i + 1], bandLowHz, bandHighHz))
            {
                continue;
            }

            pairs.Add(new AdjacentPair(
                byBand[i],
                byBand[i + 1],
                pairHz,
                bandLowHz,
                bandHighHz));
        }

        return pairs;
    }

    /// <summary>
    /// Whether the set holds a real handover at all. A chain with none has no
    /// summation loss to state — not a good one, none: the figure describes
    /// cancellation at a crossover, and there is no crossover here.
    /// </summary>
    public static bool HasJunction(IReadOnlyList<ProcessedChannel> channels) =>
        GetAdjacentPairs(OrderByBand(channels)).Count > 0;

    /// <summary>
    /// Whether the set is ONE unbroken crossover chain — every neighbour along
    /// the spectrum handing over to the next.
    /// </summary>
    /// <remarks>
    /// The distinction that <see cref="HasJunction"/> alone cannot make, and the
    /// reference car makes it in practice: its Rear + Sub view holds two
    /// subwoofers that genuinely cross (below 50 Hz into 50–110) and then a rear
    /// fill from 290 with a hole in front of it. A per-junction figure for the two
    /// subwoofers is real and worth reading. A TOTAL over the set is not: it
    /// averages the loss across a span where, for most of it, only one member
    /// plays — a single number claiming to summarise a chain that is not one.
    /// </remarks>
    public static bool IsContinuousChain(IReadOnlyList<ProcessedChannel> channels) =>
        channels.Count >= 2 &&
        GetAdjacentPairs(OrderByBand(channels)).Count == channels.Count - 1;

    /// <summary>
    /// The junctions a grouped view can speak about: those of the chain it SUMS,
    /// which is the set its loss curve and its per-junction read-out describe.
    /// Empty for a view spanning more than one listening group.
    /// </summary>
    /// <remarks>
    /// Band order over a whole installation is not a chain. A rear fill and a
    /// centre are high-passed with no upper corner, so their band centre lands
    /// between the midrange's and the tweeter's: ordered together with the front
    /// they wedge themselves into the middle of it, which invents junctions that
    /// are not crossovers (a midrange handing over to a rear fill, a rear fill to
    /// a centre) and hides the one that is — the front's own midrange/tweeter
    /// pair stops being adjacent and never gets built. Filtering by zone is what
    /// makes the ordering a chain again, and the centre drops out of every view
    /// with it, for the reason
    /// <see cref="VirtualCrossoverGroupViews.ParticipatesInTotalSum"/> states.
    /// <para>
    /// Views that span groups have no single chain to order, which is the
    /// condition the loss read-out already goes silent under
    /// (<see cref="VirtualCrossoverGroupViews.LossChainZone"/>): they list no
    /// junctions rather than a merged ordering of two.
    /// </para>
    /// </remarks>
    public static List<AdjacentPair> JunctionsInView(
        IReadOnlyList<ProcessedChannel> processed,
        VirtualCrossoverGroupView view)
    {
        ArgumentNullException.ThrowIfNull(processed);
        if (VirtualCrossoverGroupViews.LossChainZone(view) == null)
        {
            return [];
        }

        return GetAdjacentPairs(OrderByBand(
            [.. processed.Where(item =>
                VirtualCrossoverGroupViews.ParticipatesInTotalSum(
                    view, item.Channel.Pair.Zone))]));
    }

    /// <summary>
    /// The set a phase view of one channel is about: that channel and the shown
    /// drivers it actually hands a band to.
    /// </summary>
    /// <remarks>
    /// Every driver whose curve happens to be on screen is not that set — see
    /// <see cref="JunctionsInView"/> for what band-ordering a whole installation
    /// does to a chain. The chains are taken from the channel's own ZONE rather
    /// than from the group selector: a channel's PEQ menu opens from its block in
    /// every view, and which drivers it crosses with is a property of the
    /// installation, not of what the plot happens to show. A subwoofer sits in
    /// more than one (see <see cref="JunctionChains"/>), so tuning one from a car
    /// with a rear stage under it sees that junction too.
    /// <para>
    /// The result keeps band order and always holds the channel itself, whether
    /// or not its own curve is shown; a neighbour travels only when it is drawn,
    /// so hiding a curve in the panel hides it here too. Empty when the channel
    /// is not in the set handed in.
    /// </para>
    /// </remarks>
    public static List<ProcessedChannel> PhaseNeighbourhood(
        IReadOnlyList<ProcessedChannel> processed,
        VirtualCrossoverChannel channel)
    {
        ArgumentNullException.ThrowIfNull(processed);
        ArgumentNullException.ThrowIfNull(channel);
        ProcessedChannel? self = null;
        var neighbours = new List<ProcessedChannel>();
        foreach (IReadOnlyList<VirtualCrossoverZone> zones in JunctionChains)
        {
            if (!zones.Contains(channel.Pair.Zone))
            {
                continue;
            }

            List<ProcessedChannel> chain = OrderByBand(
                [.. processed.Where(item => zones.Contains(item.Channel.Pair.Zone))]);
            self ??= chain.Find(item => ReferenceEquals(item.Channel, channel));

            // Junctions, not band neighbours: a subwoofer stopping at 110 Hz beside
            // a rear fill starting at 290 is adjacent in the ordering with nothing
            // handed across, and its phase says nothing about the one being tuned.
            foreach (AdjacentPair pair in GetAdjacentPairs(chain))
            {
                ProcessedChannel? near =
                    ReferenceEquals(pair.Lower.Channel, channel) ? pair.Upper
                    : ReferenceEquals(pair.Upper.Channel, channel) ? pair.Lower
                    : null;
                if (near == null ||
                    !near.Channel.Pair.ShowProcessedCurve ||
                    neighbours.Any(seen => ReferenceEquals(seen, near)))
                {
                    continue;
                }

                neighbours.Add(near);
            }
        }

        if (self == null)
        {
            return [];
        }

        neighbours.Add(self);
        return OrderByBand(neighbours);
    }

    /// <summary>
    /// The crossover chains a car is tuned in, as the zones each one holds. The
    /// subwoofers belong to BOTH stages — they are the bottom of whichever one is
    /// on screen with them, which is what
    /// <see cref="VirtualCrossoverGroupViews.IsShown"/> already says — so a channel
    /// can sit in more than one, and a subwoofer crossing into a rear fill has that
    /// junction as surely as the one into the midbass.
    /// </summary>
    /// <remarks>
    /// Separate chains rather than one set of zones, because the union of the zones
    /// is exactly the ordering this type exists to refuse: a front stage and a rear
    /// fill sorted together invent a handover between them (see
    /// <see cref="JunctionsInView"/>). Each chain is ordered and read on its own,
    /// and only the junctions they actually produce are collected.
    /// <para>
    /// The centre is a chain of its own because it sums with nothing
    /// (<see cref="VirtualCrossoverGroupViews.ParticipatesInTotalSum"/>) — a two-way
    /// centre still crosses inside itself, and that is the whole of it.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyList<VirtualCrossoverZone>[] JunctionChains =
    [
        [VirtualCrossoverZone.Front, VirtualCrossoverZone.Sub],
        [VirtualCrossoverZone.Rear, VirtualCrossoverZone.Sub],
        [VirtualCrossoverZone.Center]
    ];

    private static bool PlaysWithin(ProcessedChannel channel, double lowHz, double highHz)
    {
        (double channelLow, double channelHigh) =
            VirtualCrossoverJunctions.GetChannelBand(channel.Channel.Settings);
        return channelHigh > lowHz && channelLow < highHz;
    }
}
