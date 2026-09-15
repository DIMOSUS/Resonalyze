using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>Headphone-only stereo auralization: each program channel convolved with its side's measured summed response, one side per ear.
/// Keeps inter-side level and timing audible; not binaural, and not the summed signal the microphone heard.</summary>
public static class Auralization
{
    // Short enough to follow a car's decay, long enough that one zero crossing cannot end it.
    private const double DecayWindowMs = 5.0;

    private const double DecayFloorDb = 60.0;

    // Too short sounds dry; too long convolves recorded hiss.
    private const double MinimumTailMs = 60.0;
    private const double MaximumTailMs = 400.0;

    // A kernel ending on a step clicks on every program sample.
    private const double FadeMs = 8.0;

    /// <summary>1 dB headroom for inter-sample overshoot in later encodes.</summary>
    public const double DefaultPeakTarget = -1.0;

    /// <summary>Cuts the chain output where decay reaches the floor (noise, numerical tail, wrapped negative delay) and fades the end.
    /// Only the end is cut: sample 0 stays, preserving propagation delay and the inter-side difference.</summary>
    public static double[] TrimResponse(
        Complex[] response,
        int sampleRate,
        out AuralizationTrim trim)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Length == 0)
        {
            throw new ArgumentException("The response is empty.", nameof(response));
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        int peakIndex = VirtualCrossoverAnalysis.FindPeakIndex(response);
        int windowLength = Math.Max(1, (int)Math.Round(DecayWindowMs * sampleRate / 1000.0));
        int minimumTail = (int)Math.Round(MinimumTailMs * sampleRate / 1000.0);
        int maximumTail = (int)Math.Round(MaximumTailMs * sampleRate / 1000.0);

        double reference = WindowRms(response, peakIndex, windowLength);
        double floor = reference * Math.Pow(10.0, -DecayFloorDb / 20.0);
        int tail = maximumTail;
        for (int start = peakIndex + windowLength;
            start + windowLength <= response.Length;
            start += windowLength)
        {
            if (start - peakIndex > maximumTail)
            {
                break;
            }

            if (WindowRms(response, start, windowLength) <= floor)
            {
                tail = start - peakIndex;
                break;
            }
        }

        tail = Math.Clamp(tail, minimumTail, maximumTail);
        int length = Math.Min(response.Length, peakIndex + tail);
        var kernel = new double[length];
        for (int i = 0; i < length; i++)
        {
            kernel[i] = response[i].Real;
        }

        int fade = Math.Min(length, Math.Max(1, (int)Math.Round(FadeMs * sampleRate / 1000.0)));
        for (int i = 0; i < fade; i++)
        {
            double phase = Math.PI * (i + 1) / (fade + 1);
            kernel[length - fade + i] *= 0.5 * (1.0 + Math.Cos(phase));
        }

        trim = new AuralizationTrim(
            length,
            tail * 1000.0 / sampleRate,
            length < response.Length);
        return kernel;
    }

    /// <summary>Channel 1 through the left kernel, channel 2 through the right; mono feeds both, extra channels are ignored.</summary>
    public static AuralizationResult Render(
        AuralizationRequest request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SourceChannels.Length == 0)
        {
            throw new ArgumentException(
                "The source has no channels.", nameof(request));
        }

        float[] left = request.SourceChannels[0];
        float[] right = request.SourceChannels.Length > 1
            ? request.SourceChannels[1]
            : request.SourceChannels[0];

        // Resample the material, never the kernels: a converter's phase would land inside the thing under test.
        bool resampled = request.SourceSampleRate != request.KernelSampleRate;
        if (resampled)
        {
            bool shared = ReferenceEquals(left, right);
            double half = ResampleProgressShare / 2.0;
            float[] convertedLeft = SampleRateConverter.Resample(
                left,
                request.SourceSampleRate,
                request.KernelSampleRate,
                Scaled(progress, 0.0, shared ? ResampleProgressShare : half),
                cancellationToken);
            float[] convertedRight = shared
                ? convertedLeft
                : SampleRateConverter.Resample(
                    right,
                    request.SourceSampleRate,
                    request.KernelSampleRate,
                    Scaled(progress, half, half),
                    cancellationToken);
            left = convertedLeft;
            right = convertedRight;
        }

        // Reference kernels (e.g. without cabin subtraction) are rendered only for their peak, so A/B renders share one gain.
        bool hasReference =
            request.ReferenceLeftKernel != null && request.ReferenceRightKernel != null;
        int convolvePasses = hasReference ? 4 : 2;
        double convolveShare = resampled ? 1.0 - ResampleProgressShare : 1.0;
        double convolveBase = resampled ? ResampleProgressShare : 0.0;
        double passShare = convolveShare / convolvePasses;
        int pass = 0;

        double referencePeak = 0.0;
        if (hasReference)
        {
            float[] referenceLeft = FastConvolution.Convolve(
                left,
                request.ReferenceLeftKernel!,
                Scaled(progress, convolveBase + passShare * pass++, passShare),
                cancellationToken);
            float[] referenceRight = FastConvolution.Convolve(
                right,
                request.ReferenceRightKernel!,
                Scaled(progress, convolveBase + passShare * pass++, passShare),
                cancellationToken);
            referencePeak = Math.Max(
                AbsolutePeak(referenceLeft), AbsolutePeak(referenceRight));
        }

        float[] renderedLeft = FastConvolution.Convolve(
            left,
            request.LeftKernel,
            Scaled(progress, convolveBase + passShare * pass++, passShare),
            cancellationToken);
        float[] renderedRight = FastConvolution.Convolve(
            right,
            request.RightKernel,
            Scaled(progress, convolveBase + passShare * pass++, passShare),
            cancellationToken);

        // Per-side trimmed kernels give different lengths; pad the shorter side with zeros.
        int commonLength = Math.Max(renderedLeft.Length, renderedRight.Length);
        renderedLeft = PadTo(renderedLeft, commonLength);
        renderedRight = PadTo(renderedRight, commonLength);

        double gain = Normalize(
            renderedLeft, renderedRight, referencePeak,
            request.PeakTargetDbfs, cancellationToken);
        progress?.Report(1.0);
        return new AuralizationResult(
            [renderedLeft, renderedRight],
            request.KernelSampleRate,
            DataHelper.AmplitudeToDecibels(gain),
            resampled);
    }

    private const double ResampleProgressShare = 0.2;

    // One gain for both channels (preserves balance), divided by the largest peak including the reference render.
    private static double Normalize(
        float[] left,
        float[] right,
        double referencePeak,
        double peakTargetDbfs,
        CancellationToken cancellationToken)
    {
        double peak = Math.Max(
            Math.Max(AbsolutePeak(left), AbsolutePeak(right)), referencePeak);
        if (peak <= 0)
        {
            return 1.0;
        }

        // The summed response has no absolute scale, so the gain is reported, not hidden.
        double gain = Math.Pow(10.0, peakTargetDbfs / 20.0) / peak;
        cancellationToken.ThrowIfCancellationRequested();
        Scale(left, gain);
        Scale(right, gain);
        return gain;
    }

    private static double AbsolutePeak(float[] samples)
    {
        double peak = 0;
        foreach (float sample in samples)
        {
            double magnitude = Math.Abs(sample);
            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }

        return peak;
    }

    private static float[] PadTo(float[] samples, int length)
    {
        if (samples.Length >= length)
        {
            return samples;
        }

        var padded = new float[length];
        Array.Copy(samples, padded, samples.Length);
        return padded;
    }

    private static void Scale(float[] samples, double gain)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(samples[i] * gain);
        }
    }

    private static double WindowRms(Complex[] response, int start, int length)
    {
        int end = Math.Min(response.Length, start + length);
        if (start >= end)
        {
            return 0.0;
        }

        double sumSquares = 0;
        for (int i = start; i < end; i++)
        {
            double value = response[i].Real;
            sumSquares += value * value;
        }

        return Math.Sqrt(sumSquares / (end - start));
    }

    // Synchronous: Progress<T> created on a worker thread posts to the pool and reorders reports.
    private static IProgress<double>? Scaled(
        IProgress<double>? progress, double offset, double share) =>
        progress == null
            ? null
            : new SynchronousProgress<double>(
                value => progress.Report(offset + value * share));
}

public readonly record struct AuralizationTrim(
    int Length,
    double TailMilliseconds,
    bool Cut);

public sealed record AuralizationRequest
{
    public required double[] LeftKernel { get; init; }

    public required double[] RightKernel { get; init; }

    /// <summary>Level-matching reference render; set both reference kernels or neither.</summary>
    public double[]? ReferenceLeftKernel { get; init; }

    public double[]? ReferenceRightKernel { get; init; }

    /// <summary>The project's rate; both kernels share it and the render adopts it.</summary>
    public required int KernelSampleRate { get; init; }

    public required float[][] SourceChannels { get; init; }

    public required int SourceSampleRate { get; init; }

    public double PeakTargetDbfs { get; init; } = Auralization.DefaultPeakTarget;
}

public sealed record AuralizationResult(
    float[][] Channels,
    int SampleRate,
    double AppliedGainDb,
    bool Resampled);
