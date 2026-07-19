using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// One junction's phase read-out: how well two adjacent processed channels sum
/// in phase across their overlap band, and what change would improve it.
/// </summary>
/// <remarks>
/// All figures read the steady-state response (a long analysis window over the
/// full processed IR, room decay included) because that is what sustained
/// program material sums to at the listening position — the same regime the
/// sum-loss metric measures. Direct-sound (frequency-dependent-windowed)
/// phase systematically disagrees with it by several ms at subwoofer
/// frequencies (the room's own group delay), so it is deliberately NOT used
/// here.
/// </remarks>
/// <param name="CurrentScore">
/// Energy-weighted in-phase score Σw·cos(Δφ)/Σw at the CURRENT settings:
/// 1 = perfectly in phase across the band, −1 = perfectly out of phase. This
/// is a phase-alignment score, NOT the magnitude-squared coherence γ² the
/// measurement pipeline reports (that lives in 0..1).
/// </param>
/// <param name="PhaseAtCrossoverDeg">
/// The phase of lower minus upper AT the crossover, wrapped to ±180°: the
/// weighted circular mean over a narrow window around fc — deliberately a
/// local measurement, not the straight-line fit's intercept. The intercept
/// extrapolates through whatever interference notches and spectral gaps bend
/// the band's phase; on a real mid/tweeter junction it read +158° where the
/// handover itself stood near −15°. Near 0° the junction is phase-aligned.
/// A φ near ±180° does NOT by itself settle polarity — an inverted channel
/// and a half-period delay are identical at fc — so the flip decision comes
/// from <paramref name="BestInvert"/> (a whole-band score comparison), not
/// from this angle.
/// </param>
/// <param name="PhaseConsistency">
/// The mean resultant length R (0..1) of that circular mean: how much the
/// window's bins agree on one phase. Near 1 the φ figure is clean; below
/// <see cref="JunctionPhaseAlignment.MinimumPhaseConsistency"/> it is mush
/// (a notch or gap sits right at the handover) and must not be presented as
/// a number. Zero when no usable energy exists around fc at all (then
/// <paramref name="PhaseAtCrossoverDeg"/> falls back to the fit intercept).
/// </param>
/// <param name="BestExtraDelayMs">
/// The extra delay ON THE LOWER channel that maximizes the band score, at the
/// polarity given by <paramref name="BestInvert"/> (positive: delay the lower
/// channel further). This is the recommended correction, relative to the
/// current settings. A negative value advances the lower channel — apply it as
/// a positive delay on the UPPER channel when the lower one is already at 0.
/// </param>
/// <param name="BestInvert">
/// True when flipping the LOWER channel's polarity (and applying
/// <paramref name="BestExtraDelayMs"/>) scores higher than any delay alone.
/// Derived by comparing the whole-band score of both polarities — a genuine
/// inversion aligns the band flat, which no single delay can match, so the two
/// separate for a wide enough band; for a narrow band they tie and the small
/// <paramref name="LobeMargin"/> flags the ambiguity.
/// </param>
/// <param name="BestScore">The phase score at that optimum.</param>
/// <param name="OppositePolarityScore">
/// The best score the OTHER polarity reaches over the sweep. The flip decision
/// is <paramref name="BestInvert"/> = this beats the kept polarity by a clear
/// margin; when the two are close, the polarity is genuinely ambiguous (a
/// low-frequency inversion and a half-period delay sum almost alike), so the
/// read-out keeps the current polarity — the non-disruptive default — rather
/// than recommending a flip on a coin toss. Exposed so the tooltip can show
/// how close the alternative sits.
/// </param>
/// <param name="RivalExtraDelayMs">
/// The nearest same-polarity rival lobe (the best local optimum at least a
/// substantial fraction of a period away from the global one), or null when the
/// sweep range holds no other same-polarity lobe. This is the whole-period-hop
/// ambiguity; the polarity ambiguity is <paramref name="OppositePolarityScore"/>.
/// </param>
/// <param name="RivalScore">The rival lobe's phase score.</param>
/// <param name="LobeMargin">
/// BestScore − RivalScore: how decisively the best lobe beats the nearest
/// same-polarity rival. Small margins mean the band is too narrow to
/// discriminate whole-period hops. Null without a rival.
/// </param>
/// <param name="FitDelayMs">
/// The residual delay from the weighted straight-line fit of the cross-phase:
/// positive = the lower channel is LATER than the upper across the band.
/// Unlike <paramref name="BestExtraDelayMs"/> it is lobe-blind (pure slope),
/// so the two disagree when the phase offset and the slope pull apart.
/// </param>
/// <param name="FitRmsDeg">
/// Weighted rms of the fit residual in degrees — how straight the cross-phase
/// actually is over the band (modal regions bend it).
/// </param>
public sealed record JunctionPhaseResult(
    double CurrentScore,
    double PhaseAtCrossoverDeg,
    double PhaseConsistency,
    double BestExtraDelayMs,
    bool BestInvert,
    double BestScore,
    double OppositePolarityScore,
    double? RivalExtraDelayMs,
    double? RivalScore,
    double? LobeMargin,
    double FitDelayMs,
    double FitRmsDeg);

