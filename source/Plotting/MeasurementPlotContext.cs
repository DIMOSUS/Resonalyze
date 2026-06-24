using System.Numerics;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

internal sealed class MeasurementPlotContext
{
    private readonly ExpSweepMeasurement expSweepMeasurement;
    private string? impulseResponseFileName;

    public MeasurementPlotContext(ExpSweepMeasurement expSweepMeasurement)
    {
        this.expSweepMeasurement = expSweepMeasurement;
    }

    public void SetImpulseResponseFileName(string? fileName)
    {
        impulseResponseFileName = string.IsNullOrWhiteSpace(fileName)
            ? null
            : Path.GetFileName(fileName);
    }

    public string CreateTitle(string baseTitle) =>
        string.IsNullOrWhiteSpace(impulseResponseFileName)
            ? baseTitle
            : $"{baseTitle} - {impulseResponseFileName}";

    public bool CanIncludeCurves(bool includeCurves) =>
        includeCurves &&
        expSweepMeasurement.HasImpulseResponse &&
        !expSweepMeasurement.InProgress;

    public bool HasTransferImpulseResponse =>
        expSweepMeasurement.TransferImpulseResponse is { Length: > 0 };

    public IImpulseMeasurement CreatePrimaryMeasurement()
    {
        if (expSweepMeasurement.TransferImpulseResponse is { Length: > 0 } transferIr)
        {
            return new ImpulseMeasurementView(
                transferIr,
                expSweepMeasurement.TransferPeakIndex,
                expSweepMeasurement.SampleRate);
        }

        return CreateSweepDeconvolutionMeasurement();
    }

    public IImpulseMeasurement CreateSweepDeconvolutionMeasurement()
    {
        Complex[] sweepIr = expSweepMeasurement.SweepDeconvolutionImpulseResponse
            ?? throw new InvalidOperationException(
                "Sweep deconvolution impulse response is not available.");
        return new ImpulseMeasurementView(
            sweepIr,
            expSweepMeasurement.SweepDeconvolutionPeakIndex,
            expSweepMeasurement.SampleRate,
            expSweepMeasurement.HarmonicIROffset);
    }

    public IReadOnlyList<AnalysisCurve> CreateFrequencyResponseCurves(
        FrequencyResponseOptions options,
        CalibrationFile calibration)
    {
        IImpulseMeasurement sweepMeasurement = CreateSweepDeconvolutionMeasurement();
        if (!HasTransferImpulseResponse)
        {
            return DataHelper.GetSpectrum(
                sweepMeasurement,
                options,
                calibration);
        }

        var curves = new List<AnalysisCurve>();
        curves.AddRange(DataHelper.GetSpectrum(
            sweepMeasurement,
            options,
            calibration,
            includePrimary: true,
            includeHarmonics: false));

        curves.AddRange(DataHelper.GetSpectrum(
            sweepMeasurement,
            options,
            calibration,
            includePrimary: false,
            includeHarmonics: true));

        return curves;
    }
}
