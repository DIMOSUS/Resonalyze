using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

internal sealed record EqWizardCurve(
    string Title,
    OxyColor Color,
    double StrokeThickness,
    LineStyle LineStyle,
    IReadOnlyList<DataPoint> Points);

// Source and SourcePlusEq are null without a source; otherwise Target is sampled on the source's frequencies.
internal sealed record EqWizardRenderSet(
    EqWizardCurve Target,
    EqWizardCurve? Source,
    EqWizardCurve? SourcePlusEq,
    EqWizardCurve? ElectricalTarget = null);

/// <summary>What the wizard shows for its session: the curves, their statistics and the hints. Pure reads.</summary>
internal static class EqWizardRender
{
    private const string NoSourceHint =
        "Load a source to equalize — an impulse response, a moving-mic capture,\n" +
        "or a measured curve from an overlay slot or a text file.\n" +
        "Use Target… to shape the goal curve.";

    // Dense enough that a Q=20 all-pass (most of 360° in 1/20 oct) gets several points per wrap, so seam detection holds.
    private const int PhaseGridPointCount = 1500;

    private static readonly double[] DefaultTargetGrid =
        EqualizationCurve.LogFrequencyGrid(20, 20_000, 512).ToArray();

    /// <summary>The bank as drawn: nothing under Bypass.</summary>
    public static EqualizationCurve DisplayedEq(EqWizardSession session) =>
        session.Bypass
            ? new EqualizationCurve(Array.Empty<PeqBand>())
            : session.Bank.Curve;

    /// <summary>
    /// Target, source and the source through <paramref name="eq"/>. A gated source's corrected curve is the last landed
    /// preview (<see cref="EqWizardPreviews"/>); a spatial average's is rebuilt with the bank inside its chain.
    /// </summary>
    public static EqWizardRenderSet RenderSet(EqWizardSession session, EqualizationCurve eq)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(eq);
        EqWizardCurve? source = session.SourceCurve;
        if (source is not { Points.Count: >= 2 })
        {
            return new EqWizardRenderSet(
                TargetCurve(session, DefaultTargetGrid),
                source,
                null,
                ElectricalTargetCurve(session, DefaultTargetGrid));
        }

