using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

internal sealed record OverlayAppearance(
    Color Color,
    double StrokeThickness,
    OverlayLineStyle LineStyle,
    int OpacityPercent)
{
    public static OverlayAppearance Default(Color color) => new(color, 2, OverlayLineStyle.Solid, 100);

    public OxyColor LineColor() => LineColor(Color, OpacityPercent);

    public static OxyColor LineColor(Color color, int opacityPercent)
    {
        byte alpha = (byte)Math.Round(opacityPercent / 100.0 * 255);
        return OxyColor.FromArgb(alpha, color.R, color.G, color.B);
    }
}

/// <summary>A captured or imported curve as the slot keeps it; the display smoothing and offset are applied on draw.</summary>
internal sealed record CapturedCurve(
    DataPoint[] Points,
    MagnitudeScale MagnitudeScale,
    string? YAxisKey = null,
    bool? PhaseUnwrapped = null,
    // Null for imported text or legacy files. Gates magnitude-only (psychoacoustic) smoothing.
    AnalysisCurveKind? CurveKind = null,
    // Captured FR only: re-smoothed by the same LogarithmicResample as the mode, so any width reproduces it exactly.
    List<SignalPoint>? RawSpectrum = null,
    // Kept separate because the primary FR smooths first and calibrates afterwards.
    double[]? RawCalibrationCorrectionDb = null,
    // Slot outlives the measurement and the spectrum is unmasked, so it carries the band itself.
    MeasuredBand MeasuredBand = default,
    // No-raw captures only: correction baked into Points, for consumers outside the plot.
    double[]? PointsCalibrationCorrectionDb = null,
    // Smoothing baked into Points (0 = none, null = unknown); the display smoothing is applied on top.
    int? BakedSmoothingCode = null,
    int? SampleRateHz = null,
    // Signed samples (negative before time zero) and raw linear values, so the trace re-draws under the view's current framing.
    ImpulseOverlayCapture? Impulse = null)
{
    public bool HasData => Points.Length > 1;
}

/// <remarks>An operand with a curve key is a live curve resolved by <see cref="CurveTag"/> key on every rebuild.</remarks>
internal sealed record OverlayOperationSettings(
    int SourceSlotA,
    string? SourceCurveKeyA,
    int SourceSlotB,
    string? SourceCurveKeyB,
    OverlayOperation Operation,
    double BlendFrequencyHz,
    double BlendWidthOctaves,
    bool UseAmplitudeSpace,
    // dB/octave slope hinged at the pivot, compensating a sloped excitation.
    bool TiltEnabled,
    double TiltDbPerOctave,
    double TiltPivotHz,
    double CompareDelayMs,
    bool CompareInvertPolarity)
{
    public static OverlayOperationSettings Default { get; } = new(
        0, null, 0, null, OverlayOperation.AMinusB, 1_000, 1, false, false,
        OverlayFile.DefaultTiltDbPerOctave, OverlayFile.DefaultTiltPivotHz, 0, false);

    // "A only": stale operand B takes no part in availability, resolution or validation.
    public bool UsesOperandB => Operation != OverlayOperation.CurveA;

    public bool ReferencesLiveCurve => SourceCurveKeyA != null || (UsesOperandB && SourceCurveKeyB != null);

    // Reads Main/Compare transfer IRs, not operands; recomputes each rebuild.
    public bool IsComplexSum => Operation is OverlayOperation.ComplexSum or OverlayOperation.ComplexSumLoss;

    public static OverlayOperationSettings From(OverlayOperationPreview preview) => new(
        preview.SourceSlotA,
        preview.SourceCurveKeyA,
        preview.SourceSlotB,
        preview.SourceCurveKeyB,
        preview.Operation,
        preview.BlendFrequencyHz,
        preview.BlendWidthOctaves,
        preview.UseAmplitudeSpace,
        preview.TiltEnabled,
        preview.TiltDbPerOctave,
        preview.TiltPivotHz,
        preview.CompareDelayMs,
        preview.CompareInvertPolarity);
}

/// <param name="SourceSlot">0 = the current measurement's primary curve, otherwise a captured slot.</param>
internal sealed record OverlayTargetSettings(
    int SourceSlot,
    TargetPreset Preset,
    TargetCurveSpec Spec,
    double ToleranceDb,
    TargetDeviationMode DeviationMode)
{
    public bool FollowsCurrentMeasurement => SourceSlot == 0;
}

