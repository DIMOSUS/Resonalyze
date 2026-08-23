using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// What a drawn overlay curve states about its own numbers: the magnitude scale they
/// are stated on, if any, and the Y axis they belong to.
/// </summary>
/// <remarks>
/// An overlay draws the values it stored, never values recomputed for the axis on
/// screen, so these travel with the curve — through an operation as well, which reuses
/// its operands' points verbatim. Both halves have been wrong in exactly that way: a
/// pass-through of a dB SPL capture drawn as relative gain, and a captured coherence
/// curve (0…1) drawn on the decibel axis because the operation dropped its axis key.
/// </remarks>
internal readonly record struct OverlayCurveSemantics(
    MagnitudeScale? Scale,
    string? YAxisKey)
{
    /// <summary>
    /// A curve stating nothing: no absolute level (so no magnitude axis excludes it)
    /// and no axis of its own (so it draws on the mode's main one). A target shape and
    /// a complex sum state this.
    /// </summary>
    public static OverlayCurveSemantics None => default;

    /// <summary>
    /// What a captured slot states. The magnitude scale it was captured under is
    /// dropped for a trace that lives on its own axis: coherence is a 0…1 ratio, and
    /// which magnitude axis the plot happened to be showing says nothing about it.
    /// </summary>
    public static OverlayCurveSemantics ForCapture(
        MagnitudeScale capturedScale,
        string? yAxisKey) =>
        new(
            yAxisKey == PlotModelFactory.CoherenceAxisKey ? null : capturedScale,
            yAxisKey);

    /// <summary>
    /// What a live plot curve states. It is re-read from the plot on every rebuild, so
    /// it is always on the axis showing and states no magnitude scale of its own — but
    /// it does carry which Y axis it is drawn against.
    /// </summary>
    public static OverlayCurveSemantics ForLiveCurve(string? yAxisKey) =>
        new(null, yAxisKey);

    /// <summary>Whether these numbers belong on a decibel axis at all.</summary>
    public bool IsDecibels => YAxisKey != PlotModelFactory.CoherenceAxisKey;

    /// <summary>
    /// Whether a curve stating this may draw on the axis showing
    /// <paramref name="shownScale"/>. A curve that carries an absolute level belongs to
    /// the axis that level is stated on — an SPL reading drawn on the relative axis
    /// would pass an ~80 dB pressure off as ~80 dB of relative gain. Outside the
    /// magnitude mode there is no such axis and nothing to match.
    /// </summary>
    public bool DrawsOn(Mode seriesMode, MagnitudeScale shownScale) =>
        seriesMode != Mode.FrequencyResponse ||
        Scale is not { } scale ||
        scale == shownScale;

    /// <summary>
    /// Whether two curves may be operated on at all. Each half may state nothing — a
    /// live curve states no scale, an imported or legacy capture no axis — and states
    /// nothing about compatibility either. Two STATED and different answers do: dB SPL
    /// against relative decibels, or coherence against decibels, is arithmetic between
    /// quantities that are not the same kind of number, and its result would be a curve
    /// no axis can honestly carry.
    /// </summary>
    public static bool AreCompatible(OverlayCurveSemantics a, OverlayCurveSemantics b) =>
        (a.Scale is not { } scaleA || b.Scale is not { } scaleB || scaleA == scaleB) &&
        (a.YAxisKey is not { } axisA || b.YAxisKey is not { } axisB || axisA == axisB);

    /// <summary>
    /// What the result of <paramref name="operation"/> states, given what its operands
    /// do — or <see cref="OverlayOperationResult.Undefined"/> when the two cannot be
    /// operated on. <paramref name="b"/> is <see cref="None"/> when the operation reads
    /// one curve.
    /// </summary>
    public static OverlayOperationResult ForOperation(
        OverlayOperation operation,
        OverlayCurveSemantics a,
        OverlayCurveSemantics b)
    {
        // The complex sum and its loss take no operands at all — they are rebuilt from
        // the two transfer impulse responses, and never take the SPL lift the plot
        // applies to its own curves, so they state nothing and draw on the main axis.
        if (operation is OverlayOperation.ComplexSum or OverlayOperation.ComplexSumLoss)
        {
            return OverlayOperationResult.Of(None);
        }

        // Curve A alone: whatever A states, the result states.
        if (operation == OverlayOperation.CurveA)
        {
            return OverlayOperationResult.Of(a);
        }

        if (!AreCompatible(a, b))
        {
            return OverlayOperationResult.Undefined;
        }

        // Compatible operands agree wherever both speak, so either one answers for the
        // axis; a difference additionally cancels the absolute level — the same number
        // of decibels whichever axis its operands were measured on, which is what lets
        // the difference of two dB SPL captures be lifted onto either axis by the slot's
        // offset. Every other operation reproduces that level and inherits it.
        bool cancelsLevel = operation
            is OverlayOperation.AMinusB
            or OverlayOperation.BMinusA
            or OverlayOperation.AbsoluteDifference;
        return OverlayOperationResult.Of(new OverlayCurveSemantics(
            cancelsLevel ? null : a.Scale ?? b.Scale,
            a.YAxisKey ?? b.YAxisKey));
    }
}

/// <summary>
/// The result of asking what an operation produces: either the curve semantics, or
/// nothing at all because the operands are not the same kind of number.
/// </summary>
/// <remarks>
/// Kept apart from a null <see cref="OverlayCurveSemantics.Scale"/>, which means
/// something quite different — "this curve states no absolute level, so any magnitude
/// axis may carry it". Reading the two as one would let dB SPL minus relative
/// decibels, a number that is neither, draw on both axes.
/// </remarks>
internal readonly record struct OverlayOperationResult(
    bool IsDefined,
    OverlayCurveSemantics Curve)
{
    public static OverlayOperationResult Undefined => default;

    public static OverlayOperationResult Of(OverlayCurveSemantics curve) =>
        new(true, curve);
}
