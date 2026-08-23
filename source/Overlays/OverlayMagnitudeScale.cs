using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Which overlay slots may be drawn on the magnitude axis currently shown.
/// </summary>
/// <remarks>
/// The rule is asked on every path that draws a slot — the checkbox, the redraw after
/// each plot rebuild, the restore after a mode switch — and those used to state it
/// separately. They drifted: a calculated slot appeared when its settings dialog saved
/// (which draws directly) yet refused the checkbox, and any redraw dropped it again. One
/// predicate, every caller.
/// </remarks>
internal static class OverlayMagnitudeScale
{
    /// <summary>
    /// Whether a slot carrying <paramref name="slotScale"/> may draw on the axis showing
    /// <paramref name="shownScale"/>. A curve that carries an absolute level belongs to
    /// the axis that level is stated on — an SPL reading drawn on the relative axis would
    /// pass an ~80 dB pressure off as ~80 dB of relative gain. A null scale means the
    /// curve states no absolute level at all, and no axis excludes it. Outside the
    /// magnitude mode there is no such axis and nothing to match.
    /// </summary>
    public static bool Draws(Mode seriesMode, MagnitudeScale? slotScale, MagnitudeScale shownScale) =>
        seriesMode != Mode.FrequencyResponse ||
        slotScale is not { } scale ||
        scale == shownScale;

    /// <summary>
    /// The scale a calculated result carries, or null when it carries none. A difference
    /// or a ratio cancels the absolute level — the same number of decibels whichever axis
    /// its operands were measured on — while a pass-through, a sum, an average or a blend
    /// reproduces that level and inherits it. The operands are read as this slot draws
    /// them: a captured operand states the scale it was measured on, a live one is
    /// whatever the axis is showing right now and constrains nothing.
    /// </summary>
    /// <remarks>
    /// The complex sum states no scale here: it is rebuilt from the two transfer impulse
    /// responses and never takes the SPL lift the plot applies to its own curves, so it
    /// is a relative reading placed, like a target shape, by the slot's offset.
    /// </remarks>
    public static MagnitudeScale? ForOperation(
        OverlayOperation operation,
        MagnitudeScale? operandA,
        MagnitudeScale? operandB) => operation switch
    {
        OverlayOperation.AMinusB or
        OverlayOperation.BMinusA or
        OverlayOperation.AbsoluteDifference or
        OverlayOperation.ComplexSumLoss or
        OverlayOperation.ComplexSum => null,
        _ => operandA ?? operandB
    };
}
