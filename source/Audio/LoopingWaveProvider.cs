using NAudio.Wave;

namespace Resonalyze;

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
                source.Position = 0;
                continue;
            }

            totalRead += read;
        }

        return totalRead;
    }
}
