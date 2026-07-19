using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// A channel's processed response ready for the metric, the complex sum and the
/// plot: the applied-chain impulse response, its peak index and the channel's
/// plot color. Shared by the redraw, the metric read-out and the Auto delay search.
/// </summary>
internal sealed record ProcessedChannel(
    VirtualCrossoverChannel Channel,
    Complex[] ImpulseResponse,
    int PeakIndex,
    OxyColor Color);

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
