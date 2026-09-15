using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary><see cref="Quality"/> is normalized correlation 0..1; a genuine acoustic take reads a few tenths.</summary>
internal readonly record struct SweepMatch(int Start, double Quality);

/// <summary>Locates the excitation by matched filtering, not level. See docs/tech/sweep-measurement.md#locating-a-recorded-sweep.</summary>
internal static class RecordedSweepDetector
{
    // Closer matches are one arrival reported twice (a strong reflection rides beside the direct sound).
    private const double SeparationShare = 0.5;

    // Coarse-search aim; both signals are thinned identically so the correlation stays a matched filter.
    private const int SearchSampleCeiling = 1 << 20;

    private const int MaximumDecimation = 32;

    private const int MinimumSearchChunk = 1 << 18;

    private const int RefinementSteps = 2;

    /// <summary>Best alignments, strongest first; empty when nothing can be matched.</summary>
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
        float[] coarseSweep = Decimate(sweep, decimation);
        int kernel = coarseSweep.Length;
        int coarseLength = samples.Length / decimation;

        var reversed = new double[kernel];
        for (int i = 0; i < kernel; i++)
        {
            reversed[i] = coarseSweep[kernel - 1 - i];
        }

        double[] excitationEnergy = CumulativeEnergy(coarseSweep);
        // Include placements running off the end: a take stopped mid-sweep has its true start only there.
        int lastStart = coarseLength - kernel / 2;
        int separation = Math.Max(1, (int)(kernel * SeparationShare));

        // Chunked so memory is set by the chunk, not the recording (short sweeps cannot be thinned far).
        int chunk = Math.Min(coarseLength, Math.Max(kernel * 4, MinimumSearchChunk));
        int advance = Math.Max(1, chunk - kernel + 1);
        var pooled = new List<SweepMatch>();
        for (int chunkStart = 0; chunkStart <= lastStart; chunkStart += advance)
        {
            int available = Math.Min(chunk, coarseLength - chunkStart);
            float[] block = DecimateRange(samples, decimation, chunkStart, available);
            float[] correlation = FastConvolution.Convolve(block, reversed);
            // Normalized by energy: shape, not level, lets a quiet channel holding the sweep outrank a loud hum channel.
            double[] blockEnergy = CumulativeEnergy(block);
            bool last = chunkStart + available >= coarseLength;
            int localLast = last
                ? lastStart - chunkStart
                : Math.Min(available - kernel, advance - 1);
            for (int local = 0; local <= localLast; local++)
            {
                int overlap = Math.Min(kernel, available - local);
                double energy = excitationEnergy[overlap] *
                    (blockEnergy[local + overlap] - blockEnergy[local]);
                if (energy <= 0)
                {
                    continue;
                }

                double quality =
                    Math.Abs(correlation[local + kernel - 1]) / Math.Sqrt(energy);
                Offer(pooled, new SweepMatch(chunkStart + local, quality), separation);
            }

            // The last chunk owns all remaining placements, or the loop re-transforms the tail one sample at a time.
            if (last)
            {
                break;
            }
        }

        // Thin BEFORE refining: pool and separation are in decimated samples.
        var matches = new List<SweepMatch>();
        foreach (SweepMatch match in pooled.OrderByDescending(candidate => candidate.Quality))
        {
            if (matches.Count == maximumMatches)
            {
                break;
            }
            if (matches.Exists(other => Math.Abs(other.Start - match.Start) < separation))
            {
                continue;
            }

            matches.Add(match);
        }

        return decimation == 1
            ? matches
            : matches.ConvertAll(match =>
                Refine(samples, sweep, match.Start * decimation, decimation * RefinementSteps));
    }

    // Only the latest entry is checked (placements arrive in order); scanning the pool took 143 s on a short sweep.
    // The final pass settles replacements left within separation of a predecessor.
    private static void Offer(List<SweepMatch> pooled, SweepMatch candidate, int separation)
    {
        if (pooled.Count > 0 && candidate.Start - pooled[^1].Start < separation)
        {
            if (candidate.Quality > pooled[^1].Quality)
            {
                pooled[^1] = candidate;
            }

            return;
        }

        pooled.Add(candidate);
    }

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

    // Decimated per chunk so the whole coarse copy is never materialized.
    private static float[] DecimateRange(
        float[] samples,
        int decimation,
        int coarseStart,
        int count)
    {
        var block = new float[count];
        if (decimation <= 1)
        {
            Array.Copy(samples, coarseStart, block, 0, count);
            return block;
        }

        for (int i = 0; i < count; i++)
        {
            double sum = 0;
            int offset = (coarseStart + i) * decimation;
            for (int k = 0; k < decimation; k++)
            {
                sum += samples[offset + k];
            }

            block[i] = (float)(sum / decimation);
        }

        return block;
    }

    // The average is the anti-alias filter; crude, but identical for both signals.
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
