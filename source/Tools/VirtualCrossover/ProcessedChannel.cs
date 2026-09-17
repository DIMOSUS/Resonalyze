using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>A channel's processed response snapshot. <see cref="SampleRate"/> is captured, never read off the live channel:
/// a session import rebinds channels mid-render and a zero rate crashed. See docs/tech/virtual-dsp-analysis.md#processing-coordinator.</summary>
internal sealed record ProcessedChannel(
    VirtualCrossoverChannel Channel,
    Complex[] ImpulseResponse,
    int PeakIndex,
    int SampleRate,
    OxyColor Color,
    ValidSampleRange ValidRange = default,
    // Snapshotted per side: the list can carry the opposite side's responses.
    MeasuredBand MeasuredBand = default,
    // This side's measurement calibration; null when its file named none or the path does not care (panel selection applies).
    CalibrationFile? MicrophoneCalibration = null);

/// <summary><see cref="Unsmoothed"/> is the sum-loss operand; smoothing before the division invents corner dips.</summary>
internal sealed record GatedMagnitude(AnalysisCurve Display, AnalysisCurve Unsmoothed)
{
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

/// <summary>One side's complex sum, its window anchor and its channels. The channels hold THIS sum's side, which may not be
/// the channel's active one: read their state for this side.</summary>
internal sealed record VirtualCrossoverSideSum(
    Complex[] ImpulseResponse,
    int AnchorIndex,
    int SampleRate,
    IReadOnlyList<ProcessedChannel> Channels)
{
    public int ChannelCount => Channels.Count;
}

internal sealed record AdjacentPair(
    ProcessedChannel Lower,
    ProcessedChannel Upper,
    double CrossoverHz,
    double BandLowHz,
    double BandHighHz);

/// <summary>Band order and junction helpers shared by the metric read-out and the Auto delay search.
/// See docs/tech/virtual-dsp-analysis.md#measured-bands-and-junctions.</summary>
internal static class ProcessedChannels
{
    /// <summary>Hull of the channels' measured bands; holes between disjoint sweeps need <see cref="MeasuredBySomeChannel"/>.</summary>
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

    // One octave around the used corner frequencies, or a midband default without crossovers.
    public static (double MinHz, double MaxHz) GetCrossoverWindow(
        IReadOnlyList<ProcessedChannel> processed) =>
        VirtualCrossoverJunctions.GetCrossoverWindow(
            processed.Select(item => item.Channel.Settings));

    /// <summary>Estimated response START (peak fallback): a filtered channel's peak trails its front by the crossover GD.</summary>
    public static int StartAnchorIndex(
        Complex[] impulseResponse,
        int peakIndex,
        int sampleRate,
        ValidSampleRange validRange = default) =>
        TransferIrStartCache.ResolveStartIndex(
            impulseResponse, sampleRate, peakIndex, validRange);

    public static int SharedStartAnchorIndex(
        IReadOnlyList<ProcessedChannel> processed) =>
        processed.Min(item => StartAnchorIndex(
            item.ImpulseResponse, item.PeakIndex, item.SampleRate,
            item.ValidRange));

    public static List<ProcessedChannel> OrderByBand(IReadOnlyList<ProcessedChannel> processed) =>
        processed
            .OrderBy(item => VirtualCrossoverJunctions.BandCenterHz(item.Channel.Settings))
            .ToList();

    /// <summary>Band neighbours that really hand over: both channels must play inside the junction's octave-each-way window.</summary>
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

    public static bool HasJunction(IReadOnlyList<ProcessedChannel> channels) =>
        GetAdjacentPairs(OrderByBand(channels)).Count > 0;

    public static bool IsContinuousChain(IReadOnlyList<ProcessedChannel> channels) =>
        channels.Count >= 2 &&
        GetAdjacentPairs(OrderByBand(channels)).Count == channels.Count - 1;

    /// <summary>Junctions of the chain a grouped view sums; empty for views spanning several groups (band order across a whole
    /// installation invents junctions).</summary>
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

    /// <summary>The channel plus drawn drivers it hands a band to, taken from its own zone's chains.</summary>
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

    /// <summary>Chains tuned together; subwoofers sit in both stages, the centre sums with nothing.</summary>
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
