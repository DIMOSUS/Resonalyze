using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// The contiguous frequency region where a transfer IR actually carries the
/// driver's energy: the 1/6-octave-smoothed magnitude spectrum's span within
/// <see cref="TransferIrDiagnostics.DominantBandThresholdDb"/> of its peak.
/// </summary>
public readonly record struct DominantBand(double LowHz, double HighHz, double PeakHz);

/// <summary>
/// A detected pre-arrival crosstalk artifact and the head gate that removes
/// it: zero everything before <see cref="GateEndSample"/> (with a short fade
/// after it). Burst figures are diagnostics for the UI/log.
/// </summary>
public readonly record struct CrosstalkHeadGate(
    int GateEndSample,
    double BurstTimeMs,
    double BurstPeakDbReMax);

/// <summary>
/// The estimated start of a measured IR's honest acoustic content: the 25 %
/// rising-front crossing (<see cref="StartMs"/>, the working figure) of the
/// first credible arrival's envelope, read INSIDE the record's dominant band
/// (see <see cref="TransferIrDiagnostics.EstimateIrStart"/>). The 10 %
/// (<see cref="EarlyMs"/>) and 50 % (<see cref="LateMs"/>) crossings bound
/// it; their spread is the front's sharpness — a direct sound keeps them
/// within a fraction of a millisecond, a modal low-frequency build-up
/// spreads them over milliseconds — and is the honesty figure a caller
/// showing one number should surface. <see cref="DominantBandLimited"/> is
/// false when the dominant-band read was invalid and the figures fall back
/// to the full-band envelope (which a head artifact CAN poison — see the
/// estimator's contract).
/// </summary>
public readonly record struct IrStartEstimate(
    double StartMs,
    double EarlyMs,
    double LateMs,
    double BandLowHz,
    double BandHighHz,
    bool DominantBandLimited);

/// <summary>
/// The time-compactness of a transfer IR: how far (dB) the per-sample energy
/// of a short circular window around the strongest peak sits above the
/// per-sample energy of everything outside it. A genuine impulse response is
/// a localized event — the direct sound plus a cabin decay of at most a few
/// hundred milliseconds — while the "IR" built from an unusable reference
/// (a loopback that was bleed instead of the wire) is stationary division
/// noise across the whole capture. <see cref="PeakDelayMs"/> is the peak's
/// circular delay, signed: a peak past the buffer midpoint reads as negative
/// (acausal) time — a diagnostic, not a verdict, because a purely electrical
/// chain measurement legitimately peaks at zero.
/// </summary>
public readonly record struct TransferIrCompactness(
    double InsideOutsideDb,
    double PeakDelayMs);

/// <summary>
/// What sits in a transfer IR's pre-arrival window (see
/// <see cref="TransferIrDiagnostics.MeasurePreArrivalDb"/>).
/// <see cref="LevelDb"/> is how far its per-sample energy stands under the
/// arrival's own neighbourhood — the verdict figure. <see cref="CrestDb"/> is
/// the window's own crest factor, which says whether that energy is a stationary
/// ring or one discrete event that the peak-relative window happened to enclose,
/// and so whether the level is worth refusing over.
/// </summary>
public readonly record struct TransferIrPreArrival(
    double LevelDb,
    double CrestDb);

/// <summary>
/// Record-hygiene diagnostics shared by the manual Time Alignment mode and
/// the auto-delay launcher: where the driver's energy actually lives in
/// frequency, and whether the record's head carries a playback-crosstalk
/// click (field evidence: a broadband spike at one fixed sample in every
/// record of a session — an electrical copy of the playback that lands
/// before any physically possible acoustic arrival and, on band-limited
/// low-frequency records, sits within the first-arrival detector's
/// threshold).
/// </summary>
public static class TransferIrDiagnostics
{
    /// <summary>
    /// The compactness floor below which a transfer IR is not a credible
    /// impulse response. Calibration at the 100 ms/500 ms window, field sets
    /// (14 records, two cabins) plus synthetic transfers pushed through the
    /// production excitation gate: genuine measurements read 28.8-48.6 dB,
    /// ideal band-limited transfers (even a 20-50 Hz band sweep) 42.9+ dB,
    /// while a session whose loopback was playback bleed instead of the wire
    /// read 11.2-18.7 dB and gated uncorrelated noise ~0 dB. 22 dB sits
    /// 3.3 dB above the worst field garbage and 6.8 dB below the weakest
    /// genuine record — deliberately closer to the garbage side, so a noisy
    /// but real capture is not refused.
    /// </summary>
    public const double MinimumCompactnessDb = 22.0;

    /// <summary>
    /// Half-width of the neighbourhood <see cref="MeasureArrivalSharpnessDb"/>
    /// compares the arrival against: long enough to span a smear from the wrong
    /// excitation, short enough that a room's decay contributes little of it.
    /// </summary>
    public const double ArrivalWindowSeconds = 0.002;

    /// <summary>
    /// Where the pre-arrival window starts ahead of the peak: the same 100 ms the
    /// compactness window reserves for the band-limited kernel's pre-ringing.
    /// </summary>
    public const double PreArrivalStartSeconds = 0.100;

    /// <summary>
    /// Where the pre-arrival window ends. Half a second of it, so a slowly decaying
    /// ring is read where it is still strong rather than at its own tail.
    /// </summary>
    public const double PreArrivalEndSeconds = 0.600;

    /// <summary>
    /// The pre-arrival reading above which a transfer IR is not a measurement of a
    /// room but of a reference that cancelled itself.
    /// <para>
    /// Calibration, on ideal gated kernels at every supported band plus field
    /// records from two rigs: an ideal half-octave-guarded kernel reads -26.8 dB at
    /// the very worst (a 20-25 Hz band sweep) and -34.1 dB at 20-50 Hz, genuine
    /// field records read -39.0 to -47.3 dB, and the two records taken through a
    /// loopback that ran through an interface's direct mixer read -14.8 and
    /// -14.1 dB. -18 dB sits 3.2 dB above the worst of those and 8.8 dB below the
    /// weakest legitimate shape — the same shape of margin
    /// <see cref="MinimumCompactnessDb"/> carries, and for the same reason: a real
    /// capture must not be refused to catch a garbage one.
    /// </para>
    /// </summary>
    public const double MaximumPreArrivalDb = -18.0;

    /// <summary>
    /// The pre-arrival reading a record has to stay under to be published without a
    /// word. Between this and <see cref="MaximumPreArrivalDb"/> the record is saved
    /// and REPORTED rather than refused.
    /// <para>
    /// Scaled by imposing a magnitude-only resonance of growing depth at 34.5 Hz,
    /// Q 40, on a clean field record — an acausal ring of controllable size, not a
    /// claim about how the field fault arose: 6 dB reads -28.8 dB, 9 dB reads
    /// -24.5, 12 dB reads -21.0, 16 dB reads -17.1. -22 dB therefore starts
    /// reporting around 11 dB of it, well before the fault costs a session, and
    /// still leaves 4.8 dB to the worst legitimate shape there is (the -26.8 dB
    /// kernel above).
    /// </para>
    /// </summary>
    public const double SuspectPreArrivalDb = -22.0;

