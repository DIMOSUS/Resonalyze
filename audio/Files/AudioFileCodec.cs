using NAudio.Wave;

namespace Resonalyze.Audio;

/// <summary>
/// Reads program material out of media files and writes rendered audio back as
/// WAV. This is the only place file codecs live: decoding MP3/FLAC/M4A means
/// NAudio and Media Foundation, and the app project cannot reference either.
/// <para>
/// Reading accepts whatever the platform can decode; writing is deliberately WAV
/// only. Re-encoding a render to a lossy format would put codec artifacts inside
/// the very thing being auditioned, and Windows does not guarantee an MP3 encoder
/// is even installed.
/// </para>
/// </summary>
public static class AudioFileCodec
{
    /// <summary>
    /// The file dialog filter for material this decoder accepts. WAV, MP3 and
    /// AIFF are decoded by NAudio itself; the rest go through Media Foundation,
    /// which covers a stock Windows install's FLAC/M4A/WMA support.
    /// </summary>
    public const string ReadableFilesFilter =
        "Audio files (*.wav;*.mp3;*.flac;*.m4a;*.aac;*.wma;*.aiff)" +
        "|*.wav;*.mp3;*.flac;*.m4a;*.aac;*.wma;*.aiff;*.aif" +
        "|All files (*.*)|*.*";

    /// <summary>Bit depth of written files: transparent, and universally playable.</summary>
    private const int WriteBitsPerSample = 24;

    private const int WriteBytesPerSample = WriteBitsPerSample / 8;

    // Frames pulled from the decoder per call. Large enough that the per-call
    // overhead disappears, small enough that the copy churn stays bounded.
    private const int ReadBlockFrames = 32_768;

    /// <summary>
    /// Reads a media file's format without decoding its samples, so a picker
    /// can report (or refuse) the file the moment it is chosen.
    /// </summary>
    public static AudioFileInfo Probe(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var reader = new AudioFileReader(path);
        return new AudioFileInfo(
            reader.WaveFormat.Channels,
            reader.WaveFormat.SampleRate,
            reader.TotalTime);
    }

