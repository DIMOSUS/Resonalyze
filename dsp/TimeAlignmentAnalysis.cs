using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

public sealed class TimeAlignmentAnalysisOptions
{
    public bool UseBandpassWindow { get; init; }
    public double BandpassCenterHz { get; init; } = 1000;
    public double BandpassPassOctaves { get; init; } = 1;
    public double BandpassFadeOctaves { get; init; } = 0.5;
    /// <summary>
    /// How far below the band maximum the first-arrival search looks. A soft
    /// direct rise under a strong in-room build-up is a real front, and this
    /// depth is what finds it. Exposed as a constant because
    /// <see cref="AutoAlignmentEngine"/> derives a threshold from it: a pick
    /// in the lower half of this range is a different feature from the band's
    /// energy, and the two must move together if this ever changes.
    /// </summary>
    public const double DefaultFirstPeakThresholdBelowMaxDb = 25;

    public double FirstPeakThresholdBelowMaxDb { get; init; } =
        DefaultFirstPeakThresholdBelowMaxDb;
    public double FirstPeakMinimumSnrDb { get; init; } = 12;
    public double PeakSearchWindowMilliseconds { get; init; } = 80;

    /// <summary>
    /// The caller's statement that the signal is a COMPLETE deconvolved record
    /// — circular by construction, its tail continuous with its head — rather
    /// than a CUT of a longer one. It does two things that must stay together:
    /// peak positions may wrap around the buffer, and the spectral transforms
    /// (the bandpass, the Hilbert envelope) run at the record's own length,
    /// because circular convolution is EXACT for a circular signal. Zero
    /// padding such a record would manufacture a discontinuity at the seam
    /// and its edge transient reads as structure: on a field subwoofer whose
    /// transfer IR carries a DC shelf, the padded envelope dipped 9 dB at the
    /// record start and "rose" back — a climb the first-arrival search rightly
    /// accepted as a front, at 0.2 ms on a record whose driver first played at
    /// 13 ms. A cut (the default) is the opposite case: its tail is NOT its
    /// head's past, and filtering it unpadded wraps the tail onto the head —
    /// see the padding note in <see cref="TimeAlignmentAnalysis.Analyze"/>.
    /// </summary>
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
    // however clean the recording. Kept apart from the SNR above: folding the
    // two into one "quality" figure misreads great woofer measurements as fair.
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
    // The band's ENERGY ONSET: where the running energy of the envelope (its
    // square, summed from the start of the search window) first reaches
    // <see cref="TimeAlignmentAnalysis.EnergyOnsetFraction"/> of the energy
    // inside the window that ends <see cref="TimeAlignmentAnalysis.EnergyOnsetTailSeconds"/>
    // after the strongest peak. Sub-sample (the crossing is interpolated
    // inside its sample), in the same coordinates as FirstArrivalPeakSample.
    // A DIFFERENT estimator from the first peak, for the band-limited low end
    // where the first peak is a coin: a direct front of rise time ~1/bandwidth
    // and an arrival a few milliseconds behind it merge into one long climb
    // with a shoulder, and whether that shoulder turns into a local maximum
    // is decided by a fraction of a dB. Measured on a midbass pair in
    // 65-200 Hz: the left envelope dipped 0.5 dB after its first hump and
    // read 14.4 ms, the right never dipped and read 21.3 ms — a 7 ms split no
    // cabin geometry produces — while the running energy, monotone by
    // construction, put the two onsets 2.2 ms apart (1.1 ms on the raw
    // responses, the owner's hand-tuned split). The same idea is the Hinkley
    // criterion of acoustic-emission onset picking and the energy-ratio
    // detectors that won the room-impulse-response onset comparison of
    // Defrance, Daudet and Polack (JASA 2008) over every maximum-based method.
    double EnergyOnsetSample = 0.0,
    double EnergyOnsetDelayMilliseconds = 0.0,
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
    // Periods of the kernel's lowest frequency to keep clear beside the
    // signal (see BandpassGuardSamples), and the ceiling on that guard — in
    // SECONDS, so a higher record rate cannot silently shrink the guard below
    // its own stated need. A fixed sample count looked equivalent at 48 kHz
    // and starved the same band at 384 kHz, where 262,144 samples buys only
    // 9.4 of the ~15 cycles the 27.5-110 Hz kernel needs. Two seconds is the
    // full twenty cycles at 10 Hz — the lowest fade start a band-limited read
    // can produce (its low edge clamps at 20 Hz, and the octave fade halves
    // it) — so only a caller asking directly for a band below that meets the
    // cap, which is what keeps a pathological transform bounded.
    private const double BandpassGuardCycles = 20.0;
    private const double MaxBandpassGuardSeconds = 2.0;