/// <summary>
/// Cross-phase alignment analysis of one crossover junction between two
/// processed channel responses. Field-validated against a manually tuned cabin:
/// the sweep optimum reproduced the tuned subwoofer delay to within 0.3 ms and
/// resolved the L/R compromise the tune had split by hand; envelope arrivals in
/// the same band disagreed in sign, and direct-sound phase carried a systematic
/// multi-ms room shift. Pure math over spectra; no UI, no engine coupling.
/// </summary>
public static class JunctionPhaseAlignment
{
    // The steady-state analysis window is sized in TIME, not samples, so the
    // physical horizon (and therefore the fix it recommends) does not change
    // when the same measurement is captured at a different sample rate. The
    // analyzed span is exactly this many seconds at every rate; the FFT is the
    // next power of two above it, and the samples between the two are zero
    // padding, NOT extra analyzed signal — otherwise the physical window would
    // jump by up to 1.5× across a power-of-two boundary (0.68 s at 48 kHz but
    // 1.02 s at 32 kHz for the same 32768-point FFT). The cap bounds the
    // UI-thread FFT cost at exotic rates (768 kHz would otherwise ask for a
    // 1M-point transform per channel), trimming the analyzed span there.
    private const double AnalysisDurationSeconds = 0.68;
    private const int MaxAnalysisLength = 262_144;

    // A truncated IR gets a half-Hann fade over this tail (in TIME, like the
    // window) so the cut into a still-decaying room tail does not splash
    // broadband ripple into the spectrum. 46 ms sits far below the window and
    // fades a tail ~60 dB under the direct sound.
    private const double TailFadeMs = 46.0;

    // Bins whose weight |H_lower|·|H_upper| falls this far below the band
    // maximum carry no trustworthy cross-phase (one side is filtered out or
    // in a null) and are excluded from the fit and the sweep. −30 dB on the
    // product, the figure validated in the field probe.
    private const double WeightGateDb = -30.0;

    // Fewer gated bins than this cannot support a slope fit; the junction
    // reports null instead of a fabricated readout.
    private const int MinimumFitBins = 8;

    // The sweep spans this many crossover periods to each side — enough to
    // include the ±1-period rival lobes that whole-period hops land on.
    private const double SweepPeriodsEachSide = 1.25;

    // Sweep resolution: steps per crossover period. The parabolic refinement
    // below brings the reported optimum well under one step.
    private const int SweepStepsPerPeriod = 128;

    // A same-polarity local optimum only counts as a RIVAL lobe when it sits at
    // least this fraction of a period away from the global one; closer bumps
    // are texture of the same lobe.
    private const double RivalMinimumSeparationPeriods = 0.4;

    /// <summary>
    /// Flipping the lower channel's polarity is recommended only when it beats
    /// the kept polarity's best by at least this score margin, and the display
    /// marks the polarity AMBIGUOUS when the two are within it. A polarity flip
    /// is a disruptive, easily-wrong change; at low frequencies over a wide
    /// relative band an inversion and a half-period delay sum almost identically
    /// (the two best scores come within ~0.001 on a real 80 Hz sub junction), so
    /// a hair-thin advantage is not enough to advise flipping — keep the current
    /// polarity, the safe default. A genuine inversion clears this easily (it
    /// aligns the whole band flat, which no delay on the kept polarity can).
    /// </summary>
    public const double PolarityFlipAdvantage = 0.05;

