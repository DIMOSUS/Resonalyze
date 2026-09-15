using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>A phase curve plus its ±180° wrap verticals, drawn as a thinner dashed twin: a wrap is not a phase transition.</summary>
internal sealed record GatedPhaseCurve(
    string Title,
    OxyColor Color,
    double Thickness,
    List<SignalPoint> Points,
    List<SignalPoint> WrapSegments);

/// <summary>Shared by the Virtual DSP and EQ Wizard phase views so a curve is identical in both.</summary>
internal static class GatedPhaseCurves
{
    /// <param name="impulseResponse">PROCESSED response; gate offsets are absolute times from sample 0.</param>
    /// <param name="gate">Offset and detrend are overwritten from the set-wide values resolved by <see cref="PhaseGatePlacement"/>.</param>
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

    /// <summary>From an already-gated spectrum (the Virtual DSP Sum is the vector sum of these).</summary>
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
                // Geometric mean = visual midpoint on the log-frequency axis.
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
