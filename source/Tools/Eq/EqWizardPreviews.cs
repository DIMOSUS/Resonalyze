using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The measured phase curves the phase view draws; the edited one is null until its first render lands.</summary>
internal sealed record EqWizardPhaseCurves(
    IReadOnlyList<GatedPhaseCurve> Neighbours,
    GatedPhaseCurve? Bare,
    GatedPhaseCurve? Edited);

/// <summary>
/// The curves too heavy to build per frame, rendered off the UI thread for the session's current bank: a gated
/// source's corrected magnitude with the side's Sum loss, and the edited channel's measured phase. Keyed by bank, newest
/// wins, and the last landed curve stays shown while a newer one renders so the plot does not strobe. The session
/// invalidates them when what they read changes; a request made after that starts over.
/// </summary>
internal sealed class EqWizardPreviews
{
    private readonly EqWizardSession session;
    private readonly EqWizardPreviewOrchestrator gatedOrchestrator;
    private readonly EqWizardPhaseOrchestrator phaseOrchestrator;

    private PeqBankState? gatedPreviewBank;
    private bool gatedPreviewWithSumLoss;
    private bool gatedPreviewInFlight;

    // The edited channel re-renders per bank; neighbours and the bare curve change only with the gate.
    private GatedPhaseCurve? phaseCurve;
    private PeqBankState? phaseCurveBank;
    private GatedPhaseCurve? barePhaseCurve;
    private List<GatedPhaseCurve>? neighbourPhaseCurves;
    private bool phaseRenderInFlight;

    public EqWizardPreviews(EqWizardSession session)
        : this(session, new EqWizardPreviewOrchestrator(), new EqWizardPhaseOrchestrator())
    {
    }

