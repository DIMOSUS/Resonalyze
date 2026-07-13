namespace Resonalyze.Audio;

/// <summary>
/// The shared sample-accumulation core of the audio recorders: multi-channel
/// append, fixed-length sequence extraction and a full snapshot, with explicit
/// capacity control. Designed for use inside audio callbacks: appends are plain
/// array copies, capacity can be pre-allocated up front (a sweep's length is
/// known before recording starts), and in sequence mode the consumed prefix is
/// dropped so a live session's memory stays bounded instead of growing — and
/// periodically re-allocating — without limit.
/// </summary>
internal sealed class CaptureAccumulator
{
    private readonly float[][] buffers;
    private int capacity;
    private int bufferedCount;
    // Absolute sample index of buffers[channel][0]; stays 0 until trimming starts.
    private int retainedStart;
    private int sequenceStart;

    public CaptureAccumulator(int channelCount, int sequenceLength, int initialCapacity)
    {
        if (channelCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channelCount));
        }

        ChannelCount = channelCount;
        SequenceLength = Math.Max(0, sequenceLength);
        capacity = Math.Max(1, initialCapacity);
        buffers = new float[channelCount][];
        for (int channel = 0; channel < channelCount; channel++)
        {
            buffers[channel] = new float[capacity];
        }
    }

    public int ChannelCount { get; }
    public int SequenceLength { get; }

    /// <summary>Total samples appended per channel since construction.</summary>
    public int ReadSamples { get; private set; }

    /// <summary>
    /// Appends <paramref name="count"/> samples per channel from
    /// <paramref name="block"/> (channel-major).
    /// </summary>
    public void Append(float[][] block, int count)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (block.Length < ChannelCount)
        {
            throw new ArgumentException(
                "The block does not carry every channel.",
                nameof(block));
        }
        if (count <= 0)
        {
            return;
        }

        EnsureCapacity(bufferedCount + count);
        for (int channel = 0; channel < ChannelCount; channel++)
        {
            Array.Copy(block[channel], 0, buffers[channel], bufferedCount, count);
        }

        bufferedCount += count;
        ReadSamples += count;
    }

    /// <summary>
    /// Extracts every complete sequence that became available, then drops the
    /// consumed prefix (sequence mode retains only the unconsumed tail, so
    /// <see cref="Snapshot"/> is meaningful only without sequences). Returns null
    /// when sequences are not configured or none is ready.
    /// </summary>
    public List<float[][]>? ExtractReadySequences()
    {
        if (SequenceLength <= 0)
        {
            return null;
        }

        List<float[][]>? ready = null;
        while (ReadSamples - sequenceStart >= SequenceLength)
        {
            var sequence = new float[ChannelCount][];
            int offset = sequenceStart - retainedStart;
            for (int channel = 0; channel < ChannelCount; channel++)
            {
                sequence[channel] = new float[SequenceLength];
                Array.Copy(
                    buffers[channel],
                    offset,
                    sequence[channel],
                    0,
                    SequenceLength);
            }

            sequenceStart += SequenceLength;
            (ready ??= new List<float[][]>()).Add(sequence);
        }

        TrimConsumed();
        return ready;
    }

    /// <summary>A copy of the retained samples per channel.</summary>
    public float[][] Snapshot()
    {
        var snapshot = new float[ChannelCount][];
        for (int channel = 0; channel < ChannelCount; channel++)
        {
            snapshot[channel] = new float[bufferedCount];
            Array.Copy(buffers[channel], snapshot[channel], bufferedCount);
        }

        return snapshot;
    }

    private void TrimConsumed()
    {
        int consumed = sequenceStart - retainedStart;
        if (consumed <= 0)
        {
            return;
        }

        int remaining = bufferedCount - consumed;
        for (int channel = 0; channel < ChannelCount; channel++)
        {
            Array.Copy(buffers[channel], consumed, buffers[channel], 0, remaining);
        }

        retainedStart = sequenceStart;
        bufferedCount = remaining;
    }

    private void EnsureCapacity(int required)
    {
        if (required <= capacity)
        {
            return;
        }

        int grown = capacity;
        while (grown < required)
        {
            grown *= 2;
        }

        for (int channel = 0; channel < ChannelCount; channel++)
        {
            var resized = new float[grown];
            Array.Copy(buffers[channel], resized, bufferedCount);
            buffers[channel] = resized;
        }

        capacity = grown;
    }
}
