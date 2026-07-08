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

    public string? ImpulseResponseFileName => impulseResponseFileName;

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

    // All analysis (magnitude, phase, group delay, impulse, decays) is derived from the
    // loopback transfer IR, which is now mandatory for every measurement. Callers must gate
    // on HasTransferImpulseResponse; the sweep deconvolution is reserved for harmonics/noise.
    public IImpulseMeasurement CreatePrimaryMeasurement()
    {
        MeasurementImpulseResponse transfer = expSweepMeasurement.Transfer
            ?? throw new InvalidOperationException(
                "Transfer impulse response is not available.");
        return new ImpulseMeasurementView(
            transfer.ImpulseResponse,
            transfer.PeakIndex,
            expSweepMeasurement.SampleRate);
    }

    public IImpulseMeasurement CreateSweepDeconvolutionMeasurement()
    {
        MeasurementImpulseResponse sweepDeconvolution = expSweepMeasurement.SweepDeconvolution
            ?? throw new InvalidOperationException(
                "Sweep deconvolution impulse response is not available.");
        return new ImpulseMeasurementView(
            sweepDeconvolution.ImpulseResponse,
            sweepDeconvolution.PeakIndex,
            expSweepMeasurement.SampleRate,
            expSweepMeasurement.HarmonicIROffset);
    }

    // Magnitude comes from the transfer IR (referenced by loopback, free of DAC/amp
    // colouration); harmonics and THD+N come from the sweep deconvolution, which is the
    // only representation that carries the harmonic time offsets.
    public IReadOnlyList<AnalysisCurve> CreateFrequencyResponseCurves(
        FrequencyResponseOptions options,
        CalibrationFile? calibration,
        SpectrumCurves curves)
    {
        // The primary comes from a cleaner primary measurement, the harmonics from
        // the sweep deconvolution; mask the requested set per computational scope.
        var result = new List<AnalysisCurve>();
        result.AddRange(DataHelper.GetSpectrum(
            CreatePrimaryMeasurement(),
            options,
            calibration,
            curves & SpectrumCurves.Primary));

        result.AddRange(DataHelper.GetSpectrum(
            CreateSweepDeconvolutionMeasurement(),
            options,
            calibration,
            curves & SpectrumCurves.Harmonics));

        return result;
    }
}
