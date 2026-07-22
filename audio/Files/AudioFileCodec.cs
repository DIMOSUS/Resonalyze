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
    /// Decodes a media file to deinterleaved float PCM at its native rate.
    /// </summary>
    /// <param name="maximumDuration">
    /// Refuses anything longer. A render holds the decoded material, its
    /// resampled copy and the result in memory at once, so an unbounded file is
    /// an out-of-memory crash rather than a slow operation.
    /// </param>
    public static AudioFileContent Read(
        string path,
        TimeSpan maximumDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var reader = new AudioFileReader(path);
        int channelCount = reader.WaveFormat.Channels;
        int sampleRate = reader.WaveFormat.SampleRate;
        if (channelCount <= 0 || sampleRate <= 0)
        {
            throw new InvalidOperationException(
                "The file reports no audio channels.");
        }

        long maximumFrames = (long)Math.Ceiling(
            maximumDuration.TotalSeconds * sampleRate);
        var blocks = new List<float[]>();
        var buffer = new float[channelCount * ReadBlockFrames];
        long sampleCount = 0;
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var block = new float[read];
            Array.Copy(buffer, block, read);
            blocks.Add(block);
            sampleCount += read;
            if (sampleCount / channelCount > maximumFrames)
            {
                throw new InvalidOperationException(
                    $"The file is longer than {maximumDuration.TotalMinutes:0} " +
                    "minutes; use a shorter excerpt.");
            }
        }

        long frameCount = sampleCount / channelCount;
        if (frameCount == 0)
        {
            throw new InvalidOperationException("The file decoded to no audio.");
        }

        var channels = new float[channelCount][];
        for (int channel = 0; channel < channelCount; channel++)
        {
            channels[channel] = new float[frameCount];
        }

        // Deinterleave by a RUNNING index across block boundaries: a decoder is
        // not contractually bound to return whole frames per call, and a
        // per-block frame loop would silently drop a trailing partial frame and
        // rotate every later sample's channel assignment (L/R swapped from that
        // point on). A trailing partial frame at the END of the file, if any,
        // falls outside frameCount and is dropped whole.
        long flatIndex = 0;
        foreach (float[] block in blocks)
        {
            foreach (float sample in block)
            {
                long frame = flatIndex / channelCount;
                if (frame >= frameCount)
                {
                    break;
                }

                channels[flatIndex % channelCount][frame] = sample;
                flatIndex++;
            }
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