    /// <summary>
    /// The share of the windowed band energy at which the ENERGY ONSET is read
    /// (see <see cref="TimeAlignmentAnalysisResult.EnergyOnsetDelayMilliseconds"/>),
    /// and how far past the strongest peak the window that defines "all" of the
    /// energy runs. Measured on the field midbass pair (65-200 Hz, raw and
    /// processed responses): the L/R onset split is flat from 2 % to 15 % of
    /// the energy (0.75-1.11 ms raw, 2.2-2.4 ms processed), then slides into
    /// the second hump of the envelope (0.46 ms at 20 %, sign flip at 30 %) —
    /// 10 % sits in the middle of the plateau with margin to both cliffs. The
    /// tail bound makes the total independent of the record's reverberant
    /// length: on those bands 99.5 % of the energy sits within 80 ms of the
    /// front, and the onset moved by at most 0.15 ms between a whole-record
    /// total and a 50 ms one (0.4 ms on the subwoofer's 33-130 Hz band, where
    /// the modal tail is longest).
    /// </summary>
    public const double EnergyOnsetFraction = 0.10;
    public const double EnergyOnsetTailSeconds = 0.060;

    /// <summary>
    /// The energy onset's gate: envelope samples this far under the strongest
    /// peak contribute nothing. FIXED against the peak, never against the
    /// record's noise: the onset must be a property of the signal alone, or
    /// two identical drivers measured with different noise would read
    /// different fronts (a gate at the noise floor moves up the front as the
    /// SNR falls, and at the 12 dB admission floor it would sit on the peak
    /// itself). What the gate is for is the noise far below the front — the
    /// window ahead of the peak is ~80 ms long and even a quiet floor
    /// integrates over it — so it sits where a Rayleigh floor 40 dB down
    /// clears it in a fraction of a percent of samples, and the CONSUMER
    /// guards the SNR: <see cref="AutoAlignmentEngine.EnergyOnsetMinimumSnrDb"/>.
    /// </summary>
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

        // The whitened correlation below reads the analysis SPECTRUM on the
        // complete-record path, where every stage runs at the record's own
        // length, and the filtered SIGNAL on the cut path, whose stages run at
        // three lengths of their own with a trim back to the caller's frame
        // between them.
        Complex[]? recordSpectrum = null;
        double[]? cutSignal = null;
        double[] envelope;
        double[]? kernelEnvelope = null;
        if (options.WrapPeakPositions)
        {
            // A COMPLETE record (see the flag's doc) is the one signal that must
            // NOT be padded: it is circular by construction, so the unpadded
            // transform is exact for it and the padding's seam transient is what
            // reads as a fabricated arrival.
            //
            // At that one length a SINGLE forward transform serves the whole
            // read: the band mask multiplies the spectrum in place, and the
            // envelope, the whitened correlation and the mask's own ringing all
            // come off it. The band-limited SIGNAL is never materialized —
            // building it and transforming it back twice (once for the
            // envelope, once for the correlation) was five transforms of the
            // full record length that returned the spectrum already in hand.
            // The Time Alignment panel is this caller, and a megabyte of
            // transfer IR is where it shows: 741 ms a read against 477.
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

                kernelEnvelope = BuildKernelEnvelope(window);
            }