    /// <summary>
    /// The low guard band, in octaves, below which <see cref="CanJudgePreArrival"/>
    /// withholds the pre-arrival verdict.
    /// </summary>
    public const double MinimumJudgeableGuardOctaves = 0.30;

    /// <summary>
    /// The crest factor at or above which the pre-arrival window is holding a
    /// discrete EVENT rather than a stationary ring, and a reading over
    /// <see cref="MaximumPreArrivalDb"/> is reported instead of refused.
    /// </summary>
    /// <remarks>
    /// The window is placed relative to the strongest sample, which every measure
    /// in this file treats as the arrival. A record whose direct path is obstructed
    /// and whose strongest sample is therefore a reflection MORE than
    /// <see cref="PreArrivalStartSeconds"/> later breaks that assumption: its real
    /// direct sound then sits inside the pre-arrival window and reads as acausal
    /// energy. Measured, at 48 kHz with a 200 ms gap, a direct carrying 4 % of the
    /// reflection's energy reads -18.0 dB while clearing the compactness floor at
    /// 23.1 dB — a refusal for a record that is merely awkward.
    /// <para>
    /// The two are told apart by WHAT fills the window. A discrete arrival with its
    /// own decay concentrates its energy; the fault is a ring that fills the window
    /// evenly, and the two field records read 13.1 and 12.8 dB of crest. Swept over
    /// 215 two-arrival records at bands this verdict is allowed for (see
    /// <see cref="MinimumRefusableSpanOctaves"/>) — first arrival 2-20 % of the
    /// later one's energy, gaps of 150-400 ms, decays of 0.10 and 0.25 s — the
    /// LOWEST crest a discrete event produced was 18.3 dB. 16 dB sits 2.9 dB above
    /// the field fault and 2.3 dB below that worst event.
    /// </para>
    /// <para>
    /// Thinner than the margins the refusals here carry, which is exactly why it
    /// decides only whether to REFUSE or to REPORT: landing on the wrong side of it
    /// costs a message, never a measurement.
    /// </para>
    /// <para>
    /// Anchoring on the first credible arrival instead was tried and does not
    /// work: <see cref="EstimateIrStart"/> answers 499.9 ms on such a record (the
    /// later, stronger arrival, not the direct 200 ms ahead of it) and 0.0 ms on
    /// both field records, where the acausal ring reaches the head of the buffer.
    /// Anchoring there also costs the fault 1.7 dB of the level margin.
    /// </para>
    /// </remarks>
    public const double PreArrivalCrestDb = 16.0;

    /// <summary>
    /// How wide the excitation has to be, in octaves, before a pre-arrival reading
    /// over <see cref="MaximumPreArrivalDb"/> may be REFUSED rather than reported.
    /// </summary>
    /// <remarks>
    /// Crest is a statement about time, and a band-limited record cannot hold a
    /// sharper event than its own bandwidth allows: the narrower the excitation,
    /// the more a single arrival smears until it is indistinguishable from a ring.
    /// Measured on the same two-arrival sweep, the lowest crest a discrete event
    /// reached falls with the band — 21.6 dB at 7.6 octaves, 18.3 at 5.9, 17.6 at
    /// 4.9, 16.1 at 4.3, and by 2.3 octaves it is 13.3 dB, inside the range the
    /// field fault itself occupies. Five octaves is where the margin appears and
    /// stays: every band at or above it read 18.3 dB or more. Below it the reading
    /// is still made and still REPORTED — it is only the refusal that is withheld,
    /// because there the two shapes genuinely cannot be told apart.
    /// <para>
    /// Both field records that provoked this gate were measured over 6.6 and 11
    /// octaves, so the refusal still reaches them.
    /// </para>
    /// </remarks>
    public const double MinimumRefusableSpanOctaves = 5.0;

    /// <summary>
    /// The sharpness floor below which a transfer IR is not a measurement of the
    /// sweep it was analyzed against.
    /// <para>
    /// Calibrated on two field takes of one exported sweep — a car cabin and a
    /// room — each analyzed against its own (correct) parameters and against six
    /// neighbouring wrong ones: correct reads 15.5 dB (cabin) and 10.7 dB (room),
    /// while every mismatched pace reads 3.1-6.3 dB. Eight sits 1.7 dB above the
    /// worst mismatch, on the garbage side of the gap, in the same spirit as
    /// <see cref="MinimumCompactnessDb"/>.
    /// </para>
    /// <para>
    /// Sharpness rather than the share of energy near the arrival, which was
    /// tried first and rejected: a synthetic record with a quarter-second decay
    /// held only 12 % of its energy within the arrival window — indistinguishable
    /// from a smear — so that measure would refuse a reverberant room outright.
    /// </para>
    /// </summary>
    public const double MinimumArrivalSharpnessDb = 8.0;

    // The compactness window around the peak, CIRCULAR because the window
    // must follow the buffer's topology: a zero-phase excitation gate turns
    // even an ideal H(f)=1 into a symmetric band-limited kernel whose
    // pre-ringing lives in negative time, i.e. at the buffer's far end. The
    // pre side is sized for that ringing at the lowest supported band edges
    // (10 ms cut a 20-50 Hz band sweep's ideal kernel down to 19.9 dB —
    // a false rejection; 100 ms reads it at 42.9 dB) and the post side for
    // any cabin decay plus processing latency. Both shrink on short records
    // so the outside stays the majority of the buffer.
    private const double CompactnessPreSeconds = 0.100;
    private const double CompactnessPostSeconds = 0.500;
    private const int CompactnessMinimumSamples = 256;

