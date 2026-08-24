using System.Numerics;
using OxyPlot;
using OxyPlot.Axes;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

// The wizard's phase view: the measured phase of the channel being edited, against
// the neighbouring drivers a Virtual DSP handoff froze into the source.
//
// This is the only view an all-pass band shows up in at all — on a magnitude plot it
// is flat by construction — and lining a driver up with its neighbour through the
// crossover region is what such a band exists for.
public partial class EqWizardPanel
{
    private readonly EqWizardPhaseOrchestrator phaseOrchestrator = new();

    // The landed curves. The edited channel is re-rendered per bank edit (keyed by the
    // bank, like the magnitude preview); the neighbours and the bare curve only change
    // when the gate does, so they are computed once and kept.
    private GatedPhaseCurve? landedPhaseCurve;
    private PeqBankState? landedPhaseBank;
    private GatedPhaseCurve? cachedBarePhaseCurve;
    private List<GatedPhaseCurve>? cachedNeighbourPhaseCurves;
    private bool phaseRenderInFlight;

    /// <summary>
    /// Whether the plot is showing phase instead of magnitude. A MODE, not an extra
    /// curve: the source, the target, the error fill and the deviation statistics are
    /// magnitude ideas and have nothing to say here, so they leave the plot entirely.
    /// </summary>
    private bool PhaseMode => checkBoxEqPhase.Checked;

    // The gate the phase curves are read through, and where each of them opens. It
    // ARRIVES with a Virtual DSP handoff — the panel resolved it over the whole set of
    // drivers, and taking anything else would draw this channel against neighbours
    // read a different way — and is built locally for a lone impulse response, which
    // has no neighbours and only its own front to open on. The Phase gate button
    // edits it from there.
    private EqWizardPhaseContext? phaseContext;
    // Whether the user pinned one window for every curve. Unpinned keeps the
    // placements as they arrived: each driver's window sits on its own arrival, which
    // is what the offsets in the context ARE.
    private bool phaseGatePinned;

    /// <summary>
    /// The measured phase this source can draw, or null when it has none — an overlay
    /// slot or a text curve is a magnitude and nothing else, and no window or
    /// correction can invent a phase for it.
    /// </summary>
    private EqWizardPhaseContext? PhaseContextFor(EqWizardCurveSource? source) =>
        source is { Measurement: not null } ? phaseContext : null;

    /// <summary>
    /// What the phase render runs: the measurement and the chain to put it through.
    /// A Virtual DSP handoff carries both, so the curve moves with the bank exactly as
    /// the panel's does. A measurement opened straight into the wizard has no chain of
    /// its own — the bank IS everything applied to it — so the identity stands in.
    /// </summary>
    private static (Complex[] Response, DspChannelChain Chain)? PhaseSourceFor(
        EqWizardCurveSource source)
    {
        Complex[]? response =
            source.PreviewImpulseResponse ?? source.Measurement?.ImpulseResponse;
        return response == null
            ? null
            : (response, source.PreviewChain ?? DspChannelChain.Identity);
    }

    /// <summary>Whether the phase view has a measured curve to draw at all.</summary>
    private bool HasMeasuredPhase => PhaseContextFor(loadedSource) != null;

    /// <summary>
    /// Adopts the phase context a newly loaded source brings, or builds one for a lone
    /// impulse response. Called wherever the source changes, before anything draws.
    /// </summary>
    /// <remarks>
    /// A handoff's context is taken as it stands: the Virtual DSP panel resolved those
    /// windows and that τ over every driver on screen, and re-deriving them here from
    /// one channel would place this curve somewhere the panel never had it.
    /// <para>
    /// A source loaded straight into the wizard has no neighbours to be comparable
    /// with, so its window simply opens on its own front and its τ references the same
    /// instant — which flattens the propagation delay out of the curve and leaves the
    /// driver's own phase, the only thing there is to see with nothing to compare
    /// against.
    /// </para>
    /// </remarks>
    private void SeedPhaseContext(EqWizardCurveSource? source)
    {
        InvalidatePhaseCurves();
        phaseGatePinned = false;
        if (source is not { Measurement: { } measurement } ||
            PhaseSourceFor(source) is not { } phaseSource)
        {
            phaseContext = null;
            UpdatePhaseGateAvailability();
            return;
        }

        if (source.PhaseContext is { } handed)
        {
            phaseContext = handed;
            UpdatePhaseGateAvailability();
            return;
        }

        double startMs = ProcessedChannels.StartAnchorIndex(
            phaseSource.Response,
            measurement.PeakIndex,
            measurement.SampleRate) * 1_000.0 / measurement.SampleRate;
        phaseContext = new EqWizardPhaseContext(
            new PhaseAnalysisSettings(
                PhaseWindowMode.Fixed,
                PhaseAnalysisSettings.DefaultFdwCycles,
                PhaseDetrendMode.Manual,
                ManualDetrendMilliseconds: startMs,
                GateOffsetMs: startMs,
                FrequencyResponseOptions.DefaultPhaseLeftMs,
                FrequencyResponseOptions.DefaultPhasePlateauMs,
                FrequencyResponseOptions.DefaultPhaseRightMs,
                Unwrap: false,
                SmoothingInverseOctaves: 0.0),
            startMs,
            startMs,
            []);
        UpdatePhaseGateAvailability();
    }