    // The half-width (in octaves) of the window around the crossover that the
    // φ readout is measured over. Wide enough to average interference texture,
    // narrow enough to stay a statement about the handover itself; on the
    // field junction that exposed the intercept artifact, widths from 1/12 to
    // 1/3 octave all agreed within a few degrees.
    private const double PhaseWindowOctaves = 1.0 / 6.0;

    /// <summary>
    /// The <see cref="JunctionPhaseResult.PhaseConsistency"/> below which the
    /// φ figure must not be presented as a number — the ONE threshold the
    /// display layers share.
    /// </summary>
    public const double MinimumPhaseConsistency = 0.5;

    /// <summary>
    /// The number of IR samples actually analyzed at a given sample rate:
    /// <see cref="AnalysisDurationSeconds"/> of signal, but never more than the
    /// FFT holds (the cap can trim it at exotic rates). This is the physical
    /// window; it is the same duration at every rate.
    /// </summary>
    public static int AnalysisSamplesFor(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        int target = Math.Max(1, (int)Math.Ceiling(sampleRate * AnalysisDurationSeconds));
        return Math.Min(target, AnalysisLengthFor(sampleRate));
    }

    /// <summary>
    /// The analysis-FFT length (a power of two) for a given sample rate: the
    /// next power of two above <see cref="AnalysisSamplesFor"/>, capped. The
    /// samples past the analyzed span are zero padding. Both spectra handed to
    /// <see cref="AnalyzeSpectra"/> must have this length.
    /// </summary>
    public static int AnalysisLengthFor(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        int target = (int)Math.Ceiling(sampleRate * AnalysisDurationSeconds);
        return Math.Min(DspMath.NextPowerOfTwo(Math.Max(1, target)), MaxAnalysisLength);
    }

    /// <summary>
    /// The steady-state analysis spectrum of one processed IR at its sample
    /// rate: exactly <see cref="AnalysisSamplesFor"/> samples (tail-faded when
    /// the IR is longer), zero-padded to <see cref="AnalysisLengthFor"/> and
    /// transformed. Callers analyzing several junctions reuse one spectrum per
    /// channel.
    /// </summary>
    public static Complex[] BuildAnalysisSpectrum(Complex[] impulseResponse, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        if (impulseResponse.Length == 0)
        {
            throw new ArgumentException(
                "The impulse response is empty.", nameof(impulseResponse));
        }

        int fftLength = AnalysisLengthFor(sampleRate);
        int analysisSamples = AnalysisSamplesFor(sampleRate);
        var spectrum = new Complex[fftLength];
        // Only the analyzed span is copied; the rest of the FFT stays zero, so
        // the physical window is analysisSamples (a fixed duration) regardless
        // of how much bigger the padded FFT is.
        int copied = Math.Min(impulseResponse.Length, analysisSamples);
        Array.Copy(impulseResponse, spectrum, copied);
        // Fade only when the IR is cut mid-decay at the window edge (a shorter
        // IR ends on its own, no cut to fade).
        if (impulseResponse.Length > analysisSamples)
        {
            int fade = Math.Min(
                (int)Math.Round(TailFadeMs * sampleRate / 1000.0), copied);
            for (int i = 0; i < fade; i++)
            {
                double x = (i + 1.0) / fade;
                spectrum[copied - fade + i] *= 0.5 * (1.0 + Math.Cos(Math.PI * x));
            }
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);
        return spectrum;
    }

    /// <summary>
    /// Analyzes one junction from the two channels' processed IRs. The
    /// spectrum overload is cheaper when a channel participates in several
    /// junctions.
    /// </summary>
    public static JunctionPhaseResult? Analyze(
        Complex[] lowerImpulseResponse,
        Complex[] upperImpulseResponse,
        int sampleRate,
        double crossoverHz,
        double bandLowHz,
        double bandHighHz) =>
        AnalyzeSpectra(
            BuildAnalysisSpectrum(lowerImpulseResponse, sampleRate),
            BuildAnalysisSpectrum(upperImpulseResponse, sampleRate),
            sampleRate,
            crossoverHz,
            bandLowHz,
            bandHighHz);

