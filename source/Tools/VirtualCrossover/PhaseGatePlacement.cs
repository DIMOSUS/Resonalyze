using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>What window placement needs from a channel; not <see cref="ProcessedChannel"/> so the EQ Wizard's frozen handoff responses can use it.</summary>
internal readonly record struct PlacementChannel(
    Complex[] ImpulseResponse,
    int PeakIndex,
    ValidSampleRange ValidRange)
{
    public static PlacementChannel From(ProcessedChannel channel) =>
        new(channel.ImpulseResponse, channel.PeakIndex, channel.ValidRange);

    public static IReadOnlyList<PlacementChannel> From(
        IReadOnlyList<ProcessedChannel> channels) =>
        channels.Select(From).ToList();
}

/// <summary>Phase window placement and common detrend τ, shared by Virtual DSP and EQ Wizard so a tune holds in both.
/// See docs/tech/junction-phase-and-group-placement.md#phase-gate-placement.</summary>
internal static class PhaseGatePlacement
{
    /// <summary>Auto gate anchor: earliest band-limited front across the channels (not a bare peak).</summary>
    public static double EarliestStartMs(
        IReadOnlyList<PlacementChannel> channels,
        int sampleRate) =>
        channels.Min(item => TransferIrStartCache.ResolveStartMs(
            item.ImpulseResponse, sampleRate, item.PeakIndex, item.ValidRange));

    public static double ResolveSharedOffsetMs(
        IReadOnlyList<PlacementChannel> channels,
        int sampleRate,
        double? configuredOffsetMs) =>
        configuredOffsetMs ?? EarliestStartMs(channels, sampleRate);

    /// <summary>Per-curve window offsets: pinned = one absolute window; Auto = each channel's own arrival, all falling back to shared if any fails <see cref="AllowsPerCurveGate"/>.</summary>
    public static List<double> ResolvePerCurveOffsets(
        IReadOnlyList<PlacementChannel> channels,
        double sharedOffsetMs,
        int sampleRate,
        double? pinnedOffsetMs,
        double leftMs,
        double plateauMs,
        double rightMs)
    {
        List<double> Shared() => channels.Select(_ => sharedOffsetMs).ToList();
        if (pinnedOffsetMs is not null)
        {
            return Shared();
        }

        var perCurve = new List<double>(channels.Count);
        foreach (PlacementChannel item in channels)
        {
            var view = new ImpulseMeasurementView(item.ImpulseResponse, 0, sampleRate);
            double startMs = TransferIrStartCache.ResolveStartMs(
                item.ImpulseResponse, sampleRate, item.PeakIndex, item.ValidRange);
            if (!AllowsPerCurveGate(
                    DataHelper.GateLeadingEdgeLossDb(
                        view, startMs, leftMs, plateauMs, rightMs),
                    DataHelper.GateLeadingEdgeLossDb(
                        view, sharedOffsetMs, leftMs, plateauMs, rightMs)))
            {
                return Shared();
            }

            perCurve.Add(startMs);
        }

        return perCurve;
    }

    /// <summary>Per-curve window allowed when under the ceiling OR no worse than the shared window (a short gate may cut a sub's edge anywhere).</summary>
    public static bool AllowsPerCurveGate(double perCurveLossDb, double sharedLossDb) =>
        perCurveLossDb <= MaxLeadingEdgeLossDb || perCurveLossDb <= sharedLossDb;

    /// <summary>Ceiling on <see cref="DataHelper.GateLeadingEdgeLossDb"/>: above it the window cuts into the channel's leading edge.</summary>
    public const double MaxLeadingEdgeLossDb = -20.0;

    private static int SharedStartAnchorIndex(
        IReadOnlyList<PlacementChannel> channels,
        int sampleRate) =>
        channels.Min(item => ProcessedChannels.StartAnchorIndex(
            item.ImpulseResponse, item.PeakIndex, sampleRate, item.ValidRange));

    /// <summary>One τ for the whole set so RELATIVE phase survives the detrend.</summary>
    /// <param name="manualDetrendMs">User τ for Manual; null references the set's shared front anchor.</param>
    public static double ResolveCommonDetrendMs(
        IReadOnlyList<PlacementChannel> channels,
        int sampleRate,
        PhaseAnalysisSettings template,
        PhaseDetrendMode detrendMode,
        double? manualDetrendMs)
    {
        if (detrendMode == PhaseDetrendMode.Off)
        {
            return 0.0;
        }

        if (detrendMode == PhaseDetrendMode.Manual)
        {
            return manualDetrendMs ??
                SharedStartAnchorIndex(channels, sampleRate) * 1_000.0 / sampleRate;
        }

        PlacementChannel anchor = channels.MinBy(item => ProcessedChannels.StartAnchorIndex(
            item.ImpulseResponse, item.PeakIndex, sampleRate, item.ValidRange));
        return DataHelper.ResolveCommonPhaseDetrendMilliseconds(
            new ImpulseMeasurementView(anchor.ImpulseResponse, 0, sampleRate),
            template with { DetrendMode = PhaseDetrendMode.Auto });
    }
}
