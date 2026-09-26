using System.Numerics;
using System.Runtime.CompilerServices;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>What a plot build reads of the open measurement: the document as it is, or one moment of it
/// (<see cref="Freeze"/>) for a build off the UI thread, which whatever lands meanwhile cannot tear.</summary>
internal sealed class MeasurementPlotContext
{
    private readonly AnalyzerDocument? document;
    private readonly FrozenDocument frozen;

    public MeasurementPlotContext(AnalyzerDocument document)
    {
        this.document = document;
    }

    private MeasurementPlotContext(FrozenDocument frozen)
    {
        this.frozen = frozen;
    }

    private FrozenDocument State => document != null ? FrozenDocument.Of(document) : frozen;

    /// <summary>Taken on the UI thread, which alone writes the document.</summary>
    public MeasurementPlotContext Freeze() => new(State);

    public MeasurementResult? Result => State.Result;

    public int SampleRate => State.Result?.SampleRate ?? 0;

    public string? ImpulseResponseFileName =>
        State.SourceName is not { } sourceName || string.IsNullOrWhiteSpace(sourceName)
            ? null
            : Path.GetFileName(sourceName);

    public string CreateTitle(string baseTitle) =>
        ImpulseResponseFileName is not { } fileName
            ? baseTitle
            : $"{baseTitle} - {fileName}";

    public bool CanIncludeCurves(bool includeCurves) =>
        includeCurves &&
        State is { Result: not null, IsBusy: false };

    public bool HasTransferImpulseResponse => State.Result?.HasTransfer == true;

    /// <summary>Estimated IR start (ms) for the Auto gate offset, memoized in <see cref="TransferIrStartCache"/>.</summary>
    public double? ResolveAutoGateOffsetMs() =>
        State.Result is { Transfer.ImpulseResponse.Length: > 0, SampleRate: > 0 } result
            ? TransferIrStartCache.ResolveStartMs(
                result.Transfer.ImpulseResponse,
                result.SampleRate,
                result.Transfer.PeakIndex)
            : null;

    /// <summary>The result's frozen anchor, so a live recalibration does not rescale what is on screen.</summary>
    public double? SplOffsetDb => State.Result?.SplOffsetDb;

    // All analysis derives from the loopback transfer IR (callers gate on HasTransferImpulseResponse); sweep deconvolution is for harmonics/noise.
    public IImpulseMeasurement CreatePrimaryMeasurement()
    {
        MeasurementResult result = State.Result
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
    public MeasuredBand MeasuredBand => State.Result?.MeasuredBand ?? MeasuredBand.Everything;

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

    // Shared by every build of a result: its decomposition and noise floor depend on nothing a build changes.
    private static readonly ConditionalWeakTable<MeasurementResult, DistortionAnalysis> DistortionAnalyses = new();

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
            State.Result is not { } result ||
            result.SweepDeconvolution.ImpulseResponse.Length == 0 ||
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

        // Noise floor as its own trace (REW-style), so THD stays harmonics-only.
        var distortionOptions = new DistortionOptions(
            // The psychoacoustic dip floor applies to the fundamental's trace only.
            SmoothingOctaves: HarmonicSmoothingWidthFactor *
                SpectrumSmoothing.SmoothingOctaves(options.SmoothingInverseOctaves),
            IncludeNoise: (curves & SpectrumCurves.NoiseFloor) != 0);

        // The harmonic and noise options the analysis reads are the defaults, never a build's own.
        DistortionAnalysis analysis = DistortionAnalyses.GetValue(
            result,
            _ => new DistortionAnalysis(deconvolution.ImpulseResponse, sweepMetadata, distortionOptions));
        return EssDistortion.ComputeDistortionCurvesResult(
            analysis.Decomposition,
            distortionOptions.IncludeNoise ? analysis.Noise : null,
            distortionOptions,
            options.UseCalibration ? calibration : null,
            curves & SpectrumCurves.Distortion);
    }

    private sealed class DistortionAnalysis
    {
        private readonly Lazy<EssHarmonicDecomposition> decomposition;
        private readonly Lazy<NoiseEstimate> noise;

        public DistortionAnalysis(Complex[] impulse, EssSweepMetadata sweep, DistortionOptions options)
        {
            decomposition = new(() => EssDistortion.Decompose(RealPart(impulse), sweep, options));
            noise = new(() => EssNoise.EstimateNoise(RealPart(impulse), decomposition.Value, options));
        }

        public EssHarmonicDecomposition Decomposition => decomposition.Value;

        public NoiseEstimate Noise => noise.Value;

        private static double[] RealPart(Complex[] impulse)
        {
            double[] real = new double[impulse.Length];
            for (int i = 0; i < impulse.Length; i++)
            {
                real[i] = impulse[i].Real;
            }

            return real;
        }
    }
}

internal readonly record struct FrozenDocument(MeasurementResult? Result, string? SourceName, bool IsBusy)
{
    public static FrozenDocument Of(AnalyzerDocument document) =>
        new(document.Result, document.SourceName, document.IsBusy);
}

/// <summary>A Frequency Response build's curves, with what explains a harmonic it could not draw.</summary>
/// <param name="DistortionPacketValidity">Separates overlap drops (amber) from below-noise drops (neutral note).</param>
internal sealed record FrequencyResponseCurves(
    IReadOnlyList<AnalysisCurve> Curves,
    IReadOnlyList<string> DistortionWarnings,
    IReadOnlyList<HarmonicPacketValidity> DistortionPacketValidity);
