using NAudio.Wave;

namespace Resonalyze.Audio;

internal static class AudioRenderBufferReader
{
    public static AudioRenderBufferRead Fill(IWaveProvider source, byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(buffer);

        Array.Clear(buffer);
        int bytesRead = 0;
        bool sourceEnded = false;
        while (bytesRead < buffer.Length)
        {
            int read = source.Read(buffer, bytesRead, buffer.Length - bytesRead);
            if (read == 0)
            {
                sourceEnded = true;
                break;
            }
            if (read < 0 || read > buffer.Length - bytesRead)
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
