using OxyPlot;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The analyzer's main plot: the open measurement drawn in the active plot mode, with that mode's overlay slots, the
/// curve labels, the zoom it was left at and the phase modes' peak read-out.
/// </summary>
/// <remarks>
/// Redraws on its own when the open measurement or the compare selection changes, once per burst of changes, building
/// curves off the UI thread (docs/tech/sweep-measurement.md#plot-builds). Live Spectrum draws its captures into the same
/// view itself (<see cref="LiveSpectrumController"/>); this class still owns the view's mode, zoom memory and overlays.
/// </remarks>
internal sealed class AnalyzerPlot : IModeView
{
    private const string PeakInfoAnnotationTag = "PeakInfoAnnotation";

    private readonly Form owner;
    private readonly AnalyzerDocument document;
    private readonly CompareSelection compare;
    private readonly Control overlaysPanel;
    private readonly Control showAllButton;
    private readonly Control hideAllButton;
    private readonly PlotLabelsPanelController labels;
    private readonly DeferredRefresh measurementChanged;
    // Each mode's checked slots, kept while another mode is shown.
    private readonly ActiveOverlaySlotTracker activeOverlaySlots = new();
    private readonly SupersedingBuild builds = new();
    private ModeDescriptor descriptor = ModeCatalog.For(ModeTab.Frequency);
    // The last tab that drew the measurement, which a history entry keeps while a tool is shown.
    private ModeTab lastAnalysisTab = ModeTab.Frequency;
    // The slot mode the overlay slots were last loaded for; entering another tab of it keeps them (null: reload).
    private Mode? preparedSlotMode;
    // What the latest draw read; a change that moves none of it draws nothing.
    private PlotDrawInputs? drawn;

    public AnalyzerPlot(
        Form owner,
        PlotView view,
        Panel overlaysPanel,
        Control showAllButton,
        Control hideAllButton,
        WrappingToolTip toolTip,
        AnalyzerDocument document,
        LiveSpectrumSession live,
        CompareSelection compare,
        PlotModelFactory factory)
    {
        this.owner = owner;
        View = view;
        this.overlaysPanel = overlaysPanel;
        this.showAllButton = showAllButton;
        this.hideAllButton = hideAllButton;
        this.document = document;
        this.compare = compare;
        Factory = factory;
        Viewports = new PlotViewportMemory(view);
        labels = new PlotLabelsPanelController(view, () => Mode);
        var overlaySources = new OverlayPlotSources(() => view.Model, () => Mode);
        // Overlays gate on the shown axis scale. Live Spectrum shares FR's overlay slots, so it reports its own scale.
        overlaySources.SetMagnitudeScaleProvider(
            () => Mode switch
            {
                Mode.FrequencyResponse => factory.EffectiveFrequencyResponseScale,
                Mode.LiveSpectrum => live.Display.Scale,
                _ => MagnitudeScale.Relative
            });
        // Overlays store the raw curve so smoothing Off reveals the original.
        overlaySources.SetRawCurveProvider(tag =>
            tag == LiveSpectrumController.LiveSpectrumInputMagnitudeTag
                ? live.BuildRawRtaCapture()
                : factory.BuildRawCurve(tag));
        // Impulse axes are view settings, so overlays store record coordinates and re-frame on draw.
        overlaySources.SetImpulseCaptureProvider(tag => factory.BuildImpulseCapture(tag));
        overlaySources.SetImpulseFrameProvider(
            () => Mode == Mode.ImpulseResponse ? factory.ImpulseFrameOf(view.Model) : null);
        overlaySources.SetComplexSumProvider(BuildComplexSumOverlayPoints);
        OverlayControls = new OverlayPanel(owner, overlaysPanel, view, toolTip, overlaySources, RefreshLabels);
        measurementChanged = new DeferredRefresh(owner, RedrawChangedMeasurement);
        document.Changed += measurementChanged.Request;
        compare.Changed += measurementChanged.Request;
        owner.Disposed += (_, _) => builds.Cancel();
    }