            envelope = SignalEnvelope.EnvelopeFromSpectrum(recordSpectrum);
        }
        else
        {
            cutSignal = options.UseBandpassWindow
                ? FilterCut(impulseResponse, sampleRate, options, out kernelEnvelope)
                : impulseResponse.ToArray();
            // The analytic signal is a spectral operation too, and just as
            // circular: an unpadded Hilbert transform folds the crop's tail onto
            // its own head exactly as the bandpass does, and it is the ENVELOPE
            // the first-arrival search walks. Padded here rather than inside
            // SignalEnvelope.Envelope, which has a real contract for a signal
            // periodic in its window (a bin-centred cosine must come back with a
            // flat envelope, and padding would break that correctly) — this
            // caller is the one that knows whether it holds a CUT or a complete
            // circular record, which keeps its envelope exactly as circular as
            // the record is.
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
        // Distances are measured in the SEARCH WINDOW's frame: when the window
        // re-anchored on a far peak (chain latency beyond its reach), the two
        // indices may straddle the circular buffer's seam, and their raw
        // difference would read as a buffer-length gap.
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

    // The energy onset (see the result field): the running energy of the
    // envelope, read in the SEARCH WINDOW's frame from its start, crossing
    // EnergyOnsetFraction of the energy accumulated up to EnergyOnsetTailSeconds
    // past the strongest peak. The crossing is interpolated inside its sample
    // and mapped back through the window rotation. Reads the rotated frame for
    // the same reason the separation does: a re-anchored window straddles the
    // buffer seam, and the contiguous geometry lives around the peak.
    //
    // Samples under the gate (EnergyOnsetGateDb under the strongest peak)
    // contribute nothing. Without a gate the onset is a function of how much
    // record precedes the front: noise power is small per sample but the
    // window in front of the peak is ~80 ms long, so at 20 dB SNR the noise
    // ahead of a 7 ms packet holds about a fifth of the total and the tenth
    // is reached in the noise, at the window's start. This is the Hinkley
    // criterion's detrending in gate form — with the gate fixed to the signal,
    // not the noise, so the read does not move with the record's SNR.
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
    // smeared direct sound. The peak search reads the same span from the other
    // side — inside it, a candidate far below the packet's peak is that packet's
    // foot, not an arrival — so both live on one constant.
    private const double SeparateArrivalThresholdMilliseconds =
        SignalEnvelope.ArrivalPacketMilliseconds;

    // How deep the envelope must dip between the two peaks before they count as
    // separate arrivals: two events have a real valley between them, one broad
    // rise does not.
    private const double SeparateArrivalValleyDb = 6.0;

    // A valley this deep proves the two events resolved even when their
    // separation sits inside the analysis band's nominal ~1/BW blur —
    // destructive interference nulls faster than the envelope's rise time. The
    // peak search ends a candidate's packet at the same null, for the same
    // reason, so both live on one constant.
    private const double SeparateArrivalResolvedValleyDb =
        SignalEnvelope.ArrivalPacketResolvedValleyDb;

    // A peak position expressed in the search window's frame: its offset from
    // the window's start, which for a re-anchored (rotated) window is where
    // the contiguous around-the-peak geometry lives. With no rotation this is
    // the index itself.
    private static int RelativeToSearchWindow(int index, int rotation, int length) =>
        ((index - rotation) % length + length) % length;

    // The envelope dip between the two peaks, in dB below the LOWER of them
    // (>= 0; 0 when the envelope never dips). Peak positions arrive in the
    // search window's frame; envelope reads map back through the rotation, so
    // the walk follows the window's contiguous geometry across the buffer seam.
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

    // A short refinement window (~0.1 ms) around the envelope peak: wide enough to
    // absorb the envelope's sub-sample bias, narrow enough not to slide onto a
    // neighbouring reflection. The cap is in samples only as a backstop: at 32 it
    // does not shrink the window in TIME at high rates the way a tighter cap
    // would (a cap of 8 gives 192 kHz ±0.04 ms instead of ~0.1 ms).
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


    // The band-limited signal of a CUT, in the caller's own frame.
    //
    // Filtered on a ZERO-PADDED buffer, then trimmed back, so every index the
    // read produces is in that frame. BandpassWindow.Apply says why in its own
    // words: the transform is circular, and this signal is a CUT of a longer
    // record (a channel's valid range), so an unpadded filter wraps the tail
    // onto the head. Not a rounding artifact — a lone impulse 64 samples from
    // the end of a 32768-sample buffer puts 93 % of its own peak into the first
    // 40 ms, which the arrival search then reads as a front (see
    // BandpassWrapTests).
    //
    // The padding is sized by the KERNEL, never by rounding alone: a guard
    // first, and only then the rise to a power of two. Rounding alone would
    // leave a length that is already a power of two — what ChainValidRange
    // hands out whenever the chain delay is a whole number of samples, zero
    // included — with no guard at all, and a length one short of one with a
    // single sample of it.
    //
    // Landing on a power of two is worth doing anyway: MathNet is quick only
    // there and falls back to Bluestein otherwise, measured 20 ms at 262144
    // samples against 317 ms at 262145.
    //
    // Three lengths live in this path on purpose — the mask's, the envelope's
    // (EnvelopeOfCrop) and the correlation's — with a trim back to the caller's
    // frame between them, which is why a cut cannot share one spectrum the way
    // a complete record does.
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
        // Indexed by DISTANCE from the kernel's centre, so the padded window's
        // longer, finer curve answers the same question the short one did.
        kernelEnvelope = BuildKernelEnvelope(window);
        return filtered.Length == impulseResponse.Count
            ? filtered
            : filtered[..impulseResponse.Count];
    }

    // The whitened correlation the arrivals are refined against. On the
    // complete-record path it shares the forward transform the read already
    // made, whenever the record's length is the one the correlation runs at; a
    // length that is not a power of two makes the correlation pad to the next
    // one — a DIFFERENT bin grid — so that case reconstructs the band-limited
    // signal and takes the padded route it always did.
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

    // The magnitude envelope of a CUT: computed on a zero-padded copy and
    // trimmed back, so the Hilbert transform's own circularity cannot carry the
    // tail round to the head. Half a buffer of silence is ample — the analytic
    // signal's kernel is 1/(pi*n), decayed to -80 dB within a few thousand
    // samples, unlike the bandpass mask above whose reach is set by its lowest
    // frequency.
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

    /// <summary>
    /// How much silence the bandpass kernel needs beside the signal so its own
    /// skirt decays inside the transform instead of around it.
    /// </summary>
    /// <remarks>
    /// The kernel reaches out by periods of the frequency its lower fade STARTS
    /// at — an octave under the passband when a fade is asked for — so the guard
    /// is counted in those periods rather than in samples: the same crop wraps
    /// harmlessly at 3.5 kHz and catastrophically at 27.5 Hz. Measured on this
    /// window, the kernel needs 13 to 18 of those periods to decay past −120 dB
    /// (20 Hz–110 Hz: 13.2, 27.5–110: 14.9, 110–290: 17.2, 3.5 k–20 k: 17.9), so
    /// twenty of them carries margin at every band the analysis is asked for.
    /// The cap exists for the same reason <see cref="VirtualCrossoverAnalysis"/>
    /// caps its own filter-tail padding — a pathological band may not size the
    /// transform without bound — and it is stated in seconds so it can never
    /// undercut the cycle count at a high record rate (see
    /// <see cref="MaxBandpassGuardSeconds"/>).
    /// </remarks>
    private static int BandpassGuardSamples(
        int sampleRate,
        TimeAlignmentAnalysisOptions options)
    {
        int maxSamples = (int)(MaxBandpassGuardSeconds * sampleRate);
        (double fadeStartHz, double passStartHz, _, _) = BandpassWindow.BandAround(
            options.BandpassCenterHz,
            options.BandpassPassOctaves,
            options.BandpassFadeOctaves);
        // With no fade the mask starts at the passband edge, and a brick wall
        // rings longer than a faded one rather than shorter — so the guard is
        // taken from whichever edge is lower, never from a zero.
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

    // The time response of the zero-phase bandpass mask, as an analytic
    // envelope indexed by |offset| from the kernel centre. The kernel is real
    // and even, so its IFFT sits centred at index 0 and the envelope's first
    // half is exactly the by-offset curve the sidelobe rejection needs: an
    // arrival can pre-ring at a given distance no louder than this envelope
    // says, which is what separates the window's own ringing from a genuine
    // earlier arrival.
    private static double[] BuildKernelEnvelope(double[] window)
    {
        // The kernel's forward spectrum IS the mask — it is real and even — so
        // the envelope reads straight off it, instead of transforming the mask
        // into a kernel only to transform that kernel back.
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
