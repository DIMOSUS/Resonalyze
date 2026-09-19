using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The curve the wizard equalizes, read from the session's source through its calibration and smoothing. A spatial
/// average IS the magnitude when present (the IR only feeds phase); stored curves keep their NaN gaps for the fitter.
/// </summary>
internal static class EqWizardSourceCurve
{
    public const string Title = "Source";
    public const string CorrectedTitle = "Source + EQ";

    public static readonly OxyColor Color = UiPalette.CurveSource.ToOxy();
    public static readonly OxyColor CorrectedColor = UiPalette.CurveSourcePlusEq.ToOxy();

    /// <summary>The bare curve; null without a source or with fewer than two usable points.</summary>
    public static EqWizardCurve? Compute(EqWizardSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Source is not { } source)
        {
            return null;
        }

        IReadOnlyList<SignalPoint> points =
            source.SpatialAverage != null ? SpatialAverageCurve(session, source)
            : source.Measurement != null ? ImpulseResponseSpectrum(session, source)
            : ImportedCurve(session, source);
        List<DataPoint> result = ToPlotPoints(points, KeepsGaps(source));
        return result.Count >= 2
            ? new EqWizardCurve(Title, Color, 1.5, LineStyle.Solid, result)
            : null;
    }

    /// <summary>Captured on the UI thread so the render touches no control and no mutable state.</summary>
    public static EqWizardGatedPreviewRequest GatedPreviewRequest(
        EqWizardSession session,
        EqWizardCurveSource source,
        EqualizationCurve? bank) =>
        new(
            source.PreviewImpulseResponse!,
            source.PreviewChain!,
            bank,
            source.Measurement!.PeakIndex,
            source.Measurement.SampleRate,
            session.ProcessorSampleRateHz,
            source.GateSettings!,
            session.ResolveCalibration(),
            session.SourceSmoothingInverseOctaves,
            new MeasuredBand(
                source.Measurement.LowestMeasuredFrequencyHz,
                source.Measurement.HighestMeasuredFrequencyHz));

    /// <summary>
    /// Channel magnitude from its spatial average through its chain, with the edited bank substituted INTO the chain
    /// (smoothing does not commute with the bank). See docs/tech/eq-auto-tuner.md#spatial-average-sources.
    /// </summary>
    public static IReadOnlyList<SignalPoint> SpatialAverageCurve(
        EqWizardSession session,
        EqWizardCurveSource source,
        EqualizationCurve? bank = null)
    {
        LiveCaptureDocument document = source.SpatialAverage!;
        List<double> grid = document.ToCurvePoints()
            .Select(point => point.X)
            .ToList();
        List<SignalPoint>? curve = SpatialAverageHybrid.BuildChannelCurve(
            document,
            (source.PreviewChain ?? DspChannelChain.Identity) with { Peq = bank },
            session.ProcessorSampleRateHz,
            // Pinned to the panel's calibration MODE, not only its curve, like every part of a handoff.
            session.SpatialAverageCalibrationFor(source),
            grid,
            session.SourceSmoothingInverseOctaves);
        if (curve == null)
        {
            return Array.Empty<SignalPoint>();
        }

        // The set's scalar offset last, so the curve hangs where the panel plotted it and Target Level means the same.
        double offset = source.SpatialAverageOffsetDb;
        return offset == 0
            ? curve
            : curve.Select(point => new SignalPoint(point.X, point.Y + offset)).ToList();
    }

    /// <summary>
    /// The single conversion to plot points: curves are paired BY INDEX (target, shading, fit), so every render must
    /// keep or drop the same gaps. See docs/tech/eq-auto-tuner.md#index-aligned-curves.
    /// </summary>
    public static List<DataPoint> ToPlotPoints(
        IReadOnlyList<SignalPoint> points,
        bool keepGaps)
    {
        var result = new List<DataPoint>(points.Count);
        foreach (SignalPoint point in points)
        {
            if (!double.IsFinite(point.X) || point.X <= 0)
            {
                continue;
            }
            if (!double.IsFinite(point.Y) && !keepGaps)
            {
                continue;
            }

            result.Add(new DataPoint(point.X, point.Y));
        }

        return result;
    }

    /// <summary>Measured curves keep NaN gaps (untrusted bands); a computed FR drops non-finite values. Decided per SOURCE, not call site.</summary>
    public static bool KeepsGaps(EqWizardCurveSource source) =>
        source.SpatialAverage != null || source.Measurement == null;

    private static IReadOnlyList<SignalPoint> ImpulseResponseSpectrum(
        EqWizardSession session,
        EqWizardCurveSource source)
    {
        // Same DataHelper call, template and offset as the DSP panel's magnitude view; the bare curve is the no-bank path.
        if (source.IsGated)
        {
            return EqWizardGatedPreview.Render(GatedPreviewRequest(session, source, bank: null));
        }

        // The Virtual DSP steady-state window (ms), realised in samples at this rate; zero-padded when the IR is shorter.
        (int window, int leftTukey, int rightTukey) =
            FrequencyResponseOptions.SteadyStateWindowSamples(
                source.Measurement!.SampleRate);
        var options = new FrequencyResponseOptions
        {
            Window = window,
            LeftTukeyWindow = leftTukey,
            RightTukeyWindow = rightTukey,
            SmoothingInverseOctaves = session.SourceSmoothingInverseOctaves,
            Offset = 0,
            // Only a configured calibration applies to a computed FR; "own" belongs to imported curves.
            CalibrationId = session.CalibrationChoice.MicrophoneCalibrationId
        };

        IReadOnlyList<AnalysisCurve> curves = DataHelper.GetSpectrum(
            source.Measurement!, options, session.ResolveCalibration(), SpectrumCurves.Primary);
        return curves.Count > 0 ? curves[0].Points : Array.Empty<SignalPoint>();
    }

    // See docs/tech/eq-auto-tuner.md#imported-curve-calibration.
    private static IReadOnlyList<SignalPoint> ImportedCurve(
        EqWizardSession session,
        EqWizardCurveSource source)
    {
        if (source.RawSpectrum is not { Count: >= 2 } raw)
        {
            return EqWizardImportedCurve.Render(
                source.Points,
                source.PointsCalibrationCorrectionDb,
                PointsCalibrationCorrection(session, source),
                source.SupportsSmoothing ? session.SourceSmoothingInverseOctaves : 0);
        }

        return RawCurveRenderer.Render(
            raw,
            RawCalibrationCorrection(session, source),
            session.SourceSmoothingInverseOctaves,
            source.RawSpectrumBand);
    }

    private static IReadOnlyList<double> PointsCalibrationCorrection(
        EqWizardSession session,
        EqWizardCurveSource source)
    {
        EqWizardCalibrationChoice choice = session.CalibrationChoice;
        if (choice.Own)
        {
            return source.PointsCalibrationCorrectionDb;
        }

        return choice.IsOff
            ? Array.Empty<double>()
            : EqWizardImportedCurve.SampleCorrection(
                session.ResolveCalibration(),
                source.Points);
    }

    private static IReadOnlyList<double> RawCalibrationCorrection(
        EqWizardSession session,
        EqWizardCurveSource source)
    {
        EqWizardCalibrationChoice choice = session.CalibrationChoice;
        if (choice.Own)
        {
            return source.OwnCalibrationCorrectionDb;
        }

        return choice.IsOff
            ? Array.Empty<double>()
            : RawCurveRenderer.CaptureCalibrationCorrection(
                session.ResolveCalibration());
    }
}