    /// <summary>
    /// Decodes a media file to deinterleaved float PCM at its native rate,
    /// keeping only the first <paramref name="channelLimit"/> channels.
    /// <para>
    /// The stream is deinterleaved AS IT DECODES, into per-channel chunk lists:
    /// no full interleaved copy ever exists beside the per-channel data, and
    /// channels beyond the limit are never stored at all — a 7.1 file read for
    /// a stereo render costs a quarter of its decoded size, not the whole of it
    /// twice. The transient peak is the kept chunks plus one channel's final
    /// array during assembly.
    /// </para>
    /// </summary>
    /// <param name="maximumDuration">
    /// Refuses anything longer. A render holds the decoded material, its
    /// resampled copy and the result in memory at once, so an unbounded file is
    /// an out-of-memory crash rather than a slow operation.
    /// </param>
    /// <param name="channelLimit">
    /// How many leading channels to keep. The full channel layout still drives
    /// frame alignment, so dropped channels cost decoding time but no memory.
    /// </param>
    public static AudioFileContent Read(
        string path,
        TimeSpan maximumDuration,
        int channelLimit = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (channelLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channelLimit));
        }

        using var reader = new AudioFileReader(path);
        int channelCount = reader.WaveFormat.Channels;
        int sampleRate = reader.WaveFormat.SampleRate;
        if (channelCount <= 0 || sampleRate <= 0)
        {
            throw new InvalidOperationException(
                "The file reports no audio channels.");
        }

        int keptChannels = Math.Min(channelCount, channelLimit);
        long maximumFrames = (long)Math.Ceiling(
            maximumDuration.TotalSeconds * sampleRate);

        var chunks = new List<float[]>[keptChannels];
        var current = new float[keptChannels][];
        var fill = new int[keptChannels];
        for (int channel = 0; channel < keptChannels; channel++)
        {
            chunks[channel] = new List<float[]>();
            current[channel] = new float[ReadBlockFrames];
        }

        // The cursor walks the FULL channel layout by a running count across
        // read boundaries: a decoder is not contractually bound to return whole
        // frames per call, and restarting the channel assignment per block
        // would rotate every later sample's channel (L/R swapped from that
        // point on) if a call ever ended mid-frame.
        var buffer = new float[channelCount * ReadBlockFrames];
        long totalSamples = 0;
        int channelCursor = 0;
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int i = 0; i < read; i++)
            {
                if (channelCursor < keptChannels)
                {
                    float[] target = current[channelCursor];
                    target[fill[channelCursor]] = buffer[i];
                    if (++fill[channelCursor] == target.Length)
                    {
                        chunks[channelCursor].Add(target);
                        current[channelCursor] = new float[ReadBlockFrames];
                        fill[channelCursor] = 0;
                    }
                }

                if (++channelCursor == channelCount)
                {
                    channelCursor = 0;
                }
            }

            totalSamples += read;
            if (totalSamples / channelCount > maximumFrames)
            {
                throw new InvalidOperationException(
                    $"The file is longer than {maximumDuration.TotalMinutes:0} " +
                    "minutes; use a shorter excerpt.");
            }
        }

        long frameCount = totalSamples / channelCount;
        if (frameCount == 0)
        {
            throw new InvalidOperationException("The file decoded to no audio.");
        }

        // Assemble channel by channel, releasing each channel's chunks as its
        // final array fills, so the transient peak stays one channel wide. A
        // kept channel may carry one sample past frameCount (a trailing partial
        // frame at the end of the file); the copy bounds drop it.
        var channels = new float[keptChannels][];
        for (int channel = 0; channel < keptChannels; channel++)
        {
            var assembled = new float[frameCount];
            long offset = 0;
            foreach (float[] chunk in chunks[channel])
            {
                long take = Math.Min(chunk.Length, frameCount - offset);
                if (take <= 0)
                {
                    break;
                }

                Array.Copy(chunk, 0, assembled, offset, take);
                offset += take;
            }

            long tail = Math.Min(fill[channel], frameCount - offset);
            if (tail > 0)
            {
                Array.Copy(current[channel], 0, assembled, offset, tail);
            }

            chunks[channel].Clear();
            current[channel] = Array.Empty<float>();
            channels[channel] = assembled;
        }

        return new AudioFileContent(channels, sampleRate);
    }

    /// <summary>
    /// Writes deinterleaved float PCM as a 24-bit WAV file. Samples outside
    /// [-1, 1] are clipped rather than allowed to wrap into full-scale noise.
    /// </summary>
    public static void WriteWav(
        string path,
        AudioFileContent content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        if (content.ChannelCount == 0 || content.FrameCount == 0)
        {
            throw new ArgumentException("There is nothing to write.", nameof(content));
        }
        if (content.SampleRate <= 0)
        {
            throw new ArgumentException("The sample rate is invalid.", nameof(content));
        }
        // The interleaving loop below indexes every channel by the first one's
        // frame count; a shorter channel would crash it mid-write and a longer
        // one would be silently truncated, so refuse loudly instead.
        foreach (float[] channel in content.Channels)
        {
            if (channel.Length != content.FrameCount)
            {
                throw new ArgumentException(
                    "All channels must have the same length.", nameof(content));
            }
        }

        int channelCount = content.ChannelCount;
        int frameCount = content.FrameCount;
        var format = new WaveFormat(content.SampleRate, WriteBitsPerSample, channelCount);
        using var writer = new WaveFileWriter(path, format);

        // Interleaving into a byte block and writing that beats WaveFileWriter's
        // per-sample path, which allocates on every call — tens of millions of
        // allocations over a full track.
        int blockFrames = ReadBlockFrames;
        var bytes = new byte[blockFrames * channelCount * WriteBytesPerSample];
        for (int start = 0; start < frameCount; start += blockFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int frames = Math.Min(blockFrames, frameCount - start);
            int offset = 0;
            for (int frame = 0; frame < frames; frame++)
            {
                for (int channel = 0; channel < channelCount; channel++)
                {
                    int value = ToInt24(content.Channels[channel][start + frame]);
                    bytes[offset++] = (byte)value;
                    bytes[offset++] = (byte)(value >> 8);
                    bytes[offset++] = (byte)(value >> 16);
                }
            }

            writer.Write(bytes, 0, offset);
        }
    }

    private const int Int24Maximum = 0x7FFFFF;

    private static int ToInt24(float sample)
    {
        double scaled = Math.Round(sample * Int24Maximum);
        return (int)Math.Clamp(scaled, -Int24Maximum - 1, Int24Maximum);
    }
}
