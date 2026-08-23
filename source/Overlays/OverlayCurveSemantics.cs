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
    /// and no axis of its own (so it draws on the mode's main one). A target shape, a
    /// complex sum and a live operand all state this.
    /// </summary>
    public static OverlayCurveSemantics None => default;

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
    /// What the result of <paramref name="operation"/> states, given what its operands
    /// do. <paramref name="b"/> is <see cref="None"/> when the operation reads one curve.
    /// </summary>
    public static OverlayCurveSemantics ForOperation(
        OverlayOperation operation,
        OverlayCurveSemantics a,
        OverlayCurveSemantics b)
    {
        // The complex sum and its loss take no operands at all — they are rebuilt from
        // the two transfer impulse responses, and never take the SPL lift the plot
        // applies to its own curves, so they state nothing and draw on the main axis.
        if (operation is OverlayOperation.ComplexSum or OverlayOperation.ComplexSumLoss)
        {
            return None;
        }

        bool readsB = operation != OverlayOperation.CurveA;

        // The axis travels with the numbers: "A only" inherits A's, and an operation
        // between two curves keeps it only while both share it — coherence minus
        // coherence is still coherence, coherence minus decibels is neither.
        string? yAxisKey = !readsB || a.YAxisKey == b.YAxisKey
            ? a.YAxisKey
            : null;

        // A difference cancels the absolute level: the same number of decibels whichever
        // axis its operands were measured on, which is what lets the difference of two
        // dB SPL captures be lifted onto either axis by the slot's offset. Every other
        // operation reproduces that level and inherits it.
        MagnitudeScale? scale = operation switch
        {
            OverlayOperation.AMinusB or
            OverlayOperation.BMinusA or
            OverlayOperation.AbsoluteDifference => null,
            _ when !readsB => a.Scale,
            // An operand stating nothing (a live curve) leaves the other to decide.
            // Two operands stating DIFFERENT scales have no axis on which both halves
            // are true — their sum is a quantity in neither — so the result states none
            // rather than claiming one of them.
            _ => (a.Scale, b.Scale) switch
            {
                (null, var other) => other,
                (var one, null) => one,
                var (one, other) => one == other ? one : null
            }
        };

        return new OverlayCurveSemantics(scale, yAxisKey);
    }
}