    /// <summary>
    /// How far (dB) a transfer IR's strongest sample stands above the RMS of its
    /// own neighbourhood, <see cref="ArrivalWindowSeconds"/> either side and
    /// measured circularly. Null when there is nothing (or nothing finite) to
    /// measure.
    /// <para>
    /// This answers a question <see cref="MeasureCompactness"/> cannot: whether
    /// the excitation the IR was derived from is the one that was actually
    /// played. A sweep deconvolved against a DIFFERENT sweep still produces a
    /// localized-looking result on the compactness window — its ±100/+500 ms
    /// span happily contains a smear a hundred milliseconds wide — so a
    /// mismatched band or per-octave pace passes that gate, and on field takes it
    /// sometimes scored HIGHER than the correct one. A peak, unlike a smear,
    /// stands above its own immediate surroundings, and a room's decay barely
    /// enters those two milliseconds.
    /// </para>
    /// </summary>
    public static double? MeasureArrivalSharpnessDb(
        IReadOnlyList<Complex> impulseResponse,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        int length = impulseResponse.Count;
        if (length == 0 || sampleRate <= 0)
        {
            return null;
        }

        double total = 0;
        double peak = 0;
        int peakIndex = 0;
        for (int i = 0; i < length; i++)
        {
            double sample = impulseResponse[i].Real;
            total += sample * sample;
            if (Math.Abs(sample) > peak)
            {
                peak = Math.Abs(sample);
                peakIndex = i;
            }
        }
        if (!double.IsFinite(total) || total <= 0)
        {
            return null;
        }

        int half = Math.Min((int)(ArrivalWindowSeconds * sampleRate), length / 2);
        double near = 0;
        for (int k = -half; k <= half; k++)
        {
            double sample = impulseResponse[((peakIndex + k) % length + length) % length].Real;
            near += sample * sample;
        }

        double rms = Math.Sqrt(near / (2 * half + 1));
        return rms > 0 ? 20.0 * Math.Log10(peak / rms) : null;
    }

    /// <summary>
    /// Measures the time-compactness of a transfer IR (see
    /// <see cref="TransferIrCompactness"/>). Null when the record is too
    /// short or silent to judge. The verdict rests on energy geometry only —
    /// deliberately no peak-position rule, so an electrical chain
    /// measurement (mic input wired to a processor output, peak at ~0 ms)
    /// passes on its clean shape. Internal: the app holds transfer IRs in
    /// their FFT form and calls the <see cref="Complex"/> overload.
    /// </summary>
    internal static TransferIrCompactness? MeasureCompactness(
        IReadOnlyList<double> impulseResponse,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        int length = impulseResponse.Count;
        if (length < CompactnessMinimumSamples || sampleRate <= 0)
        {
            return null;
        }

        double total = 0;
        double peak = 0;
        int peakIndex = 0;
        for (int i = 0; i < length; i++)
        {
            double sample = impulseResponse[i];
            total += sample * sample;
            if (Math.Abs(sample) > peak)
            {
                peak = Math.Abs(sample);
                peakIndex = i;
            }
        }
        // Null covers "nothing to measure" AND "not a number to measure":
        // a single NaN/Infinity poisons every energy sum, and the caller
        // treats an unmeasurable shape as a refusal, never a pass.
        if (!double.IsFinite(total) || total <= 0)
        {
            return null;
        }

        int pre = Math.Min((int)(CompactnessPreSeconds * sampleRate), length / 16);
        int post = Math.Min((int)(CompactnessPostSeconds * sampleRate), length / 4);
        double windowed = 0;
        for (int k = -pre; k <= post; k++)
        {
            int index = ((peakIndex + k) % length + length) % length;
            double sample = impulseResponse[index];
            windowed += sample * sample;
        }

        int windowLength = pre + post + 1;
        int outsideLength = length - windowLength;
        double insidePerSample = windowed / windowLength;
        // A perfectly clean record (synthetic delta) has zero energy outside
        // the window; the floor caps the ratio instead of dividing by zero.
        double outsidePerSample = Math.Max(
            insidePerSample * 1e-12, (total - windowed) / outsideLength);
        double peakDelayMs = peakIndex <= length / 2
            ? peakIndex * 1000.0 / sampleRate
            : (peakIndex - length) * 1000.0 / sampleRate;
        return new TransferIrCompactness(
            10 * Math.Log10(insidePerSample / outsidePerSample),
            peakDelayMs);
    }

    /// <summary>
    /// The <see cref="Complex"/> twin of the compactness measure, for
    /// callers holding the transfer IR in its FFT form.
    /// </summary>
    public static TransferIrCompactness? MeasureCompactness(
        IReadOnlyList<Complex> impulseResponse,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        // A view, not a copy. The measure reads each sample once, and materialising
        // the real parts asked for a second array the size of the response — 34 MB at
        // the transform length a 96 kHz twenty-second take reaches, for nothing.
        return MeasureCompactness(new RealPartsView(impulseResponse), sampleRate);
    }

    /// <summary>
    /// How far (dB) the per-sample energy of the stretch WELL BEFORE the arrival
    /// sits under the arrival's own neighbourhood, measured circularly. Null when
    /// the record is too short to hold the window, or too degenerate to measure.
    /// <para>
    /// This answers a question <see cref="MeasureCompactness"/> cannot: whether the
    /// ring is CAUSAL. Nothing arrives before the direct sound, so a record that
    /// rings as loudly ahead of its peak as behind it is not reporting a room. The
    /// field pair this was written for read -14.8 and -14.1 dB where clean records
    /// from the same rig read -39.0 and -44.3 dB — and both cleared the compactness
    /// floor, at 26.0 and 24.1 dB against 22.
    /// </para>
    /// <para>
    /// What separates the two measures is what they are blind to. A resonance
    /// imposed on a record in MAGNITUDE alone shows up here; a minimum-phase one of
    /// the same depth does not, because it rings forward only. Compactness reads
    /// those two as the same record (measured: 24.68 against 24.65 dB). A cabin's
    /// own resonances are minimum-phase, so this measure ignores them and can carry
    /// a threshold a reverberant cabin cannot trip — which is what lets it refuse
    /// where the compactness floor has to stay low enough to keep such a cabin.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Read against the ARRIVAL rather than the whole record, so it follows the
    /// peak: a purely electrical measurement peaks at zero, where "before the
    /// arrival" is the far end of the buffer, and an acoustic one peaks tens of
    /// milliseconds in. A share-of-total measure moves with that delay by tens of
    /// dB (an ideal band-limited kernel at zero delay puts half its energy in
    /// wrapped negative time) and cannot be given a threshold at all.
    /// <para>
    /// One way a reference earns this reading is worth naming, because it is what
    /// an interface's own monitor path does when the direct level is pulled down
    /// and a delayed return is not: once the delayed component EXCEEDS the direct
    /// one the reference stops being minimum-phase, and its inverse is anticausal.
    /// The flip is sharp — measured on a reference of one direct plus one 20 ms
    /// copy, the inverse holds 0 % of its energy in negative time while the direct
    /// leads by 1.6 dB and 100 % once it trails by 0.9 dB. It is not the only way,
    /// and the thresholds here are calibrated on field records rather than on that
    /// model: a dense reverb mixed hot enough to reach the field pair's reading
    /// also drives compactness to 12-15 dB, where the shape gate refuses it
    /// already, while the field records cleared that gate at 26.0 and 24.1 dB.
    /// </para>
    /// </remarks>
    public static TransferIrPreArrival? MeasurePreArrivalDb(
        IReadOnlyList<Complex> impulseResponse,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        return MeasurePreArrivalDb(new RealPartsView(impulseResponse), sampleRate);
    }

