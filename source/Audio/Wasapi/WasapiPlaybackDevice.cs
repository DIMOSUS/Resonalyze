using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Resonalyze;

public sealed class WasapiPlaybackDevice : IAudioPlaybackDevice
{
    private readonly MMDeviceEnumerator enumerator;
    private readonly MMDevice endpoint;
    private readonly WasapiOut output;
    private readonly string endpointId;
    private readonly string friendlyName;
    private TaskCompletionSource<bool>? playbackEnded;
    private IWaveProvider? initializedSource;
    private IWaveProvider? initializedProvider;
    private bool disposed;

    public WasapiPlaybackDevice(
        string endpointId,
        int bufferMilliseconds = 100,
        AudioClientShareMode shareMode = AudioClientShareMode.Shared,
        WaveFormat? requestedFormat = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferMilliseconds);
        enumerator = new MMDeviceEnumerator();
        endpoint = enumerator.GetDevice(endpointId);
        if (endpoint.DataFlow != DataFlow.Render)
        {
            throw new ArgumentException("The endpoint is not a render device.", nameof(endpointId));
        }
        this.endpointId = endpoint.ID;
        friendlyName = endpoint.FriendlyName;
        output = new WasapiOut(
            endpoint,
            shareMode,
            useEventSync: true,
            bufferMilliseconds);
        output.PlaybackStopped += HandlePlaybackStopped;
        PlaybackFormat = shareMode == AudioClientShareMode.Exclusive
            ? requestedFormat ?? throw new ArgumentNullException(
                nameof(requestedFormat),
                "WASAPI Exclusive playback requires an explicit format.")
            : endpoint.AudioClient.MixFormat;
        ShareMode = shareMode;
    }

    public WaveFormat PlaybackFormat { get; private set; }
    public string EndpointId => endpointId;
    public string FriendlyName => friendlyName;
    public AudioClientShareMode ShareMode { get; }

    public Task StartAsync(IWaveProvider source, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (playbackEnded is { Task.IsCompleted: false })
        {
            throw new InvalidOperationException("Playback is already running.");
        }
        if (source.WaveFormat.SampleRate != PlaybackFormat.SampleRate)
        {
            throw new InvalidOperationException(
                $"WASAPI {ShareMode} requires {PlaybackFormat.SampleRate} Hz, " +
                $"but the source is {source.WaveFormat.SampleRate} Hz.");
        }
        if (ShareMode == AudioClientShareMode.Exclusive &&
            (source.WaveFormat.BitsPerSample != PlaybackFormat.BitsPerSample ||
                source.WaveFormat.Channels != PlaybackFormat.Channels))
        {
            throw new InvalidOperationException(
                $"WASAPI Exclusive source format ({source.WaveFormat}) does not match " +
                $"the requested endpoint format ({PlaybackFormat}).");
        }
        if (initializedSource != null && !ReferenceEquals(initializedSource, source))
        {
            throw new InvalidOperationException(
                "This playback session is already initialized with another source.");
        }

        playbackEnded = SampleWaiterRegistry.NewSignal();
        if (initializedSource == null)
        {
            initializedProvider = ShareMode == AudioClientShareMode.Exclusive
                ? new WaveFormatOverrideProvider(source, PlaybackFormat)
                : source;
            output.Init(initializedProvider);
            initializedSource = source;
        }
        output.Play();
        return Task.CompletedTask;
    }

    public async Task WaitForPlaybackEndAsync(CancellationToken cancellationToken)
    {
        Task task = playbackEnded?.Task ??
            throw new InvalidOperationException("Playback has not been started.");
        await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync()
    {
        if (!disposed && playbackEnded is { Task.IsCompleted: false })
        {
            output.Stop();
        }
        return Task.CompletedTask;
    }

    private void HandlePlaybackStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception == null)
        {
            playbackEnded?.TrySetResult(true);
        }
        else
        {
            playbackEnded?.TrySetException(args.Exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        await StopAsync().ConfigureAwait(false);
        disposed = true;
        output.PlaybackStopped -= HandlePlaybackStopped;
        output.Dispose();
        // See WasapiCaptureDevice: explicitly releasing this MMDevice can poison
        // a later RCW for the same endpoint identity.
        enumerator.Dispose();
    }

    private sealed class WaveFormatOverrideProvider : IWaveProvider
    {
        private readonly IWaveProvider source;

        public WaveFormatOverrideProvider(IWaveProvider source, WaveFormat waveFormat)
        {
            this.source = source;
            WaveFormat = waveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count) =>
            source.Read(buffer, offset, count);
    }
}
