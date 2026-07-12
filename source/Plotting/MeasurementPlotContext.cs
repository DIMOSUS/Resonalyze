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
    // colouration); harmonic distortion comes from the sweep deconvolution, whose
    // linear packet is the denominator every HDn/THD ratio is measured against.
    // Twice the primary's fractional-octave width: harmonic curves are noisier.
    private const double HarmonicSmoothingWidthFactor = 2.0;

    /// <summary>
    /// Isolation warnings from the last frequency-response build: an order dropped
    /// or drawn caveated because its harmonic packet overlaps a neighbour. Exposed
    /// so the plot layer can explain a missing/marginal HD curve to the user
    /// (increase the sweep duration) rather than leaving it silently absent.
    /// </summary>
    public IReadOnlyList<string> DistortionWarnings { get; private set; } = Array.Empty<string>();

    public IReadOnlyList<AnalysisCurve> CreateFrequencyResponseCurves(
        FrequencyResponseOptions options,
        CalibrationFile? calibration,
        SpectrumCurves curves)
    {
        var result = new List<AnalysisCurve>();
        result.AddRange(DataHelper.GetSpectrum(
            CreatePrimaryMeasurement(),
            options,
            calibration,
            curves & SpectrumCurves.Primary));

        result.AddRange(CreateDistortionCurves(options, calibration, curves));
        return result;
    }

    private IReadOnlyList<AnalysisCurve> CreateDistortionCurves(
        FrequencyResponseOptions options,
        CalibrationFile? calibration,
        SpectrumCurves curves)
    {
        DistortionWarnings = Array.Empty<string>();
        if ((curves & SpectrumCurves.Harmonics) == 0 ||
            expSweepMeasurement.SweepDeconvolution is not { } deconvolution ||
            expSweepMeasurement.Sweep is not { SweepSamples: > 0 } sweep ||
            expSweepMeasurement.Octaves <= 0)
        {
            return Array.Empty<AnalysisCurve>();
        }

        var sweepMetadata = EssSweepMetadata.FromExponentialSweep(
            expSweepMeasurement.SampleRate,
            expSweepMeasurement.Octaves,
            sweep.SweepSamples,
            deconvolution.PeakIndex);

        Complex[] impulse = deconvolution.ImpulseResponse;
        double[] real = new double[impulse.Length];
        for (int i = 0; i < impulse.Length; i++)
        {
            real[i] = impulse[i].Real;
        }

        // Noise / THD+N is intentionally OFF: the current noise term's absolute
        // level is tied to the linear-packet window bandwidth (set by the sweep
        // geometry), so the curve stays the well-defined THD until the noise
        // estimate is reworked as a bandwidth-normalized PSD.
        var distortionOptions = new DistortionOptions(
            SmoothingOctaves: options.SmoothingInverseOctaves > 0
                ? HarmonicSmoothingWidthFactor / options.SmoothingInverseOctaves
                : 0.0,
            IncludeNoise: false);

        EssDistortion.DistortionCurveResult distortion =
            EssDistortion.ComputeDistortionCurvesResult(
                real,
                sweepMetadata,
                distortionOptions,
                options.UseCalibration ? calibration : null,
                curves & SpectrumCurves.Harmonics);
        DistortionWarnings = distortion.Warnings;
        return distortion.Curves;
    }
}
