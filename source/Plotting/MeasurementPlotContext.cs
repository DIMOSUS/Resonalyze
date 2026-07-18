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

    /// <summary>
    /// The offset K that turns the loopback-referenced magnitude (dBr) into dB SPL:
    /// <c>K = loopbackPeakDbFs + calibrationOffsetDb</c>. Null when SPL cannot be
    /// shown — no calibration, no captured loopback level, or a calibration that does
    /// not belong to the input that produced this result.
    /// </summary>
    public double? SplOffsetDb
    {
        get
        {
            // The result's own frozen calibration, not the configured one, so a live
            // recalibration does not retroactively rescale the measurement on screen.
            if (expSweepMeasurement.MeasurementSplCalibration is not { } calibration)
            {
                return null;
            }

            InputLevelMeterEntry loopback = expSweepMeasurement.CurrentLevels.Loopback;
            if (!loopback.Available)
            {
                return null;
            }

            // Validate against the result's own input identity (a live snapshot, or a
            // loaded file's). A loaded file's anchor matches its own identity, so it is
            // trusted; a live anchor from a different input is refused.
            if (!expSweepMeasurement.InputMatches(calibration))
            {
                return null;
            }

            return loopback.PeakDbFs + calibration.OffsetDb;
        }
    }

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

    /// <summary>
    /// The oversampled primary spectrum an overlay stores so it can reproduce the
    /// mode's smoothing EXACTLY (the same <see cref="DataHelper.LogarithmicResample"/>)
    /// at any width, Off = raw. Calibration is baked in per input point (the mode
    /// applies it post-smoothing over a near-flat correction, so on the dense
    /// oversampled grid this is exact for no calibration and within rounding with it).
    /// Null when there is no transfer IR to analyze.
    /// </summary>
    public IReadOnlyList<SignalPoint>? CreateRawPrimarySpectrum(
        FrequencyResponseOptions options,
        CalibrationFile? calibration) =>
        HasTransferImpulseResponse
            ? BuildRawPrimarySpectrum(CreatePrimaryMeasurement(), options, calibration)
            : null;

    /// <summary>
    /// The oversampled primary spectrum with calibration baked in (when the options
    /// enable it), ready to be re-smoothed by <see cref="DataHelper.LogarithmicResample"/>.
    /// </summary>
    public static IReadOnlyList<SignalPoint> BuildRawPrimarySpectrum(
        IImpulseMeasurement measurement,
        FrequencyResponseOptions options,
        CalibrationFile? calibration)
    {
        List<SignalPoint> spectrum =
            DataHelper.GetOversampledPrimarySpectrum(measurement, options);
        CalibrationFile? effective = options.UseCalibration ? calibration : null;
        if (effective == null)
        {
            return spectrum;
        }

        var baked = new List<SignalPoint>(spectrum.Count);
        foreach (SignalPoint point in spectrum)
        {
            baked.Add(new SignalPoint(
                point.X, point.Y - effective.GetDecibelCorrection(point.X)));
        }

        return baked;
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
    // The harmonic curves are smoothed at the SAME fractional-octave width the user
    // picked for the primary response, so HD2..HDn read at the same resolution as
    // HD1 rather than looking heavily blurred beside it.
    private const double HarmonicSmoothingWidthFactor = 1.0;

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
        if ((curves & SpectrumCurves.Distortion) == 0 ||
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

        // The noise floor is shown as its own trace (REW-style), so THD stays a
        // clean harmonics-only figure and the noise level is honest at its stated
        // analysis resolution — no fused THD+N, no arbitrary bandwidth convention.
        var distortionOptions = new DistortionOptions(
            // Harmonic curves scale the display width by a factor; the
            // psychoacoustic code contributes its plain base width — the dip
            // floor is for the fundamental's magnitude trace only.
            SmoothingOctaves: HarmonicSmoothingWidthFactor *
                SpectrumSmoothing.SmoothingOctaves(options.SmoothingInverseOctaves),
            // Estimate the noise floor only when its own curve is requested.
            IncludeNoise: (curves & SpectrumCurves.NoiseFloor) != 0);

        EssDistortion.DistortionCurveResult distortion =
            EssDistortion.ComputeDistortionCurvesResult(
                real,
                sweepMetadata,
                distortionOptions,
                options.UseCalibration ? calibration : null,
                curves & SpectrumCurves.Distortion);
        DistortionWarnings = distortion.Warnings;
        return distortion.Curves;
    }
}
