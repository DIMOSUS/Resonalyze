using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// A channel's processed response ready for the metric, the complex sum and the
/// plot: the applied-chain impulse response, its peak index, the channel's
/// plot color and where the MEASURED content sits inside the processed record
/// (<see cref="ValidRange"/> — the coordinator computes it with every render;
/// front detections read it so a chain delay's silent prefix cannot pose as
/// arrival SNR). Shared by the redraw, the metric read-out and the Auto delay
/// search. The range defaults to unknown for callers without one — the
/// analyses then fall back to their padding-signature heuristic.
/// </summary>
internal sealed record ProcessedChannel(
    VirtualCrossoverChannel Channel,
    Complex[] ImpulseResponse,
    int PeakIndex,
    OxyColor Color,
    ValidSampleRange ValidRange = default);

/// <summary>
/// One gated magnitude curve at the two widths the tool needs, from a single gate
/// and FFT. <see cref="Display"/> carries the chosen smoothing and is what the plot
/// draws; <see cref="Unsmoothed"/> is what the summation loss is divided out of,
/// because smoothing the operands before the division inflates a steep crossover
/// skirt far more than the (flat) sum and invents a dip at every corner — see
/// <see cref="VirtualCrossoverAnalysis.SumLossCurve"/>. The two are the same
/// instance when no smoothing is selected.
/// </summary>
internal sealed record GatedMagnitude(AnalysisCurve Display, AnalysisCurve Unsmoothed);

/// <summary>
/// One side of the car summed: the complex sum of its participating channels'
/// processed responses, the earliest arrival among them (the window anchor every
/// curve built from this sum must share) and the project rate they were measured
/// at. <see cref="ChannelCount"/> is how many channels went in, for read-outs
/// that need to say so.
/// </summary>
internal sealed record VirtualCrossoverSideSum(
    Complex[] ImpulseResponse,
    int AnchorIndex,
    int SampleRate,
    int ChannelCount);

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
        Complex[] impulseResponse, int peakIndex, int sampleRate) =>
        Math.Clamp(
            (int)Math.Floor(
                TransferIrStartCache.ResolveStartMs(
                    impulseResponse, sampleRate, peakIndex)
                / 1_000.0 * sampleRate),
            0,
            Math.Max(0, impulseResponse.Length - 1));

    /// <summary>
    /// The shared window's anchor for a channel set: the earliest
    /// <see cref="StartAnchorIndex"/> among them.
    /// </summary>
    public static int SharedStartAnchorIndex(
        IReadOnlyList<ProcessedChannel> processed) =>
        processed.Min(item => StartAnchorIndex(
            item.ImpulseResponse, item.PeakIndex, item.Channel.SampleRate));

    public static List<ProcessedChannel> OrderByBand(IReadOnlyList<ProcessedChannel> processed) =>
        processed
            .OrderBy(item => VirtualCrossoverJunctions.BandCenterHz(item.Channel.Settings))
            .ToList();

    public static List<AdjacentPair> GetAdjacentPairs(IReadOnlyList<ProcessedChannel> byBand)
    {
        var pairs = new List<AdjacentPair>();
        for (int i = 0; i < byBand.Count - 1; i++)
        {
            double pairHz = VirtualCrossoverJunctions.GetPairCrossoverHz(
                byBand[i].Channel.Settings, byBand[i + 1].Channel.Settings);
            (double bandLowHz, double bandHighHz) = VirtualCrossoverJunctions.OverlapBand(pairHz);
            pairs.Add(new AdjacentPair(
                byBand[i],
                byBand[i + 1],
                pairHz,
                bandLowHz,
                bandHighHz));
        }

        return pairs;
    }
}