    /// <summary>
    /// Analyzes one junction from precomputed analysis spectra (both from
    /// <see cref="BuildAnalysisSpectrum"/> at the same sample rate, so both are
    /// <see cref="AnalysisLengthFor"/> bins long).
    /// </summary>
    public static JunctionPhaseResult? AnalyzeSpectra(
        Complex[] lowerSpectrum,
        Complex[] upperSpectrum,
        int sampleRate,
        double crossoverHz,
        double bandLowHz,
        double bandHighHz)
    {
        ArgumentNullException.ThrowIfNull(lowerSpectrum);
        ArgumentNullException.ThrowIfNull(upperSpectrum);
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        int length = AnalysisLengthFor(sampleRate);
        if (lowerSpectrum.Length != length || upperSpectrum.Length != length)
        {
            throw new ArgumentException(
                $"Analysis spectra must be {length} bins for {sampleRate} Hz " +
                "(use BuildAnalysisSpectrum).");
        }
        if (!(crossoverHz > 0) || !(bandHighHz > bandLowHz) || !(bandLowHz > 0))
        {
            return null;
        }

        // A corner the bilinear transform cannot realize (at or above
        // 0.499·SR) is silently clamped to a DIFFERENT frequency by the
        // filter, so the spectra here roll off at the clamped corner while
        // crossoverHz still names the configured one: the sweep would use the
        // wrong period and the caller would label the result with a frequency
        // the DSP never produced. There is no measurable handover above the
        // realizable range — suppress the read-out rather than mislabel it.
        if (crossoverHz >= sampleRate * BilinearTransform.NyquistFraction)
        {
            return null;
        }

        double binWidth = sampleRate / (double)length;
        int firstBin = Math.Max(1, (int)Math.Ceiling(bandLowHz / binWidth));
        int lastBin = Math.Min(
            length / 2 - 1, (int)Math.Floor(bandHighHz / binWidth));
        if (lastBin < firstBin)
        {
            return null;
        }

        double maxWeight = 0.0;
        for (int i = firstBin; i <= lastBin; i++)
        {
            maxWeight = Math.Max(
                maxWeight,
                lowerSpectrum[i].Magnitude * upperSpectrum[i].Magnitude);
        }
        if (!(maxWeight > 0.0))
        {
            return null;
        }

        // Gated cross-phase samples. The phase is unwrapped bin to bin
        // (nearest branch) for the straight-line fit; the sweep uses the same
        // values through cos(), which is branch-blind.
        double gate = maxWeight * Math.Pow(10.0, WeightGateDb / 10.0);
        var frequencies = new List<double>();
        var phases = new List<double>();
        var weights = new List<double>();
        double previousPhase = 0.0;
        for (int i = firstBin; i <= lastBin; i++)
        {
            double weight = lowerSpectrum[i].Magnitude * upperSpectrum[i].Magnitude;
            if (weight < gate)
            {
                continue;
            }

            Complex cross = lowerSpectrum[i] * Complex.Conjugate(upperSpectrum[i]);
            double phase = cross.Phase;
            if (frequencies.Count > 0)
            {
                phase -= Math.Tau * Math.Round((phase - previousPhase) / Math.Tau);
            }
            previousPhase = phase;
            frequencies.Add(i * binWidth);
            phases.Add(phase);
            weights.Add(weight);
        }
        if (frequencies.Count < MinimumFitBins)
        {
            return null;
        }

        (double slope, double intercept, double rmsRad) =
            FitWeightedLine(frequencies, phases, weights);
        double fitDelayMs = -slope / Math.Tau * 1000.0;

        (double phaseAtCrossover, double phaseConsistency) = PhaseAtCrossover(
            frequencies, phases, weights, crossoverHz, slope, intercept);

        // One extra-delay sweep at the CURRENT polarity. Inverting the lower
        // channel negates every cross-phase, so the inverted-polarity score at
        // any delay is exactly the negative of this sweep — no second sweep is
        // needed, and the best inverted alignment is the sweep's deepest
        // trough. Comparing the two decides the flip honestly: a genuine
        // broadband inversion makes the trough reach ~+1 while no single delay
        // (the peak) can, so they separate; for a narrow band they tie and the
        // lobe margin flags it. This is why φ ≈ ±180° alone is not used to
        // recommend a flip — it cannot tell an inverted channel from a
        // half-period delay, but the whole-band score comparison can.
        double periodMs = 1000.0 / crossoverHz;
        double stepMs = periodMs / SweepStepsPerPeriod;
        double rangeMs = SweepPeriodsEachSide * periodMs;
        int steps = (int)Math.Round(rangeMs / stepMs);
        var scores = new double[2 * steps + 1];
        int maxIndex = 0;
        int minIndex = 0;
        for (int s = 0; s < scores.Length; s++)
        {
            scores[s] = Score(frequencies, phases, weights, (s - steps) * stepMs);
            if (scores[s] > scores[maxIndex]) maxIndex = s;
            if (scores[s] < scores[minIndex]) minIndex = s;
        }

        double normalBest = scores[maxIndex];
        double invertedBest = -scores[minIndex];
        // Recommend a flip only when it clearly wins — see PolarityFlipAdvantage.
        bool bestInvert = invertedBest > normalBest + PolarityFlipAdvantage;
        double oppositeScore = bestInvert ? normalBest : invertedBest;

        // Refine on the chosen polarity's sweep, where its optimum is a maximum.
        int polaritySign = bestInvert ? -1 : 1;
        double[] signedScores = bestInvert ? Negated(scores) : scores;
        int bestIndex = bestInvert ? minIndex : maxIndex;
        (double bestExtraMs, double bestScore) = RefineOptimum(
            signedScores, bestIndex, steps, stepMs,
            dt => polaritySign * Score(frequencies, phases, weights, dt));

        // The lobe margin stays a SAME-polarity question — is the best delay at
        // the right period? — because the polarity ambiguity is reported
        // separately (OppositePolarityScore) and would otherwise flag every
        // low-frequency junction, where a flip-plus-half-period always ties.
        (double? rivalExtraMs, double? rivalScore) = FindRivalLobe(
            signedScores, bestIndex, steps, stepMs,
            RivalMinimumSeparationPeriods * periodMs);

        return new JunctionPhaseResult(
            CurrentScore: Score(frequencies, phases, weights, 0.0),
            PhaseAtCrossoverDeg: phaseAtCrossover * 180.0 / Math.PI,
            PhaseConsistency: phaseConsistency,
            BestExtraDelayMs: bestExtraMs,
            BestInvert: bestInvert,
            BestScore: bestScore,
            OppositePolarityScore: oppositeScore,
            RivalExtraDelayMs: rivalExtraMs,
            RivalScore: rivalScore,
            LobeMargin: rivalScore.HasValue ? bestScore - rivalScore.Value : null,
            FitDelayMs: fitDelayMs,
            FitRmsDeg: rmsRad * 180.0 / Math.PI);
    }

