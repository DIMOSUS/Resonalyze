using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// One junction's phase read-out: how coherently two adjacent processed channels
/// sum in their overlap band, and what delay change would maximize it.
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
/// Energy-weighted band coherence Σw·cos(Δφ)/Σw at the CURRENT settings:
/// 1 = perfectly in phase across the band, −1 = perfectly out of phase.
/// </param>
/// <param name="PhaseAtCrossoverDeg">
/// The phase of lower minus upper AT the crossover, wrapped to ±180°: the
/// weighted circular mean over a narrow window around fc — deliberately a
/// local measurement, not the straight-line fit's intercept. The intercept
/// extrapolates through whatever interference notches and spectral gaps bend
/// the band's phase; on a real mid/tweeter junction it read +158° where the
/// handover itself stood near −15°. Near 0° the junction is phase-aligned;
/// near ±180° the fix is a polarity flip, not a delay.
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
/// The extra delay ON THE LOWER channel that maximizes the band coherence
/// (positive: delay the lower channel further). This is the recommended
/// correction; it is relative to the current settings.
/// </param>
/// <param name="BestScore">The coherence at that optimum.</param>
/// <param name="RivalExtraDelayMs">
/// The nearest rival lobe (the best local optimum at least Δt away from the
/// global one, where Δt is a substantial fraction of the crossover period), or
/// null when the sweep range holds no other lobe.
/// </param>
/// <param name="RivalScore">The rival lobe's coherence.</param>
/// <param name="LobeMargin">
/// BestScore − RivalScore: how decisively the best lobe beats the runner-up.
/// Small margins mean the band is too narrow to discriminate whole-period
/// hops — treat the recommendation with suspicion. Null without a rival.
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
    double BestScore,
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
    /// <summary>
    /// The steady-state analysis window in samples (≈ 0.74 s at 44.1 kHz —
    /// the same horizon as <see cref="DataHelper.GatedFftLength"/>): long
    /// enough that the room response has fully decayed into it, short enough
    /// that one FFT per channel stays cheap on the redraw path.
    /// </summary>
    public const int AnalysisLength = 32768;

    // A truncated IR gets a half-Hann fade over this tail so the cut into a
    // still-decaying room tail does not splash broadband ripple into the
    // spectrum. 2048 samples ≈ 46 ms at 44.1 kHz, far below the analysis
    // window, and the tail it fades sits ~60 dB under the direct sound.
    private const int TailFadeSamples = 2048;

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

    // A local optimum only counts as a RIVAL lobe when it sits at least this
    // fraction of a period away from the global one; closer bumps are texture
    // of the same lobe.
    private const double RivalMinimumSeparationPeriods = 0.4;

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
    /// The steady-state analysis spectrum of one processed IR: the first
    /// <see cref="AnalysisLength"/> samples (tail-faded when the IR is longer)
    /// transformed in place. Callers analyzing several junctions reuse one
    /// spectrum per channel.
    /// </summary>
    public static Complex[] BuildAnalysisSpectrum(Complex[] impulseResponse)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        if (impulseResponse.Length == 0)
        {
            throw new ArgumentException(
                "The impulse response is empty.", nameof(impulseResponse));
        }

        var spectrum = new Complex[AnalysisLength];
        int copied = Math.Min(impulseResponse.Length, AnalysisLength);
        Array.Copy(impulseResponse, spectrum, copied);
        if (impulseResponse.Length > AnalysisLength)
        {
            int fade = Math.Min(TailFadeSamples, copied);
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
            BuildAnalysisSpectrum(lowerImpulseResponse),
            BuildAnalysisSpectrum(upperImpulseResponse),
            sampleRate,
            crossoverHz,
            bandLowHz,
            bandHighHz);

    /// <summary>
    /// Analyzes one junction from precomputed analysis spectra (both from
    /// <see cref="BuildAnalysisSpectrum"/>, so both are
    /// <see cref="AnalysisLength"/> bins of the same sample rate).
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
        if (lowerSpectrum.Length != AnalysisLength ||
            upperSpectrum.Length != AnalysisLength)
        {
            throw new ArgumentException(
                $"Analysis spectra must be {AnalysisLength} bins " +
                "(use BuildAnalysisSpectrum).");
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }
        if (!(crossoverHz > 0) || !(bandHighHz > bandLowHz) || !(bandLowHz > 0))
        {
            return null;
        }

        double binWidth = sampleRate / (double)AnalysisLength;
        int firstBin = Math.Max(1, (int)Math.Ceiling(bandLowHz / binWidth));
        int lastBin = Math.Min(
            AnalysisLength / 2 - 1, (int)Math.Floor(bandHighHz / binWidth));
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

        // φ at the crossover: the weighted circular mean over a narrow window
        // around fc. Local on purpose — see the result record's remarks. The
        // unwrapped phases feed cos/sin directly, which is branch-blind.
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

        double phaseAtCrossover;
        double phaseConsistency;
        if (sumWindowWeight > 0.0)
        {
            phaseAtCrossover = Math.Atan2(sumSin, sumCos);
            phaseConsistency =
                Math.Sqrt(sumCos * sumCos + sumSin * sumSin) / sumWindowWeight;
        }
        else
        {
            // A spectral-gap junction can leave the fc window with no gated
            // bins at all; fall back to the fit's intercept but mark the
            // figure untrustworthy rather than presenting it as measured.
            phaseAtCrossover = intercept + slope * crossoverHz;
            phaseAtCrossover -=
                Math.Tau * Math.Round(phaseAtCrossover / Math.Tau);
            phaseConsistency = 0.0;
        }

        double periodMs = 1000.0 / crossoverHz;
        double stepMs = periodMs / SweepStepsPerPeriod;
        double rangeMs = SweepPeriodsEachSide * periodMs;
        int steps = (int)Math.Round(rangeMs / stepMs);
        var scores = new double[2 * steps + 1];
        for (int s = 0; s < scores.Length; s++)
        {
            scores[s] = Score(frequencies, phases, weights, (s - steps) * stepMs);
        }

        int bestIndex = 0;
        for (int s = 1; s < scores.Length; s++)
        {
            if (scores[s] > scores[bestIndex])
            {
                bestIndex = s;
            }
        }

        (double bestExtraMs, double bestScore) =
            RefineOptimum(scores, bestIndex, steps, stepMs, frequencies, phases, weights);

        (double? rivalExtraMs, double? rivalScore) = FindRivalLobe(
            scores, bestIndex, steps, stepMs,
            RivalMinimumSeparationPeriods * periodMs);

        return new JunctionPhaseResult(
            CurrentScore: Score(frequencies, phases, weights, 0.0),
            PhaseAtCrossoverDeg: phaseAtCrossover * 180.0 / Math.PI,
            PhaseConsistency: phaseConsistency,
            BestExtraDelayMs: bestExtraMs,
            BestScore: bestScore,
            RivalExtraDelayMs: rivalExtraMs,
            RivalScore: rivalScore,
            LobeMargin: rivalScore.HasValue ? bestScore - rivalScore.Value : null,
            FitDelayMs: fitDelayMs,
            FitRmsDeg: rmsRad * 180.0 / Math.PI);
    }

    // Energy-weighted band coherence with an extra delay on the lower channel:
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

    // Parabolic refinement over the three samples around the discrete optimum,
    // then one exact re-evaluation at the refined delay so the reported score
    // is a real score, not an interpolation.
    private static (double ExtraMs, double Score) RefineOptimum(
        double[] scores,
        int bestIndex,
        int steps,
        double stepMs,
        List<double> frequencies,
        List<double> phases,
        List<double> weights)
    {
        double bestExtraMs = (bestIndex - steps) * stepMs;
        if (bestIndex > 0 && bestIndex < scores.Length - 1)
        {
            double left = scores[bestIndex - 1];
            double middle = scores[bestIndex];
            double right = scores[bestIndex + 1];
            double denominator = left - 2.0 * middle + right;
            if (denominator < 0)
            {
                double offset = Math.Clamp(
                    0.5 * (left - right) / denominator, -0.5, 0.5);
                bestExtraMs += offset * stepMs;
            }
        }

        return (bestExtraMs, Score(frequencies, phases, weights, bestExtraMs));
    }

    // The best local maximum far enough from the global optimum to be a
    // different lobe (a whole-period hop candidate), not a bump on the same one.
    private static (double? ExtraMs, double? Score) FindRivalLobe(
        double[] scores,
        int bestIndex,
        int steps,
        double stepMs,
        double minimumSeparationMs)
    {
        double? rivalExtraMs = null;
        double? rivalScore = null;
        for (int s = 1; s < scores.Length - 1; s++)
        {
            if (scores[s] < scores[s - 1] || scores[s] <= scores[s + 1])
            {
                continue;
            }

            double extraMs = (s - steps) * stepMs;
            double bestExtraMs = (bestIndex - steps) * stepMs;
            if (Math.Abs(extraMs - bestExtraMs) < minimumSeparationMs)
            {
                continue;
            }
            if (!rivalScore.HasValue || scores[s] > rivalScore.Value)
            {
                rivalExtraMs = extraMs;
                rivalScore = scores[s];
            }
        }

        return (rivalExtraMs, rivalScore);
    }
}
