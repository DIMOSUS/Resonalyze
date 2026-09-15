namespace Resonalyze.Dsp;

/// <param name="SecondsPerNeper">Sweep duration over ln(f2/f1): harmonic n sits this times ln(n) before the linear peak.</param>
/// <param name="Orders">The harmonic orders whose packets were found where this rate puts them.</param>
public sealed record EssSweepRateEstimate(double SecondsPerNeper, IReadOnlyList<int> Orders);

/// <summary>Reads an exponential sweep's rate from where its harmonic packets landed, for a deconvolution whose sweep is unknown.
/// See docs/tech/dsp-ess-harmonics.md#sweep-rate-from-harmonic-positions.</summary>
public static class EssSweepRateEstimator
{
    public const int MaxOrder = 5;

    // A packet must clear the median block peak by this much; see docs/tech/dsp-ess-harmonics.md#sweep-rate-from-harmonic-positions.
    private const double DetectionDb = 15.0;
    private const double BlockSeconds = 0.0005;
    private const double ToleranceSeconds = 0.001;
    private const double LinearPacketGuardSeconds = 0.010;
    private const int MaxCandidates = 8;

    /// <param name="deconvolved">A deconvolution with its harmonic packets before <paramref name="linearPeakIndex"/>, not wrapped.</param>
    /// <returns>Null when no packet stands above the noise before the peak.</returns>
    public static EssSweepRateEstimate? Estimate(
        IReadOnlyList<double> deconvolved,
        int linearPeakIndex,
        int sampleRate,
        double maxLookBackSeconds)
    {
        ArgumentNullException.ThrowIfNull(deconvolved);
        if (sampleRate <= 0 || (uint)linearPeakIndex >= (uint)deconvolved.Count || !(maxLookBackSeconds > 0))
        {
            return null;
        }

        int blockSize = Math.Max(1, (int)Math.Round(BlockSeconds * sampleRate));
        int regionStart = Math.Max(0, linearPeakIndex - (int)Math.Round(maxLookBackSeconds * sampleRate));
        int regionEnd = linearPeakIndex - (int)Math.Round(LinearPacketGuardSeconds * sampleRate);
        int blockCount = (regionEnd - regionStart) / blockSize;
        if (blockCount < 16)
        {
            return null;
        }

        var peaks = new double[blockCount];
        var peakIndices = new int[blockCount];
        for (int block = 0; block < blockCount; block++)
        {
            int first = regionStart + (block * blockSize);
            for (int i = first; i < first + blockSize; i++)
            {
                double magnitude = Math.Abs(deconvolved[i]);
                if (magnitude > peaks[block])
                {
                    peaks[block] = magnitude;
                    peakIndices[block] = i;
                }
            }
        }

        double[] sorted = (double[])peaks.Clone();
        Array.Sort(sorted);
        double threshold = sorted[blockCount / 2] * Math.Pow(10.0, DetectionDb / 20.0);
        if (!(threshold > 0))
        {
            return null;
        }

        List<int> detected = Enumerable.Range(0, blockCount).Where(block => peaks[block] >= threshold).ToList();
        List<int> candidates = detected
            .Where(block => IsLocalMaximum(peaks, block))
            .OrderByDescending(block => peaks[block])
            .Take(MaxCandidates)
            .ToList();

        int tolerance = Math.Max(blockSize, (int)Math.Round(ToleranceSeconds * sampleRate));
        Hypothesis? best = null;
        foreach (int candidate in candidates)
        {
            double offset = linearPeakIndex - peakIndices[candidate];
            for (int order = 2; order <= MaxOrder; order++)
            {
                Hypothesis hypothesis = Score(
                    offset / Math.Log(order), linearPeakIndex, regionStart, regionEnd, tolerance, detected, peaks, peakIndices);
                if (hypothesis.IsBetterThan(best))
                {
                    best = hypothesis;
                }
            }
        }

        if (best == null || best.Orders.Count == 0)
        {
            return null;
        }

        // Least squares over every matched packet: offset_n = L * ln(n).
        double numerator = 0, denominator = 0;
        foreach ((int order, int index) in best.Orders.Zip(best.Indices))
        {
            numerator += (linearPeakIndex - index) * Math.Log(order);
            denominator += Math.Log(order) * Math.Log(order);
        }

        return new EssSweepRateEstimate(numerator / denominator / sampleRate, best.Orders);
    }

    private static bool IsLocalMaximum(double[] peaks, int block)
    {
        for (int neighbour = Math.Max(0, block - 2); neighbour <= Math.Min(peaks.Length - 1, block + 2); neighbour++)
        {
            if (neighbour != block && peaks[neighbour] > peaks[block])
            {
                return false;
            }
        }

        return true;
    }

    private static Hypothesis Score(
        double samplesPerNeper,
        int linearPeakIndex,
        int regionStart,
        int regionEnd,
        int tolerance,
        List<int> detected,
        double[] peaks,
        int[] peakIndices)
    {
        var orders = new List<int>();
        var indices = new List<int>();
        double level = 0;
        for (int order = 2; order <= MaxOrder; order++)
        {
            double predicted = linearPeakIndex - (samplesPerNeper * Math.Log(order));
            if (predicted < regionStart || predicted >= regionEnd)
            {
                continue;
            }

            int match = -1;
            foreach (int block in detected)
            {
                if (Math.Abs(peakIndices[block] - predicted) <= tolerance &&
                    (match < 0 || peaks[block] > peaks[match]))
                {
                    match = block;
                }
            }

            if (match >= 0)
            {
                orders.Add(order);
                indices.Add(peakIndices[match]);
                level += peaks[match];
            }
        }

        return new Hypothesis(orders, indices, level);
    }

    private sealed record Hypothesis(List<int> Orders, List<int> Indices, double Level)
    {
        /// <summary>More matched orders first; then a second harmonic matched, the usual strongest; then louder packets.</summary>
        public bool IsBetterThan(Hypothesis? other)
        {
            if (other == null || Orders.Count != other.Orders.Count)
            {
                return other == null || Orders.Count > other.Orders.Count;
            }

            bool hasSecond = Orders.Contains(2), otherHasSecond = other.Orders.Contains(2);
            if (hasSecond != otherHasSecond)
            {
                return hasSecond;
            }

            return Level > other.Level;
        }
    }
}
