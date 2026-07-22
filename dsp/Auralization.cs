using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>
/// Renders program material through measured virtual-crossover responses, so a
/// tune can be listened to instead of only read off a plot.
/// <para>
/// What the listener hears is what the MEASUREMENT MICROPHONE heard: the summed
/// response carries the drivers, the cabin and the microphone as well as the DSP
/// chain. It is an auralization of one point in the car, for headphones — not a
/// preview of the electrical preset, and not a binaural rendering (one omni
/// capsule is not two ears).
/// </para>
/// </summary>
public static class Auralization
{
    // The decay window the tail search reads. Five milliseconds is short enough
    // to follow a cabin's decay (RT60 in a car is tens of milliseconds) and long
    // enough that a single zero crossing cannot end the response early.
    private const double DecayWindowMs = 5.0;

    // Where the response stops being the car and starts being the measurement's
    // own noise floor. Sixty dB below the arrival is past anything audible under
    // program material, and a clean cabin sweep's floor sits above it.
    private const double DecayFloorDb = 60.0;

    // The tail is bounded both ways: too short truncates the cabin's decay into
    // an audibly dry response, too long convolves the track with half a second
    // of recorded hiss. A car's decay fits comfortably inside the upper bound.
    private const double MinimumTailMs = 60.0;
    private const double MaximumTailMs = 400.0;

    // The kernel must not end on a step — that is a click on every sample of the
    // program material, spread across the whole track by the convolution.
    private const double FadeMs = 8.0;

    /// <summary>
    /// Peak target for the rendered file, dBFS. One dB of headroom leaves room
    /// for the inter-sample overshoots any later lossy encode or resampler
    /// introduces.
    /// </summary>
    public const double DefaultPeakTarget = -1.0;

    /// <summary>
    /// Cuts a processed virtual response down to the part worth convolving with,
    /// and fades its end.
    /// <para>
    /// The response arrives as the tool's chain output: a power-of-two record
    /// whose late region is the measurement's noise floor, the deconvolution's
    /// numerical tail, and — for a negative chain delay — the samples the shift
    /// wrapped past zero. Convolving music with all of that adds audible hiss and
    /// a wrapped pre-echo, so the record is cut where the decay reaches the floor.
    /// </para>
    /// <para>
    /// The cut is at the END only: sample 0 stays sample 0, so the measurement's
    /// own propagation delay — and with it the difference between the two sides,
    /// which is the whole point of the alignment being auditioned — survives
    /// intact.
    /// </para>
    /// </summary>
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
            // Raised cosine from 1 down to 0 across the last `fade` samples.
            double phase = Math.PI * (i + 1) / (fade + 1);
            kernel[length - fade + i] *= 0.5 * (1.0 + Math.Cos(phase));
        }

        trim = new AuralizationTrim(
            length,
            tail * 1000.0 / sampleRate,
            length < response.Length);
        return kernel;
    }

    /// <summary>
    /// Runs the program material through the two side responses: channel 1 of the
    /// source through the left kernel, channel 2 through the right. A mono source
    /// feeds both sides; a source with more than two channels contributes its
    /// first two, and the caller is expected to have said so.
    /// </summary>
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

        // The MATERIAL is converted to the response's rate, never the response to
        // the material's. The kernels are the measurement — their timing and phase
        // are the object under test, and a converter's own phase response would
        // land inside the thing being auditioned. The rendered file therefore
        // comes out at the project's rate.
        bool resampled = request.SourceSampleRate != request.KernelSampleRate;
        if (resampled)
        {
            cancellationToken.ThrowIfCancellationRequested();
            float[] convertedLeft = SampleRateConverter.Resample(
                left, request.SourceSampleRate, request.KernelSampleRate);
            cancellationToken.ThrowIfCancellationRequested();
            float[] convertedRight = ReferenceEquals(left, right)
                ? convertedLeft
                : SampleRateConverter.Resample(
                    right, request.SourceSampleRate, request.KernelSampleRate);
            left = convertedLeft;
            right = convertedRight;
            progress?.Report(ResampleProgressShare);
        }

        double convolveShare = resampled ? 1.0 - ResampleProgressShare : 1.0;
        double convolveBase = resampled ? ResampleProgressShare : 0.0;
        float[] renderedLeft = FastConvolution.Convolve(
            left,
            request.LeftKernel,
            Scaled(progress, convolveBase, convolveShare / 2.0),
            cancellationToken);
        float[] renderedRight = FastConvolution.Convolve(
            right,
            request.RightKernel,
            Scaled(progress, convolveBase + convolveShare / 2.0, convolveShare / 2.0),
            cancellationToken);

        // The kernels are trimmed per side, so the two convolutions come out at
        // DIFFERENT lengths (source + kernel − 1 each). A stereo file needs one
        // frame count; the shorter side gets trailing zeros, which is exactly
        // what it is — that side's response ended earlier.
        int commonLength = Math.Max(renderedLeft.Length, renderedRight.Length);
        renderedLeft = PadTo(renderedLeft, commonLength);
        renderedRight = PadTo(renderedRight, commonLength);

        double gain = Normalize(
            renderedLeft, renderedRight, request.PeakTargetDbfs, cancellationToken);
        progress?.Report(1.0);
        return new AuralizationResult(
            [renderedLeft, renderedRight],
            request.KernelSampleRate,
            DataHelper.AmplitudeToDecibels(gain),
            resampled);
    }

    // Resampling is a single pass over the material against two block-FFT passes,
    // so it is a modest slice of the whole job; the split only has to keep the
    // bar moving at a believable rate.
    private const double ResampleProgressShare = 0.2;

    // One gain for BOTH channels: scaling the sides independently would level the
    // very inter-side balance the tune is being auditioned for.
    private static double Normalize(
        float[] left,
        float[] right,
        double peakTargetDbfs,
        CancellationToken cancellationToken)
    {
        double peak = Math.Max(AbsolutePeak(left), AbsolutePeak(right));
        if (peak <= 0)
        {
            return 1.0;
        }

        // The summed transfer response has no absolute scale — it is
        // loopback-referenced and then run through arbitrary gains and PEQ boosts
        // — so the raw render lands anywhere from far below to far above full
        // scale, and the factor is reported to the caller rather than hidden.
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

    private static IProgress<double>? Scaled(
        IProgress<double>? progress, double offset, double share) =>
        progress == null
            ? null
            : new Progress<double>(value => progress.Report(offset + value * share));
}

/// <summary>
/// What <see cref="Auralization.TrimResponse"/> kept: the kernel length, the
/// decay window past the arrival, and whether anything was actually cut.
/// </summary>
public readonly record struct AuralizationTrim(
    int Length,
    double TailMilliseconds,
    bool Cut);

/// <summary>The inputs of one auralization render.</summary>
public sealed record AuralizationRequest
{
    /// <summary>Trimmed summed response of the left side of the car.</summary>
    public required double[] LeftKernel { get; init; }

    /// <summary>Trimmed summed response of the right side of the car.</summary>
    public required double[] RightKernel { get; init; }

    /// <summary>The project's rate — both kernels share it, and the render adopts it.</summary>
    public required int KernelSampleRate { get; init; }

    /// <summary>Deinterleaved program material.</summary>
    public required float[][] SourceChannels { get; init; }

    public required int SourceSampleRate { get; init; }

    public double PeakTargetDbfs { get; init; } = Auralization.DefaultPeakTarget;
}

/// <summary>The rendered stereo pair and what the render had to do to it.</summary>
public sealed record AuralizationResult(
    float[][] Channels,
    int SampleRate,
    double AppliedGainDb,
    bool Resampled);
