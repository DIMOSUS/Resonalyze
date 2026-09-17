using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// A neighbouring driver frozen at handoff. Its PROCESSED response is frozen, not a drawn curve, so it can be
/// re-gated when the wizard's gate changes.
/// </summary>
internal sealed record EqWizardPhaseNeighbour(
    string Name,
    OxyColor Color,
    PlacementChannel Channel,
    double GateOffsetMs)
{
    public Complex[] ImpulseResponse => Channel.ImpulseResponse;
}

/// <summary>
/// Neighbours, gate and shared τ from a chain handoff, resolved ONCE over the drawn set. Null for other sources.
/// See docs/tech/eq-auto-tuner.md#phase-mode.
/// </summary>
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
    public IReadOnlyList<PlacementChannel> PlacementSet =>
        Neighbours.Select(neighbour => neighbour.Channel).Prepend(Channel).ToList();
}

/// <summary>Everything one phase render needs, captured on the UI thread; the response is BEFORE the chain.</summary>
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
/// Measured phase of the edited channel through chain + bank, against frozen neighbours. Windows and τ do not
/// move with bank edits. Pure and thread-safe. See docs/tech/eq-auto-tuner.md#phase-mode.
/// </summary>
internal static class EqWizardPhaseRender
{
    public const string EditedChannelTitle = "This channel";
    public const double EditedChannelThickness = 2.2;
    public static readonly OxyColor EditedChannelColor = UiPalette.CurveSourcePlusEq.ToOxy();

    /// <summary>Dashed in the channel's colour, not grey: a grey curve would compete with the bank's white phase.</summary>
    public const string BareChannelTitle = "Without EQ";
    public const double NeighbourThickness = 1.6;
    public static readonly OxyColor BareChannelColor = EditedChannelColor;

    public static GatedPhaseCurve RenderEditedChannel(
        EqWizardPhaseRequest request,
        string title,
        OxyColor color,
        double thickness)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Substituted in one pass, like the magnitude preview and the Virtual DSP panel, so views cannot drift.
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

    /// <summary>Depends only on the gate, so callers reuse it across bank edits.</summary>
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

/// <summary>Newest-wins renders off the UI thread, like the gated magnitude preview.</summary>
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

    public void Invalidate() => Interlocked.Increment(ref revision);

    /// <summary>Null when a newer render started meanwhile; callers keep the last landed curve.</summary>
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
