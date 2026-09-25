using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The points an overlay slot draws and exports, read from the session and the plot it sits on.</summary>
internal sealed class OverlayCurves
{
    private readonly OverlaySession session;

    public OverlayCurves(OverlaySession session)
    {
        this.session = session;
    }

    private OverlayPlotSources Sources => session.Sources;

    // A capture states its measured scale; an operation carries its operands'; a target states nothing.
    public OverlayCurveSemantics SlotSemantics(OverlaySlot slot)
    {
        OverlaySlotState state = slot.State;
        return state.Kind switch
        {
            OverlayKind.Captured => OverlayCurveSemantics.ForCurve(
                state.MagnitudeScale,
                state.Captured?.YAxisKey),
            OverlayKind.Operation => ResultFor(state.Operation!).Curve,
            _ => OverlayCurveSemantics.None
        };
    }

    public bool DrawsOnShownScale(OverlaySlot slot) =>
        SlotSemantics(slot).DrawsOn(slot.SeriesMode, Sources.CurrentMagnitudeScale);

    // dB SPL vs relative dB, or coherence vs dB, yields a meaningless number; the slot stays unavailable.
    public bool OperationIsDefined(OverlaySlot slot) =>
        slot.State.Operation is not { } operation || ResultFor(operation).IsDefined;

    public OverlayOperationResult ResultFor(OverlayOperationSettings settings) =>
        OverlayCurveSemantics.ForOperation(
            settings.Operation,
            OperandSemantics(settings.SourceCurveKeyA, settings.SourceSlotA),
            settings.Operation == OverlayOperation.CurveA
                ? OverlayCurveSemantics.None
                : OperandSemantics(settings.SourceCurveKeyB, settings.SourceSlotB));

    public OverlayCurveSemantics OperandSemantics(string? curveKey, int slot)
    {
        if (curveKey != null)
        {
            return Sources.LiveCurveSemantics(curveKey);
        }

        return session.FindCaptureSlot(slot) is { } source
            ? SlotSemantics(source)
            : OverlayCurveSemantics.None;
    }

    // Psychoacoustic cubic averaging needs dB magnitude; axis is read off the slot so operations use the inherited axis.
    public bool MagnitudeSmoothingSemantics(OverlaySlot slot) =>
        OverlayMath.SupportsAmplitudeSpace(slot.SeriesMode) &&
        SlotSemantics(slot).YAxisKey != PlotModelFactory.CoherenceAxisKey &&
        slot.State.Captured?.CurveKind is not (AnalysisCurveKind.MinimumPhase or AnalysisCurveKind.ExcessPhase);

    public OverlayOperationSource? CaptureSource(int slotIndex)
    {
        OverlaySlot? slot = session.FindCaptureSlot(slotIndex);
        if (slot == null || slot.DrawPoints == null || slot.Title == "")
        {
            return null;
        }

        return new OverlayOperationSource(
            slot.Index,
            slot.Title,
            slot.DrawPoints
                .Select(point => new OverlayPoint(point.X, point.Y))
                .ToArray(),
            slot.State.Captured?.PhaseUnwrapped,
            SlotSemantics(slot));
    }

    public OverlayOperationSource? ResolveOperand(string? curveKey, int slot) =>
        curveKey != null ? Sources.LiveCurveSource(curveKey) : CaptureSource(slot);

    public bool HasOperands(OverlayOperationSettings settings)
    {
        OverlayOperationSource? sourceA = ResolveOperand(settings.SourceCurveKeyA, settings.SourceSlotA);
        OverlayOperationSource? sourceB = settings.UsesOperandB
            ? ResolveOperand(settings.SourceCurveKeyB, settings.SourceSlotB)
            : null;
        return sourceA != null && (sourceB != null || !settings.UsesOperandB);
    }