    // φ at the crossover: the weighted circular mean over a narrow window around
    // fc. Local on purpose (see the result record's remarks): a fit intercept
    // extrapolates through notches. Falls back to the intercept, flagged
    // untrustworthy (R = 0), when a spectral gap leaves the window empty.
    private static (double PhaseRad, double Consistency) PhaseAtCrossover(
        List<double> frequencies,
        List<double> phases,
        List<double> weights,
        double crossoverHz,
        double slope,
        double intercept)
    {
        double windowLowHz = crossoverHz * Math.Pow(2.0, -PhaseWindowOctaves);
        double windowHighHz = crossoverHz * Math.Pow(2.0, PhaseWindowOctaves);
        double sumCos = 0.0, sumSin = 0.0, sumWindowWeight = 0.0;
        for (int k = 0; k < frequencies.Count; k++)
        {
            if (frequencies[k] < windowLowHz || frequencies[k] > windowHighHz)
            {
                continue;
            }

            sumCos += weights[k] * Math.Cos(phases[k]);
            sumSin += weights[k] * Math.Sin(phases[k]);
            sumWindowWeight += weights[k];
        }

        if (sumWindowWeight > 0.0)
        {
            return (
                Math.Atan2(sumSin, sumCos),
                Math.Sqrt(sumCos * sumCos + sumSin * sumSin) / sumWindowWeight);
        }

        double fallback = intercept + slope * crossoverHz;
        fallback -= Math.Tau * Math.Round(fallback / Math.Tau);
        return (fallback, 0.0);
    }

