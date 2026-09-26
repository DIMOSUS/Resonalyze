using System.Numerics;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>What a plot build reads of the open measurement; one build reads one result, whatever lands meanwhile.</summary>
internal sealed class MeasurementPlotContext
{
    private readonly AnalyzerDocument document;

    public MeasurementPlotContext(AnalyzerDocument document)
    {
        this.document = document;
    }

    /// <summary>The open result; builds that span several reads take it once.</summary>
    public MeasurementResult? Result => document.Result;

    public int SampleRate => document.Result?.SampleRate ?? 0;

    public string? ImpulseResponseFileName =>
        string.IsNullOrWhiteSpace(document.SourceName)
            ? null
            : Path.GetFileName(document.SourceName);

    public string CreateTitle(string baseTitle) =>
        ImpulseResponseFileName is not { } fileName
            ? baseTitle
            : $"{baseTitle} - {fileName}";

    public bool CanIncludeCurves(bool includeCurves) =>
        includeCurves &&
        document.HasResult &&
        !document.IsBusy;

    public bool HasTransferImpulseResponse => document.Result?.HasTransfer == true;

    /// <summary>Estimated IR start (ms) for the Auto gate offset, memoized in <see cref="TransferIrStartCache"/>.</summary>
    public double? ResolveAutoGateOffsetMs() =>
        document.Result is { Transfer.ImpulseResponse.Length: > 0, SampleRate: > 0 } result
            ? TransferIrStartCache.ResolveStartMs(
                result.Transfer.ImpulseResponse,
                result.SampleRate,
                result.Transfer.PeakIndex)
            : null;

    /// <summary>The result's frozen anchor, so a live recalibration does not rescale what is on screen.</summary>
    public double? SplOffsetDb => document.Result?.SplOffsetDb;

    // All analysis derives from the loopback transfer IR (callers gate on HasTransferImpulseResponse); sweep deconvolution is for harmonics/noise.
    public IImpulseMeasurement CreatePrimaryMeasurement()
    {
        MeasurementResult result = document.Result
            ?? throw new InvalidOperationException("Transfer impulse response is not available.");
        MeasurementImpulseResponse transfer = result.Transfer
            ?? throw new InvalidOperationException(
                "Transfer impulse response is not available.");
        MeasuredBand band = result.MeasuredBand;
        return new ImpulseMeasurementView(
            transfer.ImpulseResponse,
            transfer.PeakIndex,
            result.SampleRate)
        {
            // From the result's filter and sweep, never the configured ones (those describe the next sweep).
            LowestMeasuredFrequencyHz = band.LowEdgeHz,
            HighestMeasuredFrequencyHz = band.HighEdgeHz
        };
    }

    /// <summary>Band every derived curve stops at; overlays carry it past the measurement's lifetime.</summary>
    public MeasuredBand MeasuredBand => document.Result?.MeasuredBand ?? MeasuredBand.Everything;

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

    public FrequencyResponseCurves CreateFrequencyResponseCurves(
        FrequencyResponseOptions options,
        CalibrationFile? calibration,
        SpectrumCurves curves,
        CancellationToken cancellationToken = default)
    {
        var result = new List<AnalysisCurve>();
        result.AddRange(DataHelper.GetSpectrum(
            CreatePrimaryMeasurement(),
            options,
            calibration,
            curves & SpectrumCurves.Primary));
        cancellationToken.ThrowIfCancellationRequested();

        if (CreateDistortionCurves(options, calibration, curves) is not { } distortion)
        {
            return new FrequencyResponseCurves(result, [], []);
        }

        result.AddRange(distortion.Curves);
        return new FrequencyResponseCurves(result, distortion.Warnings, distortion.PacketValidity);
    }

    private EssDistortion.DistortionCurveResult? CreateDistortionCurves(
        FrequencyResponseOptions options,
        CalibrationFile? calibration,
        SpectrumCurves curves)
    {
        // The result's recorded sweep geometry, not the rebuilt one (length-capped, legacy edges unreachable).
        if ((curves & SpectrumCurves.Distortion) == 0 ||
            document.Result is not { } result ||
            result.SweepSampleCount <= 0 ||
            !(result.AchievedLowFrequencyHz > 0) ||
            !(result.AchievedHighFrequencyHz > result.AchievedLowFrequencyHz))
        {
            return null;
        }

        MeasurementImpulseResponse deconvolution = result.SweepDeconvolution;
        var sweepMetadata = new EssSweepMetadata(
            result.AchievedLowFrequencyHz,
            result.AchievedHighFrequencyHz,
            result.SweepSampleDurationSeconds,
            result.SampleRate,
            result.SweepSampleCount,
            deconvolution.PeakIndex,
            result.MeasuredHighFrequencyHz);

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

        return EssDistortion.ComputeDistortionCurvesResult(
            real,
            sweepMetadata,
            distortionOptions,
            options.UseCalibration ? calibration : null,
            curves & SpectrumCurves.Distortion);
    }
}

/// <summary>A Frequency Response build's curves, with what explains a harmonic it could not draw.</summary>
/// <param name="DistortionPacketValidity">Separates overlap drops (amber) from below-noise drops (neutral note).</param>
internal sealed record FrequencyResponseCurves(
    IReadOnlyList<AnalysisCurve> Curves,
    IReadOnlyList<string> DistortionWarnings,
    IReadOnlyList<HarmonicPacketValidity> DistortionPacketValidity);
