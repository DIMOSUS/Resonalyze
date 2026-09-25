using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

public sealed class TimeAlignmentAnalysisOptions
{
    public bool UseBandpassWindow { get; init; }
    public double BandpassCenterHz { get; init; } = 1000;
    public double BandpassPassOctaves { get; init; } = 1;
    public double BandpassFadeOctaves { get; init; } = 0.5;
    /// <summary>First-arrival search depth below the band maximum. <see cref="AutoAlignmentEngine"/> derives its seed-veto threshold from it.</summary>
    public const double DefaultFirstPeakThresholdBelowMaxDb = 25;

    public double FirstPeakThresholdBelowMaxDb { get; init; } =
        DefaultFirstPeakThresholdBelowMaxDb;
    public double FirstPeakMinimumSnrDb { get; init; } = 12;
    public double PeakSearchWindowMilliseconds { get; init; } = 80;

    /// <summary>The signal is a COMPLETE circular deconvolved record, not a cut: peaks may wrap and transforms run unpadded (padding would fabricate a seam front).
    /// See docs/tech/auto-alignment.md#bandpass-padding.</summary>
    public bool WrapPeakPositions { get; init; }
}

public readonly record struct TimeAlignmentAnalysisResult(
    double[] EnvelopeSamples,
    int EnvelopePeakIndex,
    double EnvelopePeak,
    int StrongestEnvelopePeakIndex,
    double StrongestEnvelopePeak,
    // Strongest envelope peak vs the RMS of the quietest quarter: grades the measurement, not the pick.
    double SignalToNoiseDecibels,
    // First-arrival level vs the strongest peak (≤ 0 dB); kept apart from SNR so good woofer measurements do not read as fair.
    double FirstArrivalProminenceDecibels,
    double FirstArrivalPeakSample,
    double FirstArrivalDelayMilliseconds,
    double StrongestPeakSample,
    double StrongestDelayMilliseconds,
    double StrongestPeakSeparationMilliseconds,
    bool StrongestPeakIsSeparateArrival,
    // PHAT peak height [0, 1]; RefinedByPhat=false means the envelope parabola set the sample (coarse).
    double FirstArrivalConfidence,
    bool FirstArrivalRefinedByPhat,
    double StrongestConfidence,
    bool StrongestRefinedByPhat,
    // Energy onset: running envelope energy crossing EnergyOnsetFraction; an estimator for the low end where the first peak is a coin.
    // See docs/tech/auto-alignment.md#energy-onset.
    double EnergyOnsetSample = 0.0,
    double EnergyOnsetDelayMilliseconds = 0.0,
    // False for a zero-energy band: the peak walk would fabricate a delay. Invalid results report zeros.
    bool IsValid = true);

public readonly record struct TimeAlignmentArrivalProbe(
    AutoAlignmentEngine.ArrivalCertificate Certificate,
    TimeAlignmentAnalysisResult ProbeResult,
    double ProbeLowHz,
    double ProbeHighHz,
    double ToleranceMs);

public static class TimeAlignmentAnalysis
{
    // Guard in kernel periods and capped in SECONDS, so a high record rate cannot shrink it. See docs/tech/auto-alignment.md#bandpass-padding.
    private const double BandpassGuardCycles = 20.0;
    private const double MaxBandpassGuardSeconds = 2.0;

    /// <summary>Onset energy share and the tail past the strongest peak bounding "all" the energy. See docs/tech/auto-alignment.md#energy-onset.</summary>
    public const double EnergyOnsetFraction = 0.10;
    public const double EnergyOnsetTailSeconds = 0.060;

    /// <summary>Onset gate fixed under the peak, never the noise, so the read is a property of the signal; the consumer guards SNR.</summary>
    public const double EnergyOnsetGateDb = 30;

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

