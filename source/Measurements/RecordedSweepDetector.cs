using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Where a sweep was found inside a recording, and how well it matched there.
/// <see cref="Quality"/> is the normalized correlation (0..1): 1 is the recording
/// holding nothing but the sweep, and a genuine acoustic take reads a few tenths
/// — the room, the noise and the system's own response all cost coherence.
/// </summary>
internal readonly record struct SweepMatch(int Start, double Quality);

/// <summary>
/// Finds the excitation in a recording by matching the SWEEP against it, rather
/// than by looking for something loud.
/// <para>
/// The difference is what the question is. A level detector asks where the
/// recording is loud, which is a proxy that content can defeat from either side:
/// a system that barely reproduces one end of the band reads as a sweep starting
/// a second late, and a voice or a door louder than a quiet measurement hides it
/// completely. Correlating against the known excitation asks where THIS sweep is,
/// and answers with the sample it starts at.
/// </para>
/// <para>
/// It also answers from much further down. Matched filtering concentrates the
/// whole sweep into one peak, so its gain is the time-bandwidth product — about
/// 46 dB for two seconds across 20 Hz to 20 kHz, more for longer sweeps. A sweep
/// well below the noise floor of the recording still produces a peak; a level
/// rule cannot see below that floor at all.
/// </para>
/// </summary>
internal static class RecordedSweepDetector
{
    // How far apart two matches must sit to count as separate takes: half a
    // sweep. Closer than that and they are the same arrival being reported twice
    // (the correlation of a chirp with itself is narrow, but a strong reflection
    // rides beside the direct sound).
    private const double SeparationShare = 0.5;

    // The search runs on a decimated copy. Correlating a ten-minute recording at
    // full rate is exact but costs gigabytes of transient spectra — 5.9 GB, more
    // than the analysis it exists to set up — while the position it finds is then
    // refined at full rate anyway. Both signals are averaged and thinned the SAME
    // way, so the correlation stays a matched filter for the pair: the peak keeps
    // its place, it only gets broader, and some of the 46 dB of processing gain
    // goes with the bandwidth.
    private const int SearchSampleCeiling = 1 << 20;

    private const int MaximumDecimation = 32;

    // How far the full-rate refinement looks either side of the decimated answer:
    // two decimated samples, which is the most the coarse peak can be out by.
    private const int RefinementSteps = 2;

