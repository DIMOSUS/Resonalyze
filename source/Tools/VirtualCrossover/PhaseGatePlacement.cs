using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The three things placing a phase window needs from a channel: the response, where
/// its peak is, and which part of the record is MEASURED rather than the chain's own
/// padding. Deliberately not <see cref="ProcessedChannel"/> — the arithmetic belongs
/// to any caller holding a response, including the EQ Wizard, which has responses
/// frozen out of a handoff and no live channels at all.
/// </summary>
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

/// <summary>
/// Where a set of channels' phase windows sit, and against which τ their phase is
/// read. The three answers belong together: a phase curve is only comparable to
/// another one when both were gated from the same set of arrivals and detrended by
/// the same τ, which is what makes the crossover region readable at all.
/// </summary>
/// <remarks>
/// <para>
/// Stateless on purpose. The Virtual DSP panel resolves this per redraw from its
/// project and its open gate dialog; the EQ Wizard resolves it once from what the
/// handoff froze, and again whenever its own gate is edited. Both must get the same
/// numbers from the same channels, or a tune made in one view would not hold in the
/// other — so the arithmetic lives here rather than in either panel.
/// </para>
/// <para>
/// Every method takes the channels it is resolving OVER. That set is the caller's
/// choice and it matters: resolving over hidden curves would let a channel nobody
/// can see move the window of the ones on screen.
/// </para>
/// </remarks>
internal static class PhaseGatePlacement
{
    /// <summary>
    /// The Auto gate anchor: the earliest estimated IR start across the channels —
    /// the band-limited first-arrival front, robust to the head garbage that poisons
    /// a bare peak read (memoized per IR in <see cref="TransferIrStartCache"/>).
    /// </summary>
    public static double EarliestStartMs(
        IReadOnlyList<PlacementChannel> channels,
        int sampleRate) =>
        channels.Min(item => TransferIrStartCache.ResolveStartMs(
            item.ImpulseResponse, sampleRate, item.PeakIndex, item.ValidRange));

    /// <summary>
    /// The one window every curve falls back to: a stored offset as-is, or — for an
    /// unconfigured (Auto) gate — the earliest front, so the gate tracks source and
    /// delay changes until the user pins it.
    /// </summary>
    public static double ResolveSharedOffsetMs(
        IReadOnlyList<PlacementChannel> channels,
        int sampleRate,
        double? configuredOffsetMs) =>
        configuredOffsetMs ?? EarliestStartMs(channels, sampleRate);

    /// <summary>
    /// Where each phase curve's window sits, aligned with <paramref name="channels"/>.
    /// <para>
    /// A pinned gate is one absolute window for every curve. Auto gives each channel
    /// its OWN estimated arrival, which is what lets FDW's short high-frequency
    /// windows sit on that channel's own first cycles instead of on whichever channel
    /// happened to arrive first — the whole point of reading phase through FDW.
    /// Per-curve placement is only comparable while every window opens before its
    /// channel's response does, so each one is put to
    /// <see cref="AllowsPerCurveGate"/> and the whole set drops back to the shared
    /// window if any fails: mixing the two placements would be worse than either.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Whether a per-curve window may be used for a channel, from what it discards
    /// ahead of its plateau against what the shared window would.
    /// <para>
    /// The ceiling alone is not the question, because a gate can be too short to hold
    /// a channel's leading edge WHEREVER it is placed: the project default
    /// (0.5/4/1.5 ms) cannot contain one period of a 55 Hz subwoofer, and on the field
    /// session it read -19.4 dB at the channel's own arrival and -19.4 dB at the
    /// shared one — identical. Refusing there buys no accuracy and costs the per-curve
    /// placement that keeps a late channel inside FDW's short windows, so the shared
    /// window has to be the better placement for this channel before it is worth
    /// taking. The arrival-PEAK placements this guard exists to catch are 25.7 to
    /// 61.4 dB worse than the shared window, so both conditions hold there with room
    /// to spare.
    /// </para>
    /// </summary>
    public static bool AllowsPerCurveGate(double perCurveLossDb, double sharedLossDb) =>
        perCurveLossDb <= MaxLeadingEdgeLossDb || perCurveLossDb <= sharedLossDb;

    /// <summary>
    /// The ceiling on <see cref="DataHelper.GateLeadingEdgeLossDb"/>: above it a
    /// window is cutting into its channel's leading edge, and the curve stops
    /// describing that channel — it starts describing what came after it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two placements are put to this figure: whether a phase curve may take its own
    /// window (<see cref="AllowsPerCurveGate"/>) and whether the window in use holds
    /// the channels at all (the panel's gate-placement warning).
    /// </para>
    /// <para>
    /// Measured on the v5 field session (four processed channels, gate 5/50/20 ms):
    /// windows placed on each channel's own arrival START read -28.4 to -72.2 dB,
    /// while the arrival-PEAK placement that drew a summing pair as antiphase read
    /// -3.5 to -10.8 dB — it discards a fifth to nearly half of a steeply low-passed
    /// channel's own energy. -20 dB sits in the middle of that 17.6 dB gap.
    /// </para>
    /// <para>
    /// The Passat session put the other end on the scale: a 15.06 ms gate inherited
    /// from another car's project, against processed arrivals at 4.10 to 5.97 ms,
    /// read +1.0 to +15.2 dB — at the top of that range the window discards thirty
    /// times the energy it keeps — while the same channels gated on their own
    /// arrivals read -42.9 to -55.0 dB.
    /// </para>
    /// </remarks>
    public const double MaxLeadingEdgeLossDb = -20.0;

    // The set's shared front: the earliest anchor among them, each read within its
    // own valid range so a chain delay's silent prefix cannot certify a front.
    private static int SharedStartAnchorIndex(
        IReadOnlyList<PlacementChannel> channels,
        int sampleRate) =>
        channels.Min(item => ProcessedChannels.StartAnchorIndex(
            item.ImpulseResponse, item.PeakIndex, sampleRate, item.ValidRange));

    /// <summary>
    /// The single τ every curve of the set is detrended by. One τ serves them all, so
    /// their RELATIVE phase — the whole point of the view — survives the detrend:
    /// per-channel τ would flatten each curve onto its own arrival and erase exactly
    /// the offsets a crossover region is read for.
    /// </summary>
    /// <param name="template">
    /// The gate as everything but the detrend: window mode, FDW cycles and the three
    /// durations, with the shared offset already in it. Only the Auto branch reads it,
    /// which estimates τ from the anchor channel through that very window.
    /// </param>
    /// <param name="manualDetrendMs">
    /// The user's τ for <see cref="PhaseDetrendMode.Manual"/>, or null to reference
    /// the set's own shared front anchor.
    /// </param>
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

        // Estimate once from the existing common anchor (the earliest processed FRONT,
        // the same channel the shared window opens on), then apply that exact value to
        // every driver and the sum.
        PlacementChannel anchor = channels.MinBy(item => ProcessedChannels.StartAnchorIndex(
            item.ImpulseResponse, item.PeakIndex, sampleRate, item.ValidRange));
        return DataHelper.ResolveCommonPhaseDetrendMilliseconds(
            new ImpulseMeasurementView(anchor.ImpulseResponse, 0, sampleRate),
            template with { DetrendMode = PhaseDetrendMode.Auto });
    }
}
