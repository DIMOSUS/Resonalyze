namespace Resonalyze
{
    /// <summary>
    /// Re-frames a contiguous multi-channel sample stream into overlapping
    /// frames of a fixed size, advancing by a fractional hop. This decouples
    /// the analysis window size from the audio capture block size so a live
    /// analyzer can slide its FFT window with overlap instead of consuming the
    /// stream in non-overlapping blocks.
    /// </summary>
    internal sealed class OverlapReframer
    {
        private readonly int frameSize;
        private readonly int hopSize;
        private float[][]? buffers;
        private int bufferedCount;

        public OverlapReframer(int frameSize, int hopSize)
        {
            if (frameSize < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(frameSize));
            }
            if (hopSize < 1 || hopSize > frameSize)
            {
                throw new ArgumentOutOfRangeException(nameof(hopSize));
            }

            this.frameSize = frameSize;
            this.hopSize = hopSize;
        }

        public void Reset()
        {
            buffers = null;
            bufferedCount = 0;
        }

        /// <summary>
        /// Appends an incoming capture block (channels × samples) to the
        /// internal buffer and yields every complete overlapping frame that
        /// becomes available. The retained tail preserves the overlap region
        /// for frames straddling block boundaries.
        /// </summary>
        public IEnumerable<float[][]> Push(float[][] block)
        {
            ArgumentNullException.ThrowIfNull(block);
            if (block.Length == 0)
            {
                yield break;
            }

            int channelCount = block.Length;
            int incoming = block[0].Length;
            EnsureCapacity(channelCount, bufferedCount + incoming);

            for (int channel = 0; channel < channelCount; channel++)
            {
                Array.Copy(block[channel], 0, buffers![channel], bufferedCount, incoming);
            }
            bufferedCount += incoming;

            int consumed = 0;
            while (bufferedCount - consumed >= frameSize)
            {
                var frame = new float[channelCount][];
                for (int channel = 0; channel < channelCount; channel++)
                {
                    frame[channel] = new float[frameSize];
                    Array.Copy(buffers![channel], consumed, frame[channel], 0, frameSize);
                }
                yield return frame;
                consumed += hopSize;
            }

            if (consumed > 0)
            {
                int remaining = bufferedCount - consumed;
                for (int channel = 0; channel < channelCount; channel++)
                {
                    Array.Copy(buffers![channel], consumed, buffers[channel], 0, remaining);
                }
                bufferedCount = remaining;
            }
        }

        private void EnsureCapacity(int channelCount, int requiredCapacity)
        {
            if (buffers != null &&
                buffers.Length == channelCount &&
                buffers[0].Length >= requiredCapacity)
            {
                return;
            }

            int capacity = Math.Max(requiredCapacity, frameSize);
            var resized = new float[channelCount][];
            for (int channel = 0; channel < channelCount; channel++)
            {
                resized[channel] = new float[capacity];
                if (buffers != null && channel < buffers.Length && bufferedCount > 0)
                {
                    Array.Copy(buffers[channel], 0, resized[channel], 0, bufferedCount);
                }
            }
            buffers = resized;
        }
    }
}
