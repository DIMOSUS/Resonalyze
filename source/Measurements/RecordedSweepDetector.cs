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

    // How many coarse samples the search aims to work in. Correlating a
    // ten-minute recording at full rate is exact but costs far more than the
    // analysis it exists to set up, while the position it finds is then refined
    // at full rate anyway. Both signals are averaged and thinned the SAME way, so
    // the correlation stays a matched filter for the pair: the peak keeps its
    // place, it only gets broader, and some of the 46 dB of processing gain goes
    // with the bandwidth. It is an aim rather than a guarantee — thinning stops
    // while the sweep is still long enough to correlate — which is why the search
    // is also chunked.
    private const int SearchSampleCeiling = 1 << 20;

    private const int MaximumDecimation = 32;

    // The least a search chunk may be. Large enough that the transform inside it
    // is efficient, small enough that what the search holds stays a few megabytes
    // whatever the recording and the sweep turn out to be.
    private const int MinimumSearchChunk = 1 << 18;

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
        float[] coarseSweep = Decimate(sweep, decimation);
        int kernel = coarseSweep.Length;
        int coarseLength = samples.Length / decimation;

        // Correlation is convolution with the kernel reversed.
        var reversed = new double[kernel];
        for (int i = 0; i < kernel; i++)
        {
            reversed[i] = coarseSweep[kernel - 1 - i];
        }

        double[] excitationEnergy = CumulativeEnergy(coarseSweep);
        // Placements where the sweep runs off the end of the recording are
        // included, down to half of it overlapping. A take that stopped mid-sweep
        // has its true start ONLY among those, and leaving them out does not make
        // the take usable — it makes the detector answer with the best of the
        // wrong positions, which then reads as a complete take.
        int lastStart = coarseLength - kernel / 2;
        int separation = Math.Max(1, (int)(kernel * SeparationShare));

        // Searched in chunks, and each chunk is thinned straight out of the
        // recording, so what the search holds is set by the chunk rather than by
        // the recording. Decimation alone cannot promise that: a sweep can be
        // short enough (5 ms per octave is 53 ms of signal) that thinning it any
        // further would leave nothing to correlate, and then a ten-minute file
        // would hold a correlation, a cumulative energy and a decimated copy of
        // itself all at once.
        int chunk = Math.Min(coarseLength, Math.Max(kernel * 4, MinimumSearchChunk));
        int advance = Math.Max(1, chunk - kernel + 1);
        var pooled = new List<SweepMatch>();
        for (int chunkStart = 0; chunkStart <= lastStart; chunkStart += advance)
        {
            int available = Math.Min(chunk, coarseLength - chunkStart);
            float[] block = DecimateRange(samples, decimation, chunkStart, available);
            float[] correlation = FastConvolution.Convolve(block, reversed);
            // Cumulative energy over the chunk, so every placement normalizes in
            // constant time. Judging the match on SHAPE rather than on level is
            // what lets a quiet channel that holds the sweep outrank a loud one
            // full of hum.
            double[] blockEnergy = CumulativeEnergy(block);
            // Interior chunks only own the placements whose window they hold whole;
            // the last one also owns those running past the end of the recording.
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

                // Convolution output index local + kernel - 1 is the sum of the
                // chunk from `local` against the sweep from its own zero.
                double quality =
                    Math.Abs(correlation[local + kernel - 1]) / Math.Sqrt(energy);
                Offer(pooled, new SweepMatch(chunkStart + local, quality), separation);
            }

            // The last chunk owns every remaining placement, including the ones
            // that run past the end. Without this the loop keeps stepping toward
            // lastStart re-transforming the same tail — and when the whole
            // recording fits one chunk, `advance` is a single sample, so it does
            // that thousands of times.
            if (last)
            {
                break;
            }
        }

        // Thinned to one match per neighbourhood BEFORE refining, because the pool
        // and `separation` are both in decimated samples: a refined start is a
        // full-rate one, and comparing it against a coarse candidate would measure
        // a distance in two different units and suppress nothing. Offer() cannot
        // stand in for this pass — replacing a pooled candidate can leave the
        // replacement within `separation` of another one.
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

    // Keeps the pooled candidates to one entry per neighbourhood: without it a
    // strong arrival contributes thousands of near-identical placements and the
    // pool grows with the recording, which is what the chunking is avoiding.
    //
    // Only the most recent entry is examined, because placements arrive in
    // increasing position — within a chunk and from one chunk to the next.
    // Scanning the whole pool instead made the search quadratic in a way only a
    // SHORT sweep shows: separation is half a sweep, so a 36 ms one leaves tens
    // of thousands of neighbourhoods in a ten-minute recording, and each of the
    // twenty-six million placements walked all of them. That cost 143 s where
    // this costs a fraction of a second.
    //
    // A replacement can leave the entry within `separation` of the one before it;
    // the final pass over the pool is what settles that.
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

    // One chunk's worth of coarse samples, averaged straight out of the recording
    // so the whole decimated copy is never materialized. On a short sweep the
    // ceiling cannot thin the recording far — a 53 ms sweep stops it at two — and
    // a ten-minute file would then carry tens of megabytes of coarse copy for the
    // whole search, beside the chunk that is the only part being read.
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