    private void UpdatePhaseGateAvailability()
    {
        buttonPhaseGate.Enabled = HasMeasuredPhase;
    }

    /// <summary>
    /// Opens the Virtual DSP gate dialog on the wizard's own phase gate — the same
    /// dialog, so a window dialled in one tool reads the same in the other, with this
    /// channel and its neighbours drawn on its impulse preview.
    /// </summary>
    private void OpenPhaseGateDialog()
    {
        if (loadedSource is not { } source || PhaseContextFor(source) is not { } context)
        {
            return;
        }

        int sampleRate = source.Measurement!.SampleRate;
        // The preview shows what the windows actually sit on: this channel through its
        // chain and the bank as it stands, plus the frozen neighbours.
        var traces = new List<IrPreviewTrace>
        {
            new(
                VirtualCrossoverAnalysis.ApplyChain(
                    PhaseSourceFor(source)!.Value.Response,
                    PhaseSourceFor(source)!.Value.Chain with
                    {
                        Peq = BuildEqualizationCurve()
                    },
                    sampleRate),
                EqWizardPhaseRender.EditedChannelTitle,
                EqWizardPhaseRender.EditedChannelColor)
        };
        traces.AddRange(context.Neighbours.Select(neighbour =>
            new IrPreviewTrace(neighbour.ImpulseResponse, neighbour.Name, neighbour.Color)));

        using var dialog = new VirtualCrossoverGateDialog();
        dialog.Init(
            traces,
            sampleRate,
            context.GateOffsetMs,
            context.Gate.LeftMs,
            context.Gate.PlateauMs,
            context.Gate.RightMs,
            context.DetrendMs,
            context.Gate.WindowMode,
            context.Gate.FdwCycles,
            context.Gate.DetrendMode,
            fitToMs: context.GateOffsetMs,
            autoOffset: !phaseGatePinned);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        phaseGatePinned = !dialog.AutoOffset;
        phaseContext = new EqWizardPhaseContext(
            context.Gate with
            {
                LeftMs = dialog.LeftMs,
                PlateauMs = dialog.PlateauMs,
                RightMs = dialog.RightMs,
                WindowMode = dialog.WindowMode,
                FdwCycles = dialog.FdwCycles,
                DetrendMode = dialog.DetrendMode
            },
            // Pinned is one absolute window for every curve; unpinned keeps the
            // placements as they came, each on its own driver's arrival.
            phaseGatePinned ? dialog.GateOffsetMs : context.GateOffsetMs,
            dialog.DetrendMs,
            phaseGatePinned
                ? context.Neighbours
                    .Select(neighbour => neighbour with
                    {
                        GateOffsetMs = dialog.GateOffsetMs
                    })
                    .ToList()
                : context.Neighbours);
        InvalidatePhaseCurves();
        DrawSelectedCurves();
    }

    // The dB axis has nothing on it in phase mode, and an empty axis with gridlines
    // reads as a scale for the curves that ARE drawn — which are degrees.
    private void SetMagnitudeAxisVisible(bool visible)
    {
        if (plotWizard.Model?.Axes.FirstOrDefault(axis =>
                axis.Position == AxisPosition.Left && axis.Key == null) is { } left)
        {
            left.IsAxisVisible = visible;
            left.MajorGridlineStyle = visible ? LineStyle.Solid : LineStyle.None;
            left.MinorGridlineStyle = visible ? LineStyle.Dot : LineStyle.None;
        }
    }

    private void InvalidatePhaseCurves()
    {
        phaseOrchestrator.Invalidate();
        landedPhaseCurve = null;
        landedPhaseBank = null;
        cachedBarePhaseCurve = null;
        cachedNeighbourPhaseCurves = null;
    }

    // One render request: the channel's own measurement and chain, the bank to
    // substitute, and the placements the handoff resolved. Captured here, on the UI
    // thread, so the render itself touches no control.
    private EqWizardPhaseRequest BuildPhaseRequest(
        EqWizardCurveSource source,
        EqWizardPhaseContext context,
        EqualizationCurve? bank)
    {
        (Complex[] response, DspChannelChain chain) = PhaseSourceFor(source)!.Value;
        return new EqWizardPhaseRequest(
            response,
            chain,
            bank,
            context.GateOffsetMs,
            context.Neighbours,
            context.Gate,
            context.DetrendMs,
            source.Measurement!.SampleRate);
    }

