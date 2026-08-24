using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// One channel the EQ Wizard draws phase for but does not edit: a neighbouring
/// driver, frozen when the Virtual DSP handoff was taken.
/// </summary>
/// <param name="ImpulseResponse">
/// Its PROCESSED response — chain and its own bank already applied. The processed
/// response is frozen rather than the drawn curve on purpose: the wizard's own gate
/// can be changed, and a curve gated at the panel's window could not be re-read at
/// the new one, so the two sides would stop being comparable exactly when the user
/// went looking.
/// </param>
/// <param name="GateOffsetMs">
/// Where this channel's window opens, resolved over the whole set by
/// <see cref="PhaseGatePlacement"/> — not re-derived per channel here.
/// </param>
internal sealed record EqWizardPhaseNeighbour(
    string Name,
    OxyColor Color,
    Complex[] ImpulseResponse,
    double GateOffsetMs);

/// <summary>
/// Everything one phase render needs, captured before it leaves the UI thread.
/// </summary>
/// <param name="SourceImpulseResponse">
/// The edited channel's source response, BEFORE its chain — the chain and the bank
/// under edit are applied per render, which is what makes the curve move when an
/// all-pass band is turned.
/// </param>
/// <param name="Chain">That channel's chain with the PEQ left out.</param>
/// <param name="Bank">The bank under edit, or null for the bare curve.</param>
/// <param name="DetrendMs">
/// The one τ every curve of the set is read against. Shared, so the relative phase
/// between this channel and its neighbours survives the detrend — which is the only
/// reason the view can answer "does the junction line up".
/// </param>
internal sealed record EqWizardPhaseRequest(
    Complex[] SourceImpulseResponse,
    DspChannelChain Chain,
    EqualizationCurve? Bank,
    double GateOffsetMs,
    IReadOnlyList<EqWizardPhaseNeighbour> Neighbours,
    PhaseAnalysisSettings Gate,
    double DetrendMs,
    int SampleRate);

/// <summary>
/// The EQ Wizard's phase view: the channel being edited, drawn through its chain and
/// its edited bank, against the neighbouring drivers it was handed.
/// </summary>
/// <remarks>
/// <para>
/// This is the measured phase of a response, not the analytic phase of the filter
/// bank — the difference being everything the room and the driver do. An all-pass
/// band is invisible on a magnitude plot by construction, so this is the only view
/// in which its work can be seen, and lining a driver up with its neighbour through
/// the crossover region is what it exists for.
/// </para>
/// <para>
/// The windows do NOT move while the bank is edited. Their placement and the shared
/// τ were resolved once, from the channels as they stood when the handoff was taken;
/// re-resolving them per keystroke would slide every curve under its own correction
/// (the same rule <see cref="EqWizardGatedPreview"/> follows for magnitude). Only an
/// edit of the gate itself re-resolves them, for the whole set at once.
/// </para>
/// <para>
/// Pure and thread-safe: every input is captured in the request, so a render runs on
/// a worker thread while the panel stays live.
/// </para>
/// </remarks>
internal static class EqWizardPhaseRender
{
    /// <summary>The edited channel's own phase, through its chain and the bank.</summary>
    public static GatedPhaseCurve RenderEditedChannel(
        EqWizardPhaseRequest request,
        string title,
        OxyColor color,
        double thickness)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Substituted, not layered — the same single pass the magnitude preview and
        // the Virtual DSP panel run for this channel, so the two views cannot drift.
        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            request.SourceImpulseResponse,
            request.Chain with { Peq = request.Bank },
            request.SampleRate);

        return GatedPhaseCurves.Read(
            processed,
            request.SampleRate,
            request.Gate,
            request.GateOffsetMs,
            request.DetrendMs,
            title,
            color,
            thickness);
    }

    /// <summary>
    /// The frozen neighbours' phase. Their responses do not change while the wizard
    /// is open — only the gate can — so a caller redraws these when the gate moves
    /// and reuses them across bank edits.
    /// </summary>
    public static List<GatedPhaseCurve> RenderNeighbours(
        EqWizardPhaseRequest request,
        double thickness)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Neighbours
            .Select(neighbour => GatedPhaseCurves.Read(
                neighbour.ImpulseResponse,
                request.SampleRate,
                request.Gate,
                neighbour.GateOffsetMs,
                request.DetrendMs,
                neighbour.Name,
                neighbour.Color,
                thickness))
            .ToList();
    }
}