    internal static TransferIrPreArrival? MeasurePreArrivalDb(
        IReadOnlyList<double> impulseResponse,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        int length = impulseResponse.Count;
        if (length < CompactnessMinimumSamples || sampleRate <= 0)
        {
            return null;
        }

        int start = (int)(PreArrivalStartSeconds * sampleRate);
        // The far edge shrinks on a short record so the two windows cannot meet
        // around the circle and read each other's content.
        int end = Math.Min((int)(PreArrivalEndSeconds * sampleRate), length / 4);
        if (start <= 0 || end < 2 * start)
        {
            return null;
        }

        double peak = 0;
        int peakIndex = 0;
        for (int i = 0; i < length; i++)
        {
            double sample = impulseResponse[i];
            if (Math.Abs(sample) > peak)
            {
                peak = Math.Abs(sample);
                peakIndex = i;
            }
        }

        double arrival = 0;
        for (int k = -start; k <= start; k++)
        {
            double sample = impulseResponse[((peakIndex + k) % length + length) % length];
            arrival += sample * sample;
        }

        double before = 0;
        double beforePeak = 0;
        for (int k = -end; k < -start; k++)
        {
            double sample = impulseResponse[((peakIndex + k) % length + length) % length];
            before += sample * sample;
            beforePeak = Math.Max(beforePeak, Math.Abs(sample));
        }

        // Unmeasurable covers "nothing to measure" AND "not a number to measure":
        // a single NaN/Infinity poisons the sums, and a caller must not read a
        // poisoned ratio as a pass.
        if (!double.IsFinite(arrival) || !double.IsFinite(before) || arrival <= 0)
        {
            return null;
        }

        double arrivalPerSample = arrival / (2 * start + 1);
        double beforePerSample = before / (end - start);
        // A synthetic record with nothing at all before its arrival would divide by
        // zero; the floor caps the reading at -120 dB instead.
        double levelDb = 10 * Math.Log10(
            Math.Max(beforePerSample, arrivalPerSample * 1e-12) / arrivalPerSample);
        double crestDb = beforePerSample > 0
            ? 20 * Math.Log10(beforePeak / Math.Sqrt(beforePerSample))
            : 0.0;
        return new TransferIrPreArrival(levelDb, crestDb);
    }

    /// <summary>
    /// Whether <see cref="MeasurePreArrivalDb"/> can be given a verdict for a record
    /// gated by <paramref name="gate"/>, or whether the estimator's own kernel rings
    /// too much like the fault to tell them apart.
    /// </summary>
    /// <remarks>
    /// The zero-phase excitation gate turns even an ideal H(f)=1 into a symmetric
    /// band-limited kernel, and how long that kernel rings is set by the width of its
    /// narrowest transition — the LOW guard band, which is the narrowest in Hz at
    /// every band, since the same octave guard spans fewer Hz at the bottom. The
    /// sweep generator aims for half an octave (<c>DesiredGuardOctaves</c>) and only
    /// falls short when the requested duration cannot open one. Measured on ideal
    /// gated kernels: with the half-octave guard the worst band the app supports
    /// (20-25 Hz, a fifth of an octave wide) reads -26.8 dB, and it stays at or
    /// under -22.7 dB down to a quarter-octave guard — but at 0.05 octaves it reaches
    /// -14.9 dB, which is where the two broken field records read. Below the floor
    /// here the check says nothing rather than guessing, in the same spirit as the
    /// compactness floor sitting on the garbage side of its own gap.
    /// </remarks>
    public static bool CanJudgePreArrival(ExcitationBandGate gate)
    {
        // No low edge at all (full-band excitation) means no low-edge kernel to
        // confuse the reading with.
        if (gate.LowFullNyquistFraction <= 0)
        {
            return true;
        }
        if (gate.LowZeroNyquistFraction <= 0)
        {
            return true;
        }

        double guardOctaves = Math.Log2(
            gate.LowFullNyquistFraction / gate.LowZeroNyquistFraction);
        return double.IsFinite(guardOctaves) &&
            guardOctaves >= MinimumJudgeableGuardOctaves;
    }

    /// <summary>
    /// Whether a pre-arrival reading over <see cref="MaximumPreArrivalDb"/> may be
    /// refused for a record gated by <paramref name="gate"/>, or only reported —
    /// see <see cref="MinimumRefusableSpanOctaves"/> for why the excitation's width
    /// decides that.
    /// </summary>
    public static bool CanRefuseOnPreArrival(ExcitationBandGate gate)
    {
        if (!CanJudgePreArrival(gate))
        {
            return false;
        }

        // No low edge means the excitation runs to DC: the widest span there is.
        if (gate.LowZeroNyquistFraction <= 0)
        {
            return true;
        }

        double spanOctaves = Math.Log2(
            gate.HighZeroNyquistFraction / gate.LowZeroNyquistFraction);
        return double.IsFinite(spanOctaves) &&
            spanOctaves >= MinimumRefusableSpanOctaves;
    }

