using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Which overlay slots may be drawn on the magnitude axis currently shown.
/// </summary>
/// <remarks>
/// The rule is asked twice — once when the user ticks a slot's checkbox, once for every
/// redraw of the plot — and the two used to state it separately. They drifted: a
/// calculated slot appeared when its settings dialog saved (which draws directly) yet
/// refused the checkbox, and any redraw dropped it again. One predicate, both callers.
/// </remarks>
internal static class OverlayMagnitudeScale
{
    /// <summary>
    /// A CAPTURED curve carries the scale it was measured on and belongs to that axis
    /// alone: an SPL capture drawn on the dBr axis would read as an ~80 dB error. A
    /// CALCULATED slot — an operation or a target — has no absolute scale of its own. It
    /// is recomputed from whatever is on the plot, and the slot's offset is what places
    /// it (a difference of two SPL captures is a handful of dB that the user lifts onto
    /// the SPL axis), so neither axis excludes it. Outside the magnitude mode there is
    /// no such axis and nothing to match.
    /// </summary>
    public static bool Draws(
        Mode seriesMode,
        OverlayKind kind,
        MagnitudeScale capturedScale,
        MagnitudeScale shownScale) =>
        seriesMode != Mode.FrequencyResponse ||
        kind != OverlayKind.Captured ||
        capturedScale == shownScale;
}