    // Starts a render unless the landed curve already answers for this bank — the same
    // identity the magnitude preview uses, so a target nudge or a selection change
    // does not re-run a convolution.
    private void RequestPhaseCurve(
        EqWizardCurveSource source,
        EqWizardPhaseContext context,
        EqualizationCurve eq)
    {
        // Nothing starts before the panel is on screen: a handoff installs its source
        // while the wizard is still the hidden mode, and a render landing inside that
        // pump would draw into a half-created control.
        if (!IsHandleCreated)
        {
            return;
        }

        var bank = new PeqBankState(eq.Bands, eq.PreampDb);
        if (phaseRenderInFlight || bank.Equals(landedPhaseBank))
        {
            return;
        }

        phaseRenderInFlight = true;
        _ = RenderPhaseCurveAsync(BuildPhaseRequest(source, context, eq), bank);
    }

    private async Task RenderPhaseCurveAsync(EqWizardPhaseRequest request, PeqBankState bank)
    {
        try
        {
            GatedPhaseCurve? curve = await phaseOrchestrator.RenderLatestAsync(request);
            if (IsDisposed || !IsHandleCreated || curve == null)
            {
                return;
            }

            landedPhaseCurve = curve;
            landedPhaseBank = bank;
        }
        catch (Exception exception)
        {
            // A render that throws must not take the panel with it: the curve stays as
            // it was, and the bank is still editable and exportable.
            System.Diagnostics.Debug.WriteLine($"EQ Wizard phase render failed: {exception}");
        }
        finally
        {
            phaseRenderInFlight = false;
        }

        if (!IsDisposed && IsHandleCreated)
        {
            // The bank may have moved on while this rendered; drawing now both paints
            // what landed and starts the follow-up render for the newer bank.
            DrawSelectedCurves();
        }
    }

    // The reference curves — the neighbours, and this channel before its bank. Neither
    // depends on the bank, so they are built once per gate and kept: they are what the
    // moving curve is read against, and rebuilding them per keystroke would cost a
    // convolution each for a picture that never changes.
    private void EnsurePhaseReferenceCurves(
        EqWizardCurveSource source,
        EqWizardPhaseContext context)
    {
        if (cachedNeighbourPhaseCurves != null && cachedBarePhaseCurve != null)
        {
            return;
        }

        EqWizardPhaseRequest request = BuildPhaseRequest(source, context, bank: null);
        cachedNeighbourPhaseCurves ??= EqWizardPhaseRender.RenderNeighbours(
            request, EqWizardPhaseRender.NeighbourThickness);
        cachedBarePhaseCurve ??= EqWizardPhaseRender.RenderEditedChannel(
            request,
            EqWizardPhaseRender.BareChannelTitle,
            EqWizardPhaseRender.BareChannelColor,
            EqWizardPhaseRender.NeighbourThickness);
    }

    // Draws the measured phase curves. The analytic curves — the bank's own phase and
    // the selected band's — are added by the shared path afterwards, so the view shows
    // both what the filter does and what the driver does with it.
    private void DrawMeasuredPhaseCurves(PlotModel model, EqualizationCurve eq)
    {
        if (loadedSource is not { } source ||
            PhaseContextFor(source) is not { } context)
        {
            return;
        }

        EnsurePhaseReferenceCurves(source, context);
        RequestPhaseCurve(source, context, eq);

        // The neighbours first, then this channel's own before/after on top: the curve
        // the user is moving must never be hidden under a reference.
        foreach (GatedPhaseCurve neighbour in cachedNeighbourPhaseCurves!)
        {
            AddPhaseSeries(model, neighbour, dimmed: true);
        }

        if (cachedBarePhaseCurve is { } bare)
        {
            AddPhaseSeries(model, bare, dimmed: true, LineStyle.Dash);
        }

        if (landedPhaseCurve is { } live)
        {
            AddPhaseSeries(model, live, dimmed: false);
        }
    }

    private void AddPhaseSeries(
        PlotModel model,
        GatedPhaseCurve curve,
        bool dimmed,
        LineStyle style = LineStyle.Solid)
    {
        OxyColor color = dimmed
            ? OxyColor.FromAColor(170, curve.Color)
            : curve.Color;
        if (curve.WrapSegments.Count > 0)
        {
            // The wrap verticals, faded and thinned well below the curve: visible as
            // wraps without competing with the traces. The empty title keeps them out
            // of the labels panel — the same treatment the Virtual DSP view gives them.
            AddWizardSeries(
                model,
                new EqWizardCurve(
                    string.Empty,
                    OxyColor.FromAColor(90, curve.Color),
                    curve.Thickness * 0.4,
                    LineStyle.Dash,
                    curve.WrapSegments
                        .Select(point => new DataPoint(point.X, point.Y))
                        .ToArray()),
                EqGainAxisKey,
                PhaseTrackerFormat);
        }

        AddWizardSeries(
            model,
            new EqWizardCurve(
                curve.Title,
                color,
                curve.Thickness,
                style,
                curve.Points
                    .Select(point => new DataPoint(point.X, point.Y))
                    .ToArray()),
            EqGainAxisKey,
            PhaseTrackerFormat);
    }
}