    internal EqWizardPreviews(
        EqWizardSession session,
        EqWizardPreviewOrchestrator gatedOrchestrator,
        EqWizardPhaseOrchestrator phaseOrchestrator)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.gatedOrchestrator = gatedOrchestrator ?? throw new ArgumentNullException(nameof(gatedOrchestrator));
        this.phaseOrchestrator = phaseOrchestrator ?? throw new ArgumentNullException(nameof(phaseOrchestrator));
    }

    /// <summary>The gated source's corrected magnitude, as plot points aligned with the bare curve; null until one lands.</summary>
    public IReadOnlyList<DataPoint>? GatedPreview { get; private set; }

    /// <summary>The side's Sum loss with the last rendered bank in the chain; null until one lands, or where none exists.</summary>
    public IReadOnlyList<DataPoint>? SumLoss { get; private set; }

    public bool Rendering => gatedPreviewInFlight || phaseRenderInFlight;

    /// <summary>
    /// Starts rendering the gated corrected curve, and the Sum loss while it is drawn, for this bank, unless one is
    /// rendering or this bank's has landed. One job for both: the chain is applied once and the plot redrawn once. The
    /// task finishes on the caller's context, after which the plot is redrawn: that also starts the render an
    /// invalidation dropped, which the redraw that caused it could not start while this one was in flight.
    /// </summary>
    public Task? RequestGatedPreview(EqualizationCurve eq)
    {
        ArgumentNullException.ThrowIfNull(eq);
        if (session.Source is not { PreviewImpulseResponse: not null, GateSettings: not null } source)
        {
            return null;
        }

        bool withSumLoss = session.SumLossDrawn;
        if (!source.IsGated && !withSumLoss)
        {
            return null;
        }

        var bank = new PeqBankState(eq.Bands, eq.PreampDb);
        if (gatedPreviewInFlight ||
            (bank.Equals(gatedPreviewBank) && withSumLoss == gatedPreviewWithSumLoss))
        {
            return null;
        }

        EqWizardGatedPreviewRequest request =
            EqWizardSourceCurve.GatedPreviewRequest(session, source, eq, withSumLoss);
        gatedPreviewInFlight = true;
        return RenderGatedPreviewAsync(request, bank, EqWizardSourceCurve.KeepsGaps(source));
    }

    /// <summary>The phase view's curves for the current gate; builds the bank-independent ones on first use.</summary>
    public EqWizardPhaseCurves PhaseCurves()
    {
        if (session.Source is { } source && session.PhaseContext is { } context &&
            (neighbourPhaseCurves == null || barePhaseCurve == null))
        {
            EqWizardPhaseRequest request = EqWizardPhase.Request(
                source, context, bank: null, session.ProcessorSampleRateHz);
            neighbourPhaseCurves ??= EqWizardPhaseRender.RenderNeighbours(
                request, EqWizardPhaseRender.NeighbourThickness);
            barePhaseCurve ??= EqWizardPhaseRender.RenderEditedChannel(
                request,
                EqWizardPhaseRender.BareChannelTitle,
                context.ChannelColor,
                EqWizardPhaseRender.NeighbourThickness);
        }

        return new EqWizardPhaseCurves(neighbourPhaseCurves ?? [], barePhaseCurve, phaseCurve);
    }

    /// <summary>
    /// Starts rendering the edited channel's phase for this bank, unless one is rendering or this bank's has landed. The
    /// task finishes on the caller's context, after which the plot is redrawn: that also starts the render a dropped
    /// request is waiting for.
    /// </summary>
    public Task? RequestPhaseCurve(EqualizationCurve eq)
    {
        ArgumentNullException.ThrowIfNull(eq);
        if (session.Source is not { } source || session.PhaseContext is not { } context)
        {
            return null;
        }

        var bank = new PeqBankState(eq.Bands, eq.PreampDb);
        if (phaseRenderInFlight || bank.Equals(phaseCurveBank))
        {
            return null;
        }

        phaseRenderInFlight = true;
        return RenderPhaseCurveAsync(
            EqWizardPhase.Request(source, context, eq, session.ProcessorSampleRateHz),
            bank,
            context.ChannelColor);
    }

    /// <summary>Source, calibration or smoothing changed: every curve here read the old one.</summary>
    internal void InvalidateSourceCurves()
    {
        gatedOrchestrator.Invalidate();
        GatedPreview = null;
        gatedPreviewBank = null;
        // The phase view reads the same measurement and chain, neighbours included.
        InvalidatePhase();
    }

    internal void InvalidatePhase()
    {
        phaseOrchestrator.Invalidate();
        phaseCurve = null;
        phaseCurveBank = null;
        barePhaseCurve = null;
        neighbourPhaseCurves = null;
        // A direct-window Sum loss reads the phase gate, so a landed one is re-rendered with its preview.
        if (gatedPreviewWithSumLoss)
        {
            gatedOrchestrator.Invalidate();
            gatedPreviewBank = null;
        }

        SumLoss = null;
        gatedPreviewWithSumLoss = false;
    }

    private async Task RenderGatedPreviewAsync(
        EqWizardGatedPreviewRequest request, PeqBankState bank, bool keepGaps)
    {
        try
        {
            EqWizardGatedRender? landed = await gatedOrchestrator.RenderLatestAsync(request);
            // Null = dropped by an invalidation: nothing to keep.
            if (landed != null)
            {
                if (landed.Magnitude != null)
                {
                    // Same conversion as the bare curve, so both keep the same points.
                    GatedPreview = EqWizardSourceCurve.ToPlotPoints(landed.Magnitude, keepGaps);
                }

                if (request.SumLoss != null)
                {
                    // A landed null (one member, mixed rates) is this bank's answer.
                    SumLoss = landed.SumLoss == null
                        ? null
                        : EqWizardSourceCurve.ToPlotPoints(landed.SumLoss, keepGaps: true);
                }

                gatedPreviewBank = bank;
                gatedPreviewWithSumLoss = request.SumLoss != null;
            }
        }
        catch (Exception exception)
        {
            // A failed preview leaves the curves as they were; the bank stays exportable.
            System.Diagnostics.Debug.WriteLine($"EQ Wizard preview failed: {exception}");
        }
        finally
        {
            gatedPreviewInFlight = false;
        }
    }

    private async Task RenderPhaseCurveAsync(
        EqWizardPhaseRequest request,
        PeqBankState bank,
        OxyColor color)
    {
        try
        {
            GatedPhaseCurve? curve =
                await phaseOrchestrator.RenderLatestAsync(request, color);
            if (curve != null)
            {
                phaseCurve = curve;
                phaseCurveBank = bank;
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"EQ Wizard phase render failed: {exception}");
        }
        finally
        {
            phaseRenderInFlight = false;
        }
    }
}
