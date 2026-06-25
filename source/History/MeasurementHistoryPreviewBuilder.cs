using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.History;

internal static class MeasurementHistoryPreviewBuilder
{
    public const int PreviewWindow = 4096;
    public const int PreviewLeftTukey = 256;
    public const int PreviewRightTukey = 256;
    public const int PreviewSmoothingInverseOctaves = 2;

    private static readonly FrequencyResponseOptions PreviewOptions = new()
    {
        Window = PreviewWindow,
        LeftTukeyWindow = PreviewLeftTukey,
        RightTukeyWindow = PreviewRightTukey,
        SmoothingInverseOctaves = PreviewSmoothingInverseOctaves,
        Offset = 0,
        UseCalibration = false
    };

    private static readonly CalibrationFile EmptyCalibration = new(string.Empty);

    public static MeasurementHistoryPreview Build(
        Complex[] sweepDeconvolutionImpulseResponse,
        int sweepPeakIndex,
        int sampleRate,
        SweepMeasurementMode measurementMode,
        Complex[]? transferImpulseResponse,
        int? transferPeakIndex)
    {
        IImpulseMeasurement measurement = measurementMode == SweepMeasurementMode.LoopbackTransfer &&
            transferImpulseResponse is { Length: > 0 } transfer &&
            transferPeakIndex.HasValue
                ? new ImpulseMeasurementView(
                    transfer,
                    transferPeakIndex.Value,
                    sampleRate)
                : new ImpulseMeasurementView(
                    sweepDeconvolutionImpulseResponse,
                    sweepPeakIndex,
                    sampleRate);

        AnalysisCurve curve = DataHelper.GetSpectrum(
            measurement,
            PreviewOptions,
            EmptyCalibration,
            includePrimary: true,
            includeHarmonics: false)[0];

        List<double> frequencies = [];
        List<double> magnitudesDb = [];
        foreach (SignalPoint point in curve.Points)
        {
            if (!double.IsFinite(point.X) ||
                !double.IsFinite(point.Y) ||
                point.X <= 0)
            {
                continue;
            }

            frequencies.Add(point.X);
            magnitudesDb.Add(point.Y);
        }

        return new MeasurementHistoryPreview
        {
            Window = PreviewWindow,
            LeftTukeyWindow = PreviewLeftTukey,
            RightTukeyWindow = PreviewRightTukey,
            SmoothingInverseOctaves = PreviewSmoothingInverseOctaves,
            Frequencies = frequencies.ToArray(),
            MagnitudesDb = magnitudesDb.ToArray()
        };
    }
}
