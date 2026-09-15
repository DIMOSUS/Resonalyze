using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Scale and Y axis an overlay's stored numbers are stated on; travels through operations verbatim.</summary>
internal readonly record struct OverlayCurveSemantics(
    MagnitudeScale? Scale,
    string? YAxisKey)
{
    /// <summary>No absolute level and no own axis (targets, complex sum).</summary>
    public static OverlayCurveSemantics None => default;

    /// <summary>A live curve states the scale showing now; the scale is dropped on its own axis (coherence is 0…1).</summary>
    public static OverlayCurveSemantics ForCurve(
        MagnitudeScale scale,
        string? yAxisKey) =>
        new(
            yAxisKey == PlotModelFactory.CoherenceAxisKey ? null : scale,
            yAxisKey);

    public bool IsDecibels => YAxisKey != PlotModelFactory.CoherenceAxisKey;

    /// <summary>An absolute level draws only on its own axis (~80 dB SPL is not ~80 dB relative gain).</summary>
    public bool DrawsOn(Mode seriesMode, MagnitudeScale shownScale) =>
        seriesMode != Mode.FrequencyResponse ||
        Scale is not { } scale ||
        scale == shownScale;

    /// <summary>Unstated halves say nothing; two stated, different answers are incompatible.</summary>
    public static bool AreCompatible(OverlayCurveSemantics a, OverlayCurveSemantics b) =>
        (a.Scale is not { } scaleA || b.Scale is not { } scaleB || scaleA == scaleB) &&
        (a.YAxisKey is not { } axisA || b.YAxisKey is not { } axisB || axisA == axisB);

    public static OverlayOperationResult ForOperation(
        OverlayOperation operation,
        OverlayCurveSemantics a,
        OverlayCurveSemantics b)
    {
        // Rebuilt from the transfer IRs without the plot's SPL lift: states nothing.
        if (operation is OverlayOperation.ComplexSum or OverlayOperation.ComplexSumLoss)
        {
            return OverlayOperationResult.Of(None);
        }

        if (operation == OverlayOperation.CurveA)
        {
            return OverlayOperationResult.Of(a);
        }

        if (!AreCompatible(a, b))
        {
            return OverlayOperationResult.Undefined;
        }

        // A difference cancels the absolute level; other operations inherit it.
        bool cancelsLevel = operation
            is OverlayOperation.AMinusB
            or OverlayOperation.BMinusA
            or OverlayOperation.AbsoluteDifference;
        return OverlayOperationResult.Of(new OverlayCurveSemantics(
            cancelsLevel ? null : a.Scale ?? b.Scale,
            a.YAxisKey ?? b.YAxisKey));
    }
}

/// <remarks>Distinct from a null Scale ("any magnitude axis"): merging them would draw SPL minus relative on both axes.</remarks>
internal readonly record struct OverlayOperationResult(
    bool IsDefined,
    OverlayCurveSemantics Curve)
{
    public static OverlayOperationResult Undefined => default;

    public static OverlayOperationResult Of(OverlayCurveSemantics curve) =>
        new(true, curve);
}
