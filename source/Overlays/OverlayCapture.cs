using OxyPlot;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Turns a curve on the plot, or a text file's curve, into what a captured slot keeps.</summary>
internal static class OverlayCapture
{
    /// <param name="seedSmoothing">The display smoothing the slot starts at; a no-raw curve already carries its own.</param>
    public static CapturedCurve FromSeries(LineSeries selected, OverlayPlotSources sources, out int seedSmoothing)
    {
        CurveTag? tag = selected.Tag as CurveTag;

        // Prefer the raw reference so the overlay's own smoothing starts from true data; no-raw curves capture as drawn.
        RawCurveCapture? raw = tag != null ? sources.TryGetRawCapture(tag) : null;
        ImpulseOverlayCapture? impulse = tag != null ? sources.TryGetImpulseCapture(tag) : null;
        DataPoint[] points;
        List<SignalPoint>? spectrum;
        double[] calibrationCorrectionDb;
        double[] pointsCorrectionDb;
        // Smoothing baked into the points, distinct from display smoothing. Null when unknown, so consumers do not smooth twice.
        int? bakedSmoothing;
        MeasuredBand measuredBand = raw?.Band ?? default;
        if (raw is { } rawCapture && rawCapture.Spectrum.Count >= 2)
        {
            spectrum = rawCapture.Spectrum as List<SignalPoint> ?? rawCapture.Spectrum.ToList();
            calibrationCorrectionDb = rawCapture.CalibrationCorrectionDb.ToArray();
            points = OverlayCurves.SmoothRawSpectrum(spectrum, calibrationCorrectionDb, 0, measuredBand);
            pointsCorrectionDb = Array.Empty<double>();
            seedSmoothing = rawCapture.SmoothingCode;
            bakedSmoothing = 0;
        }
        else
        {
            points = new DataPoint[selected.Points.Count];
            selected.Points.CopyTo(points);
            spectrum = null;
            calibrationCorrectionDb = Array.Empty<double>();
            // No raw form: the drawn points are the reference, so freeze their baked correction per point.
            pointsCorrectionDb = raw is { } describedCapture
                ? RawCurveRenderer.CaptureCalibrationCorrectionAt(describedCapture.PointsCalibration, points)
                : Array.Empty<double>();
            // Source smoothing is already baked in; applying it again would compound.
            seedSmoothing = 0;
            bakedSmoothing = raw?.SmoothingCode;
        }

        return new CapturedCurve(
            points,
            sources.CurrentMagnitudeScale,
            string.IsNullOrEmpty(selected.YAxisKey) ? null : selected.YAxisKey,
            tag?.PhaseUnwrapped,
            tag?.Kind,
            spectrum,
            calibrationCorrectionDb,
            measuredBand,
            pointsCorrectionDb,
            bakedSmoothing,
            // Rate describes the measurement, so it survives a fallback capture.
            raw?.SampleRateHz ?? impulse?.SampleRateHz,
            impulse);
    }

    public static CapturedCurve FromText(OverlayTextCurve imported, MagnitudeScale shownScale) => new(
        imported.Points.Select(point => new DataPoint(point.X, point.Y)).ToArray(),
        imported.Metadata.Scale ?? shownScale,
        CurveKind: imported.Metadata.CurveKind,
        RawCalibrationCorrectionDb: Array.Empty<double>(),
        PointsCalibrationCorrectionDb: Array.Empty<double>(),
        SampleRateHz: imported.Metadata.SampleRateHz);

    /// <summary>Only a measured response enters a captured slot, which has no role field to carry anything else.</summary>
    public static string? RefusalFor(OverlayTextCurve imported) =>
        imported.Metadata.Role is { } role && role != OverlayCurveRole.Response
            ? "This file holds a " + DescribeRole(role) + " curve, not a " +
              "measured response, so it cannot be imported as an overlay. Import the " +
              "response it was derived from instead."
            : null;

    private static string DescribeRole(OverlayCurveRole role) => role switch
    {
        OverlayCurveRole.Deviation => "deviation",
        OverlayCurveRole.EqCorrection => "EQ-correction",
        OverlayCurveRole.Target => "target",
        OverlayCurveRole.Calculated => "calculated",
        _ => "derived"
    };

    public static string CandidateTitle(LineSeries series) =>
        string.IsNullOrWhiteSpace(series.Title) ? "Untitled curve" : series.Title;

    // The slot kind decides the role, so a derived shape cannot re-enter as a measured response via text.
    public static OverlayTextMetadata ExportMetadata(OverlaySlotState state) =>
        OverlayTextFile.BuildCurveMetadata(
            state.Kind,
            state.Captured?.CurveKind,
            state.MagnitudeScale,
            state.Captured?.SampleRateHz,
            state.Title);

    public static OverlayTextMetadata DeviationMetadata(OverlaySlotState state, OverlayDeviationExport export) =>
        new(
            Role: export.Role,
            Scale: MagnitudeScale.Relative,
            SampleRateHz: state.Captured?.SampleRateHz,
            Title: $"{state.Title} - {export.Suffix}");

    public static string SanitizeFileName(string title)
    {
        string trimmed = string.IsNullOrWhiteSpace(title) ? "overlay" : title.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(invalid, '_');
        }

        return trimmed;
    }
}
