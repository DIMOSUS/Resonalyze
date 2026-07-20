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
    // How clean the recording is: the strongest envelope peak against the
    // noise floor (the RMS of the record's quietest quarter, so reflections
    // and modal decay do not count as noise). It grades the measurement, not
    // the pick.
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
    bool StrongestRefinedByPhat,
    // False when the analysis band carried no energy at all (silence, or a
    // bandpass entirely outside the measured band): with a flat-zero envelope
    // every sample "passes" the thresholds and the peak walk would fabricate a
    // confident-looking delay near the end of the search window. An invalid
    // result reports zeros and must not be shown as an alignment.
    bool IsValid = true);

/// <summary>
/// The verdict and figures of <see cref="TimeAlignmentAnalysis.ProbeArrivalHonesty"/>:
/// the upper-half re-read ([ProbeLowHz, ProbeHighHz]) and the tolerance the
/// full-band arrival was graded against.
/// </summary>
public readonly record struct TimeAlignmentArrivalProbe(
    AutoAlignmentEngine.ArrivalCertificate Certificate,
    TimeAlignmentAnalysisResult ProbeResult,
    double ProbeLowHz,
    double ProbeHighHz,
    double ToleranceMs);

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

        // No energy anywhere in the search window: nothing downstream is
        // meaningful (thresholds collapse to zero and every zero sample reads
        // as a "peak"), so return an explicitly invalid result instead of a
        // fabricated delay.
        if (!(strongestPeak > 0.0) || !double.IsFinite(strongestPeak))
        {
            return new TimeAlignmentAnalysisResult(
                envelope, 0, 0.0, 0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
                false, 0.0, false, 0.0, false, IsValid: false);
        }

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
        // first arrival. The coarse index gap drives the flag so wrapping does
        // not, and a genuine second arrival must be separated by a real valley:
        // a band-limited low-frequency driver's direct sound keeps rising for
        // milliseconds, and an early shoulder of that one wave packet peaking
        // later must not be called a reflection. In a band-limited analysis the
        // envelope's time resolution is ~1/bandwidth, so within that blur a
        // separation is the same wave packet's interference structure, not two
        // events — unless the valley between them is deep enough to prove the
        // events resolved anyway (destructive interference can resolve faster
        // than the nominal 1/BW).
        double separationMilliseconds =
            (strongestPeakIndex - envelopePeakIndex) * 1000.0 / sampleRate;
        double valleyDepthDb = ValleyDepthDb(
            envelope, envelopePeakIndex, strongestPeakIndex);
        double blurMilliseconds = SeparateArrivalThresholdMilliseconds;
        if (options.UseBandpassWindow)
        {
            double bandwidthHz = options.BandpassCenterHz * (
                Math.Pow(2.0, options.BandpassPassOctaves / 2.0)
                - Math.Pow(2.0, -options.BandpassPassOctaves / 2.0));
            blurMilliseconds = Math.Max(
                SeparateArrivalThresholdMilliseconds,
                1_000.0 / Math.Max(1e-9, bandwidthHz));
        }
        bool strongestIsSeparateArrival =
            strongestPeakIndex != envelopePeakIndex &&
            separationMilliseconds >= SeparateArrivalThresholdMilliseconds &&
            valleyDepthDb >= SeparateArrivalValleyDb &&
            (separationMilliseconds >= blurMilliseconds ||
                valleyDepthDb >= SeparateArrivalResolvedValleyDb);

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

    /// <summary>
    /// The arrival honesty probe for a bandpass-windowed manual measurement:
    /// the same full-band-vs-upper-half check the auto-alignment engine runs
    /// on every cross-side read. The upper half of the pass band is
    /// re-analyzed with the SAME pipeline (only the lower edge rises; the top
    /// edge and its fade stay put) and the full read is graded against it: a
    /// full-band arrival far LATER than its own upper half is the proven
    /// modal latch — the read times the band's late build-up (a room mode),
    /// not the direct front. Returns null when no bandpass window is active,
    /// or when the pass band is too narrow to carve a measurable upper half
    /// (<see cref="VirtualCrossoverAnalysis.MinimumArrivalBandRatio"/>).
    /// </summary>
    public static TimeAlignmentArrivalProbe? ProbeArrivalHonesty(
        IReadOnlyList<double> impulseResponse,
        int sampleRate,
        TimeAlignmentAnalysisOptions options,
        TimeAlignmentAnalysisResult fullResult,
        IReadOnlyList<double>? coherence = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.UseBandpassWindow)
        {
            return null;
        }

        (_, double f2, double f3, _) = BandpassWindow.BandAround(
            options.BandpassCenterHz,
            options.BandpassPassOctaves,
            options.BandpassFadeOctaves);
        double probeLowHz = Math.Sqrt(f2 * f3);
        if (f3 < probeLowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio)
        {
            return null;
        }

        var probeOptions = new TimeAlignmentAnalysisOptions
        {
            UseBandpassWindow = true,
            BandpassCenterHz = Math.Sqrt(probeLowHz * f3),
            BandpassPassOctaves = Math.Log2(f3 / probeLowHz),
            BandpassFadeOctaves = options.BandpassFadeOctaves,
            FirstPeakThresholdBelowMaxDb = options.FirstPeakThresholdBelowMaxDb,
            FirstPeakMinimumSnrDb = options.FirstPeakMinimumSnrDb,
            PeakSearchWindowMilliseconds = options.PeakSearchWindowMilliseconds,
            WrapPeakPositions = options.WrapPeakPositions
        };
        TimeAlignmentAnalysisResult probeResult = Analyze(
            impulseResponse, sampleRate, probeOptions, coherence);
        // The engine's bridge-probe allowance: the dispersion one wavefront
        // can show across the band — half a period at the probe's lower edge,
        // never tighter than 1 ms.
        double toleranceMs = Math.Max(1.0, 500.0 / probeLowHz);
        return new TimeAlignmentArrivalProbe(
            AutoAlignmentEngine.ClassifyArrival(fullResult, probeResult, toleranceMs),
            probeResult,
            probeLowHz,
            f3,
            toleranceMs);
    }

    // The minimum normalized GCC-PHAT peak height for its refined lag to be
    // trusted over the envelope parabola; below it the whitened correlation
    // carries no clear delay (e.g. too few in-band periods).
    private const double PhatTrustCoefficient = 0.2;

    // How much later than the first arrival the strongest peak must sit before it
    // is called a separate arrival (reflection or room mode) rather than the same
    // smeared direct sound.
    private const double SeparateArrivalThresholdMilliseconds = 1.0;

    // How deep the envelope must dip between the two peaks before they count as
    // separate arrivals: two events have a real valley between them, one broad
    // rise does not.
    private const double SeparateArrivalValleyDb = 6.0;

    // A valley this deep proves the two events resolved even when their
    // separation sits inside the analysis band's nominal ~1/BW blur —
    // destructive interference nulls faster than the envelope's rise time.
    private const double SeparateArrivalResolvedValleyDb = 20.0;

    // The envelope dip between the two peaks, in dB below the LOWER of them
    // (>= 0; 0 when the envelope never dips).
    private static double ValleyDepthDb(
        IReadOnlyList<double> envelope,
        int firstIndex,
        int secondIndex)
    {
        int from = Math.Min(firstIndex, secondIndex);
        int to = Math.Max(firstIndex, secondIndex);
        double valley = double.MaxValue;
        for (int i = from; i <= to; i++)
        {
            valley = Math.Min(valley, envelope[i]);
        }

        double reference = Math.Min(envelope[firstIndex], envelope[secondIndex]);
        if (reference <= 0.0 || valley <= 0.0)
        {
            return valley <= 0.0 && reference > 0.0 ? double.PositiveInfinity : 0.0;
        }

        return Math.Max(0.0, DataHelper.AmplitudeToDecibels(reference / valley));
    }

    // A short refinement window (~0.1 ms) around the envelope peak: wide enough to
    // absorb the envelope's sub-sample bias, narrow enough not to slide onto a
    // neighbouring reflection. The cap is in samples only as a backstop — at
    // 32 it no longer shrinks the window in TIME at high sample rates the way
    // the old cap of 8 did (192 kHz used to get ±0.04 ms instead of ~0.1 ms).
    private const double PhatSearchRadiusSeconds = 0.0001;

    private static int ComputePhatSearchRadius(int sampleRate) =>
        Math.Clamp((int)Math.Round(sampleRate * PhatSearchRadiusSeconds), 2, 32);

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
