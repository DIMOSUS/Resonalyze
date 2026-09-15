using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>One junction's phase read-out: how well two adjacent processed channels sum in phase over their overlap, and what change would improve it.</summary>
/// <remarks>See docs/tech/junction-phase-and-group-placement.md#junction-phase-read-out.</remarks>
/// <param name="CurrentScore">Σw·cos(Δφ)/Σw in -1..1 at current settings; not the γ² coherence.</param>
/// <param name="PhaseAtCrossoverDeg">Lower minus upper at fc, ±180°, local circular mean (not the fit intercept). ±180° does not settle polarity; <paramref name="BestInvert"/> does.</param>
/// <param name="PhaseConsistency">Mean resultant length R; below <see cref="JunctionPhaseAlignment.MinimumPhaseConsistency"/> φ is not shown; 0 = no energy near fc (φ is then the fit intercept).</param>
/// <param name="BestExtraDelayMs">Extra delay on the LOWER channel, relative to current settings, at the <paramref name="BestInvert"/> polarity; negative advances it (a positive delay on the upper one when the lower is at 0).</param>
/// <param name="RivalExtraDelayMs">Highest-scoring same-polarity local optimum at least 0.4 period from the best (whole-period hop), or null.</param>
/// <param name="FitDelayMs">Lobe-blind slope delay; positive = lower channel later.</param>
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

/// <summary>Cross-phase alignment analysis of one crossover junction. Pure math over spectra; no UI, no engine coupling.</summary>
public static class JunctionPhaseAlignment
{
    // Window sized in TIME so the fix does not change with sample rate; FFT padding is zeros, not extra signal. Cap bounds FFT cost at exotic rates.
    private const double AnalysisDurationSeconds = 0.68;
    private const int MaxAnalysisLength = 262_144;

    // Half-Hann fade of a truncated tail (in time), ~60 dB under the direct sound.
    private const double TailFadeMs = 46.0;

    // Bins with |H_lower|·|H_upper| this far under the band max carry no trustworthy cross-phase.
    private const double WeightGateDb = -30.0;

    private const int MinimumFitBins = 8;

    // Enough to include the ±1-period rival lobes.
    private const double SweepPeriodsEachSide = 1.25;

    private const int SweepStepsPerPeriod = 128;

    // Closer same-polarity bumps are texture of the same lobe, not a rival.
    private const double RivalMinimumSeparationPeriods = 0.4;

    /// <summary>Flip advantage needed to recommend inversion; within it polarity is shown AMBIGUOUS (on a real 80 Hz sub junction inversion and half-period delay came within ~0.001).</summary>
    public const double PolarityFlipAdvantage = 0.05;

    // ±1/6 octave around fc; 1/12 to 1/3 octave agreed within a few degrees in the field.
    private const double PhaseWindowOctaves = 1.0 / 6.0;

    /// <summary>Below this R the φ figure is not presented as a number; the one threshold all display layers share.</summary>
    public const double MinimumPhaseConsistency = 0.5;

    /// <summary>Below this best score the recommended delay/polarity is not presented: no delay brings the band into phase.</summary>
    /// <remarks>See docs/tech/junction-phase-and-group-placement.md#thresholds.</remarks>
    public const double MinimumAlignableScore = 0.5;

    /// <summary>Samples actually analyzed: the fixed duration, trimmed only by the FFT cap.</summary>
    public static int AnalysisSamplesFor(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        int target = Math.Max(1, (int)Math.Ceiling(sampleRate * AnalysisDurationSeconds));
        return Math.Min(target, AnalysisLengthFor(sampleRate));
    }

    /// <summary>Next power of two above <see cref="AnalysisSamplesFor"/>, capped; both spectra passed to <see cref="AnalyzeSpectra"/> must have it.</summary>
    public static int AnalysisLengthFor(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        int target = (int)Math.Ceiling(sampleRate * AnalysisDurationSeconds);
        return Math.Min(DspMath.NextPowerOfTwo(Math.Max(1, target)), MaxAnalysisLength);
    }

    /// <summary>Steady-state reference spectrum of one IR (tail-faded, zero-padded). The read-out uses the windowed path; this is the baseline for comparisons and tests.</summary>
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
        int copied = Math.Min(impulseResponse.Length, analysisSamples);
        Array.Copy(impulseResponse, spectrum, copied);
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

        return AnalyzeWindowedSpectra(
            lowerSpectrum, upperSpectrum, sampleRate,
            crossoverHz, bandLowHz, bandHighHz);
    }

    /// <summary>Junction arithmetic over gated/FDW spectra (what the Virtual DSP read-out calls). Both spectra must share length AND one absolute time origin
    /// (<see cref="Resonalyze.Dsp.DataHelper.SumGatedSpectra"/>), or window placement reads as delay.</summary>
    public static JunctionPhaseResult? AnalyzeWindowedSpectra(
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

        int length = lowerSpectrum.Length;
        if (upperSpectrum.Length != length || length < 4)
        {
            throw new ArgumentException(
                "Both spectra must share one FFT length of at least 4 bins.");
        }
        if (!(crossoverHz > 0) || !(bandHighHz > bandLowHz) || !(bandLowHz > 0))
        {
            return null;
        }

        // Bilinear clamps a corner at/above the realizable limit to another frequency: suppress rather than mislabel.
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

        // Phase unwrapped bin-to-bin for the fit; the sweep uses cos(), which is branch-blind.
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

        // Inverting the lower channel negates every cross-phase, so the inverted score is -sweep: its trough is the best inverted alignment.
        // A genuine inversion reaches ~+1 there where no delay can; that, not φ≈180°, decides a flip.
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
        bool bestInvert = invertedBest > normalBest + PolarityFlipAdvantage;
        double oppositeScore = bestInvert ? normalBest : invertedBest;

        int polaritySign = bestInvert ? -1 : 1;
        double[] signedScores = bestInvert ? Negated(scores) : scores;
        int bestIndex = bestInvert ? minIndex : maxIndex;
        (double bestExtraMs, double bestScore) = RefineOptimum(
            signedScores, bestIndex, steps, stepMs,
            dt => polaritySign * Score(frequencies, phases, weights, dt));

        // Same-polarity question only: a flip plus half period always ties at low frequencies.
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

    // Falls back to the intercept with R = 0 when a spectral gap empties the window.
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

    // The delay rotates the lower channel's phase by -2πf·dt.
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
        // Degenerate determinant = bins collapsed to one frequency: flat fit through the weighted mean.
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

    // Parabolic refinement, then an exact re-evaluation so the score is real, not interpolated.
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
