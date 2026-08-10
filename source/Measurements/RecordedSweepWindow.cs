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
    /// Kept before the detected onset: enough for the detector to be late (its
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

    // Onset threshold, relative to the loudest window in the recording. An
    // exponential sweep holds a flat envelope for its whole length, so its body
    // sits within a few dB of the maximum; 20 dB down finds the fade-in without
    // being tripped by room noise.
    private const double OnsetThresholdRatio = 0.01;

    // How long the level has to STAY above the threshold to count as the
    // excitation. A door slam or a tap on the recorder is one window wide and
    // would otherwise define the onset, cutting the window before the sweep.
    private const double SustainSeconds = 0.1;

    private const double WindowSeconds = 0.01;

    /// <summary>
    /// The span to analyze. Degenerate cases (a silent file, no sustained onset,
    /// a recording already shorter than the window) fall back to as much of the
    /// recording as the bound allows, starting at its beginning — the caller's
    /// credibility check is what judges the result either way.
    /// </summary>
    public static RecordedSweepSpan Locate(
        float[] samples,
        int sampleRate,
        int sweepSamples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (sampleRate <= 0 || sweepSamples <= 0)
        {
            return new RecordedSweepSpan(0, samples.Length);
        }

        int lead = (int)(LeadInSeconds * sampleRate);
        int bound = sweepSamples + lead + (int)(TailSeconds * sampleRate);
        if (samples.Length <= bound)
        {
            return new RecordedSweepSpan(0, samples.Length);
        }

        int onset = FindOnset(samples, sampleRate);
        int start = Math.Max(0, onset - lead);
        return new RecordedSweepSpan(start, Math.Min(bound, samples.Length - start));
    }

    // The first sample of the first sustained rise above the threshold, or 0 when
    // there is none (a silent recording, or one with no level structure at all).
    private static int FindOnset(float[] samples, int sampleRate)
    {
        int window = Math.Max(1, (int)(WindowSeconds * sampleRate));
        int windowCount = samples.Length / window;
        if (windowCount == 0)
        {
            return 0;
        }

        var power = new double[windowCount];
        double loudest = 0;
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
            loudest = Math.Max(loudest, power[index]);
        }
        if (loudest <= 0)
        {
            return 0;
        }

        double threshold = loudest * OnsetThresholdRatio;
        int sustain = Math.Max(1, (int)(SustainSeconds * sampleRate) / window);
        int run = 0;
        for (int index = 0; index < windowCount; index++)
        {
            if (power[index] < threshold)
            {
                run = 0;
                continue;
            }

            run++;
            if (run >= sustain)
            {
                return (index - run + 1) * window;
            }
        }

        return 0;
    }
}