        Complex[]? recordSpectrum = null;
        double[]? cutSignal = null;
        double[] envelope;
        double[]? kernelEnvelope = null;
        if (options.WrapPeakPositions)
        {
            // A complete record is not padded, and one forward transform serves the mask, envelope and correlation (741 → 477 ms on 1 MB).
            recordSpectrum = new Complex[impulseResponse.Count];
            for (int i = 0; i < recordSpectrum.Length; i++)
            {
                recordSpectrum[i] = new Complex(impulseResponse[i], 0.0);
            }

            Fourier.Forward(recordSpectrum, FourierOptions.Matlab);
            if (options.UseBandpassWindow)
            {
                double[] window = BandpassWindow.Create(
                    recordSpectrum.Length,
                    sampleRate,
                    options.BandpassCenterHz,
                    options.BandpassPassOctaves,
                    options.BandpassFadeOctaves);
                for (int bin = 0; bin < recordSpectrum.Length; bin++)
                {
                    recordSpectrum[bin] *= window[bin];
                }

                kernelEnvelope = AlignmentRunMemo.KernelEnvelope(
                    window.Length, sampleRate, options, () => BuildKernelEnvelope(window));
            }

            envelope = SignalEnvelope.EnvelopeFromSpectrum(recordSpectrum);
        }
        else
        {
            cutSignal = options.UseBandpassWindow
                ? FilterCut(impulseResponse, sampleRate, options, out kernelEnvelope)
                : impulseResponse.ToArray();
            // Hilbert is circular too: pad here, not in SignalEnvelope.Envelope, whose periodic-signal contract padding would break.
            envelope = EnvelopeOfCrop(cutSignal);
        }

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

        // No energy in the search window: return an invalid result, not a fabricated delay.
        if (!(strongestPeak > 0.0) || !double.IsFinite(strongestPeak))
        {
            return new TimeAlignmentAnalysisResult(
                envelope, 0, 0.0, 0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
                false, 0.0, false, 0.0, false, IsValid: false);
        }

        // GCC-PHAT sharpens the envelope anchor to sub-sample; falls back to the envelope parabola when weak.
        PhaseTransformCorrelation phaseTransform = recordSpectrum is { } spectrum
            ? ComputePhaseTransform(spectrum, coherence)
            : TransferFunction.ComputePhaseTransformFromResponse(
                cutSignal!, coherence: coherence);
        int refineRadius = ComputePhatSearchRadius(sampleRate);
        RefinedArrival firstArrival = RefineArrivalSample(
            phaseTransform, envelope, envelopePeakIndex, refineRadius);
        RefinedArrival strongest = RefineArrivalSample(
            phaseTransform, envelope, strongestPeakIndex, refineRadius);
        double firstArrivalPeakSample = firstArrival.Sample;
        double strongestPeakSample = strongest.Sample;

