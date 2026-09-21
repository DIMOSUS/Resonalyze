using OxyPlot;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The analyzer's main plot: the open measurement drawn in the active plot mode, with that mode's overlay slots, the
/// curve labels, the zoom it was left at and the phase modes' peak read-out.
/// </summary>
/// <remarks>
/// Redraws on its own when the open measurement or the compare selection changes, once per burst of changes. Live
/// Spectrum draws its captures into the same view itself (<see cref="LiveSpectrumController"/>); this class still owns
/// the view's mode, zoom memory and overlays while it does.
/// </remarks>
internal sealed class AnalyzerPlot : IModeView
{
    private const string PeakInfoAnnotationTag = "PeakInfoAnnotation";

    private readonly Form owner;
    private readonly AnalyzerDocument document;
    private readonly Control overlaysPanel;
    private readonly Control showAllButton;
    private readonly Control hideAllButton;
    private readonly PlotLabelsPanelController labels;
    private readonly DeferredRefresh measurementChanged;
    // Each mode's checked slots, kept while another mode is shown.
    private readonly ActiveOverlaySlotTracker activeOverlaySlots = new();
    private ModeDescriptor descriptor = ModeCatalog.For(ModeTab.Frequency);
    // A build that finishes after a newer one started is dropped.
    private int refreshVersion;

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
        Factory = factory;
        Viewports = new PlotViewportMemory(view);
        labels = new PlotLabelsPanelController(view, () => Mode);
        Overlays = new OverlayCollection(owner, () => Mode, overlaysPanel, view, toolTip, RefreshLabels);
        // Overlays gate on the shown axis scale. Live Spectrum shares FR's overlay slots, so it reports its own scale.
        Overlays.SetMagnitudeScaleProvider(
            () => Mode switch
            {
                Mode.FrequencyResponse => factory.EffectiveFrequencyResponseScale,
                Mode.LiveSpectrum => live.Display.Scale,
                _ => MagnitudeScale.Relative
            });
        // Overlays store the raw curve so smoothing Off reveals the original.
        Overlays.SetRawCurveProvider(tag =>
            tag == LiveSpectrumController.LiveSpectrumInputMagnitudeTag
                ? live.BuildRawRtaCapture()
                : factory.BuildRawCurve(tag));
        // Impulse axes are view settings, so overlays store record coordinates and re-frame on draw.
        Overlays.SetImpulseCaptureProvider(tag => factory.BuildImpulseCapture(tag));
        Overlays.SetImpulseFrameProvider(
            () => Mode == Mode.ImpulseResponse ? factory.ImpulseFrame : null);
        Overlays.SetComplexSumProvider(BuildComplexSumOverlayPoints);
        measurementChanged = new DeferredRefresh(owner, RedrawChangedMeasurement);
        document.Changed += measurementChanged.Request;
        compare.Changed += measurementChanged.Request;
    }

    public PlotView View { get; }

    public PlotModelFactory Factory { get; }

    public PlotViewportMemory Viewports { get; }

    public OverlayCollection Overlays { get; }

    /// <summary>The mode on screen; <see cref="Mode.None"/> until the first switch.</summary>
    public Mode Mode { get; private set; }

    /// <summary>The overlay slots checked in the mode on screen, for a history entry.</summary>
    public List<int> ActiveOverlaySlots => Overlays.CaptureActiveSlots(Mode);

    private bool CanDrawMeasurement => document.HasResult && !document.IsBusy;

    public void Leave()
    {
        // Modes without a main plot share Frequency slots but never draw them; capturing their empty set would wipe the selection.
        if (!descriptor.HasPlotView || !OverlayCollection.SupportsMode(Mode))
        {
            return;
        }

        activeOverlaySlots.Store(
            OverlayCollection.OverlayModeFor(Mode),
            Overlays.CaptureActiveSlots(Mode));
    }

    public void Enter(ModeDescriptor mode)
    {
        descriptor = mode;
        Mode = mode.Mode;
        Viewports.Show(null, Mode);
        RefreshLabels();
        if (OverlayCollection.SupportsMode(Mode))
        {
            Overlays.Prepare(Mode);
        }

        UpdateOverlayAvailability();
    }

    public void Present()
    {
        if (DrawsMeasurement)
        {
            Draw();
        }

        // Show() with a null model unchecks the slots and loses the saved selection.
        if (!descriptor.HasPlotView || !OverlayCollection.SupportsMode(Mode))
        {
            return;
        }

        if (activeOverlaySlots.TryGet(OverlayCollection.OverlayModeFor(Mode), out List<int> slots))
        {
            Overlays.RestoreActiveSlots(Mode, slots);
        }
    }

    /// <summary>Redraws the mode on screen now; a slower build still running is dropped.</summary>
    public void Redraw()
    {
        Interlocked.Increment(ref refreshVersion);
        if (DrawsMeasurement)
        {
            Draw();
        }
    }

    /// <summary>Builds off the UI thread, for edits that arrive one after another; only the newest build is shown.</summary>
    public async Task RedrawAsync()
    {
        if (!DrawsMeasurement)
        {
            Redraw();
            return;
        }

        bool includeCurves = IncludesCurves;
        bool showOverlay = descriptor.ShowOverlayCurves;
        int version = Interlocked.Increment(ref refreshVersion);
        // A change during the build queues a redraw, which drops this build.
        measurementChanged.Refreshed();
        Mode mode = Mode;
        PlotModel model = await Task.Run(() => Factory.Create(mode, includeCurves));
        if (owner.IsDisposed ||
            version != Volatile.Read(ref refreshVersion) ||
            Mode != mode)
        {
            return;
        }

        Show(model, includeCurves, showOverlay);
    }

    /// <summary>For a setting that changes what an axis means: the next draw fits instead of restoring the zoom.</summary>
    public void ForgetZoom() => Viewports.Forget(Mode);

    public void RestoreOverlaySlots(IReadOnlyList<int>? slots) => Overlays.RestoreActiveSlots(Mode, slots);

    public void ShowAllOverlays()
    {
        Overlays.Show(Mode);
        RefreshLabels();
    }

    public void HideAllOverlays()
    {
        Overlays.HideAll();
        RefreshLabels();
    }

    public void UpdateOverlayAvailability()
    {
        bool available = OverlayCollection.SupportsMode(Mode);
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

    // A run or an import still producing keeps what is on screen; its result redraws when it lands.
    private void RedrawChangedMeasurement()
    {
        if (!document.IsBusy)
        {
            Redraw();
        }
    }

    private void Draw()
    {
        using var _ = AppProfiler.Zone("AnalyzerPlot.Draw");
        measurementChanged.Refreshed();
        bool includeCurves = IncludesCurves;
        Show(Factory.Create(Mode, includeCurves), includeCurves, descriptor.ShowOverlayCurves);
    }

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
        bool hasOverlays = OverlayCollection.SupportsMode(Mode) && Overlays.HasOverlays(Mode);
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