    private sealed class RealPartsView(IReadOnlyList<Complex> source)
        : IReadOnlyList<double>
    {
        public int Count => source.Count;

        public double this[int index] => source[index].Real;

        public IEnumerator<double> GetEnumerator()
        {
            for (int i = 0; i < source.Count; i++)
            {
                yield return source[i].Real;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    /// <summary>
    /// How far below the smoothed spectrum's peak the dominant band extends.
    /// Field calibration (v3 cabin, 7 records, threshold sweep 10/12/15/20):
    /// 15 dB is the sweet spot — driver-shaped bands (sub 20-110, midbass
    /// 78-320, mid 110-2560, tweeter 1108-20k) with arrivals intact. 20 dB
    /// lets a mid record swallow 20 Hz-14 kHz (cabin gain and door leakage
    /// keep its LF/HF shelves within 20 dB of the peak); 12 dB and tighter
    /// squeezes the midbass band to 82-155 Hz and the band-limited arrival
    /// read modal-latches (7.4 ms -> 15.3 ms).
    /// </summary>
    public const double DominantBandThresholdDb = 15.0;

    // Spectral analysis window: long enough to resolve 1/6 octave at 20 Hz,
    // short enough to keep the FFT cheap; the head artifact this feeds sits
    // in the first milliseconds anyway.
    private const int MaxAnalysisSamples = 65_536;

    // The complement band (where the record has no driver energy) starts
    // half an octave above the dominant band's top, and must span at least
    // one octave to have any detection leverage — full-range records have no
    // complement, and their head clicks measurably do not move the engine
    // (v3: proposals shift ≤ 0.01 ms).
    private const double ComplementGapOctaves = 0.5;
    private const double ComplementMinimumOctaves = 1.0;

    // The click island ends where the complement envelope falls this far
    // below the click's own peak and stays there.
    private const double IslandEndBelowPeakDb = 12.0;
    private const double IslandEndHoldSeconds = 0.00025;

    // The artifact must precede the in-band front by at least this much: a
    // GENUINE early arrival carries the driver's out-of-band content along
    // with the in-band front (same wavefront), so the two bands read the
    // same time; only a non-acoustic event can be complement-early.
    private const double PreFrontGuardSeconds = 0.002;

    // The island must close within this long of the candidate — a click is
    // a millisecond-scale event; anything longer is sound.
    private const double IslandCapSeconds = 0.010;

    // How far below the in-band first-arrival peak the in-band envelope must
    // stay over the whole gated stretch (the gate claims that stretch is
    // pre-sound). Field margins: the click's in-band shadow reads ~-25 dB,
    // a co-onset genuine rise ~-10 dB.
    private const double InBandQuietBeforeGateDb = 15.0;

    // How much later the full-band first arrival must move after the trial
    // removal for the burst to be convicted (safely above the band-to-band
    // dispersion of one wavefront in a cabin, ~1-1.5 ms measured).
    private const double FirstArrivalJumpMs = 2.0;

    private const double FadeSeconds = 0.0004;

    public static DominantBand DetectDominantBand(
        IReadOnlyList<double> impulseResponse,
        int sampleRate,
        double thresholdDb = DominantBandThresholdDb,
        IReadOnlyList<double>? coherence = null)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        if (impulseResponse.Count == 0)
        {
            throw new ArgumentException(
                "Impulse response must not be empty.", nameof(impulseResponse));
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        int length = Math.Min(impulseResponse.Count, MaxAnalysisSamples);
        var spectrum = new Complex[length];
        for (int i = 0; i < length; i++)
        {
            spectrum[i] = impulseResponse[i];
        }
        Fourier.Forward(spectrum, FourierOptions.Matlab);

        int half = length / 2;
        double topHz = Math.Min(20_000, sampleRate * 0.45);

        double CoherenceAt(double hz)
        {
            if (coherence == null || coherence.Count < 2)
            {
                return 1.0;
            }

            double position = hz * 2.0 * (coherence.Count - 1) / sampleRate;
            if (position <= 0.0)
            {
                return coherence[0];
            }
            if (position >= coherence.Count - 1)
            {
                return coherence[^1];
            }

            int lower = (int)position;
            double fraction = position - lower;
            return coherence[lower] * (1.0 - fraction) +
                coherence[lower + 1] * fraction;
        }

        double SmoothedDb(double hz)
        {
            double lo = hz / Math.Pow(2.0, 1.0 / 12);
            double hi = hz * Math.Pow(2.0, 1.0 / 12);
            int i1 = Math.Max(1, (int)(lo * length / sampleRate));
            int i2 = Math.Min(half - 1, (int)(hi * length / sampleRate));
            if (i2 < i1)
            {
                i2 = i1;
            }
            double sum = 0;
            int trustedCount = 0;
            int binCount = i2 - i1 + 1;
            for (int i = i1; i <= i2; i++)
            {
                double binCoherence = CoherenceAt((double)i * sampleRate / length);
                if (!double.IsFinite(binCoherence) || binCoherence < 0.5)
                {
                    continue;
                }

                sum += spectrum[i].Magnitude * spectrum[i].Magnitude;
                trustedCount++;
            }
            // A lone coherent bin must not represent a whole 1/6-octave
            // window. Require at least half of the window to be trustworthy;
            // this also makes the detected band edge stable when γ² jitters
            // around the threshold in a few adjacent bins.
            return trustedCount * 2 >= binCount
                ? 10 * Math.Log10(Math.Max(1e-24, sum / trustedCount))
                : double.NegativeInfinity;
        }

        // 1/24-octave log grid over the audible range.
        var gridHz = new List<double>();
        var gridDb = new List<double>();
        for (double hz = 20; hz <= topHz; hz *= Math.Pow(2.0, 1.0 / 24))
        {
            gridHz.Add(hz);
            gridDb.Add(SmoothedDb(hz));
        }

        if (coherence != null && gridDb.All(double.IsNegativeInfinity))
        {
            throw new InvalidOperationException(
                "No reliable signal remains above the coherence threshold.");
        }

        int peakIndex = 0;
        for (int i = 1; i < gridDb.Count; i++)
        {
            if (gridDb[i] > gridDb[peakIndex])
            {
                peakIndex = i;
            }
        }

        // Expand from the peak, bridging dips narrower than
        // MaxBridgedGapOctaves: an in-cabin cancellation notch is deep but
        // narrow and must not cut the driver's working band in half, while
        // a wide stretch of silence is a real band edge.
        double floorDb = gridDb[peakIndex] - thresholdDb;
        int maxGapSteps = (int)Math.Round(MaxBridgedGapOctaves * 24);
        int solidSteps = (int)Math.Round(MinSolidLandingOctaves * 24);
        // A bridge must LAND on a solid stretch of band (≥ solidSteps
        // consecutive points above the floor): real cancellation notches sit
        // between solid regions, while broadband ripple hovering around the
        // threshold offers only isolated spikes — chaining bridges through
        // those would crawl the band arbitrarily far (measured on the field
        // records: the midbass band tripled before this rule).
        bool SolidAt(int start, int step)
        {
            for (int i = 0; i < solidSteps; i++)
            {
                int index = start + step * i;
                if (index < 0 || index >= gridDb.Count || gridDb[index] < floorDb)
                {
                    return false;
                }
            }
            return true;
        }
        int Expand(int from, int step)
        {
            int edge = from;
            while (true)
            {
                int next = edge + step;
                if (next < 0 || next >= gridDb.Count)
                {
                    return edge;
                }
                if (gridDb[next] >= floorDb)
                {
                    edge = next;
                    continue;
                }
                int across = -1;
                for (int k = 2; k <= maxGapSteps; k++)
                {
                    int candidate = edge + step * k;
                    if (candidate < 0 || candidate >= gridDb.Count)
                    {
                        break;
                    }
                    if (SolidAt(candidate, step))
                    {
                        across = candidate;
                        break;
                    }
                }
                if (across < 0)
                {
                    return edge;
                }
                edge = across;
            }
        }
        int low = Expand(peakIndex, -1);
        int high = Expand(peakIndex, +1);

        return new DominantBand(gridHz[low], gridHz[high], gridHz[peakIndex]);
    }

    /// <summary>
    /// Estimates where the IR's honest acoustic content starts: the rising-front
    /// crossings of the first credible arrival's Hilbert envelope, read inside
    /// the record's DOMINANT band, taken at
    /// <see cref="IrStartBandThresholdDb"/> rather than the tighter content
    /// floor (a band too narrow cannot resolve the time being read — see that
    /// constant). The band-pass is what makes the figure robust
    /// on real cabin records — a playback-crosstalk click or other broadband head
    /// garbage carries almost no energy inside a band-limited driver's working
    /// band, so the in-band envelope never sees it and no head-cleaning pass is
    /// needed (v3 field data: a click at ~0.5 ms poisons every full-band read of
    /// the midbass and subwoofer records, 0.46-3.3 ms against fronts at 4.5-9 ms,
    /// while the in-band read lands on the front on all seven). The arrival
    /// itself is the shared first-arrival physics (25 dB depth, noise gate,
    /// pre-ringing rejection), so a stronger modal build-up peak later cannot
    /// usurp the front.
    /// <para>
    /// Falls back to the full-band envelope when the dominant-band read is
    /// invalid — a head artifact CAN poison that path, and
    /// <see cref="IrStartEstimate.DominantBandLimited"/> reports which one
    /// answered. Null when no credible arrival exists at all: empty, too short or
    /// silent records, and noise-only records, which the envelope SNR floor
    /// refuses (a noise envelope still has a "strongest peak" the first-arrival
    /// search would fall back to, and only the peak-to-noise grade exposes it).
    /// The caller keeps whatever figure it used before.
    /// </para>
    /// <para>
    /// Analysis is capped to the record's first <see cref="MaxAnalysisSamples"/>
    /// samples: the front lives there, and the cap keeps the per-refresh cost
    /// flat, the same convention as <see cref="DetectCrosstalkHead"/>.
    /// </para>
    /// </summary>
    public static IrStartEstimate? EstimateIrStart(
        IReadOnlyList<double> impulseResponse,
        int sampleRate,
        ValidSampleRange validRange = default)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        if (sampleRate <= 0)
        {
            return null;
        }

        // A known valid range restricts the ANALYSIS to the measured content
        // while every reported time stays in the full record's coordinates:
        // a chain delay's silent prefix would otherwise sink the noise-floor
        // estimate and let a record the SNR gate should refuse pose as a
        // credible front (measured: 25 ms of zeros ahead of a short noise
        // record lift its grade from ~6 to ~26 dB, past the 20 dB floor).
        int rangeStart = 0;
        if (validRange.IsKnown)
        {
            rangeStart = Math.Clamp(
                validRange.StartSample, 0, impulseResponse.Count);
            int rangeEnd = Math.Clamp(
                validRange.EndSample, rangeStart, impulseResponse.Count);
            var window = new double[rangeEnd - rangeStart];
            for (int i = 0; i < window.Length; i++)
            {
                window[i] = impulseResponse[rangeStart + i];
            }
            impulseResponse = window;
        }
        if (impulseResponse.Count < IrStartMinimumSamples)
        {
            return null;
        }

        if (impulseResponse.Count > MaxAnalysisSamples)
        {
            var head = new double[MaxAnalysisSamples];
            for (int i = 0; i < head.Length; i++)
            {
                head[i] = impulseResponse[i];
            }
            impulseResponse = head;
        }

        DominantBand band = DetectDominantBand(
            impulseResponse, sampleRate, IrStartBandThresholdDb);
        TimeAlignmentAnalysisResult inBand = TimeAlignmentAnalysis.Analyze(
            impulseResponse, sampleRate, new TimeAlignmentAnalysisOptions
            {
                UseBandpassWindow = true,
                BandpassCenterHz = Math.Sqrt(band.LowHz * band.HighHz),
                BandpassPassOctaves = Math.Log2(band.HighHz / band.LowHz),
                BandpassFadeOctaves = 0.25
            });
        double offsetMs = rangeStart * 1_000.0 / sampleRate;
        if (IsCredible(inBand))
        {
            return Offset(
                CrossingsOf(inBand, band, sampleRate, dominantBandLimited: true),
                offsetMs);
        }

        TimeAlignmentAnalysisResult fullBand = TimeAlignmentAnalysis.Analyze(
            impulseResponse, sampleRate, new TimeAlignmentAnalysisOptions());
        return IsCredible(fullBand)
            ? Offset(
                CrossingsOf(fullBand, band, sampleRate, dominantBandLimited: false),
                offsetMs)
            : null;
    }

    // Shifts an estimate computed on a range-restricted window back into the
    // full record's coordinates.
    private static IrStartEstimate Offset(IrStartEstimate estimate, double offsetMs) =>
        offsetMs == 0
            ? estimate
            : estimate with
            {
                StartMs = estimate.StartMs + offsetMs,
                EarlyMs = estimate.EarlyMs + offsetMs,
                LateMs = estimate.LateMs + offsetMs
            };

    /// <summary>
    /// How far below the smoothed spectrum's peak the band
    /// <see cref="EstimateIrStart"/> reads the arrival in extends. Wider than
    /// <see cref="DominantBandThresholdDb"/> on purpose: that floor answers
    /// "where does this driver's energy live", which the crosstalk complement
    /// band needs answered tightly, while a TIME can only be read as finely as
    /// the analysis band's own resolution (~1/bandwidth) allows. A 15 dB floor
    /// around a cabin mode can collapse to a single octave — field case, a door
    /// woofer at the listening position reading 75-155 Hz, its cancellation
    /// notches on both sides just too wide to bridge — and in that band nothing
    /// shorter than ~12 ms resolves: the envelope's only maximum is the mode's
    /// build-up 8.4 ms past the arrival, and the 25 % crossing walked back from
    /// it lands at 3.00 ms — 2.5 ms before the record leaves its noise floor,
    /// 3.4 ms before its peak.
    /// <para>
    /// Calibrated across the field sets (v2-v5, 23 records) at 15/20/25/30 dB.
    /// 25 and 30 agree on the collapsed record (5.64 ms, against a 6.42 ms peak
    /// and a front at ~5.6 ms) while 20 is still transitional (4.46 ms), so 25
    /// sits at the near edge of the plateau. Every record whose 15 dB band
    /// already spanned four octaves or more moves by at most 0.5 ms; the rest
    /// (subwoofers included, and they stay 2.8 octaves even at this floor) move
    /// 0.3-1.7 ms later — towards their arrival, and on all 23 still short of
    /// the record's own peak. None falls back onto the head burst the
    /// band-limiting exists to reject: every v3/v4 read stays in the 5.2-7.5 ms
    /// front range.
    /// </para>
    /// </summary>
    private const double IrStartBandThresholdDb = 25.0;

    // Below this length the spectral analysis behind the estimate is
    // degenerate (no dominant band to speak of, no quantile noise floor).
    private const int IrStartMinimumSamples = 256;

    // The envelope peak-to-noise grade a record must clear before its front
    // is trusted: IsValid alone only refuses flat-zero envelopes, so a
    // noise-only record would otherwise "measure" a front at wherever its
    // strongest random peak happens to sit. Same floor as the auto-delay
    // onset lock (AutoAlignmentEngine.OnsetLockMinimumSnrDb, field-derived);
    // clean cabin records grade 50+ dB, pure noise ~10-13 dB.
    private const double IrStartMinimumSnrDb = 20;

    private static bool IsCredible(TimeAlignmentAnalysisResult result) =>
        result.IsValid && result.SignalToNoiseDecibels >= IrStartMinimumSnrDb;

    /// <summary>
    /// The <see cref="Complex"/> twin of the estimator above, for callers
    /// holding a transfer or processed IR in its FFT form.
    /// </summary>
    public static IrStartEstimate? EstimateIrStart(
        IReadOnlyList<Complex> impulseResponse,
        int sampleRate,
        ValidSampleRange validRange = default)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        // With a known range the head cap must count from the range's start,
        // or a late-content record would be truncated before its own front;
        // the double overload re-restricts in its own coordinates.
        int rangeStart = validRange.IsKnown
            ? Math.Clamp(validRange.StartSample, 0, impulseResponse.Count)
            : 0;
        int length = Math.Min(
            impulseResponse.Count, rangeStart + MaxAnalysisSamples);
        var samples = new double[length];
        for (int i = 0; i < length; i++)
        {
            samples[i] = impulseResponse[i].Real;
        }
        return EstimateIrStart(samples, sampleRate, validRange);
    }

    private static IrStartEstimate CrossingsOf(
        TimeAlignmentAnalysisResult result,
        DominantBand band,
        int sampleRate,
        bool dominantBandLimited)
    {
        double[] envelope = result.EnvelopeSamples;
        int peakIndex = result.EnvelopePeakIndex;
        double peak = envelope[peakIndex];
        return new IrStartEstimate(
            RisingFrontCrossingMs(envelope, peakIndex, 0.25 * peak, sampleRate),
            RisingFrontCrossingMs(envelope, peakIndex, 0.10 * peak, sampleRate),
            RisingFrontCrossingMs(envelope, peakIndex, 0.50 * peak, sampleRate),
            band.LowHz,
            band.HighHz,
            dominantBandLimited);
    }

    // Walks backward down the arrival peak's own rising front to the first
    // sample below the threshold and interpolates the crossing (sub-sample).
    // Pinned to THAT front: an earlier, disjoint event past a dip never
    // captures the crossing. Reaches 0.0 when the front starts at the record.
    private static double RisingFrontCrossingMs(
        IReadOnlyList<double> envelope,
        int peakIndex,
        double threshold,
        int sampleRate)
    {
        int i = peakIndex;
        while (i > 0 && envelope[i] > threshold)
        {
            i--;
        }
        if (i == peakIndex)
        {
            return peakIndex * 1_000.0 / sampleRate;
        }

        double below = envelope[i];
        double above = envelope[i + 1];
        // The walk can stop at index 0 with the envelope still ABOVE the
        // threshold — a front that runs off the head of the record. The
        // interpolation would then extrapolate backwards without bound
        // (field: a subwoofer's 10 % crossing read -1.5 ms), so it is floored
        // at the record start, which is as early as the crossing can be.
        double fraction = above > below
            ? Math.Max(0.0, (threshold - below) / (above - below))
            : 0.0;
        return (i + fraction) * 1_000.0 / sampleRate;
    }

    // The widest spectral dip the dominant-band expansion steps across
    // (interference notches); anything wider counts as the band's real edge.
    private const double MaxBridgedGapOctaves = 0.5;

    // A bridged gap must land on at least this much contiguous above-floor
    // band on the far side.
    private const double MinSolidLandingOctaves = 1.0 / 6.0;

    /// <summary>
    /// Looks for a playback-crosstalk click in the record's head. The
    /// candidate is the COMPLEMENT band's first arrival (half an octave
    /// above the dominant band's top, where the driver has nothing to say;
    /// sidelobe-rejected, so window pre-ring cannot masquerade as it), and
    /// it must be a short ISLAND far ahead of the in-band front. The island
    /// is judged on its own envelope, never against later complement
    /// content: a click hotter than the driver's out-of-band tail — or one
    /// that is the only complement event at all — is the MORE dangerous
    /// artifact and must not detect worse than a faint one. The verdict is
    /// an experiment rather than a threshold: the island is trial-removed
    /// and the record's full-band first arrival re-read — only a jump well
    /// past one wavefront's band-to-band dispersion convicts. A genuine
    /// early arrival never trips this: its complement energy rises into the
    /// room decay (no island), it sits at the front rather than in the head
    /// (proportionality guard), and removing sound that IS the first
    /// arrival of only one band cannot move the full-band read.
    /// CONTRACT: this is a field-calibrated detector for BAND-LIMITED
    /// records, not a universal crosstalk detector — a full-range record
    /// offers no complement band to test and is always returned untouched
    /// (null), even if its head carries a click (measured inert on the v3
    /// field data: engine proposals move ≤ 0.01 ms). Null likewise
    /// whenever any of the guards is not met.
    /// </summary>
    public static CrosstalkHeadGate? DetectCrosstalkHead(
        IReadOnlyList<double> impulseResponse,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        if (impulseResponse.Count == 0 || sampleRate <= 0)
        {
            return null;
        }

        // The click and the front both live in the record's first second;
        // capping the analysis keeps the per-refresh cost flat and the
        // verdict identical between the panel, the engine and the probes.
        if (impulseResponse.Count > MaxAnalysisSamples)
        {
            var head = new double[MaxAnalysisSamples];
            for (int i = 0; i < head.Length; i++)
            {
                head[i] = impulseResponse[i];
            }
            impulseResponse = head;
        }

        DominantBand band = DetectDominantBand(impulseResponse, sampleRate);
        double complementLow = band.HighHz * Math.Pow(2.0, ComplementGapOctaves);
        double complementHigh = Math.Min(20_000, sampleRate * 0.45);
        if (complementHigh < complementLow * Math.Pow(2.0, ComplementMinimumOctaves))
        {
            return null;
        }

        TimeAlignmentAnalysisResult complement = TimeAlignmentAnalysis.Analyze(
            impulseResponse, sampleRate, new TimeAlignmentAnalysisOptions
            {
                UseBandpassWindow = true,
                BandpassCenterHz = Math.Sqrt(complementLow * complementHigh),
                BandpassPassOctaves = Math.Log2(complementHigh / complementLow),
                BandpassFadeOctaves = 0.25
            });
        if (!complement.IsValid)
        {
            return null;
        }

        TimeAlignmentAnalysisResult inBand = TimeAlignmentAnalysis.Analyze(
            impulseResponse, sampleRate, new TimeAlignmentAnalysisOptions
            {
                UseBandpassWindow = true,
                BandpassCenterHz = Math.Sqrt(band.LowHz * band.HighHz),
                BandpassPassOctaves = Math.Log2(band.HighHz / band.LowHz),
                BandpassFadeOctaves = 0.25
            });
        if (!inBand.IsValid)
        {
            return null;
        }

        // The candidate: the complement's first arrival, and its island end —
        // where the complement envelope falls well below the candidate's
        // peak and stays there, within a short cap. A genuine early front's
        // complement energy rises into the room decay instead of dropping,
        // so no end is found.
        int clickIndex = complement.EnvelopePeakIndex;
        double[] envelope = complement.EnvelopeSamples;
        double clickPeak = envelope[clickIndex];
        double islandFloor = clickPeak * Math.Pow(10, -IslandEndBelowPeakDb / 20);
        int hold = Math.Max(1, (int)(sampleRate * IslandEndHoldSeconds));
        int islandCap = Math.Min(
            envelope.Length, clickIndex + (int)(sampleRate * IslandCapSeconds));
        int islandEnd = -1;
        int below = 0;
        for (int i = clickIndex; i < islandCap; i++)
        {
            if (envelope[i] < islandFloor)
            {
                below++;
                if (below >= hold)
                {
                    islandEnd = i - hold + 1;
                    break;
                }
            }
            else
            {
                below = 0;
            }
        }
        if (islandEnd < 0)
        {
            return null;
        }
        int fade = Math.Max(1, (int)(sampleRate * FadeSeconds));
        int gateEnd = islandEnd + fade;

        // Proportionality guard: the candidate must sit far ahead of the
        // in-band front, and the gate may reach at most half-way from the
        // candidate to it (midpoint, so an artifact that does not sit near
        // sample zero is not penalized). This is what protects genuine
        // co-onset out-of-band content (its island sits AT the front, not in
        // the head) — and it is deliberately not an onset-threshold walk,
        // which the click's own in-band shadow and the window pre-ring ramp
        // both poison (two field-tested failures).
        int guard = (int)(sampleRate * PreFrontGuardSeconds);
        if (clickIndex + guard >= inBand.EnvelopePeakIndex ||
            gateEnd > clickIndex + (inBand.EnvelopePeakIndex - clickIndex) / 2)
        {
            return null;
        }

        // The gate's own claim is "everything before it is pre-sound": the
        // IN-BAND envelope must stay far below its first-arrival peak over
        // the whole gated stretch. This is what refuses a co-onset genuine
        // event (a driver's out-of-band burst travelling WITH a front whose
        // envelope peaks much later) — there the in-band envelope is already
        // rising where the island ends. The click's own in-band shadow sits
        // ~25 dB down on the field records and clears the ceiling.
        double[] inBandEnvelope = inBand.EnvelopeSamples;
        double inBandFirstPeak = inBandEnvelope[inBand.EnvelopePeakIndex];
        double quietCeiling =
            inBandFirstPeak * Math.Pow(10, -InBandQuietBeforeGateDb / 20);
        for (int i = 0; i < gateEnd && i < inBandEnvelope.Length; i++)
        {
            if (inBandEnvelope[i] > quietCeiling)
            {
                return null;
            }
        }

        // The verdict is an experiment, not a threshold: trial-remove the
        // island and re-read the FULL-BAND first arrival. If it jumps later
        // by more than the dispersion tolerance, the record's first arrival
        // WAS the head burst — a disjoint event ahead of all sound, i.e.
        // crosstalk. If the read barely moves, the burst was not driving
        // anything (measured inert on the field data) and the record is left
        // alone.
        var fullBandOptions = new TimeAlignmentAnalysisOptions();
        TimeAlignmentAnalysisResult rawFull = TimeAlignmentAnalysis.Analyze(
            impulseResponse, sampleRate, fullBandOptions);
        if (!rawFull.IsValid)
        {
            return null;
        }
        var gate = new CrosstalkHeadGate(gateEnd, 0, 0);
        double[] trialCleaned = CleanCrosstalkHead(
            impulseResponse is double[] array ? array : [.. impulseResponse],
            sampleRate,
            gate);
        TimeAlignmentAnalysisResult cleanedFull = TimeAlignmentAnalysis.Analyze(
            trialCleaned, sampleRate, fullBandOptions);
        if (!cleanedFull.IsValid ||
            cleanedFull.FirstArrivalDelayMilliseconds -
            rawFull.FirstArrivalDelayMilliseconds < FirstArrivalJumpMs)
        {
            return null;
        }

        double recordMax = 0;
        int length = Math.Min(impulseResponse.Count, MaxAnalysisSamples);
        for (int i = 0; i < length; i++)
        {
            recordMax = Math.Max(recordMax, Math.Abs(impulseResponse[i]));
        }
        return new CrosstalkHeadGate(
            gateEnd,
            clickIndex * 1000.0 / sampleRate,
            recordMax > 0
                ? 20 * Math.Log10(Math.Max(1e-12, clickPeak / recordMax))
                : 0.0);
    }

    /// <summary>
    /// Applies a head gate to a copy of the IR: zeros [0, GateEndSample) and
    /// raised-cosine-fades the next <see cref="FadeSeconds"/> worth of
    /// samples. The artifact is removed from the record before any linear
    /// processing, so every downstream band-limited read is clean.
    /// </summary>
    public static Complex[] CleanCrosstalkHead(
        Complex[] impulseResponse,
        int sampleRate,
        CrosstalkHeadGate gate)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        var clean = (Complex[])impulseResponse.Clone();
        int end = Math.Min(gate.GateEndSample, clean.Length);
        for (int i = 0; i < end; i++)
        {
            clean[i] = Complex.Zero;
        }
        int fade = Math.Max(1, (int)(sampleRate * FadeSeconds));
        for (int i = 0; i < fade && end + i < clean.Length; i++)
        {
            double w = 0.5 - 0.5 * Math.Cos(Math.PI * i / fade);
            clean[end + i] *= w;
        }
        return clean;
    }

    /// <summary>
    /// The real-valued twin of the gate above, for callers holding the
    /// transfer IR as samples (the Time Alignment panel).
    /// </summary>
    public static double[] CleanCrosstalkHead(
        double[] impulseResponse,
        int sampleRate,
        CrosstalkHeadGate gate)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        var clean = (double[])impulseResponse.Clone();
        int end = Math.Min(gate.GateEndSample, clean.Length);
        Array.Clear(clean, 0, end);
        int fade = Math.Max(1, (int)(sampleRate * FadeSeconds));
        for (int i = 0; i < fade && end + i < clean.Length; i++)
        {
            double w = 0.5 - 0.5 * Math.Cos(Math.PI * i / fade);
            clean[end + i] *= w;
        }
        return clean;
    }

}
