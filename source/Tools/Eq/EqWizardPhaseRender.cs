using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// One channel the EQ Wizard draws phase for but does not edit: a neighbouring
/// driver, frozen when the Virtual DSP handoff was taken.
/// </summary>
/// <param name="Channel">
/// Its PROCESSED response — chain and its own bank already applied — with what
/// placing a window needs beside it. The response is frozen rather than the drawn
/// curve on purpose: the wizard's own gate can be changed, and a curve gated at the
/// panel's window could not be re-read at the new one, so the two sides would stop
/// being comparable exactly when the user went looking.
/// </param>
/// <param name="GateOffsetMs">
/// Where this channel's window opens, resolved over the whole set by
/// <see cref="PhaseGatePlacement"/> — not re-derived per channel here.
/// </param>
internal sealed record EqWizardPhaseNeighbour(
    string Name,
    OxyColor Color,
    PlacementChannel Channel,
    double GateOffsetMs)
{
    public Complex[] ImpulseResponse => Channel.ImpulseResponse;
}

/// <summary>
/// What a Virtual DSP handoff hands the wizard's phase view: the neighbouring drivers
/// frozen as they stood, the window they and the edited channel were placed under, and
/// the one τ they are all read against.
/// </summary>
/// <remarks>
/// <para>
/// All three are resolved ONCE, over the set the wizard will draw — the edited channel
/// and the neighbours travelling with it. Resolving over channels nobody can see would
/// let a hidden driver move the windows of the drawn ones, and resolving per channel
/// would flatten each curve onto its own arrival, erasing exactly the offsets a
/// crossover region is read for.
/// </para>
/// <para>
/// Null for every source that is not a chain handoff: a raw handoff lives in its own
/// time rather than the processed view's, and a curve imported from a file carries no
/// phase to draw at all.
/// </para>
/// </remarks>
/// <param name="Gate">
/// The phase gate — the dialog's window, NOT the steady-state one the magnitude
/// curves use. Its offset is per curve, so the render overwrites it.
/// </param>
/// <param name="Gate">
/// The window as the user has it — mode, FDW cycles, durations AND detrend mode. The
/// curves are rendered against <paramref name="DetrendMs"/> as a Manual τ whatever the
/// mode says, because one τ for the whole set is what keeps their relative phase
/// honest; the mode rides along so an editor can offer the user back what they chose.
/// </param>
/// <param name="PinnedOffset">
/// Whether the user pinned one absolute window for every curve, as opposed to letting
/// each open on its own driver's arrival.
/// </param>
/// <param name="ChannelColor">
/// The colour the channel under edit is drawn in. It comes from the panel that handed
/// it over, so one driver reads the same in both views; a source with no panel behind
/// it falls back to the wizard's own.
/// </param>
/// <param name="Channel">
/// The edited channel's own processed response and what placing its window needs, for
/// the same reason its neighbours carry theirs: an editor that changes a window length
/// has to resolve the placements AGAIN, over the same set and by the same rule the
/// panel used, or unpinning would leave every window frozen where the pin left it and
/// a shortened gate would keep placements the panel would have refused.
/// </param>
internal sealed record EqWizardPhaseContext(
    PhaseAnalysisSettings Gate,
    double GateOffsetMs,
    double DetrendMs,
    bool PinnedOffset,
    PlacementChannel Channel,
    int SampleRate,
    OxyColor ChannelColor,
    IReadOnlyList<EqWizardPhaseNeighbour> Neighbours)
{
    /// <summary>This channel and its neighbours, in the order the placements are in.</summary>
    public IReadOnlyList<PlacementChannel> PlacementSet =>
        Neighbours.Select(neighbour => neighbour.Channel).Prepend(Channel).ToList();
}

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
    int SampleRate,
    int ProcessorSampleRate);

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
    /// <summary>
    /// How the channel under edit is drawn: it is the one curve that MOVES, so it
    /// carries the same colour the magnitude view gives Source + EQ and is the
    /// heaviest stroke on the plot. The neighbours keep the colours the Virtual DSP
    /// panel gave them, so the same driver reads the same in both views.
    /// </summary>
    public const string EditedChannelTitle = "This channel";
    public const double EditedChannelThickness = 2.2;
    public static readonly OxyColor EditedChannelColor = OxyColor.FromRgb(0, 209, 255);

    /// <summary>
    /// The same channel without the bank: where it started. Drawn in the channel's own
    /// colour and dashed rather than in a neutral grey — it is the SAME driver, and a
    /// second grey curve would compete with the bank's own phase, which is white.
    /// </summary>
    public const string BareChannelTitle = "Without EQ";
    public const double NeighbourThickness = 1.6;
    public static readonly OxyColor BareChannelColor = EditedChannelColor;

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
            request.SampleRate,
            request.ProcessorSampleRate);

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

/// <summary>
/// Runs phase renders off the UI thread and accepts only the newest result — the same
/// newest-wins contract the gated magnitude preview uses, and for the same reason: a
/// fader drag asks for one per frame and each costs a convolution and a transform.
/// </summary>
internal sealed class EqWizardPhaseOrchestrator
{
    private readonly Func<EqWizardPhaseRequest, OxyColor, GatedPhaseCurve> render;
    private long revision;

    public EqWizardPhaseOrchestrator()
        : this((request, color) => EqWizardPhaseRender.RenderEditedChannel(
            request,
            EqWizardPhaseRender.EditedChannelTitle,
            color,
            EqWizardPhaseRender.EditedChannelThickness))
    {
    }

    internal EqWizardPhaseOrchestrator(
        Func<EqWizardPhaseRequest, OxyColor, GatedPhaseCurve> render)
    {
        this.render = render ?? throw new ArgumentNullException(nameof(render));
    }

    /// <summary>Orphans any render in flight, so a stale one cannot land.</summary>
    public void Invalidate() => Interlocked.Increment(ref revision);

    /// <summary>
    /// The render, or null when a newer one started while this was running. Callers
    /// keep the last landed curve on screen meanwhile: blanking it would flicker the
    /// view on every keystroke.
    /// </summary>
    public async Task<GatedPhaseCurve?> RenderLatestAsync(
        EqWizardPhaseRequest request,
        OxyColor color)
    {
        ArgumentNullException.ThrowIfNull(request);
        long requestRevision = Interlocked.Increment(ref revision);
        GatedPhaseCurve curve = await Task.Run(() => render(request, color));
        return Interlocked.Read(ref revision) == requestRevision ? curve : null;
    }
}