    public PlotView View { get; }

    /// <summary>The draw in flight, done once its model is shown or dropped.</summary>
    public Task Drawing { get; private set; } = Task.CompletedTask;

    public PlotModelFactory Factory { get; }

    public PlotViewportMemory Viewports { get; }

    public OverlaySession Overlays => OverlayControls.Session;

    public OverlayPanel OverlayControls { get; }

    /// <summary>The mode on screen; <see cref="Mode.None"/> until the first switch.</summary>
    public Mode Mode { get; private set; }

    /// <summary>The tab and overlay selection a history entry keeps: the analysis tab on screen, or the one shown
    /// before a tool, with the slots it had checked. A tab without overlays (Waterfall, a tool) has none of its own.</summary>
    public (ModeTab Tab, List<int> OverlaySlots) SessionView()
    {
        if (ShowsOverlays)
        {
            return (descriptor.Tab, Overlays.CaptureActiveSlots(Mode));
        }
        if (descriptor.HasPlotView)
        {
            return (descriptor.Tab, []);
        }

        Mode analysisMode = ModeCatalog.For(lastAnalysisTab).Mode;
        if (!OverlayModes.Supports(analysisMode))
        {
            return (lastAnalysisTab, []);
        }

        activeOverlaySlots.TryGet(OverlayModes.SlotModeFor(analysisMode), out List<int> remembered);
        return (lastAnalysisTab, remembered.ToList());
    }

    private bool ShowsOverlays => descriptor.HasPlotView && OverlayModes.Supports(Mode);

    private bool CanDrawMeasurement => document.HasResult && !document.IsBusy;

    public void Leave()
    {
        // Modes without a main plot share Frequency slots but never draw them; capturing their empty set would wipe the selection.
        if (!ShowsOverlays)
        {
            return;
        }

        activeOverlaySlots.Store(
            OverlayModes.SlotModeFor(Mode),
            Overlays.CaptureActiveSlots(Mode));
    }

    public void Enter(ModeDescriptor mode)
    {
        descriptor = mode;
        Mode = mode.Mode;
        // The mode left will not show what it was building, whether or not the new one draws.
        builds.Cancel();
        Viewports.Show(null, Mode);
        RefreshLabels();
        if (descriptor.HasPlotView)
        {
            lastAnalysisTab = mode.Tab;
        }

        if (ShowsOverlays)
        {
            // Frequency Response and Live Spectrum share one slot set: re-reading and re-smoothing every slot from disk
            // on a switch between them changes nothing. A tab without a plot draws no slots, so it loads none.
            Mode slotMode = OverlayModes.SlotModeFor(Mode);
            if (preparedSlotMode != slotMode)
            {
                Overlays.Prepare(Mode);
                preparedSlotMode = slotMode;
            }
        }

        UpdateOverlayAvailability();
    }

    public void Present()
    {
        if (DrawsMeasurement)
        {
            Observe(Draw(placeholder: true));
        }

        // Show() with a null model unchecks the slots and loses the saved selection.
        if (!ShowsOverlays)
        {
            return;
        }

        if (activeOverlaySlots.TryGet(OverlayModes.SlotModeFor(Mode), out List<int> slots))
        {
            Overlays.RestoreActiveSlots(Mode, slots);
        }
    }

    /// <summary>Redraws the mode on screen; a build still running is cancelled.</summary>
    public void Redraw() => Observe(RedrawAsync());

