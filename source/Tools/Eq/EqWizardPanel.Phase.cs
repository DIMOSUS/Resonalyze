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
            // The pin travels with the gate: an absolute window the user placed by hand
            // in the panel must not read as Auto in the dialog here.
            phaseGatePinned = handed.PinnedOffset;
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
            PinnedOffset: false,
            new PlacementChannel(
                phaseSource.Response, measurement.PeakIndex, default),
            measurement.SampleRate,
            EqWizardPhaseRender.EditedChannelColor,
            []);
        UpdatePhaseGateAvailability();
    }

    private void UpdatePhaseGateAvailability()
    {
        buttonPhaseGate.Enabled = HasMeasuredPhase;
    }

    /// <summary>
    /// What the phase plot says about itself when it is showing less than the source
    /// suggests it might. Empty when there is nothing to explain.
    /// </summary>
    /// <remarks>
    /// The absence of the neighbours is the part worth spelling out: a channel arrives
    /// from Virtual DSP and the drivers beside it do not, which reads as a bug rather
    /// than as the deliberate refusal it is.
    /// </remarks>
    private string PhaseModeHint()
    {
        if (loadedSource is not { } source)
        {
            return string.Empty;
        }

        if (PhaseContextFor(source) == null)
        {
            return "This source is a magnitude curve — it carries no phase.\n" +
                "Only the EQ's own phase is drawn.";
        }

        return source.Kind == EqWizardSourceKind.VirtualDspChannel &&
            source.PhaseContext == null
            ? "Raw handoff: the neighbouring drivers are not drawn.\n" +
                "This curve has no crossover, delay or polarity in front of it and\n" +
                "they do, so lining it up against them would line up a system that\n" +
                "does not exist. Use Edit in EQ Wizard for junction work."
            : string.Empty;
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

        bool committedPin = phaseGatePinned;
        using var dialog = new VirtualCrossoverGateDialog();
        // The plot tracks the dialog while it is open, exactly as the Virtual DSP plots
        // do: a gate is placed by looking at what it does to the curves, and a window
        // whose effect only appears after Save is one dialled in blind.
        dialog.PreviewChanged = (offsetMs, autoOffset, leftMs, plateauMs, rightMs,
            windowMode, fdwCycles, detrendMode, detrendMs) =>
        {
            ApplyPhaseGate(
                context, offsetMs, autoOffset, leftMs, plateauMs, rightMs,
                windowMode, fdwCycles, detrendMode, detrendMs);
            if (PhaseMode)
            {
                DrawSelectedCurves();
            }
        };
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
            // What Auto snaps the offset to: the earliest window of the set, the same
            // figure the panel fits to — not this channel's own, which would drag the
            // shared window onto whichever driver happens to be under edit.
            fitToMs: context.Neighbours
                .Select(neighbour => neighbour.GateOffsetMs)
                .Append(context.GateOffsetMs)
                .Min(),
            autoOffset: !phaseGatePinned);
        DialogResult result = dialog.ShowDialog(FindForm());
        dialog.PreviewChanged = null;
        if (result == DialogResult.OK)
        {
            // The last preview already built exactly what Save commits.
            ApplyPhaseGate(
                context, dialog.GateOffsetMs, dialog.AutoOffset, dialog.LeftMs,
                dialog.PlateauMs, dialog.RightMs, dialog.WindowMode, dialog.FdwCycles,
                dialog.DetrendMode, dialog.DetrendMs);
        }
        else
        {
            // Cancel drops the candidates: the stored gate is what the plot goes back
            // to, which is the only reason previewing live is safe.
            phaseGatePinned = committedPin;
            phaseContext = context;
            InvalidatePhaseCurves();
        }

        DrawSelectedCurves();
    }

    // One candidate gate over the context the dialog opened on. Pinned is one absolute
    // window for every curve; unpinned puts each on its own driver's arrival — the
    // distinction the Auto flag carries.
    private void ApplyPhaseGate(
        EqWizardPhaseContext opened,
        double offsetMs,
        bool autoOffset,
        double leftMs,
        double plateauMs,
        double rightMs,
        PhaseWindowMode windowMode,
        int fdwCycles,
        PhaseDetrendMode detrendMode,
        double detrendMs)
    {
        phaseGatePinned = !autoOffset;
        PhaseAnalysisSettings gate = opened.Gate with
        {
            LeftMs = leftMs,
            PlateauMs = plateauMs,
            RightMs = rightMs,
            WindowMode = windowMode,
            FdwCycles = fdwCycles,
            DetrendMode = detrendMode
        };
        // Resolved AGAIN, over the frozen set, with the same arithmetic the panel
        // runs. Not reused from what arrived: the per-curve placement is only allowed
        // while every window still opens before its own channel's response, and that
        // verdict depends on the window LENGTHS the dialog just changed. Carrying the
        // old answer would let the wizard keep placements the panel would refuse — or
        // stay on a shared window the panel would have released — and the two views
        // would read the junction differently.
        IReadOnlyList<PlacementChannel> set = opened.PlacementSet;
        double sharedOffsetMs = PhaseGatePlacement.ResolveSharedOffsetMs(
            set, opened.SampleRate, phaseGatePinned ? offsetMs : null);
        List<double> offsets = PhaseGatePlacement.ResolvePerCurveOffsets(
            set,
            sharedOffsetMs,
            opened.SampleRate,
            phaseGatePinned ? offsetMs : null,
            leftMs,
            plateauMs,
            rightMs);

        phaseContext = new EqWizardPhaseContext(
            gate,
            offsets[0],
            // The τ comes out of the SAME helper the panel calls, over the same set and
            // the window just resolved. A second implementation here read the
            // neighbours' offsets from the context the dialog opened on — the ones this
            // very call was replacing — so an estimated τ could be taken through a
            // window that no longer existed.
            PhaseGatePlacement.ResolveCommonDetrendMs(
                set,
                opened.SampleRate,
                gate with { GateOffsetMs = sharedOffsetMs },
                detrendMode,
                detrendMs),
            phaseGatePinned,
            opened.Channel,
            opened.SampleRate,
            opened.ChannelColor,
            opened.Neighbours
                .Select((neighbour, index) => neighbour with
                {
                    GateOffsetMs = offsets[index + 1]
                })
                .ToList());
        InvalidatePhaseCurves();
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
        _ = RenderPhaseCurveAsync(
            BuildPhaseRequest(source, context, eq), bank, context.ChannelColor);
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
            // A null curve means a newer render started while this one ran, so there is
            // nothing to land — but the redraw below still has to happen. It is the one
            // that starts the follow-up, and this is the ONLY case that needs starting:
            // returning here left the view with no curve and nothing on its way, which
            // is exactly what turning the gate's τ produced.
            if (curve != null && !IsDisposed && IsHandleCreated)
            {
                landedPhaseCurve = curve;
                landedPhaseBank = bank;
            }
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
            // Whatever happened above: paint what landed, and — since a request that
            // arrived while this was in flight was dropped by the in-flight guard —
            // start the render the current gate and bank are waiting for.
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
            context.ChannelColor,
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
            // The only curve whose wraps are marked. See AddPhaseSeries.
            AddPhaseSeries(model, live, dimmed: false, markWraps: true);
        }
    }

    /// <remarks>
    /// Wrap verticals are drawn for the curve under edit ALONE. Every measured phase
    /// curve wraps many times over the decade above a few kHz, and with the
    /// neighbours, the before-curve and this one all marking their own, the plot turns
    /// into a picket fence in which no trace can be followed — the opposite of what
    /// the markers are for. A curve that is only being read has its NaN break at the
    /// wrap, which is enough to keep the jump from reading as a phase transition.
    /// </remarks>
    private void AddPhaseSeries(
        PlotModel model,
        GatedPhaseCurve curve,
        bool dimmed,
        LineStyle style = LineStyle.Solid,
        bool markWraps = false)
    {
        OxyColor color = dimmed
            ? OxyColor.FromAColor(170, curve.Color)
            : curve.Color;
        if (markWraps && curve.WrapSegments.Count > 0)
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