    private static double[] Negated(double[] values)
    {
        var result = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            result[i] = -values[i];
        }

        return result;
    }

    // Energy-weighted in-phase score with an extra delay on the lower channel:
    // the delay rotates its phase by −2πf·dt.
    private static double Score(
        List<double> frequencies,
        List<double> phases,
        List<double> weights,
        double extraDelayMs)
    {
        double numerator = 0.0;
        double denominator = 0.0;
        for (int k = 0; k < frequencies.Count; k++)
        {
            numerator += weights[k] * Math.Cos(
                phases[k] - Math.Tau * frequencies[k] * extraDelayMs / 1000.0);
            denominator += weights[k];
        }

        return denominator > 0.0 ? numerator / denominator : 0.0;
    }

    private static (double Slope, double Intercept, double RmsRad) FitWeightedLine(
        List<double> frequencies,
        List<double> phases,
        List<double> weights)
    {
        double sw = 0, swx = 0, swxx = 0, swy = 0, swxy = 0;
        for (int k = 0; k < frequencies.Count; k++)
        {
            double w = weights[k], x = frequencies[k], y = phases[k];
            sw += w;
            swx += w * x;
            swxx += w * x * x;
            swy += w * y;
            swxy += w * x * y;
        }

        double determinant = sw * swxx - swx * swx;
        // The bins passed the minimum-count gate, so a degenerate determinant
        // means they collapsed onto (numerically) one frequency; a flat fit
        // through their weighted mean is the honest fallback.
        double slope = Math.Abs(determinant) > 1e-9
            ? (sw * swxy - swx * swy) / determinant
            : 0.0;
        double intercept = Math.Abs(determinant) > 1e-9
            ? (swxx * swy - swx * swxy) / determinant
            : swy / sw;

        double residual = 0.0;
        for (int k = 0; k < frequencies.Count; k++)
        {
            double r = phases[k] - (intercept + slope * frequencies[k]);
            residual += weights[k] * r * r;
        }

        return (slope, intercept, Math.Sqrt(residual / sw));
    }

    // Parabolic refinement over the three samples around the discrete optimum
    // (a maximum of signedScores), then one exact re-evaluation at the refined
    // delay through scoreAt so the reported score is real, not interpolated.
    private static (double ExtraMs, double Score) RefineOptimum(
        double[] signedScores,
        int bestIndex,
        int steps,
        double stepMs,
        Func<double, double> scoreAt)
    {
        double bestExtraMs = (bestIndex - steps) * stepMs;
        if (bestIndex > 0 && bestIndex < signedScores.Length - 1)
        {
            double left = signedScores[bestIndex - 1];
            double middle = signedScores[bestIndex];
            double right = signedScores[bestIndex + 1];
            double denominator = left - 2.0 * middle + right;
            if (denominator < 0)
            {
                double offset = Math.Clamp(
                    0.5 * (left - right) / denominator, -0.5, 0.5);
                bestExtraMs += offset * stepMs;
            }
        }

        return (bestExtraMs, scoreAt(bestExtraMs));
    }

    // The best same-polarity local maximum far enough from the global optimum to
    // be a different lobe (a whole-period hop candidate), not a bump on it.
    private static (double? ExtraMs, double? Score) FindRivalLobe(
        double[] signedScores,
        int bestIndex,
        int steps,
        double stepMs,
        double minimumSeparationMs)
    {
        double? rivalExtraMs = null;
        double? rivalScore = null;
        for (int s = 1; s < signedScores.Length - 1; s++)
        {
            if (signedScores[s] < signedScores[s - 1] ||
                signedScores[s] <= signedScores[s + 1])
            {
                continue;
            }

            double extraMs = (s - steps) * stepMs;
            double bestExtraMs = (bestIndex - steps) * stepMs;
            if (Math.Abs(extraMs - bestExtraMs) < minimumSeparationMs)
            {
                continue;
            }
            if (!rivalScore.HasValue || signedScores[s] > rivalScore.Value)
            {
                rivalExtraMs = extraMs;
                rivalScore = signedScores[s];
            }
        }

        return (rivalExtraMs, rivalScore);
    }
}
