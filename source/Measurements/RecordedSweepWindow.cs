namespace Resonalyze;

/// <summary>
/// The stretch of a recorded sweep file that is worth analyzing: the excitation
/// plus room for what arrives before and decays after it.
/// </summary>
internal readonly record struct RecordedSweepSpan(int Start, int Length);

/// <summary>
/// Finds the excitation inside a recording that may be far longer than it. The
/// natural way to make one is to start the recorder, walk to the listening
/// position, play the sweep and walk back, which leaves tens of seconds of
/// silence on both sides — and every FFT in the analysis is sized by the
/// recording, not by the sweep. A five-minute file holding a two-second sweep
/// costs gigabytes of transient spectra and leaves behind a transfer IR of the
/// same length, which then has to be held in memory and written into any saved
/// measurement. Cutting the analysis down to the excitation and its decay bounds
/// all of that by the sweep's own length.
/// </summary>
internal static class RecordedSweepWindow
{
    /// <summary>
    /// Kept before the excitation: enough for the detector to be late (its
    /// threshold is crossed inside the fade-in, not at the first sample), and
    /// enough that the arrival does not land at index zero.
    /// </summary>
    private const double LeadInSeconds = 0.5;

    /// <summary>
    /// Kept after the excitation ends, for the decay that follows it. Half a
    /// second covers a car cabin and two seconds a live room; anything longer is
    /// below the noise floor of a recording made this way.
    /// </summary>
    private const double TailSeconds = 2.0;

    // Level above which a window counts as excited, relative to the loudest
    // level the recording SUSTAINS. An exponential sweep holds a flat envelope
    // for its whole length, so its body sits within a few dB of that; 20 dB down
    // finds the fade-in without being tripped by room noise.
    private const double ExcitationThresholdRatio = 0.01;

    // How long a level has to hold to set the reference above. A door slam or a
    // tap on the recorder is one window wide, and letting it define the loudest
    // level would put the whole sweep below the threshold.
    private const double SustainSeconds = 0.1;

    // Silence this long ends a stretch. Shorter drops (a pause between spoken
    // words, a dip in the sweep's fade) keep the stretch together.
    private const double GapSeconds = 0.2;

    private const double WindowSeconds = 0.01;

    /// <summary>
    /// Spans that may hold the excitation, most likely first — by how much loud
    /// audio each stretch carries, earliest first among equals. Ranking cannot be
    /// certain (a passage of speech before the sweep is loud and sustained too),
    /// so the caller analyzes them in order and keeps the one that produces a
    /// credible impulse response instead of committing to the first.
    /// </summary>
    /// <remarks>
    /// Always returns at least one span. Degenerate cases — a silent file, no
    /// loud stretch at all, a recording already shorter than the bound — fall
    /// back to as much of the recording as the bound allows, starting at its
    /// beginning; the caller's credibility check is what judges the result
    /// either way.
    /// </remarks>
    public static IReadOnlyList<RecordedSweepSpan> LocateCandidates(
        float[] samples,
        int sampleRate,
        int sweepSamples,
        int maximumCandidates = 3)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCandidates);
        if (sampleRate <= 0 || sweepSamples <= 0)
        {
            return [new RecordedSweepSpan(0, samples.Length)];
        }

        int lead = (int)(LeadInSeconds * sampleRate);
        int bound = sweepSamples + lead + (int)(TailSeconds * sampleRate);
        if (samples.Length <= bound)
        {
            return [new RecordedSweepSpan(0, samples.Length)];
        }

        var spans = new List<RecordedSweepSpan>();
        foreach (int onset in FindExcitationStarts(
            samples, sampleRate, sweepSamples, maximumCandidates))
        {
            int start = Math.Max(0, onset - lead);
            if (!spans.Exists(span => span.Start == start))
            {
                spans.Add(new RecordedSweepSpan(
                    start, Math.Min(bound, samples.Length - start)));
            }
        }

        return spans.Count > 0 ? spans : [new RecordedSweepSpan(0, bound)];
    }

    // The starts of the loud stretches, longest first. The sweep is one
    // uninterrupted stretch as long as itself; speech, footsteps and handling
    // noise are shorter ones, so this usually ranks the excitation first — and
    // when it does not, the caller falls through to the next candidate.
    private static List<int> FindExcitationStarts(
        float[] samples,
        int sampleRate,
        int sweepSamples,
        int maximumCandidates)
    {
        int window = Math.Max(1, (int)(WindowSeconds * sampleRate));
        int windowCount = samples.Length / window;
        if (windowCount == 0)
        {
            return [];
        }

        var power = new double[windowCount];
        for (int index = 0; index < windowCount; index++)
        {
            double sum = 0;
            int offset = index * window;
            for (int i = 0; i < window; i++)
            {
                double sample = samples[offset + i];
                sum += sample * sample;
            }
            power[index] = sum / window;
        }

        double reference = LoudestSustainedPower(
            power,
            Math.Max(1, (int)(SustainSeconds * sampleRate) / window));
        if (reference <= 0)
        {
            return [];
        }

        double threshold = reference * ExcitationThresholdRatio;
        int gap = Math.Max(1, (int)(GapSeconds * sampleRate) / window);
        // (start, loud windows) per stretch, in the order they occur.
        var stretches = new List<(int Start, int Loud)>();
        int stretchStart = -1;
        int loud = 0;
        int silent = 0;
        for (int index = 0; index < windowCount; index++)
        {
            if (power[index] >= threshold)
            {
                if (stretchStart < 0)
                {
                    stretchStart = index;
                    loud = 0;
                }
                loud++;
                silent = 0;
                continue;
            }

            if (stretchStart >= 0 && ++silent >= gap)
            {
                stretches.Add((stretchStart, loud));
                stretchStart = -1;
            }
        }
        if (stretchStart >= 0)
        {
            stretches.Add((stretchStart, loud));
        }

        // Longest first, earliest among equals — so a take holding the same sweep
        // twice measures the first one.
        return stretches
            .OrderByDescending(stretch => stretch.Loud)
            .ThenBy(stretch => stretch.Start)
            .Take(maximumCandidates)
            .Select(stretch => stretch.Start * window)
            .ToList();
    }

    // The loudest level the recording holds for a whole run of windows, which is
    // what a sustained excitation reaches and a transient cannot.
    private static double LoudestSustainedPower(double[] power, int run)
    {
        if (power.Length < run)
        {
            return power.Length > 0 ? power.Max() : 0.0;
        }

        double loudest = 0;
        for (int start = 0; start + run <= power.Length; start++)
        {
            double quietest = double.MaxValue;
            for (int i = 0; i < run; i++)
            {
                quietest = Math.Min(quietest, power[start + i]);
            }
            loudest = Math.Max(loudest, quietest);
        }

        return loudest;
    }
}