    /// <summary>
    /// The best alignments of <paramref name="sweep"/> inside
    /// <paramref name="samples"/>, strongest first. Empty when there is nothing to
    /// match — no samples, no sweep, or a recording shorter than the sweep.
    /// </summary>
    public static IReadOnlyList<SweepMatch> FindSweeps(
        float[] samples,
        float[] sweep,
        int maximumMatches)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(sweep);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumMatches);
        if (sweep.Length == 0 || samples.Length < sweep.Length)
        {
            return [];
        }

        int decimation = ChooseDecimation(samples.Length, sweep.Length);
        float[] coarseSamples = Decimate(samples, decimation);
        float[] coarseSweep = Decimate(sweep, decimation);

        // Correlation is convolution with the kernel reversed, and the app's
        // overlap-add convolution already does the long-signal case in bounded
        // blocks — the whole point of not transforming a whole recording in one
        // piece.
        var reversed = new double[coarseSweep.Length];
        for (int i = 0; i < coarseSweep.Length; i++)
        {
            reversed[i] = coarseSweep[coarseSweep.Length - 1 - i];
        }

        float[] correlation = FastConvolution.Convolve(coarseSamples, reversed);
        // Cumulative energies, so every placement can be normalized in constant
        // time. Judging the match on SHAPE rather than on level is what lets a
        // quiet channel that holds the sweep outrank a loud one full of hum.
        double[] recordingEnergy = CumulativeEnergy(coarseSamples);
        double[] excitationEnergy = CumulativeEnergy(coarseSweep);

        // Placements where the sweep runs off the end of the recording are
        // included, down to half of it overlapping. A take that stopped mid-sweep
        // has its true start ONLY among those, and leaving them out does not make
        // the take usable — it makes the detector answer with the best of the
        // wrong positions, which then reads as a complete take.
        int lastStart = coarseSamples.Length - coarseSweep.Length / 2;
        int separation = Math.Max(1, (int)(coarseSweep.Length * SeparationShare));
        var matches = new List<SweepMatch>();
        var taken = new List<int>();
        for (int match = 0; match < maximumMatches; match++)
        {
            int best = -1;
            double bestQuality = 0;
            for (int start = 0; start <= lastStart; start++)
            {
                if (taken.Exists(other => Math.Abs(other - start) < separation))
                {
                    continue;
                }

                int overlap = Math.Min(coarseSweep.Length, coarseSamples.Length - start);
                double energy =
                    excitationEnergy[overlap] *
                    (recordingEnergy[start + overlap] - recordingEnergy[start]);
                if (energy <= 0)
                {
                    continue;
                }

                // Convolution output index start + kernel - 1 is the sum of the
                // recording from `start` against the sweep from its own zero.
                double quality =
                    Math.Abs(correlation[start + coarseSweep.Length - 1]) / Math.Sqrt(energy);
                if (quality > bestQuality)
                {
                    bestQuality = quality;
                    best = start;
                }
            }

            if (best < 0)
            {
                break;
            }

            taken.Add(best);
            matches.Add(decimation == 1
                ? new SweepMatch(best, bestQuality)
                : Refine(samples, sweep, best * decimation, decimation * RefinementSteps));
        }

        return matches;
    }

    // The coarse answer is out by up to a decimated sample, so the neighbourhood
    // is searched at full rate — directly, since a few dozen placements of one
    // kernel is nothing next to a transform.
    private static SweepMatch Refine(float[] samples, float[] sweep, int around, int reach)
    {
        int best = Math.Clamp(around, 0, Math.Max(0, samples.Length - sweep.Length / 2));
        double bestQuality = -1;
        double[] excitationEnergy = CumulativeEnergy(sweep);
        for (int start = around - reach; start <= around + reach; start++)
        {
            if (start < 0 || start > samples.Length - sweep.Length / 2)
            {
                continue;
            }

            int overlap = Math.Min(sweep.Length, samples.Length - start);
            double product = 0;
            double energy = 0;
            for (int i = 0; i < overlap; i++)
            {
                double recorded = samples[start + i];
                product += recorded * sweep[i];
                energy += recorded * recorded;
            }

            double scale = excitationEnergy[overlap];
            double quality = energy > 0 && scale > 0
                ? Math.Abs(product) / Math.Sqrt(scale * energy)
                : 0.0;
            if (quality > bestQuality)
            {
                bestQuality = quality;
                best = start;
            }
        }

        return new SweepMatch(best, Math.Max(bestQuality, 0.0));
    }

    // A power of two that brings the search under the ceiling, leaving the sweep
    // itself long enough to still be a chirp after thinning.
    private static int ChooseDecimation(int sampleCount, int sweepLength)
    {
        int decimation = 1;
        while (decimation < MaximumDecimation &&
            sampleCount / decimation > SearchSampleCeiling &&
            sweepLength / (decimation * 2) >= 1024)
        {
            decimation *= 2;
        }

        return decimation;
    }

    // Averaged, then thinned. The average is the anti-alias filter — crude, but
    // both signals get the same one, which is all a matched filter needs.
    private static float[] Decimate(float[] samples, int decimation)
    {
        if (decimation <= 1)
        {
            return samples;
        }

        var thinned = new float[samples.Length / decimation];
        for (int i = 0; i < thinned.Length; i++)
        {
            double sum = 0;
            int offset = i * decimation;
            for (int k = 0; k < decimation; k++)
            {
                sum += samples[offset + k];
            }

            thinned[i] = (float)(sum / decimation);
        }

        return thinned;
    }

    // Energy of samples[0..i), so any stretch costs one subtraction. Accumulated
    // in double over what can be tens of millions of terms.
    private static double[] CumulativeEnergy(float[] samples)
    {
        var energy = new double[samples.Length + 1];
        for (int i = 0; i < samples.Length; i++)
        {
            energy[i + 1] = energy[i] + (double)samples[i] * samples[i];
        }

        return energy;
    }
}
