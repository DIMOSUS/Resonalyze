using NAudio.Wave;

namespace Resonalyze.Audio;

/// <summary>The only home of file codecs (NAudio / Media Foundation). Writing is WAV only: no lossy artifacts in auditioned renders.</summary>
public static class AudioFileCodec
{
    /// <summary>WAV, MP3 and AIFF via NAudio; the rest via Media Foundation.</summary>
    public const string ReadableFilesFilter =
        "Audio files (*.wav;*.mp3;*.flac;*.m4a;*.aac;*.wma;*.aiff)" +
        "|*.wav;*.mp3;*.flac;*.m4a;*.aac;*.wma;*.aiff;*.aif" +
        "|All files (*.*)|*.*";

    private const int WriteBitsPerSample = 24;

    private const int WriteBytesPerSample = WriteBitsPerSample / 8;

    private const int ReadBlockFrames = 32_768;

    // KSDATAFORMAT_SUBTYPE_PCM and _IEEE_FLOAT.
    private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    public static AudioFileInfo Probe(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using AudioFileSource source = OpenSource(path);
        return new AudioFileInfo(
            source.Format.Channels,
            source.Format.SampleRate,
            source.Duration);
    }

    private sealed record AudioFileSource(
        ISampleProvider Samples,
        WaveFormat Format,
        TimeSpan Duration,
        IDisposable Reader) : IDisposable
    {
        public void Dispose() => Reader.Dispose();
    }

    /// <summary>WAV bypasses AudioFileReader: it sends WAVE_FORMAT_EXTENSIBLE 24-bit files to ACM, which fails. See docs/tech/audio-layer.md#wave-format-extensible.</summary>
    private static AudioFileSource OpenSource(string path)
    {
        if (!IsWaveFile(path))
        {
            var decoded = new AudioFileReader(path);
            return new AudioFileSource(
                decoded, decoded.WaveFormat, decoded.TotalTime, decoded);
        }

        var wave = new WaveFileReader(path);
        try
        {
            if (StandardizeExtensible(wave.WaveFormat) is { } standard)
            {
                return new AudioFileSource(
                    new RawSourceWaveStream(wave, standard).ToSampleProvider(),
                    standard,
                    wave.TotalTime,
                    wave);
            }

            if (wave.WaveFormat.Encoding is WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat)
            {
                return new AudioFileSource(
                    wave.ToSampleProvider(), wave.WaveFormat, wave.TotalTime, wave);
            }
        }
        catch
        {
            wave.Dispose();
            throw;
        }

        // Genuinely compressed payload in a .wav: leave it to the ACM-based reader.
        wave.Dispose();
        var file = new AudioFileReader(path);
        return new AudioFileSource(file, file.WaveFormat, file.TotalTime, file);
    }

    // NAudio exposes the extension as raw WaveFormatExtraData bytes (not WaveFormatExtensible):
    // validBitsPerSample (2), channel mask (4), subformat GUID.
    private static WaveFormat? StandardizeExtensible(WaveFormat format)
    {
        if (format.Encoding != WaveFormatEncoding.Extensible ||
            format is not WaveFormatExtraData { ExtraData.Length: >= 22 } extensible)
        {
            return null;
        }

        var subFormat = new Guid(extensible.ExtraData.AsSpan(6, 16));
        if (subFormat == PcmSubFormat)
        {
            return new WaveFormat(format.SampleRate, format.BitsPerSample, format.Channels);
        }

        return subFormat == IeeeFloatSubFormat && format.BitsPerSample == 32
            ? WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels)
            : null;
    }

    private static bool IsWaveFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".wave", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Decodes to deinterleaved float at the native rate, deinterleaving while decoding so dropped channels never take memory.</summary>
    /// <param name="maximumStoredBytes">Cap on PEAK decode memory, checked per block; holds even when the header lies about duration.</param>
    public static AudioFileContent Read(
        string path,
        TimeSpan maximumDuration,
        int channelLimit = int.MaxValue,
        long maximumStoredBytes = long.MaxValue,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (channelLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channelLimit));
        }
        if (maximumStoredBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumStoredBytes));
        }

        using AudioFileSource source = OpenSource(path);
        int channelCount = source.Format.Channels;
        int sampleRate = source.Format.SampleRate;
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

        // A running channel cursor across reads: decoders may return partial frames, and restarting per block would swap channels.
        var buffer = new float[channelCount * ReadBlockFrames];
        long totalSamples = 0;
        long keptSamples = 0;
        int channelCursor = 0;
        int read;
        while ((read = source.Samples.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int i = 0; i < read; i++)
            {
                if (channelCursor < keptChannels)
                {
                    float[] target = current[channelCursor];
                    target[fill[channelCursor]] = buffer[i];
                    keptSamples++;
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
            // Peak during assembly is payload + payload / keptChannels.
            long payloadBytes = keptSamples * sizeof(float);
            if (payloadBytes + payloadBytes / keptChannels > maximumStoredBytes)
            {
                throw new InvalidOperationException(
                    "The file decodes past the memory budget " +
                    $"({maximumStoredBytes / 1_000_000} MB); use a shorter " +
                    "excerpt.");
            }
        }

        long frameCount = totalSamples / channelCount;
        if (frameCount == 0)
        {
            throw new InvalidOperationException("The file decoded to no audio.");
        }

        // Release each channel's chunks as its array fills; a trailing partial frame is dropped by the copy bounds.
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

    /// <summary>24-bit WAV; samples outside [-1, 1] clip rather than wrap.</summary>
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
        // Unequal channel lengths would crash or truncate the interleave: refuse.
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

        // Own interleaving: WaveFileWriter's per-sample path allocates on every call.
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

    /// <summary>32-bit float WAV without scaling or clipping, for non-recordings such as FIR taps beyond ±1.</summary>
    public static void WriteWavFloat32(string path, AudioFileContent content)
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
        var format = WaveFormat.CreateIeeeFloatWaveFormat(content.SampleRate, channelCount);
        using var writer = new WaveFileWriter(path, format);
        var interleaved = new float[frameCount * channelCount];
        int offset = 0;
        for (int frame = 0; frame < frameCount; frame++)
        {
            for (int channel = 0; channel < channelCount; channel++)
            {
                interleaved[offset++] = content.Channels[channel][frame];
            }
        }

        writer.WriteSamples(interleaved, 0, interleaved.Length);
    }

    private const int Int24Maximum = 0x7FFFFF;

    private static int ToInt24(float sample)
    {
        double scaled = Math.Round(sample * Int24Maximum);
        return (int)Math.Clamp(scaled, -Int24Maximum - 1, Int24Maximum);
    }
}