    public DataPoint[]? CapturedPoints(OverlaySlot slot, int smoothing)
    {
        if (slot.State.Captured is not { } captured)
        {
            return null;
        }

        double offset = (double)slot.State.Offset;

        // Time-domain capture re-drawn under the current framing; octave smoothing does not apply. Without a framing
        // yet (the slots load before the mode's first build) there is nothing to draw, which is not a damaged file.
        if (captured.Impulse is { Samples.Count: > 1 } capture)
        {
            if (Sources.TryGetImpulseFrame() is not { } frame)
            {
                return null;
            }

            DataPoint[] framed = ImpulseOverlayRenderer.Render(capture, frame);
            if (offset != 0.0)
            {
                for (int i = 0; i < framed.Length; i++)
                {
                    framed[i] = new DataPoint(framed[i].X, framed[i].Y + offset);
                }
            }

            return framed;
        }

        if (captured.RawSpectrum != null)
        {
            DataPoint[] exact = SmoothRawSpectrum(
                captured.RawSpectrum,
                captured.RawCalibrationCorrectionDb ?? [],
                smoothing,
                captured.MeasuredBand);
            if (exact.Length < 2)
            {
                return null;
            }

            for (int i = 0; i < exact.Length; i++)
            {
                exact[i] = new DataPoint(exact[i].X, exact[i].Y + offset);
            }

            return exact;
        }

        OverlayPoint[] smoothed = OverlayMath.SmoothByOctaves(
            captured.Points.Select(point => new OverlayPoint(point.X, point.Y)).ToArray(),
            smoothing,
            psychoacousticMagnitude: MagnitudeSmoothingSemantics(slot));
        return smoothed
            .Select(point => new DataPoint(point.X, point.Y + offset))
            .ToArray();
    }

    /// <summary>Re-smooths a stored raw spectrum, masked after smoothing so the break does not slide with width.</summary>
    public static DataPoint[] SmoothRawSpectrum(
        List<SignalPoint> spectrum,
        IReadOnlyList<double> calibrationCorrectionDb,
        int smoothing,
        MeasuredBand band)
    {
        List<SignalPoint> resampled = RawCurveRenderer.Render(spectrum, calibrationCorrectionDb, smoothing, band);
        var result = new DataPoint[resampled.Count];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = new DataPoint(resampled[i].X, resampled[i].Y);
        }

