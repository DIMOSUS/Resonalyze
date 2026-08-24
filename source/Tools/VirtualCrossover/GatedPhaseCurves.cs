using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// One phase curve ready to draw: the trace itself, plus the ±180° wrap verticals
/// the caller draws as a thinner dashed twin under it.
/// </summary>
/// <remarks>
/// The two are separate because a wrap is not a phase transition: drawn at full
/// stroke it reads as one, and drawn not at all the curve looks like it jumps for
/// no reason.
/// </remarks>
internal sealed record GatedPhaseCurve(
    string Title,
    OxyColor Color,
    double Thickness,
    List<SignalPoint> Points,
    List<SignalPoint> WrapSegments);

/// <summary>
/// Reads a gated phase curve out of an analysis spectrum. Shared by the Virtual DSP
/// phase view and the EQ Wizard's, so a curve drawn in one is the same curve in the
/// other — including where it breaks at a wrap.
/// </summary>
internal static class GatedPhaseCurves
{
    /// <summary>
    /// The gated phase of one channel, in degrees, referenced to an absolute τ.
    /// </summary>
    /// <param name="impulseResponse">
    /// The channel's PROCESSED response — its chain already applied. Gate offsets are
    /// absolute times from sample 0, so the view is built on that origin rather than
    /// on any peak.
    /// </param>
    /// <param name="gate">
    /// The window: mode, FDW cycles and durations. Its offset and detrend are
    /// overwritten here from <paramref name="gateOffsetMs"/> and
    /// <paramref name="detrendMs"/>, which the caller resolved for the whole channel
    /// set (see <see cref="PhaseGatePlacement"/>).
    /// </param>
    public static GatedPhaseCurve Read(
        Complex[] impulseResponse,
        int sampleRate,
        PhaseAnalysisSettings gate,
        double gateOffsetMs,
        double detrendMs,
        string title,
        OxyColor color,
        double thickness)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        ArgumentNullException.ThrowIfNull(gate);

        Complex[] spectrum = DataHelper.GetPhaseAnalysisSpectrum(
            new ImpulseMeasurementView(impulseResponse, 0, sampleRate),
            gate with
            {
                GateOffsetMs = gateOffsetMs,
                DetrendMode = PhaseDetrendMode.Manual,
                ManualDetrendMilliseconds = detrendMs
            },
            out int extractionStart);

        return Read(
            spectrum,
            extractionStart,
            detrendMs * sampleRate / 1_000.0,
            sampleRate,
            title,
            color,
            thickness);
    }

    /// <summary>
    /// The same read from an already-gated spectrum — for callers that build the
    /// spectra themselves because something else needs them too (the Virtual DSP
    /// Sum is the vector sum of exactly these).
    /// </summary>
    public static GatedPhaseCurve Read(
        Complex[] spectrum,
        int extractionStart,
        double referenceSamples,
        int sampleRate,
        string title,
        OxyColor color,
        double thickness)
    {
        (List<SignalPoint> points, List<SignalPoint> wrapSegments) = SplitWrapSegments(
            DataHelper.GetGatedPhaseData(
                spectrum, extractionStart, referenceSamples, sampleRate, unwrap: false));
        return new GatedPhaseCurve(title, color, thickness, points, wrapSegments);
    }

    // Wrapped phase jumps from +180° to −180° between adjacent bins. The main curve
    // breaks at the wrap (NaN) so the jump does not read as a real phase transition
    // drawn at full stroke; the jump itself goes into WrapSegments — NaN-separated
    // two-point verticals the caller draws as a thinner dashed twin, keeping the
    // wrap visible.
    private static (List<SignalPoint> Points, List<SignalPoint> WrapSegments)
        SplitWrapSegments(List<SignalPoint> phase)
    {
        var points = new List<SignalPoint>(phase.Count);
        var wrapSegments = new List<SignalPoint>();
        SignalPoint? previous = null;
        foreach (SignalPoint point in phase)
        {
            if (point.X is < 20 or > 20_000)
            {
                continue;
            }

            var current = new SignalPoint(point.X, point.Y / Math.PI * 180.0);
            if (previous is { } before && !double.IsNaN(before.Y) &&
                !double.IsNaN(current.Y) &&
                Math.Abs(current.Y - before.Y) > 180.0)
            {
                points.Add(new SignalPoint(point.X, double.NaN));
                // Strictly vertical, halfway between the two bins (geometric mean =
                // the visual midpoint on the log-frequency axis).
                double wrapHz = Math.Sqrt(before.X * current.X);
                wrapSegments.Add(new SignalPoint(wrapHz, before.Y));
                wrapSegments.Add(new SignalPoint(wrapHz, current.Y));
                wrapSegments.Add(new SignalPoint(wrapHz, double.NaN));
            }

            points.Add(current);
            previous = current;
        }

        return (points, wrapSegments);
    }
}