        // A strongest peak clearly later than the first, past a real valley, is a reflection/mode. See docs/tech/auto-alignment.md#arrival-detector.
        // Distances in the search window's frame: a re-anchored window can straddle the buffer seam.
        int searchRotation = peakSearchResult.SearchRotation;
        int relativeFirst = RelativeToSearchWindow(
            envelopePeakIndex, searchRotation, envelope.Length);
        int relativeStrongest = RelativeToSearchWindow(
            strongestPeakIndex, searchRotation, envelope.Length);
        double signalToNoiseDb = SignalEnvelope.EstimatePeakConfidenceDecibels(
            envelope, strongestPeak);
        double energyOnsetSample = EnergyOnsetSample(
            envelope, searchRotation, relativeStrongest, sampleRate);
        double separationMilliseconds =
            (relativeStrongest - relativeFirst) * 1000.0 / sampleRate;
        double valleyDepthDb = ValleyDepthDb(
            envelope, relativeFirst, relativeStrongest, searchRotation);
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
            energyOnsetSample = ToSignedDelaySamples(
                energyOnsetSample,
                envelope.Length);
        }

        return new TimeAlignmentAnalysisResult(
            envelope,
            envelopePeakIndex,
            envelopePeak,
            strongestPeakIndex,
            strongestPeak,
            signalToNoiseDb,
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
            strongest.RefinedByPhat,
            energyOnsetSample,
            energyOnsetSample * 1000.0 / sampleRate);
    }

    // Read in the rotated search-window frame. The gate stops ~80 ms of pre-front noise from reaching the fraction first.
    private static double EnergyOnsetSample(
        IReadOnlyList<double> envelope,
        int rotation,
        int relativeStrongest,
        int sampleRate)
    {
        int length = envelope.Count;
        int tailSamples = (int)Math.Round(EnergyOnsetTailSeconds * sampleRate);
        int end = Math.Min(length - 1, relativeStrongest + tailSamples);
        double gate = envelope[(relativeStrongest + rotation) % length] *
            Math.Pow(10.0, -EnergyOnsetGateDb / 20.0);
        double Power(int i)
        {
            double value = envelope[(i + rotation) % length];
            return value >= gate ? value * value : 0.0;
        }

        double total = 0.0;
        for (int i = 0; i <= end; i++)
        {
            total += Power(i);
        }

        if (!(total > 0.0) || !double.IsFinite(total))
        {
            return (relativeStrongest + rotation) % length;
        }

        double target = EnergyOnsetFraction * total;
        double previous = 0.0;
        double running = 0.0;
        double relative = end;
        for (int i = 0; i <= end; i++)
        {
            running += Power(i);
            if (running >= target)
            {
                double step = running - previous;
                double fraction = step > 0.0 ? (target - previous) / step : 1.0;
                relative = Math.Max(0.0, i - 1 + fraction);
                break;
            }

            previous = running;
        }

        double original = relative + rotation;
        return original >= length ? original - length : original;
    }

    /// <summary>Manual-mode arrival honesty probe: the upper half of the pass band re-read and the full read graded against it.
    /// Null without a bandpass window or when the upper half is too narrow.</summary>
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
        double toleranceMs = Math.Max(1.0, 500.0 / probeLowHz);
        return new TimeAlignmentArrivalProbe(
            AutoAlignmentEngine.ClassifyArrival(fullResult, probeResult, toleranceMs),
            probeResult,
            probeLowHz,
            f3,
            toleranceMs);
    }

    // Below this PHAT peak height the envelope parabola stands (e.g. too few in-band periods).
    private const double PhatTrustCoefficient = 0.2;

    // Shared with the peak search: inside one packet span a weak candidate is the packet's foot.
    private const double SeparateArrivalThresholdMilliseconds =
        SignalEnvelope.ArrivalPacketMilliseconds;

    private const double SeparateArrivalValleyDb = 6.0;

    // Destructive interference nulls faster than 1/BW, so a valley this deep proves resolved events.
    private const double SeparateArrivalResolvedValleyDb =
        SignalEnvelope.ArrivalPacketResolvedValleyDb;

    private static int RelativeToSearchWindow(int index, int rotation, int length) =>
        ((index - rotation) % length + length) % length;

    // Dip in dB below the lower peak (≥ 0), walked in the search window's frame.
    private static double ValleyDepthDb(
        IReadOnlyList<double> envelope,
        int relativeFirstIndex,
        int relativeSecondIndex,
        int rotation)
    {
        int from = Math.Min(relativeFirstIndex, relativeSecondIndex);
        int to = Math.Max(relativeFirstIndex, relativeSecondIndex);
        double valley = double.MaxValue;
        for (int i = from; i <= to; i++)
        {
            valley = Math.Min(valley, envelope[(i + rotation) % envelope.Count]);
        }

        double reference = Math.Min(
            envelope[(relativeFirstIndex + rotation) % envelope.Count],
            envelope[(relativeSecondIndex + rotation) % envelope.Count]);
        if (reference <= 0.0 || valley <= 0.0)
        {
            return valley <= 0.0 && reference > 0.0 ? double.PositiveInfinity : 0.0;
        }

        return Math.Max(0.0, DataHelper.AmplitudeToDecibels(reference / valley));
    }

    // ~0.1 ms: absorbs envelope bias without sliding onto a reflection; the sample cap is only a backstop.
    private const double PhatSearchRadiusSeconds = 0.0001;

    private static int ComputePhatSearchRadius(int sampleRate) =>
        Math.Clamp((int)Math.Round(sampleRate * PhatSearchRadiusSeconds), 2, 32);

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


    // A CUT is filtered zero-padded (kernel-sized guard, then power of two) and trimmed back: unpadded, the tail wraps onto the head.
    // See docs/tech/auto-alignment.md#bandpass-padding.
    private static double[] FilterCut(
        IReadOnlyList<double> impulseResponse,
        int sampleRate,
        TimeAlignmentAnalysisOptions options,
        out double[] kernelEnvelope)
    {
        int transformLength = DspMath.NextPowerOfTwo(
            impulseResponse.Count + BandpassGuardSamples(sampleRate, options));
        var padded = new double[transformLength];
        for (int i = 0; i < impulseResponse.Count; i++)
        {
            padded[i] = impulseResponse[i];
        }

        double[] window = BandpassWindow.Create(
            transformLength,
            sampleRate,
            options.BandpassCenterHz,
            options.BandpassPassOctaves,
            options.BandpassFadeOctaves);
        double[] filtered = BandpassWindow.Apply(padded, window);
        kernelEnvelope = AlignmentRunMemo.KernelEnvelope(
            window.Length, sampleRate, options, () => BuildKernelEnvelope(window));
        return filtered.Length == impulseResponse.Count
            ? filtered
            : filtered[..impulseResponse.Count];
    }

    // Shares the record's transform only when its length is the correlation's power of two; otherwise the padded route.
    private static PhaseTransformCorrelation ComputePhaseTransform(
        Complex[] recordSpectrum,
        IReadOnlyList<double>? coherence)
    {
        int length = recordSpectrum.Length;
        if (DspMath.NextPowerOfTwo(length) == length)
        {
            return TransferFunction.ComputePhaseTransformFromSpectrum(
                recordSpectrum, coherence: coherence);
        }

        var filtered = (Complex[])recordSpectrum.Clone();
        Fourier.Inverse(filtered, FourierOptions.Matlab);
        var signal = new double[length];
        for (int i = 0; i < length; i++)
        {
            signal[i] = filtered[i].Real;
        }

        return TransferFunction.ComputePhaseTransformFromResponse(
            signal, coherence: coherence);
    }

    // Hilbert kernel decays to -80 dB within a few thousand samples, so half a buffer of padding is ample.
    private static double[] EnvelopeOfCrop(double[] signal)
    {
        int transformLength = DspMath.NextPowerOfTwo(signal.Length + signal.Length / 2);
        if (transformLength == signal.Length)
        {
            return SignalEnvelope.Envelope(signal);
        }

        var padded = new double[transformLength];
        Array.Copy(signal, padded, signal.Length);
        double[] envelope = SignalEnvelope.Envelope(padded);
        return envelope[..signal.Length];
    }

    /// <summary>Silence the bandpass kernel needs so its skirt decays inside the transform. See docs/tech/auto-alignment.md#bandpass-padding.</summary>
    private static int BandpassGuardSamples(
        int sampleRate,
        TimeAlignmentAnalysisOptions options)
    {
        int maxSamples = (int)(MaxBandpassGuardSeconds * sampleRate);
        (double fadeStartHz, double passStartHz, _, _) = BandpassWindow.BandAround(
            options.BandpassCenterHz,
            options.BandpassPassOctaves,
            options.BandpassFadeOctaves);
        // A brick wall rings longer than a fade: take whichever edge is lower.
        double lowestHz = fadeStartHz > 0 ? fadeStartHz : passStartHz;
        if (!double.IsFinite(lowestHz) || lowestHz <= 0)
        {
            return maxSamples;
        }

        double samples = BandpassGuardCycles * sampleRate / lowestHz;
        return samples >= maxSamples
            ? maxSamples
            : (int)Math.Ceiling(samples);
    }

    // Zero-phase mask response by |offset|: bounds the window's own pre-ring, separating it from a real earlier arrival.
    private static double[] BuildKernelEnvelope(double[] window)
    {
        var spectrum = new Complex[window.Length];
        for (int i = 0; i < window.Length; i++)
        {
            spectrum[i] = new Complex(window[i], 0.0);
        }

        return SignalEnvelope.EnvelopeFromSpectrum(spectrum);
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
