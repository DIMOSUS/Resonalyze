using NAudio.Wave;

namespace Resonalyze.Audio;

internal sealed class LoopingWaveProvider : IWaveProvider
{
    private readonly WaveStream source;

    public LoopingWaveProvider(WaveStream source)
    {
        this.source = source;
    }

    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(byte[] buffer, int offset, int count)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = source.Read(buffer, offset + totalRead, count - totalRead);
            if (read == 0)
            {
                if (source.Position == 0)
                {
                    // An empty (or stuck) source would loop forever inside the
                    // audio callback; emit silence for the remainder instead.
                    Array.Clear(buffer, offset + totalRead, count - totalRead);
                    totalRead = count;
                    break;
                }

                source.Position = 0;
                continue;
            }

            totalRead += read;
        }

        return totalRead;
    }
}
