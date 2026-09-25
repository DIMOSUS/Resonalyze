using NAudio.Wave;

namespace Resonalyze.Audio;

internal static class AudioRenderBufferReader
{
    public static AudioRenderBufferRead Fill(IWaveProvider source, byte[] buffer) =>
        Fill(source, buffer, buffer?.Length ?? 0);

    /// <summary>Fills the first <paramref name="count"/> bytes, zeroing what the source leaves short.</summary>
    public static AudioRenderBufferRead Fill(IWaveProvider source, byte[] buffer, int count)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, buffer.Length);

        Array.Clear(buffer, 0, count);
        int bytesRead = 0;
        bool sourceEnded = false;
        while (bytesRead < count)
        {
            int read = source.Read(buffer, bytesRead, count - bytesRead);
            if (read == 0)
            {
                sourceEnded = true;
                break;
            }
            if (read < 0 || read > count - bytesRead)
            {
                throw new InvalidOperationException(
                    "The audio source returned an invalid byte count.");
            }
            bytesRead += read;
        }

        return new AudioRenderBufferRead(bytesRead, sourceEnded);
    }
}

internal readonly record struct AudioRenderBufferRead(int BytesRead, bool SourceEnded);
