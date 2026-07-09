using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

public sealed class TimeAlignmentAnalysisOptions
{
    public bool UseBandpassWindow { get; init; }
    public double BandpassCenterHz { get; init; } = 1000;
    public double BandpassPassOctaves { get; init; } = 1;
    public double BandpassFadeOctaves { get; init; } = 0.5;
    public double FirstPeakThresholdBelowMaxDb { get; init; } = 25;
    public double FirstPeakMinimumSnrDb { get; init; } = 12;
    public double PeakSearchWindowMilliseconds { get; init; } = 80;
    public bool WrapPeakPositions { get; init; }
}

public readonly record struct TimeAlignmentAnalysisResult(
    double[] EnvelopeSamples,
    int EnvelopePeakIndex,
    double EnvelopePeak,
    int StrongestEnvelopePeakIndex,
    double StrongestEnvelopePeak,
    // How clean the recording is: the strongest envelope peak against the RMS
    // of the rest of the record. It grades the measurement, not the pick.
    double SignalToNoiseDecibels,
    // How pronounced the first arrival is: its envelope level relative to the
    // strongest peak, <= 0 dB (0 when they coincide). A low value means the
    // pick sits on a broad leading edge — physically normal for band-limited
    // low-frequency drivers — so its exact position carries less certainty,
    // however clean the recording. This is what used to be folded into a
    // single "quality" figure and misread great woofer measurements as fair.
    double FirstArrivalProminenceDecibels,
    double FirstArrivalPeakSample,
    double FirstArrivalDelayMilliseconds,
    double StrongestPeakSample,
    double StrongestDelayMilliseconds,
    double StrongestPeakSeparationMilliseconds,
    bool StrongestPeakIsSeparateArrival,
    // Per-arrival GCC-PHAT trust: the normalized whitened-correlation peak height in
    // [0, 1] used to refine each arrival (magnitude-based, so polarity-blind). The
    // RefinedByPhat flag is false when the peak was too weak (below the trust gate)
    // and the envelope parabola set the sample instead — a sub-gate confidence next
    // to RefinedByPhat=false is the honest "this alignment is coarse" signal, not a
    // trustworthy sub-sample figure.
    double FirstArrivalConfidence,
    bool FirstArrivalRefinedByPhat,
    double StrongestConfidence,
    bool StrongestRefinedByPhat);

public static class TimeAlignmentAnalysis
{
    public static TimeAlignmentAnalysisResult Analyze(
        IReadOnlyList<double> impulseResponse,
        int sampleRate,
        TimeAlignmentAnalysisOptions options,
        IReadOnlyList<double>? coherence = null)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        ArgumentNullException.ThrowIfNull(options);
        if (impulseResponse.Count == 0)
        {
            throw new ArgumentException(
                "Impulse response must not be empty.",
                nameof(impulseResponse));
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        double[] analysisSignal;
        double[]? kernelEnvelope = null;
        if (options.UseBandpassWindow)
        {
            double[] window = BandpassWindow.Create(
                impulseResponse.Count,
                sampleRate,
                options.BandpassCenterHz,
                options.BandpassPassOctaves,
                options.BandpassFadeOctaves);
            analysisSignal = ApplyBandpassWindow(impulseResponse, window);
            kernelEnvelope = BuildKernelEnvelope(window);
        }
        else
        {
            analysisSignal = impulseResponse.ToArray();
        }

        double[] envelope = SignalEnvelope.Envelope(analysisSignal);
        PeakSearchResult peakSearchResult = SignalEnvelope.FindPeak(
            envelope,
            sampleRate,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.FirstArrival,
                FirstPeakThresholdBelowMaxDb = options.FirstPeakThresholdBelowMaxDb,
                FirstPeakMinimumSnrDb = options.FirstPeakMinimumSnrDb,
                SearchWindowMilliseconds = options.PeakSearchWindowMilliseconds,
                AnalysisKernelEnvelope = kernelEnvelope
            });

        int envelopePeakIndex = peakSearchResult.SelectedIndex;
        double envelopePeak = envelope[envelopePeakIndex];
        double strongestPeak = peakSearchResult.StrongestPeak;
        int strongestPeakIndex = peakSearchResult.StrongestIndex;

        // Refine each arrival to sub-sample precision with a GCC-PHAT correlation
        // of the transfer IR (its spectrum already carries the microphone/loopback
        // cross-phase). The envelope peak stays the robust coarse anchor; the
        // whitened correlation sharpens its position, independent of the driver's
        // magnitude shape, and falls back to the envelope parabola when weak.
        PhaseTransformCorrelation phaseTransform =
            TransferFunction.ComputePhaseTransformFromResponse(
                analysisSignal, coherence: coherence);
        int refineRadius = ComputePhatSearchRadius(sampleRate);
        RefinedArrival firstArrival = RefineArrivalSample(
            phaseTransform, envelope, envelopePeakIndex, refineRadius);
        RefinedArrival strongest = RefineArrivalSample(
            phaseTransform, envelope, strongestPeakIndex, refineRadius);
        double firstArrivalPeakSample = firstArrival.Sample;
        double strongestPeakSample = strongest.Sample;

        // When the strongest peak is a distinct, clearly later arrival than the
        // first, it is a reflection or a room mode rather than the direct sound —
        // the usual narrowband-subwoofer trap. Flag it so the reader trusts the
        // first arrival. The coarse index gap drives the flag so wrapping does not.
        double separationMilliseconds =
            (strongestPeakIndex - envelopePeakIndex) * 1000.0 / sampleRate;
        bool strongestIsSeparateArrival =
            strongestPeakIndex != envelopePeakIndex &&
            separationMilliseconds >= SeparateArrivalThresholdMilliseconds;

