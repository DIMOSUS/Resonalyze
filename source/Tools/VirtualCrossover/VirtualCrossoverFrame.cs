using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>One side as a group view reads it: what it draws, what it sums, and whether its junctions are quoted. The screen
/// and the AI package build their frames here, so a package's numbers are the screen's by construction.</summary>
internal sealed record VirtualCrossoverFrame(
    IReadOnlyList<ProcessedChannel> All,
    List<ProcessedChannel> Shown,
    List<ProcessedChannel> Summed,
    bool QuotesJunctions)
{
    public static VirtualCrossoverFrame Of(
        IReadOnlyList<ProcessedChannel> processed,
        VirtualCrossoverGroupView view)
    {
        List<ProcessedChannel> shown = [.. processed.Where(item =>
            VirtualCrossoverGroupViews.IsShown(view, item.Channel.Pair.Zone))];
        // Drawn and summed differ where a centre is shown: compared, not added.
        List<ProcessedChannel> summed = [.. shown.Where(item =>
            VirtualCrossoverGroupViews.ParticipatesInTotalSum(view, item.Channel.Pair.Zone))];
        // No loss (curve or figure) across groups or where the chain has no junction.
        // See docs/tech/virtual-dsp-panel.md#sum-loss-and-group-views.
        return new VirtualCrossoverFrame(
            processed,
            shown,
            summed,
            VirtualCrossoverGroupViews.LossChainZone(view) != null &&
                ProcessedChannels.HasJunction(summed));
    }

    /// <summary>The Sum the screen draws and the Full-window loss read-out it quotes: what leaves the tool (an overlay
    /// capture, a tuning sheet, the Auto delay log) states the screen's structure, not every channel as one chain.</summary>
    public (AnalysisCurve? Sum, List<VirtualCrossoverMetric.Entry> Entries) ReadSum(
        VirtualCrossoverMetrics metrics, int smoothingInverseOctaves)
    {
        (_, AnalysisCurve? sum, List<SignalPoint>? loss) =
            metrics.BuildCurves([.. Shown], smoothingInverseOctaves, Summed);
        return (sum, metrics.BuildEntries(Summed, QuotesJunctions ? loss : null));
    }

    /// <summary>The junction phase read-out over the SUMMING channels, and the direct-sound loss built from the same gated
    /// spectra; empty unless junctions are quoted. Off the UI thread: the FDW gate is 50–100 ms per new response set.
    /// See docs/tech/virtual-dsp-panel.md#junction-phase-read-out.</summary>
    public async Task<(List<VirtualCrossoverMetric.PhaseEntry> Entries, List<SignalPoint>? DirectLoss)>
        ReadJunctionsAsync(
            VirtualCrossoverMetrics metrics,
            VirtualCrossoverPhaseGate gate,
            double? pinnedOffsetMs,
            int smoothingInverseOctaves,
            bool withDirectLoss)
    {
        if (!QuotesJunctions)
        {
            return ([], null);
        }

        int phaseRate = Summed[0].SampleRate;
        double leftMs = gate.LeftMs;
        double plateauMs = gate.PlateauMs;
        double rightMs = gate.RightMs;
        List<ProcessedChannel> summed = Summed;
        return await Task.Run(() =>
        {
            // Zone inside the task: Tracy zones are per-thread LIFO.
            using var _ = AppProfiler.Zone("VirtualDSP.BuildPhaseEntries");
            IReadOnlyList<ProcessedChannel>? orderedSet = null;
            IReadOnlyList<Complex[]>? spectra = null;
            List<VirtualCrossoverMetric.PhaseEntry> entries = metrics.BuildPhaseEntries(
                summed,
                ordered =>
                {
                    orderedSet = ordered;
                    spectra = JunctionPhaseSpectra.Build(
                        ordered, phaseRate, pinnedOffsetMs, leftMs, plateauMs, rightMs);
                    return spectra;
                });
            List<SignalPoint>? direct = withDirectLoss && spectra != null
                ? metrics.BuildDirectLossCurve(orderedSet!, spectra, smoothingInverseOctaves)
                : null;
            return (entries, direct);
        });
    }
}
