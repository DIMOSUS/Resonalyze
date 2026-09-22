using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Everything a render reads, taken from the session before the render leaves the UI thread.</summary>
internal sealed record AuditionRenderRequest(
    VirtualCrossoverAuditionContext Context,
    string SourcePath,
    string TargetPath,
    CalibrationFile? Calibration,
    string CalibrationLabel,
    CabinTransferFunction? Cabin,
    string CabinLabel,
    VirtualCrossoverAuditionSpatialAverage? SpatialAverage,
    string MagnitudeLabel);

internal sealed record AuditionRenderOutcome(
    int SourceSampleRate,
    AuralizationResult Rendered,
    AuralizationTrim LeftTrim,
    AuralizationTrim RightTrim,
    int LeftKernelTaps,
    int RightKernelTaps,
    int CorrectionFirTaps,
    string CalibrationLabel,
    bool CabinApplied,
    string CabinLabel,
    double CabinTwentyHzDb,
    string MagnitudeLabel);

/// <summary>The audition render: when Render can be pressed, the request it reads and the worker that writes the WAV.</summary>
/// <remarks>Calibration and cabin subtraction are linear-phase FIRs in both side kernels. See docs/tech/spatial-average.md#audition-render.</remarks>
internal static class VirtualCrossoverAuditionRender
{
    private const double DecodeShare = 0.06;
    private const double RenderShare = 0.86;

    /// <summary>A render in flight keeps the button (it cancels); otherwise a track, an output and a calibration the render
    /// can carry.</summary>
    public static bool Available(VirtualCrossoverAuditionSession session) =>
        session.Rendering ||
        (session.SourcePath != null && session.TargetPath != null &&
            VirtualCrossoverAuditionCalibration.Note(session)?.Refused != true);

    /// <summary>Reads the session once, on the UI thread, before the render's first await.</summary>
    public static AuditionRenderRequest Request(VirtualCrossoverAuditionSession session)
    {
        (CalibrationFile? calibration, string calibrationLabel) =
            VirtualCrossoverAuditionCalibration.ForRender(session);
        CabinTransferFunction? cabin = session.CabinStyle is { } cabinStyle
            ? CabinTransferFunction.FromBodyStyle(cabinStyle)
            : null;
        VirtualCrossoverAuditionSpatialAverage? spatialAverage = session.RequestedSpatialAverage;
        return new AuditionRenderRequest(
            session.Context,
            session.SourcePath!,
            session.TargetPath!,
            calibration,
            calibrationLabel,
            cabin,
            cabin == null ? "off" : session.CabinLabel,
            spatialAverage,
            spatialAverage != null
                ? "spatial averages (MMM / array)"
                : "impulse responses (one microphone position)");
    }

    // Worker thread: reads the request only.
    public static AuditionRenderOutcome Run(
        AuditionRenderRequest request,
        IProgress<AuditionProgress> progress,
        CancellationToken cancellationToken)
    {
        VirtualCrossoverAuditionContext context = request.Context;
        CalibrationFile? calibration = request.Calibration;
        CabinTransferFunction? cabin = request.Cabin;
        progress.Report(new AuditionProgress("Preparing the responses…", 0));
        Complex[] leftSum = request.SpatialAverage?.LeftSum ?? context.LeftSum;
        Complex[] rightSum = request.SpatialAverage?.RightSum ?? context.RightSum;
        double[] leftKernel = Auralization.TrimResponse(
            leftSum, context.SampleRate, out AuralizationTrim leftTrim);
        double[] rightKernel = Auralization.TrimResponse(
            rightSum, context.SampleRate, out AuralizationTrim rightTrim);

        // With cabin subtraction a calibration-only reference pair is built first for level matching.
        double[]? referenceLeftKernel = null;
        double[]? referenceRightKernel = null;
        if (cabin != null)
        {
            if (calibration != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double[] calFir = CalibrationFirFilter.Design(
                    calibration.GetDecibelCorrection, context.SampleRate);
                referenceLeftKernel = FastConvolution.Convolve(leftKernel, calFir);
                referenceRightKernel = FastConvolution.Convolve(rightKernel, calFir);
            }
            else
            {
                referenceLeftKernel = leftKernel;
                referenceRightKernel = rightKernel;
            }
        }

        int correctionFirTaps = 0;
        if (calibration != null || cabin != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double[] fir = CalibrationFirFilter.Design(
                frequencyHz =>
                    (calibration?.GetDecibelCorrection(frequencyHz) ?? 0.0) +
                    (cabin?.Evaluate(frequencyHz) ?? 0.0),
                context.SampleRate);
            correctionFirTaps = fir.Length;
            leftKernel = FastConvolution.Convolve(leftKernel, fir);
            rightKernel = FastConvolution.Convolve(rightKernel, fir);
        }

        progress.Report(new AuditionProgress("Decoding the track…", 0.01));
        // Only two channels are decoded; the byte cap also bounds the decode itself.
        AudioFileContent material = AudioFileCodec.Read(
            request.SourcePath,
            TimeSpan.FromMinutes(VirtualCrossoverAuditionBudget.MaximumTrackMinutes),
            channelLimit: 2,
            VirtualCrossoverAuditionBudget.MaximumPipelineBytes,
            cancellationToken);
        VirtualCrossoverAuditionBudget.CheckDecoded(
            material.FrameCount, material.SampleRate, context.SampleRate);

        var renderProgress = new SynchronousProgress<double>(value =>
            progress.Report(new AuditionProgress(
                "Rendering through the tune…",
                DecodeShare + value * RenderShare)));
        AuralizationResult rendered = Auralization.Render(
            new AuralizationRequest
            {
                LeftKernel = leftKernel,
                RightKernel = rightKernel,
                ReferenceLeftKernel = referenceLeftKernel,
                ReferenceRightKernel = referenceRightKernel,
                KernelSampleRate = context.SampleRate,
                SourceChannels = material.Channels,
                SourceSampleRate = material.SampleRate
            },
            renderProgress,
            cancellationToken);

        progress.Report(new AuditionProgress(
            "Writing the WAV file…", DecodeShare + RenderShare));
        WriteRenderedTrack(request.TargetPath, rendered, cancellationToken);
        progress.Report(new AuditionProgress("Finished", 1.0));

        return new AuditionRenderOutcome(
            material.SampleRate,
            rendered,
            leftTrim,
            rightTrim,
            leftKernel.Length,
            rightKernel.Length,
            correctionFirTaps,
            request.CalibrationLabel,
            cabin != null,
            request.CabinLabel,
            cabin?.Evaluate(20.0) ?? 0.0,
            request.MagnitudeLabel);
    }

    // Via a temporary file so a cancel or failure never leaves a truncated WAV in place.
    private static void WriteRenderedTrack(
        string targetPath,
        AuralizationResult result,
        CancellationToken cancellationToken)
    {
        string temporaryPath = targetPath + ".partial";
        try
        {
            AudioFileCodec.WriteWav(
                temporaryPath,
                new AudioFileContent(result.Channels, result.SampleRate),
                cancellationToken);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }

            throw;
        }
    }
}