    /// <summary><see cref="Redraw"/>, done once the model is shown or a superseded build has stopped; a failed build
    /// faults it.</summary>
    public Task RedrawAsync()
    {
        if (DrawsMeasurement)
        {
            return Draw(placeholder: false);
        }

        builds.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>For a setting that changes what an axis means: the next draw fits instead of restoring the zoom.</summary>
    public void ForgetZoom() => Viewports.Forget(Mode);

    /// <summary>A history entry's or New session's selection, in place of what is shown and remembered.</summary>
    public void ReplaceOverlaySlots(IReadOnlyList<int> slots)
    {
        activeOverlaySlots.Clear();
        if (!ShowsOverlays)
        {
            // The loaded slots keep the checks of the state being replaced; the next tab with overlays loads them afresh.
            preparedSlotMode = null;
            return;
        }

        Overlays.ReplaceActiveSlots(Mode, slots.ToHashSet());
        activeOverlaySlots.Store(OverlayModes.SlotModeFor(Mode), slots.ToList());
    }

    public void ShowAllOverlays()
    {
        Overlays.ShowAll(Mode);
        RefreshLabels();
    }

    public void HideAllOverlays()
    {
        Overlays.HideAll();
        RefreshLabels();
    }

    public void UpdateOverlayAvailability()
    {
        bool available = OverlayModes.Supports(Mode);
        overlaysPanel.Enabled = available;
        RefreshOverlayButtons();
        if (!available)
        {
            Overlays.HideAll();
        }
    }

    public void RefreshLabels()
    {
        labels.Refresh();
        RefreshOverlayButtons();
    }

    /// <summary>Virtual DSP's capture into the first free Frequency Response slot; null when all are taken.</summary>
    /// <remarks>Prepare() loads it on the next frequency-mode switch, already checked.</remarks>
    public int? SaveFrequencyResponseOverlay(string title, OverlayPoint[] points)
    {
        for (int slot = 1; slot <= OverlayFile.MaximumSlotCount; slot++)
        {
            bool occupied;
            try
            {
                occupied = OverlayFile.Load(Mode.FrequencyResponse, slot) != null;
            }
            catch (Exception)
            {
                occupied = true;
            }
            if (occupied)
            {
                continue;
            }

            var file = new OverlayFile
            {
                SavedAtUtc = DateTimeOffset.UtcNow,
                Mode = Mode.FrequencyResponse,
                Slot = slot,
                Kind = OverlayKind.Captured,
                Title = title,
                ColorArgb = UiPalette.CurveOverlayDefault.ToArgb(),
                Points = points
            };
            file.Save();
            activeOverlaySlots.MarkActive(Mode.FrequencyResponse, slot);
            // Written past the session: the slots load again when Frequency Response is next entered.
            preparedSlotMode = null;
            return slot;
        }

        return null;
    }

    /// <summary>Phase and group delay show the transfer IR's peak; other modes show nothing.</summary>
    public void UpdatePeakInfo()
    {
        PlotModel? model = View.Model;
        if (model == null)
        {
            return;
        }

        for (int index = model.Annotations.Count - 1; index >= 0; index--)
        {
            if (model.Annotations[index] is OverlayTextAnnotation
                {
                    Tag: PeakInfoAnnotationTag
                })
            {
                model.Annotations.RemoveAt(index);
            }
        }

        if (Mode is not (Mode.PhaseResponse or Mode.GroupDelay))
        {
            model.InvalidatePlot(false);
            return;
        }

        string transferPeak;
        if (document.Result is not { Transfer: { } transfer } result)
        {
            transferPeak = "--";
        }
        else
        {
            int peakSamples = transfer.PeakIndex;
            double peakMs = result.SampleRate > 0
                ? peakSamples * 1000.0 / result.SampleRate
                : 0;
            transferPeak = $"{peakMs:0.000} ms ({peakSamples} samples)";
        }
        string text = document.IsBusy
            ? "Peaks: measuring..."
            : "Transfer IR Peak: " + transferPeak;
        model.Annotations.Add(new OverlayTextAnnotation
        {
            Tag = PeakInfoAnnotationTag,
            Text = text,
            TextPosition = new DataPoint(0, 0),
            OffsetX = PlotZoomButtons.LeftPairClearance,
            TextFlowDirection = TextFlowDirection.TopDown,
            FontSize = 12,
            FontWeight = 700,
            TextColor = UiPalette.GraphAxisText.ToOxy(),
            TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left
        });
        model.InvalidatePlot(false);
    }

    // Live Spectrum draws its own captures; a non-plot mode has nothing here.
    private bool DrawsMeasurement => descriptor.HasPlotView && Mode != Mode.LiveSpectrum;

    private bool IncludesCurves => descriptor.SupportsCurveDrawing && CanDrawMeasurement;

    private PlotDrawInputs ReadInputs() =>
        PlotDrawInputs.Read(Mode, document, IncludesCurves, compare.Current);

    // A run or an import still producing keeps what is on screen; its result redraws when it lands.
    private void RedrawChangedMeasurement()
    {
        if (document.IsBusy)
        {
            return;
        }

        PlotDrawInputs inputs = ReadInputs();
        // A failed draw left something other than what it read on screen.
        PlotRedraw redraw = Drawing.IsFaulted ? PlotRedraw.Rebuild : inputs.RedrawFrom(drawn);
        // A build in flight carries the old name.
        if (redraw == PlotRedraw.Retitle && Drawing.IsCompleted && View.Model is { } model)
        {
            model.Title = Factory.Title(Mode);
            model.InvalidatePlot(false);
            drawn = inputs;
        }
        else if (redraw != PlotRedraw.None)
        {
            Redraw();
        }
    }

    // placeholder: a switch shows the mode's frame while the curves build, never a blank plot or another mode's.
    private Task Draw(bool placeholder)
    {
        using var _ = AppProfiler.Zone("AnalyzerPlot.Draw");
        // A change during the build queues a redraw, which cancels this one.
        measurementChanged.Refreshed();
        drawn = ReadInputs();
        bool showOverlay = descriptor.ShowOverlayCurves;
        if (!IncludesCurves)
        {
            builds.Cancel();
            Show(Factory.Create(Mode, includeCurves: false), includeCurves: false, showOverlay);
            return Drawing = Task.CompletedTask;
        }

        if (placeholder)
        {
            Viewports.ShowPlaceholder(Factory.Create(Mode, includeCurves: false), Mode);
            UpdatePeakInfo();
            RefreshLabels();
        }

        Mode mode = Mode;
        PlotModelFactory frozen = Factory.Freeze();
        return Drawing = builds.RunAsync(
            token => frozen.Create(mode, includeCurves: true, token),
            model =>
            {
                if (!owner.IsDisposed && Mode == mode)
                {
                    Show(model, includeCurves: true, showOverlay);
                }
            });
    }

    // A failed build nobody awaits reaches Application.ThreadException.
    private static async void Observe(Task drawing) => await drawing;

    private void Show(PlotModel model, bool includeCurves, bool showOverlay)
    {
        using var _ = AppProfiler.Zone("AnalyzerPlot.Show");
        Viewports.Show(model, Mode);
        UpdatePeakInfo();

        if (includeCurves && showOverlay)
        {
            Overlays.Show(Mode);
        }
        else
        {
            RefreshLabels();
        }
    }

    private void RefreshOverlayButtons()
    {
        bool hasOverlays = OverlayModes.Supports(Mode) && Overlays.HasOverlays(Mode);
        showAllButton.Enabled = hasOverlays;
        hideAllButton.Enabled = hasOverlays;
    }

    // Null while unavailable (the overlay stays armed). showLoss returns the sum-loss gap instead, smoothed at the slot's own width.
    private OverlayPoint[]? BuildComplexSumOverlayPoints(
        double compareDelayMs,
        bool invertComparePolarity,
        bool showLoss,
        double? lossSmoothingInverseOctaves)
    {
        AnalysisCurve? curve = showLoss
            ? Factory.TryBuildComplexSumLossCurve(
                compareDelayMs, invertComparePolarity, lossSmoothingInverseOctaves)
            : Factory.TryBuildComplexSumCurve(compareDelayMs, invertComparePolarity);
        return curve?.Points
            .Select(point => new OverlayPoint(point.X, point.Y))
            .ToArray();
    }
}
