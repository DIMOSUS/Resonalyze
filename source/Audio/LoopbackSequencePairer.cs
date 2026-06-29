namespace Resonalyze;

/// <summary>
/// Pairs sequence blocks arriving independently from a microphone device and a separate
/// loopback device into combined <c>[microphone, loopback]</c> blocks for the live transfer
/// function. The two devices run on independent clocks, so blocks are paired in arrival
/// order (best-effort) and each queue is bounded so a faster device cannot accumulate
/// unbounded drift - the oldest unpaired block is dropped instead. The result is degraded
/// coherence/phase compared with a single shared device or ASIO, which is the accepted
/// trade-off for using two Wave devices.
/// </summary>
internal sealed class LoopbackSequencePairer
{
    private readonly object sync = new();
    private readonly Queue<float[]> microphoneQueue = new();
    private readonly Queue<float[]> loopbackQueue = new();
    private readonly int microphoneChannel;
    private readonly int loopbackChannel;
    private readonly int maxQueueDepth;
    private readonly Action<float[][]> emitPair;

    public LoopbackSequencePairer(
        int microphoneChannel,
        int loopbackChannel,
        Action<float[][]> emitPair,
        int maxQueueDepth = 8)
    {
        ArgumentNullException.ThrowIfNull(emitPair);
        if (maxQueueDepth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxQueueDepth));
        }

        this.microphoneChannel = microphoneChannel;
        this.loopbackChannel = loopbackChannel;
        this.emitPair = emitPair;
        this.maxQueueDepth = maxQueueDepth;
    }

    public void PushMicrophone(float[][] block) =>
        Enqueue(microphoneQueue, ExtractChannel(block, microphoneChannel));

    public void PushLoopback(float[][] block) =>
        Enqueue(loopbackQueue, ExtractChannel(block, loopbackChannel));

    public void Reset()
    {
        lock (sync)
        {
            microphoneQueue.Clear();
            loopbackQueue.Clear();
        }
    }

    private void Enqueue(Queue<float[]> queue, float[] channel)
    {
        List<float[][]>? pairs = null;
        lock (sync)
        {
            queue.Enqueue(channel);
            DropExcess(microphoneQueue);
            DropExcess(loopbackQueue);

            while (microphoneQueue.Count > 0 && loopbackQueue.Count > 0)
            {
                float[] microphone = microphoneQueue.Dequeue();
                float[] loopback = loopbackQueue.Dequeue();
                int count = Math.Min(microphone.Length, loopback.Length);
                (pairs ??= new List<float[][]>()).Add(
                    [Trim(microphone, count), Trim(loopback, count)]);
            }
        }

        if (pairs == null)
        {
            return;
        }

        // Emit outside the lock so downstream processing never blocks the capture callbacks.
        foreach (float[][] pair in pairs)
        {
            emitPair(pair);
        }
    }

    private void DropExcess(Queue<float[]> queue)
    {
        while (queue.Count > maxQueueDepth)
        {
            queue.Dequeue();
        }
    }

    private static float[] ExtractChannel(float[][] block, int channel) =>
        (uint)channel < (uint)block.Length
            ? block[channel]
            : Array.Empty<float>();

    private static float[] Trim(float[] samples, int count)
    {
        if (samples.Length == count)
        {
            return samples;
        }

        var trimmed = new float[count];
        Array.Copy(samples, trimmed, count);
        return trimmed;
    }
}