/// <summary>
/// One overlay slot's content: a captured curve, an operation between curves or a target, or nothing. It is what the
/// slot file stores; <see cref="Offset"/> is held as the offset field shows it.
/// </summary>
internal sealed record OverlaySlotState(
    Mode Mode,
    string Title,
    decimal Offset,
    OverlayAppearance Appearance,
    int SmoothingInverseOctaves,
    CapturedCurve? Captured = null,
    OverlayOperationSettings? Operation = null,
    OverlayTargetSettings? Target = null)
{
    public static OverlaySlotState Empty(Color color, decimal offset) =>
        new(Mode.None, "", offset, OverlayAppearance.Default(color), 0);

    public OverlayKind Kind =>
        Operation != null ? OverlayKind.Operation :
        Target != null ? OverlayKind.Target :
        OverlayKind.Captured;

    public bool HasCaptureData => Captured is { HasData: true };

    public bool HasContent => Kind switch
    {
        OverlayKind.Operation or OverlayKind.Target => true,
        _ => HasCaptureData
    };

    // Targets and operations are defined in relative dB.
    public MagnitudeScale MagnitudeScale => Captured?.MagnitudeScale ?? MagnitudeScale.Relative;

    public bool IsCurrentMeasurementTarget => Target is { FollowsCurrentMeasurement: true };

    public bool ReferencesLiveCurve => Operation is { ReferencesLiveCurve: true };

    public bool IsComplexSumOperation => Operation is { IsComplexSum: true };

    public OverlaySlotState WithCaptured(CapturedCurve curve, Mode mode, string title) =>
        this with { Mode = mode, Title = title, Captured = curve, Operation = null, Target = null };

    public static OverlaySlotState FromFile(OverlayFile file, NumericFieldRange offsetRange)
    {
        var appearance = new OverlayAppearance(
            Color.FromArgb(file.ColorArgb),
            file.StrokeThickness,
            file.LineStyle,
            file.OpacityPercent);
        decimal offset = offsetRange.Assign((decimal)Math.Clamp(
            file.Offset,
            (double)offsetRange.Minimum,
            (double)offsetRange.Maximum));
        var state = new OverlaySlotState(file.Mode, file.Title, offset, appearance, file.SmoothingCode);

        return file.Kind switch
        {
            OverlayKind.Operation => state with
            {
                Operation = new OverlayOperationSettings(
                    file.SourceSlotA,
                    file.SourceCurveKeyA,
                    file.SourceSlotB,
                    file.SourceCurveKeyB,
                    file.Operation,
                    file.BlendFrequencyHz,
                    file.BlendWidthOctaves,
                    file.UseAmplitudeSpace,
                    file.TiltEnabled,
                    file.TiltDbPerOctave,
                    file.TiltPivotHz,
                    file.CompareDelayMs,
                    file.CompareInvertPolarity)
            },
            OverlayKind.Target => state with
            {
                Target = new OverlayTargetSettings(
                    file.TargetSourceSlot,
                    file.TargetPreset,
                    new TargetCurveSpec(
                        file.TargetTiltDbPerOctave,
                        file.TargetBassShelfGainDb,
                        file.TargetBassShelfFrequencyHz,
                        file.TargetBassShelfWidthOctaves,
                        file.TargetTrebleShelfGainDb,
                        file.TargetTrebleShelfFrequencyHz,
                        file.TargetTrebleShelfWidthOctaves,
                        file.TargetPresenceGainDb,
                        file.TargetPresenceFrequencyHz,
                        file.TargetPresenceWidthOctaves),
                    file.TargetToleranceDb,
                    file.TargetDeviationMode)
            },
            _ => state with { Captured = CapturedFromFile(file) }
        };
    }

    private static CapturedCurve CapturedFromFile(OverlayFile file) => new(
        file.Points.Select(point => new DataPoint(point.X, point.Y)).ToArray(),
        file.CapturedMagnitudeScale,
        CapturedYAxisKey(file),
        file.PhaseUnwrapped,
        file.CapturedCurveKind,
        file.RawSpectrum.Length >= 2
            ? file.RawSpectrum.Select(point => new SignalPoint(point.X, point.Y)).ToList()
            : null,
        file.RawCalibrationCorrectionDb.ToArray(),
        new MeasuredBand(file.MeasuredLowFrequencyHz, file.MeasuredHighFrequencyHz),
        file.PointsCalibrationCorrectionDb.ToArray(),
        file.CapturedSmoothingCode,
        file.SampleRateHz,
        file.RawImpulse.Length >= 2
            ? new ImpulseOverlayCapture(
                RawImpulseSamples(file),
                file.CapturedCurveKind ?? AnalysisCurveKind.Primary,
                file.RawImpulsePeakReference ?? 0.0,
                file.SampleRateHz ?? 0)
            : null);

    private static IReadOnlyList<SignalPoint> RawImpulseSamples(OverlayFile file)
    {
        SignalPoint[] samples = file.RawImpulse.Select(point => new SignalPoint(point.X, point.Y)).ToArray();
        return file.RawImpulseSignedLags
            ? samples
            : ImpulseOverlayLegacy.FromRecordStartIndices(
                samples, file.CapturedCurveKind ?? AnalysisCurveKind.Primary);
    }

    private static string? CapturedYAxisKey(OverlayFile file)
    {
        if (!string.IsNullOrEmpty(file.CapturedYAxisKey))
        {
            return file.CapturedYAxisKey;
        }

        return file.Mode is Mode.FrequencyResponse or Mode.PhaseResponse or Mode.GroupDelay or Mode.LiveSpectrum &&
            file.Title.Contains("Coherence", StringComparison.OrdinalIgnoreCase)
                ? PlotModelFactory.CoherenceAxisKey
                : null;
    }

    public OverlayFile ToFile(int slot)
    {
        var file = new OverlayFile
        {
            SavedAtUtc = DateTimeOffset.UtcNow,
            Mode = Mode,
            Slot = slot,
            Kind = Kind,
            Title = Title,
            CapturedMagnitudeScale = MagnitudeScale,
            Offset = (double)Offset,
            ColorArgb = Appearance.Color.ToArgb(),
            StrokeThickness = Appearance.StrokeThickness,
            LineStyle = Appearance.LineStyle,
            OpacityPercent = Appearance.OpacityPercent
        };
        file.SetSmoothingCode(SmoothingInverseOctaves);

        if (Operation is { } operation)
        {
            file.SourceSlotA = operation.SourceSlotA;
            file.SourceSlotB = operation.SourceSlotB;
            file.SourceCurveKeyA = operation.SourceCurveKeyA;
            file.SourceCurveKeyB = operation.SourceCurveKeyB;
            file.Operation = operation.Operation;
            file.BlendFrequencyHz = operation.BlendFrequencyHz;
            file.BlendWidthOctaves = operation.BlendWidthOctaves;
            file.UseAmplitudeSpace = operation.UseAmplitudeSpace;
            file.TiltEnabled = operation.TiltEnabled;
            file.TiltDbPerOctave = operation.TiltDbPerOctave;
            file.TiltPivotHz = operation.TiltPivotHz;
            file.CompareDelayMs = operation.CompareDelayMs;
            file.CompareInvertPolarity = operation.CompareInvertPolarity;
        }
        else if (Target is { } target)
        {
            TargetCurveSpec spec = target.Spec;
            file.TargetSourceSlot = target.SourceSlot;
            file.TargetPreset = target.Preset;
            file.TargetTiltDbPerOctave = spec.TiltDbPerOctave;
            file.TargetBassShelfGainDb = spec.BassShelfGainDb;
            file.TargetBassShelfFrequencyHz = spec.BassShelfFrequencyHz;
            file.TargetBassShelfWidthOctaves = spec.BassShelfWidthOctaves;
            file.TargetTrebleShelfGainDb = spec.TrebleShelfGainDb;
            file.TargetTrebleShelfFrequencyHz = spec.TrebleShelfFrequencyHz;
            file.TargetTrebleShelfWidthOctaves = spec.TrebleShelfWidthOctaves;
            file.TargetPresenceGainDb = spec.PresenceGainDb;
            file.TargetPresenceFrequencyHz = spec.PresenceFrequencyHz;
            file.TargetPresenceWidthOctaves = spec.PresenceWidthOctaves;
            file.TargetToleranceDb = target.ToleranceDb;
            file.TargetDeviationMode = target.DeviationMode;
        }
        else if (Captured is { } captured)
        {
            file.Points = captured.Points
                .Select(point => new OverlayPoint(point.X, point.Y))
                .ToArray();
            file.PhaseUnwrapped = captured.PhaseUnwrapped;
            file.CapturedCurveKind = captured.CurveKind;
            file.RawSpectrum = captured.RawSpectrum != null
                ? captured.RawSpectrum.Select(point => new OverlayPoint(point.X, point.Y)).ToArray()
                : Array.Empty<OverlayPoint>();
            file.RawCalibrationCorrectionDb = (captured.RawCalibrationCorrectionDb ?? []).ToArray();
            file.MeasuredLowFrequencyHz = captured.MeasuredBand.LowestHz;
            file.MeasuredHighFrequencyHz = captured.MeasuredBand.HighestHz;
            file.PointsCalibrationCorrectionDb = (captured.PointsCalibrationCorrectionDb ?? []).ToArray();
            file.CapturedSmoothingCode = captured.BakedSmoothingCode;
            file.SampleRateHz = captured.SampleRateHz;
            file.RawImpulse = captured.Impulse is { } impulse
                ? impulse.Samples.Select(point => new OverlayPoint(point.X, point.Y)).ToArray()
                : Array.Empty<OverlayPoint>();
            file.RawImpulseSignedLags = captured.Impulse != null;
            file.RawImpulsePeakReference = captured.Impulse?.PeakReference;
            file.CapturedYAxisKey = captured.YAxisKey;
        }

        return file;
    }
}