        return result;
    }

    public DataPoint[]? OperationPoints(OverlaySlot slot, OverlayOperationSettings settings, int smoothing)
    {
        // Computed from Main/Compare transfer IRs by the pipeline; sum-loss is smoothed once there at this slot's width.
        if (settings.IsComplexSum)
        {
            return ComplexSumPoints(
                slot,
                settings,
                smoothing,
                showLoss: settings.Operation == OverlayOperation.ComplexSumLoss);
        }

        OverlayOperationResult result = ResultFor(settings);
        if (!result.IsDefined)
        {
            return null;
        }

        OverlayOperationSource? sourceA = ResolveOperand(settings.SourceCurveKeyA, settings.SourceSlotA);
        OverlayOperationSource? sourceB = settings.UsesOperandB
            ? ResolveOperand(settings.SourceCurveKeyB, settings.SourceSlotB)
            : null;
        if (sourceA == null || (settings.UsesOperandB && sourceB == null))
        {
            return null;
        }

        // Wrapped operands need the wrapped (shortest-angle) difference; unwrapped keep raw subtraction to preserve delay slope.
        bool wrapPhaseDifference = slot.SeriesMode == Mode.PhaseResponse &&
            (sourceA.PhaseUnwrapped == false || sourceB?.PhaseUnwrapped == false);

        OverlayPoint[] points = OverlayMath.CalculateOperation(
            sourceA.Points,
            sourceB?.Points ?? Array.Empty<OverlayPoint>(),
            settings.Operation,
            settings.BlendFrequencyHz,
            settings.BlendWidthOctaves,
            settings.UseAmplitudeSpace && result.Curve.IsDecibels,
            wrapPhaseDifference);
        points = OverlayMath.SmoothByOctaves(
            points,
            smoothing,
            psychoacousticMagnitude: MagnitudeSmoothingSemantics(slot));
        if (points.Length < 2)
        {
            return null;
        }

        return ApplyOffsetAndTilt(points, settings, result.Curve, slot.State.Offset);
    }

    private DataPoint[]? ComplexSumPoints(
        OverlaySlot slot,
        OverlayOperationSettings settings,
        int smoothing,
        bool showLoss)
    {
        // A ratio, smoothed once by the pipeline at this slot's width (DataHelper.SmoothRatioLevels); smoothing here
        // would double-smooth and flatten the psychoacoustic mode's variable bandwidth to 1/6 octave.
        OverlayPoint[]? sumPoints = Sources.BuildComplexSum(
            settings.CompareDelayMs,
            settings.CompareInvertPolarity,
            showLoss,
            showLoss ? smoothing : null);
        if (sumPoints == null || sumPoints.Length < 2)
        {
            return null;
        }

        OverlayPoint[] smoothed = showLoss
            ? sumPoints
            : OverlayMath.SmoothByOctaves(sumPoints, smoothing);
        return ApplyOffsetAndTilt(smoothed, settings, OverlayCurveSemantics.None, slot.State.Offset);
    }

    // Offset and tilt last, after smoothing, and only on decibels (dB/octave is meaningless on coherence).
    private static DataPoint[] ApplyOffsetAndTilt(
        IReadOnlyList<OverlayPoint> points,
        OverlayOperationSettings settings,
        OverlayCurveSemantics semantics,
        decimal offsetDb)
    {
        double offset = (double)offsetDb;
        bool tilted = settings.TiltEnabled && semantics.IsDecibels;
        var result = new DataPoint[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            OverlayPoint point = points[i];
            double tilt = tilted
                ? OverlayMath.TiltDb(point.X, settings.TiltDbPerOctave, settings.TiltPivotHz)
                : 0;
            result[i] = new DataPoint(point.X, point.Y + offset + tilt);
        }

        return result;
    }

    public OverlayPoint[]? TargetSource(int sourceSlot)
    {
        if (sourceSlot != 0)
        {
            return CaptureSource(sourceSlot)?.Points.ToArray();
        }

        return Sources.PrimaryCurve();
    }

    /// <returns>Null when the target has no shape to draw.</returns>
    public (TargetOverlayShape Shape, DataPoint[] Deviation)? Target(
        OverlaySlot slot,
        OverlayTargetSettings settings,
        int smoothing)
    {
        double offset = (double)slot.State.Offset;
        TargetOverlayShape shape = slot.TargetBuilder.BuildShape(settings.Spec, offset, settings.ToleranceDb);
        if (shape.Target.Length < 2)
        {
            return null;
        }

        // Clipped to where the source has data (gaps where coherence is below threshold).
        DataPoint[] deviation =
            settings.DeviationMode != TargetDeviationMode.None &&
            TargetSource(settings.SourceSlot) is { Length: >= 2 } source
                ? TargetOverlayCurveBuilder.BuildDeviation(
                    source,
                    settings.Spec,
                    offset,
                    smoothing,
                    settings.DeviationMode)
                : Array.Empty<DataPoint>();
        return (shape, deviation);
    }

    /// <summary>What "Export to text" writes for the slot; null when it holds nothing drawable.</summary>
    public OverlayPoint[]? ExportPoints(OverlaySlot slot)
    {
        OverlaySlotState state = slot.State;
        switch (state.Kind)
        {
            case OverlayKind.Operation:
                return OperationPoints(slot, state.Operation!, state.SmoothingInverseOctaves)?
                    .Select(point => new OverlayPoint(point.X, point.Y))
                    .ToArray();
            case OverlayKind.Target:
                OverlayTargetSettings target = state.Target!;
                OverlayPoint[]? source = TargetSource(target.SourceSlot);
                if (source == null || source.Length < 2)
                {
                    return null;
                }

                return OverlayMath.BuildTarget(
                    source,
                    target.Spec,
                    (double)state.Offset,
                    target.ToleranceDb,
                    state.SmoothingInverseOctaves).Target;
            default:
                return state.Captured?.Points
                    .Select(point => new OverlayPoint(point.X, point.Y))
                    .ToArray();
        }
    }

    /// <summary>A target slot's deviation (or EQ correction) as exported; null when there is none to write.</summary>
    public OverlayDeviationExport? DeviationExport(OverlaySlot slot)
    {
        if (slot.State.Target is not { } target)
        {
            return null;
        }

        OverlayPoint[]? source = TargetSource(target.SourceSlot);
        if (source == null || source.Length < 2)
        {
            return null;
        }

        TargetDeviationMode mode = target.DeviationMode == TargetDeviationMode.None
            ? TargetDeviationMode.Deviation
            : target.DeviationMode;
        TargetCurveResult result = OverlayMath.BuildTarget(
            source,
            target.Spec,
            (double)slot.State.Offset,
            target.ToleranceDb,
            slot.State.SmoothingInverseOctaves,
            mode);
        return result.Deviation.Length < 2 ? null : new OverlayDeviationExport(result.Deviation, mode);
    }
}

internal sealed record OverlayDeviationExport(OverlayPoint[] Points, TargetDeviationMode Mode)
{
    public string Suffix => Mode == TargetDeviationMode.Correction ? "EQ correction" : "deviation";

    // A deviation is a difference, not a response; saying so keeps curve equalizers from using it.
    public OverlayCurveRole Role => Mode == TargetDeviationMode.Correction
        ? OverlayCurveRole.EqCorrection
        : OverlayCurveRole.Deviation;
}