        double[] frequencies = source.Points.Select(point => point.X).ToArray();
        return new EqWizardRenderSet(
            TargetCurve(session, frequencies),
            source,
            SourcePlusEq(session, source.Points, eq),
            ElectricalTargetCurve(session, frequencies));
    }

    /// <summary>What Auto Tune fits: the bare source and the target on its frequencies; null source without one.</summary>
    public static (EqWizardCurve? Source, EqWizardCurve Target) FitCurves(EqWizardSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        EqWizardCurve? source = session.SourceCurve;
        return source is { Points.Count: >= 2 }
            ? (source, TargetCurve(session, source.Points.Select(point => point.X).ToArray()))
            : (source, TargetCurve(session, DefaultTargetGrid));
    }

    /// <summary>Error against the target within the Auto Tune window, and the bank's reach; null without a corrected curve.</summary>
    public static EqTuneStats? Stats(
        EqWizardSession session,
        EqWizardRenderSet? render,
        EqualizationCurve eq)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(eq);
        if (render?.SourcePlusEq == null)
        {
            return null;
        }

        IReadOnlyList<DataPoint> corrected = render.SourcePlusEq.Points;
        IReadOnlyList<DataPoint> target = render.Target.Points;
        int count = Math.Min(corrected.Count, target.Count);

        (double minHz, double maxHz) = session.FrequencyWindow;

        double sumSquares = 0;
        double maxError = 0;
        int valid = 0;
        for (int i = 0; i < count; i++)
        {
            double frequency = corrected[i].X;
            if (frequency < minHz || frequency > maxHz)
            {
                continue;
            }

            double error = target[i].Y - corrected[i].Y;
            if (!double.IsFinite(error))
            {
                continue;
            }

            sumSquares += error * error;
            maxError = Math.Max(maxError, Math.Abs(error));
            valid++;
        }

        // No point in the window is no error to grade, not a perfect fit.
        double? rms = valid > 0 ? Math.Sqrt(sumSquares / valid) : null;
        double? maxInWindow = valid > 0 ? maxError : null;
        // Counts the bank, not the bypassed curve. All-pass is always "used": its work is phase, which the gain threshold cannot see.
        int filtersUsed = session.Bank.Bands.Count(
            band => band.Type.IsAllPass() || Math.Abs(band.GainDb) >= 0.05);

        double peakBoost = double.NegativeInfinity;
        double peakCut = double.PositiveInfinity;
        foreach (double frequency in EqualizationCurve.LogFrequencyGrid(20, 20_000, 256))
        {
            double gain = DigitalEqualizationResponse.MagnitudeDbAt(
                eq, frequency, session.ProcessorSampleRateHz);
            peakBoost = Math.Max(peakBoost, gain);
            peakCut = Math.Min(peakCut, gain);
        }

        double headroom = -peakBoost;
        return new EqTuneStats(rms, maxInWindow, filtersUsed, peakBoost, peakCut, headroom);
    }

    /// <summary>The statistics of the bank a tuning sheet prints: under Bypass the plot draws no bank, the sheet still does.</summary>
    public static EqTuneStats? CurrentStats(EqWizardSession session)
    {
        EqualizationCurve eq = session.Bank.Curve;
        EqWizardRenderSet render = RenderSet(session, eq);
        // A gated source's landed preview is the bypassed (empty) bank's, so the bank's own is rendered here.
        if (session.Bypass && session.Source is { IsGated: true } gated && render.Source != null)
        {
            List<DataPoint> corrected = EqWizardSourceCurve.ToPlotPoints(
                EqWizardGatedPreview.Render(EqWizardSourceCurve.GatedPreviewRequest(session, gated, eq)),
                EqWizardSourceCurve.KeepsGaps(gated));
            render = render with
            {
                SourcePlusEq = new EqWizardCurve(
                    EqWizardSourceCurve.CorrectedTitle,
                    EqWizardSourceCurve.CorrectedColor,
                    2,
                    LineStyle.Solid,
                    corrected)
            };
        }

        return Stats(session, render, eq);
    }

    /// <summary>The bank's own gain (no preamp) on the baseline's frequencies.</summary>
    public static IReadOnlyList<DataPoint> EqGainPoints(
        EqWizardSession session,
        EqualizationCurve eq,
        EqWizardCurve baseline)
    {
        var withoutPreamp = new EqualizationCurve(eq.Bands, 0);
        return baseline.Points
            .Select(point => new DataPoint(
                point.X,
                DigitalEqualizationResponse.MagnitudeDbAt(
                    withoutPreamp, point.X, session.ProcessorSampleRateHz)))
            .ToArray();
    }

    /// <summary>One band's own gain on the baseline's frequencies.</summary>
    public static IReadOnlyList<DataPoint> BandGainPoints(
        EqWizardSession session,
        PeqBand band,
        EqWizardCurve baseline) =>
        baseline.Points
            .Select(point => new DataPoint(
                point.X,
                DigitalEqualizationResponse.MagnitudeDbAt(band, point.X, session.ProcessorSampleRateHz)))
            .ToArray();

    /// <summary>One band's contribution riding on the baseline (the target), so its shape reads against the goal.</summary>
    public static IReadOnlyList<DataPoint> BandPoints(
        EqWizardSession session,
        PeqBand band,
        EqWizardCurve baseline) =>
        baseline.Points
            .Select(point => new DataPoint(
                point.X,
                point.Y + DigitalEqualizationResponse.MagnitudeDbAt(
                    band, point.X, session.ProcessorSampleRateHz)))
            .ToArray();

    /// <summary>Wrapped (like every phase plot here and REW) with breaks at ±180° seams, keeping the axis fixed.</summary>
    public static IReadOnlyList<DataPoint> PhasePoints(
        EqWizardSession session,
        IReadOnlyList<PeqBand> bands,
        EqWizardCurve baseline)
    {
        int sampleRate = session.ProcessorSampleRateHz;
        BiquadCoefficients[] sections = bands
            .Where(band => !band.IsTransparent)
            .Select(band => PeqBiquad.Compute(band, sampleRate))
            .ToArray();
        IReadOnlyList<double> grid = EqualizationCurve.LogFrequencyGrid(
            Math.Max(1, baseline.Points[0].X),
            Math.Max(2, baseline.Points[^1].X),
            PhaseGridPointCount);

        var points = new List<DataPoint>(grid.Count + 16);
        double previous = double.NaN;
        foreach (double frequency in grid)
        {
            Complex response = Complex.One;
            foreach (BiquadCoefficients section in sections)
            {
                response *= BiquadResponse.Evaluate(section, frequency, sampleRate);
            }

            double degrees = response.Phase * (180.0 / Math.PI);
            if (!double.IsNaN(previous) && Math.Abs(degrees - previous) > 180.0)
            {
                points.Add(new DataPoint(frequency, double.NaN));
            }

            previous = degrees;
            points.Add(new DataPoint(frequency, degrees));
        }

        return points;
    }

    /// <summary>The plot's watermark: what to load, or what the phase view cannot show for this source.</summary>
    public static string Hint(EqWizardSession session) =>
        session.Source == null
            ? NoSourceHint
            : session.PhaseMode ? EqWizardPhase.Hint(session.Source, session.PhaseContext) : string.Empty;

    /// <summary>An imported dB SPL curve sits near 80 dB, outside the IR bounds, which are ABSOLUTE limits.</summary>
    public static (EqWizardAxisRange Range, bool SoundPressureLevel) SourceAxis(EqWizardSession session)
    {
        bool splCurve = session.Source is
        {
            Measurement: null,
            Scale: MagnitudeScale.SoundPressureLevel
        };
        EqWizardAxisRange range = session.Source is { Measurement: null }
            ? EqWizardPlotFit.ForCurve(
                session.SourceCurve?.Points.Select(point => new SignalPoint(point.X, point.Y))
                    ?? Enumerable.Empty<SignalPoint>())
            : EqWizardPlotFit.ImpulseResponseRange;
        return (range, splCurve);
    }

    /// <summary>The target at its level on these frequencies: what the plot draws and Auto Tune corrects toward.</summary>
    public static EqWizardCurve TargetCurve(EqWizardSession session, IReadOnlyList<double> frequencies)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(frequencies);
        EqTargetCurve target = session.Target;
        // The channel's crossover is part of the goal when the wizard is told to follow it: flat inside the passband,
        // the filter's own slope outside. See docs/tech/eq-auto-tuner.md#the-crossover-in-the-target.
        return new EqWizardCurve(
            "Target",
            OxyColor.FromArgb(target.Color.A, target.Color.R, target.Color.G, target.Color.B),
            target.StrokeThickness,
            OverlayLineStyles.ToOxy(target.LineStyle),
            ShapedTarget(session, frequencies, session.CrossoverInTarget ? session.TargetCrossover : null));
    }

    /// <summary>The highest level the target is drawn at across the plot, Target Level excluded; null when none is finite.</summary>
    public static double? TargetShapePeakDb(EqWizardSession session)
    {
        double[] levels = TargetCurve(session, DefaultTargetGrid).Points
            .Select(point => point.Y)
            .Where(double.IsFinite)
            .ToArray();
        return levels.Length == 0 ? null : levels.Max() - (double)session.TargetOffsetDb;
    }

    public const string ElectricalTargetTitle = "Target on the electrical crossover";

    /// <summary>The target on the electrical crossover, drawn as a reference where the target follows a stated
    /// acoustic one; null otherwise.</summary>
    public static EqWizardCurve? ElectricalTargetCurve(EqWizardSession session, IReadOnlyList<double> frequencies)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(frequencies);
        if (!session.CrossoverInTarget || session.ElectricalCrossover is not { } electrical)
        {
            return null;
        }

        EqTargetCurve target = session.Target;
        return new EqWizardCurve(
            ElectricalTargetTitle,
            OxyColor.FromArgb((byte)(target.Color.A / 2), target.Color.R, target.Color.G, target.Color.B),
            target.StrokeThickness,
            // Always the other pattern than the target's: opacity alone does not tell them apart.
            target.LineStyle == OverlayLineStyle.Dot ? LineStyle.Dash : LineStyle.Dot,
            ShapedTarget(session, frequencies, electrical));
    }

    private static DataPoint[] ShapedTarget(
        EqWizardSession session, IReadOnlyList<double> frequencies, EqTargetSlope? slope)
    {
        EqTargetCurve target = session.Target;
        double offset = (double)session.TargetOffsetDb;
        int sampleRateHz = session.ProcessorSampleRateHz;
        var points = new DataPoint[frequencies.Count];
        for (int i = 0; i < frequencies.Count; i++)
        {
            double frequency = frequencies[i];
            double shape = slope is { } shaped
                ? EqTargetCrossover.ShapeDb(shaped, frequency, sampleRateHz)
                : 0;
            double level = target.Spec.Evaluate(frequency) + offset + shape;
            points[i] = new DataPoint(frequency, double.IsFinite(level) ? level : double.NaN);
        }

        return points;
    }

    private static EqWizardCurve? SourcePlusEq(
        EqWizardSession session,
        IReadOnlyList<DataPoint> sourcePoints,
        EqualizationCurve eq)
    {
        // Filtered THEN windowed (they do not commute; several dB in the bass). Too heavy per frame, so it renders async.
        if (session.Source is { IsGated: true })
        {
            return session.Previews.GatedPreview is { } landed
                ? new EqWizardCurve(
                    EqWizardSourceCurve.CorrectedTitle,
                    EqWizardSourceCurve.CorrectedColor,
                    2,
                    LineStyle.Solid,
                    landed)
                : null;
        }

        // Bank substituted inside the chain, not added after smoothing; same builder keeps points aligned by index.
        if (session.Source is { SpatialAverage: not null } average)
        {
            List<DataPoint> corrected = EqWizardSourceCurve.ToPlotPoints(
                EqWizardSourceCurve.SpatialAverageCurve(session, average, eq),
                EqWizardSourceCurve.KeepsGaps(average));
            return corrected.Count >= 2
                ? new EqWizardCurve(
                    EqWizardSourceCurve.CorrectedTitle,
                    EqWizardSourceCurve.CorrectedColor,
                    2,
                    LineStyle.Solid,
                    corrected)
                : null;
        }

        int sampleRate = session.ProcessorSampleRateHz;
        var points = new DataPoint[sourcePoints.Count];
        for (int i = 0; i < sourcePoints.Count; i++)
        {
            DataPoint point = sourcePoints[i];
            points[i] = new DataPoint(
                point.X,
                point.Y + DigitalEqualizationResponse.MagnitudeDbAt(eq, point.X, sampleRate));
        }

        return new EqWizardCurve(
            EqWizardSourceCurve.CorrectedTitle,
            EqWizardSourceCurve.CorrectedColor,
            2,
            LineStyle.Solid,
            points);
    }
}
