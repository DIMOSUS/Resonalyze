namespace Resonalyze.Audio;

/// <summary>
/// Combines per-block meter statistics and creates snapshots at a bounded rate.
/// This keeps packet size and driver callback frequency from controlling managed
/// allocation and subscriber publication rates.
/// </summary>
internal sealed class AudioLevelAccumulator
{
    private const int UpdatesPerSecond = 30;

    private readonly double[] peaks;
    private readonly double[] sumSquares;
    private readonly int minimumSampleCount;
    private int sampleCount;

    public AudioLevelAccumulator(int channelCount, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        peaks = new double[channelCount];
        sumSquares = new double[channelCount];
        minimumSampleCount = Math.Max(1, sampleRate / UpdatesPerSecond);
    }

    public AudioChannelLevel[]? AddBlock(
        IReadOnlyList<double> blockPeaks,
        IReadOnlyList<double> blockSumSquares,
        int blockSampleCount)
    {
        ArgumentNullException.ThrowIfNull(blockPeaks);
        ArgumentNullException.ThrowIfNull(blockSumSquares);
        ArgumentOutOfRangeException.ThrowIfNegative(blockSampleCount);
        if (blockPeaks.Count != peaks.Length || blockSumSquares.Count != peaks.Length)
        {
            throw new ArgumentException("Meter blocks must contain one value per channel.");
        }

        for (int channel = 0; channel < peaks.Length; channel++)
        {
            peaks[channel] = Math.Max(peaks[channel], blockPeaks[channel]);
            sumSquares[channel] += blockSumSquares[channel];
        }
        sampleCount = checked(sampleCount + blockSampleCount);
        if (sampleCount < minimumSampleCount)
        {
            return null;
        }

        AudioChannelLevel[] levels = AudioLevelMetering.MeasureChannels(
            peaks,
            sumSquares,
            sampleCount);
        Array.Clear(peaks);
        Array.Clear(sumSquares);
        sampleCount = 0;
        return levels;
    }
}
