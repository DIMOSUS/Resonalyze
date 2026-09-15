using System.Numerics;
using OxyPlot;
using OxyPlot.Axes;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

// Phase view: the edited channel's measured phase against handoff neighbours. The only view where all-pass bands show. See docs/tech/eq-auto-tuner.md#phase-mode.
public partial class EqWizardPanel
{
    private readonly EqWizardPhaseOrchestrator phaseOrchestrator = new();

    // The edited channel re-renders per bank; neighbours and the bare curve change only with the gate.
    private GatedPhaseCurve? landedPhaseCurve;
    private PeqBankState? landedPhaseBank;
    private GatedPhaseCurve? cachedBarePhaseCurve;
    private List<GatedPhaseCurve>? cachedNeighbourPhaseCurves;
    private bool phaseRenderInFlight;

    private bool PhaseMode => checkBoxEqPhase.Checked;

    // Arrives with a handoff (resolved over the whole set); built locally for a lone IR.
    private EqWizardPhaseContext? phaseContext;
    // Unpinned keeps each driver's window on its own arrival (the context's offsets).
    private bool phaseGatePinned;

    /// <summary>Null for magnitude-only sources (overlay slot, text curve).</summary>
    private EqWizardPhaseContext? PhaseContextFor(EqWizardCurveSource? source) =>
        source is { Measurement: not null } ? phaseContext : null;

    /// <summary>A handoff brings its chain; a measurement opened directly uses the identity (the bank is everything).</summary>
    private static (Complex[] Response, DspChannelChain Chain)? PhaseSourceFor(
        EqWizardCurveSource source)
    {
        Complex[]? response =
            source.PreviewImpulseResponse ?? source.Measurement?.ImpulseResponse;
        return response == null
            ? null
            : (response, source.PreviewChain ?? DspChannelChain.Identity);
    }

    private bool HasMeasuredPhase => PhaseContextFor(loadedSource) != null;

    /// <summary>
    /// Adopts a handoff's context as-is (resolved over every driver), or builds one for a lone IR whose window and τ
    /// open on its own front. Call before anything draws.
    /// </summary>
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
            // An absolute window placed by hand must not read as Auto here.
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

    /// <summary>The Virtual DSP gate dialog, so a window reads the same in both tools.</summary>
    private void OpenPhaseGateDialog()
    {
        if (loadedSource is not { } source || PhaseContextFor(source) is not { } context)
        {
            return;
        }

        int sampleRate = source.Measurement!.SampleRate;
        var traces = new List<IrPreviewTrace>
        {
            new(
                VirtualCrossoverAnalysis.ApplyChain(
                    PhaseSourceFor(source)!.Value.Response,
                    PhaseSourceFor(source)!.Value.Chain with
                    {
                        Peq = BuildEqualizationCurve()
                    },
                    sampleRate,
                    EqProcessorSampleRate),
                EqWizardPhaseRender.EditedChannelTitle,
                EqWizardPhaseRender.EditedChannelColor)
        };
        traces.AddRange(context.Neighbours.Select(neighbour =>
            new IrPreviewTrace(neighbour.ImpulseResponse, neighbour.Name, neighbour.Color)));

        bool committedPin = phaseGatePinned;
        using var dialog = new VirtualCrossoverGateDialog();
        // The plot tracks the dialog live, like Virtual DSP: a gate is placed by watching its effect.
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
            fitToMs: AutoGateFitOffsetMs(context),
            autoOffset: !phaseGatePinned);
        DialogResult result = dialog.ShowDialog(FindForm());
        dialog.PreviewChanged = null;
        if (result == DialogResult.OK)
        {
            ApplyPhaseGate(
                context, dialog.GateOffsetMs, dialog.AutoOffset, dialog.LeftMs,
                dialog.PlateauMs, dialog.RightMs, dialog.WindowMode, dialog.FdwCycles,
                dialog.DetrendMode, dialog.DetrendMs);
        }
        else
        {
            // Cancel returns to the stored gate, which is what makes live preview safe.
            phaseGatePinned = committedPin;
            phaseContext = context;
            InvalidatePhaseCurves();
        }

        DrawSelectedCurves();
    }

    /// <summary>
    /// Auto snaps to the set's earliest front, read from the RESPONSES: under a pin the offsets are one absolute time
    /// and would make the dialog's preview disagree with the plot.
    /// </summary>
    private static double AutoGateFitOffsetMs(EqWizardPhaseContext context) =>
        PhaseGatePlacement.EarliestStartMs(context.PlacementSet, context.SampleRate);

    // Pinned: one absolute window for every curve; unpinned: each on its driver's arrival.
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
        // Re-resolved with the panel's arithmetic: per-curve placement validity depends on the window lengths just changed.
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
            // Same helper as the panel, over the window just resolved (not the offsets being replaced).
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

    // In phase mode the empty dB axis would read as a scale for the degree curves.
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

    // In magnitude the right axis holds only the EQ curve; in phase every measured curve is on it.
    private void SetEqAxisVisible(bool visible)
    {
        if (plotWizard.Model?.Axes.FirstOrDefault(axis => axis.Key == EqGainAxisKey)
            is { } eqAxis)
        {
            eqAxis.IsAxisVisible = visible;
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

    // Captured on the UI thread so the render touches no control.
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
            source.Measurement!.SampleRate,
            EqProcessorSampleRate);
    }

    // Keyed by bank, like the magnitude preview.
    private void RequestPhaseCurve(
        EqWizardCurveSource source,
        EqWizardPhaseContext context,
        EqualizationCurve eq)
    {
        // Not before the handle exists: a render landing in the creation pump draws into a half-created control.
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
            // Null = superseded; still redraw below, since that starts the follow-up render (returning early left the view empty).
            if (curve != null && !IsDisposed && IsHandleCreated)
            {
                landedPhaseCurve = curve;
                landedPhaseBank = bank;
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

        if (!IsDisposed && IsHandleCreated)
        {
            // Paint what landed and start the render a dropped in-flight request is waiting for.
            DrawSelectedCurves();
        }
    }

    // Built once per gate: they do not depend on the bank.
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

    private void DrawMeasuredPhaseCurves(PlotModel model, EqualizationCurve eq)
    {
        if (loadedSource is not { } source ||
            PhaseContextFor(source) is not { } context)
        {
            return;
        }

        EnsurePhaseReferenceCurves(source, context);
        RequestPhaseCurve(source, context, eq);

        // Neighbours first so the edited curve is never hidden under a reference.
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
            AddPhaseSeries(model, live, dimmed: false, markWraps: true);
        }
    }

    /// <remarks>Only the edited curve marks wraps: every curve marking them turns the HF into a picket fence.</remarks>
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
            // Empty title keeps them out of the labels panel, as in Virtual DSP.
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
