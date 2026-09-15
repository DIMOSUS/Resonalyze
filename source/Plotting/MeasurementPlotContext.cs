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

    /// <summary>Estimated IR start (ms) for the Auto gate offset, memoized in <see cref="TransferIrStartCache"/>.</summary>
    public double? ResolveAutoGateOffsetMs() =>
        expSweepMeasurement.Transfer is { ImpulseResponse.Length: > 0 } transfer &&
        expSweepMeasurement.SampleRate > 0
            ? TransferIrStartCache.ResolveStartMs(
                transfer.ImpulseResponse,
                expSweepMeasurement.SampleRate,
                transfer.PeakIndex)
            : null;

    /// <summary><c>K = loopbackPeakDbFs + calibrationOffsetDb</c> turns dBr into dB SPL. Null without a calibration matching this result's input.</summary>
    public double? SplOffsetDb
    {
        get
        {
            // The result's frozen calibration, so a live recalibration does not rescale what is on screen.
            if (expSweepMeasurement.MeasurementSplCalibration is not { } calibration)
            {
                return null;
            }

            InputLevelMeterEntry loopback = expSweepMeasurement.CurrentLevels.Loopback;
            if (!loopback.Available)
            {
                return null;
            }

            if (!expSweepMeasurement.InputMatches(calibration))
            {
                return null;
            }

            return loopback.PeakDbFs + calibration.OffsetDb;
        }
    }

    // All analysis derives from the loopback transfer IR (callers gate on HasTransferImpulseResponse); sweep deconvolution is for harmonics/noise.
    public IImpulseMeasurement CreatePrimaryMeasurement()
    {
        MeasurementImpulseResponse transfer = expSweepMeasurement.Transfer
            ?? throw new InvalidOperationException(
                "Transfer impulse response is not available.");
        return new ImpulseMeasurementView(
            transfer.ImpulseResponse,
            transfer.PeakIndex,
            expSweepMeasurement.SampleRate)
        {
            // From the result's filter and sweep, never the configured ones (those describe the next sweep).
            LowestMeasuredFrequencyHz = MeasuredBand.LowEdgeHz,
            HighestMeasuredFrequencyHz = MeasuredBand.HighEdgeHz
        };
    }

    /// <summary>Band every derived curve stops at; overlays carry it past the measurement's lifetime.</summary>
    public MeasuredBand MeasuredBand => MeasuredBand.Resolve(
        expSweepMeasurement.MeasurementProtectiveHighPass,
        expSweepMeasurement.MeasuredLowFrequencyHz,
        expSweepMeasurement.MeasuredHighFrequencyHz,
        expSweepMeasurement.SampleRate);

    /// <summary>Uncalibrated oversampled spectrum for exact re-smoothing; calibration applies after smoothing.</summary>
    public IReadOnlyList<SignalPoint>? CreateRawPrimarySpectrum(
        FrequencyResponseOptions options) =>
        HasTransferImpulseResponse
            ? BuildRawPrimarySpectrum(CreatePrimaryMeasurement(), options)
            : null;

    public static IReadOnlyList<SignalPoint> BuildRawPrimarySpectrum(
        IImpulseMeasurement measurement,
        FrequencyResponseOptions options) =>
        DataHelper.GetOversampledPrimarySpectrum(measurement, options);

    // HD curves smoothed at the primary's width so HD2..HDn read at HD1's resolution.
    private const double HarmonicSmoothingWidthFactor = 1.0;

    public IReadOnlyList<string> DistortionWarnings { get; private set; } = Array.Empty<string>();

    /// <summary>Separates overlap drops (amber) from below-noise drops (neutral note).</summary>
    public IReadOnlyList<HarmonicPacketValidity> DistortionPacketValidity
    { get; private set; } = Array.Empty<HarmonicPacketValidity>();

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
        DistortionPacketValidity = Array.Empty<HarmonicPacketValidity>();
        // The result's recorded sweep geometry, not the rebuilt one (length-capped, legacy edges unreachable).
        if ((curves & SpectrumCurves.Distortion) == 0 ||
            expSweepMeasurement.SweepDeconvolution is not { } deconvolution ||
            expSweepMeasurement.AchievedSweepSampleCount <= 0 ||
            !(expSweepMeasurement.AchievedLowFrequencyHz > 0) ||
            !(expSweepMeasurement.AchievedHighFrequencyHz >
                expSweepMeasurement.AchievedLowFrequencyHz))
        {
            return Array.Empty<AnalysisCurve>();
        }

        var sweepMetadata = new EssSweepMetadata(
            expSweepMeasurement.AchievedLowFrequencyHz,
            expSweepMeasurement.AchievedHighFrequencyHz,
            expSweepMeasurement.AchievedSweepDurationSeconds,
            expSweepMeasurement.SampleRate,
            expSweepMeasurement.AchievedSweepSampleCount,
            deconvolution.PeakIndex);

        Complex[] impulse = deconvolution.ImpulseResponse;
        double[] real = new double[impulse.Length];
        for (int i = 0; i < impulse.Length; i++)
        {
            real[i] = impulse[i].Real;
        }

        // Noise floor as its own trace (REW-style), so THD stays harmonics-only.
        var distortionOptions = new DistortionOptions(
            // The psychoacoustic dip floor applies to the fundamental's trace only.
            SmoothingOctaves: HarmonicSmoothingWidthFactor *
                SpectrumSmoothing.SmoothingOctaves(options.SmoothingInverseOctaves),
            IncludeNoise: (curves & SpectrumCurves.NoiseFloor) != 0);

        EssDistortion.DistortionCurveResult distortion =
            EssDistortion.ComputeDistortionCurvesResult(
                real,
                sweepMetadata,
                distortionOptions,
                options.UseCalibration ? calibration : null,
                curves & SpectrumCurves.Distortion);
        DistortionWarnings = distortion.Warnings;
        DistortionPacketValidity = distortion.PacketValidity;
        return distortion.Curves;
    }
}
