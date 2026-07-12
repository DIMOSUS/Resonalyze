using NAudio.Wave;

namespace Resonalyze;

/// <summary>
/// Compatibility facade for existing measurement callers. Device lifecycle and
/// PCM processing are implemented by IAudioCaptureDevice and PcmCaptureSession.
/// </summary>
public sealed class SoundRecorder : IDisposable
{
    private PcmCaptureSession? session;
    private bool disposed;
    private int sequence;

    public event Action<float[]>? SequenceReady;
    public event Action<float[][]>? SequenceChannelsReady;
    internal event Action<AudioChannelLevel[]>? LevelsAvailable;

    public int Sequence
    {
        get => sequence;
        set
        {
            sequence = value;
            if (session != null)
            {
                session.Sequence = value;
            }
        }
    }

    public int ReadSamples => session?.ReadSamples ?? 0;
    public int SampleRate { get; private set; }
    public int Bits { get; private set; }
    public int ChannelCount { get; private set; }
    public int InputDeviceNumber { get; private set; } = -1;

    public void Init(
        int sampleRate,
        int bits,
        int channelCount,
        int inputDeviceNumber = -1,
        int expectedSamples = 0)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (bits is not (16 or 24 or 32))
        {
            throw new NotSupportedException($"Unsupported sample size: {bits} bits.");
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelCount);

        DisposeSession();
        SampleRate = sampleRate;
        Bits = bits;
        ChannelCount = channelCount;
        InputDeviceNumber = inputDeviceNumber;
        var device = new MmeCaptureDevice(
            inputDeviceNumber,
            new WaveFormat(sampleRate, bits, channelCount));
        session = new PcmCaptureSession(device, sequence, expectedSamples);
        session.SequenceReady += HandleSequenceReady;
        session.SequenceChannelsReady += HandleSequenceChannelsReady;
        session.LevelsAvailable += HandleLevelsAvailable;
    }

    public Task StartRecordingAsync(CancellationToken cancellationToken) =>
        GetSession().StartAsync(cancellationToken);

    public Task WaitForSamplesAsync(int sampleCount, CancellationToken cancellationToken) =>
        GetSession().WaitForSamplesAsync(sampleCount, cancellationToken);

    public Task StopRecordingAsync() => GetSession().StopAsync();

    public float[][] GetSamplesSnapshot() => GetSession().GetSamplesSnapshot();

    private PcmCaptureSession GetSession() => session ??
        throw new InvalidOperationException("Recorder is not initialized.");

    private void HandleSequenceReady(float[] samples) => SequenceReady?.Invoke(samples);
    private void HandleSequenceChannelsReady(float[][] samples) => SequenceChannelsReady?.Invoke(samples);
    private void HandleLevelsAvailable(AudioChannelLevel[] levels) => LevelsAvailable?.Invoke(levels);

    private void DisposeSession()
    {
        if (session == null)
        {
            return;
        }
        session.SequenceReady -= HandleSequenceReady;
        session.SequenceChannelsReady -= HandleSequenceChannelsReady;
        session.LevelsAvailable -= HandleLevelsAvailable;
        session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        session = null;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        DisposeSession();
        GC.SuppressFinalize(this);
    }
}