        if (options.WrapPeakPositions)
        {
            firstArrivalPeakSample = ToSignedDelaySamples(
                firstArrivalPeakSample,
                envelope.Length);
            strongestPeakSample = ToSignedDelaySamples(
                strongestPeakSample,
                envelope.Length);
        }

        return new TimeAlignmentAnalysisResult(
            envelope,
            envelopePeakIndex,
            envelopePeak,
            strongestPeakIndex,
            strongestPeak,
            SignalEnvelope.EstimatePeakConfidenceDecibels(
                envelope,
                strongestPeakIndex,
                strongestPeak),
            strongestPeak > 0.0
                ? DataHelper.AmplitudeToDecibels(envelopePeak / strongestPeak)
                : 0.0,
            firstArrivalPeakSample,
            firstArrivalPeakSample * 1000.0 / sampleRate,
            strongestPeakSample,
            strongestPeakSample * 1000.0 / sampleRate,
            separationMilliseconds,
            strongestIsSeparateArrival,
            firstArrival.Confidence,
            firstArrival.RefinedByPhat,
            strongest.Confidence,
            strongest.RefinedByPhat);
    }

    // The minimum normalized GCC-PHAT peak height for its refined lag to be
    // trusted over the envelope parabola; below it the whitened correlation
    // carries no clear delay (e.g. too few in-band periods).
    private const double PhatTrustCoefficient = 0.2;

    // How much later than the first arrival the strongest peak must sit before it
    // is called a separate arrival (reflection or room mode) rather than the same
    // smeared direct sound.
    private const double SeparateArrivalThresholdMilliseconds = 1.0;

    // A short refinement window (~0.1 ms) around the envelope peak: wide enough to
    // absorb the envelope's sub-sample bias, narrow enough not to slide onto a
    // neighbouring reflection.
    private static int ComputePhatSearchRadius(int sampleRate) =>
        Math.Clamp((int)Math.Round(sampleRate * 0.0001), 2, 8);

    // A refined arrival position plus the GCC-PHAT trust it was refined with.
    // RefinedByPhat is true when the whitened correlation drove the sample; false
    // when its peak was too weak and the envelope parabola set it instead. Confidence
    // is the PHAT peak height on both branches, so the caller always sees the same
    // [0, 1] measure the trust decision used.
    private readonly record struct RefinedArrival(
        double Sample,
        double Confidence,
        bool RefinedByPhat);

    private static RefinedArrival RefineArrivalSample(
        PhaseTransformCorrelation phaseTransform,
        IReadOnlyList<double> envelope,
        int coarseIndex,
        int searchRadius)
    {
        PhaseTransformDelay phat = phaseTransform.RefineAround(coarseIndex, searchRadius);
        bool refinedByPhat = phat.Refined && phat.PeakCorrelation >= PhatTrustCoefficient;
        double sample = refinedByPhat
            ? phat.LagSamples
            : coarseIndex + FindFractionalPeakOffset(envelope, coarseIndex);
        return new RefinedArrival(
            sample,
            Math.Clamp(phat.PeakCorrelation, 0.0, 1.0),
            refinedByPhat);
    }

    private static double[] ApplyBandpassWindow(
        IReadOnlyList<double> signal,
        double[] window)
    {
        var spectrum = new Complex[signal.Count];
        for (int i = 0; i < signal.Count; i++)
        {
            spectrum[i] = new Complex(signal[i], 0.0);
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);
        for (int i = 0; i < spectrum.Length; i++)
        {
            spectrum[i] *= window[i];
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);

        var filtered = new double[spectrum.Length];
        for (int i = 0; i < spectrum.Length; i++)
        {
            filtered[i] = spectrum[i].Real;
        }

        return filtered;
    }

    // The time response of the zero-phase bandpass mask, as an analytic
    // envelope indexed by |offset| from the kernel centre. The kernel is real
    // and even, so its IFFT sits centred at index 0 and the envelope's first
    // half is exactly the by-offset curve the sidelobe rejection needs: an
    // arrival can pre-ring at a given distance no louder than this envelope
    // says, which is what separates the window's own ringing from a genuine
    // earlier arrival.
    private static double[] BuildKernelEnvelope(double[] window)
    {
        var spectrum = new Complex[window.Length];
        for (int i = 0; i < window.Length; i++)
        {
            spectrum[i] = new Complex(window[i], 0.0);
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);

        var kernel = new double[window.Length];
        for (int i = 0; i < kernel.Length; i++)
        {
            kernel[i] = spectrum[i].Real;
        }

        return SignalEnvelope.Envelope(kernel);
    }

    private static double FindFractionalPeakOffset(
        IReadOnlyList<double> envelope,
        int peakIndex)
    {
        if (peakIndex <= 0 || peakIndex >= envelope.Count - 1)
        {
            return 0.0;
        }

        return SignalEnvelope.FindFractionalPeakOffset(
            envelope[peakIndex - 1],
            envelope[peakIndex],
            envelope[peakIndex + 1]);
    }

    private static double ToSignedDelaySamples(double wrappedPeakSample, int length) =>
        wrappedPeakSample <= length * 0.5
            ? wrappedPeakSample
            : wrappedPeakSample - length;
}
