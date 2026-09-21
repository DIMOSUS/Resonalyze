using OxyPlot;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Null while unavailable (the overlay stays armed); showLoss asks for the sum-loss gap instead.</summary>
internal delegate OverlayPoint[]? ComplexSumOverlayBuilder(
    double compareDelayMs,
    bool invertComparePolarity,
    bool showLoss,
    double? lossSmoothingInverseOctaves);

/// <summary>What the overlay slots read from outside themselves: the plot they draw on and the analyzer behind it.</summary>
internal sealed class OverlayPlotSources
{
    private readonly Func<PlotModel?> model;
    private readonly Func<Mode> currentMode;
    private Func<MagnitudeScale>? magnitudeScale;
    private Func<CurveTag, RawCurveCapture?>? rawCurve;
    private Func<CurveTag, ImpulseOverlayCapture?>? impulseCapture;
    private Func<ImpulseOverlayFrame?>? impulseFrame;
    private ComplexSumOverlayBuilder? complexSum;

    /// <param name="currentMode">The mode on the plot; overlays show and capture in its slots.</param>
    public OverlayPlotSources(Func<PlotModel?> model, Func<Mode> currentMode)
    {
        this.model = model;
        this.currentMode = currentMode;
    }

    public PlotModel? Model => model();

    public Mode CurrentMode => currentMode();

    public Mode CurrentOverlayMode => OverlayModes.SlotModeFor(CurrentMode);

    public MagnitudeScale CurrentMagnitudeScale => magnitudeScale?.Invoke() ?? MagnitudeScale.Relative;

    public void SetMagnitudeScaleProvider(Func<MagnitudeScale> provider) => magnitudeScale = provider;

    /// <summary>Recomputes a captured curve without display smoothing so the overlay re-smooths itself; null = no raw form.</summary>
    public void SetRawCurveProvider(Func<CurveTag, RawCurveCapture?> provider) => rawCurve = provider;

    public void SetImpulseCaptureProvider(Func<CurveTag, ImpulseOverlayCapture?> provider) =>
        impulseCapture = provider;

    public void SetImpulseFrameProvider(Func<ImpulseOverlayFrame?> provider) => impulseFrame = provider;

    public void SetComplexSumProvider(ComplexSumOverlayBuilder provider) => complexSum = provider;

    public RawCurveCapture? TryGetRawCapture(CurveTag tag) => rawCurve?.Invoke(tag);

    public ImpulseOverlayCapture? TryGetImpulseCapture(CurveTag tag) => impulseCapture?.Invoke(tag);

    public ImpulseOverlayFrame? TryGetImpulseFrame() => impulseFrame?.Invoke();

    public OverlayPoint[]? BuildComplexSum(
        double compareDelayMs,
        bool invertComparePolarity,
        bool showLoss,
        double? lossSmoothingInverseOctaves) =>
        complexSum?.Invoke(compareDelayMs, invertComparePolarity, showLoss, lossSmoothingInverseOctaves);

    /// <summary>The curves on the plot that a slot may capture: everything but the overlays themselves.</summary>
    public List<LineSeries> CaptureCandidates()
    {
        PlotModel? current = Model;
        if (current == null)
        {
            return [];
        }

        return current.Series
            .OfType<LineSeries>()
            .Where(series => series.Tag is not string tag || !OverlaySeries.IsOverlayTag(tag))
            .ToList();
    }

    public IReadOnlyList<LiveCurveOption> LiveCurveOptions()
    {
        PlotModel? current = Model;
        if (current == null)
        {
            return [];
        }

        return current.Series
            .OfType<LineSeries>()
            .Where(series => series.Tag is CurveTag && series.Points.Count >= 2)
            .Select(series =>
            {
                var tag = (CurveTag)series.Tag!;
                return new LiveCurveOption(tag.Key, tag.Label, LiveCurveSemantics(series));
            })
            .ToArray();
    }

    // Separate from the source because the draw gate asks this of every checked slot on every rebuild.
    public OverlayCurveSemantics LiveCurveSemantics(string key) =>
        FindLiveCurve(key) is { } series ? LiveCurveSemantics(series) : OverlayCurveSemantics.None;

    public OverlayOperationSource? LiveCurveSource(string key)
    {
        LineSeries? match = FindLiveCurve(key);
        if (match == null)
        {
            return null;
        }

        var curveTag = (CurveTag)match.Tag!;
        return new OverlayOperationSource(
            0,
            curveTag.Label,
            match.Points.Select(point => new OverlayPoint(point.X, point.Y)).ToArray(),
            curveTag.PhaseUnwrapped,
            LiveCurveSemantics(match));
    }

    // Keeps NaN gaps so a deviation breaks over unreliable bands instead of bridging them.
    public OverlayPoint[]? PrimaryCurve()
    {
        LineSeries? primary = Model?.Series
            .OfType<LineSeries>()
            .FirstOrDefault(series =>
                series.Tag is CurveTag
                {
                    Source: CurveSource.Main,
                    Kind: AnalysisCurveKind.Primary
                });
        if (primary == null || primary.Points.Count < 2)
        {
            return null;
        }

        return primary.Points
            .Select(point => new OverlayPoint(point.X, point.Y))
            .ToArray();
    }

    private LineSeries? FindLiveCurve(string key) =>
        Model?.Series
            .OfType<LineSeries>()
            .FirstOrDefault(series =>
                series.Tag is CurveTag tag && tag.Key == key && series.Points.Count >= 2);

    // A live curve states the scale of the axis showing now (re-read each rebuild), not "no scale".
    private OverlayCurveSemantics LiveCurveSemantics(LineSeries series) =>
        OverlayCurveSemantics.ForCurve(CurrentMagnitudeScale, series.YAxisKey);
}

internal sealed record OverlayOperationSource(
    int Slot,
    string Title,
    IReadOnlyList<OverlayPoint> Points,
    bool? PhaseUnwrapped = null,
    // Operations reuse the points verbatim, so the result inherits these semantics.
    OverlayCurveSemantics Semantics = default);
