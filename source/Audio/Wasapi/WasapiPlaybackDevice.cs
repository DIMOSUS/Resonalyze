using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Resonalyze;

public sealed class WasapiPlaybackDevice : IAudioPlaybackDevice
{
    private const long ReferenceTimesPerMillisecond = 10_000;

    private readonly MMDeviceEnumerator enumerator;
    private readonly MMDevice endpoint;
    private readonly AudioClient audioClient;
    private readonly EventWaitHandle bufferReady = new(false, EventResetMode.AutoReset);
    private readonly string endpointId;
    private readonly string friendlyName;
    private readonly int bufferMilliseconds;
    private Thread? renderThread;
    private TaskCompletionSource<bool>? playbackEnded;
    private IWaveProvider? initializedSource;
    private WaveFormat? streamFormat;
    private volatile bool stopRequested;
    private bool initialized;
    private bool disposed;
    private long lastBufferFillTimestamp;

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
        this.bufferMilliseconds = bufferMilliseconds;
        audioClient = endpoint.AudioClient;
        PlaybackFormat = shareMode == AudioClientShareMode.Exclusive
            ? requestedFormat ?? throw new ArgumentNullException(
                nameof(requestedFormat),
                "WASAPI Exclusive playback requires an explicit format.")
            : audioClient.MixFormat;
        ShareMode = shareMode;
    }

    public WaveFormat PlaybackFormat { get; }
    public string EndpointId => endpointId;
    public string FriendlyName => friendlyName;
    public AudioClientShareMode ShareMode { get; }
    public long RenderCallbacks { get; private set; }
    public long RenderUnderruns { get; private set; }
    public int ActualBufferFrames { get; private set; }

    public Task StartAsync(IWaveProvider source, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (renderThread != null)
        {
            throw new InvalidOperationException("Playback is already running.");
        }
        ValidateFormat(source.WaveFormat);
        if (initializedSource != null && !ReferenceEquals(initializedSource, source))
        {
            throw new InvalidOperationException(
                "This playback session is already initialized with another source.");
        }

        Initialize(source);
        initializedSource = source;
        stopRequested = false;
        playbackEnded = SampleWaiterRegistry.NewSignal();
        renderThread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "Resonalyze WASAPI render"
        };
        renderThread.Start();
        return Task.CompletedTask;
    }

    public async Task WaitForPlaybackEndAsync(CancellationToken cancellationToken)
    {
        Task task = playbackEnded?.Task ??
            throw new InvalidOperationException("Playback has not been started.");
        await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        if (disposed || renderThread == null || playbackEnded == null)
        {
            return;
        }
        stopRequested = true;
        bufferReady.Set();
        await playbackEnded.Task.WaitAsync(AudioCaptureStop.StopTimeout).ConfigureAwait(false);
    }

    private void ValidateFormat(WaveFormat sourceFormat)
    {
        if (sourceFormat.SampleRate != PlaybackFormat.SampleRate)
        {
            throw new InvalidOperationException(
                $"WASAPI {ShareMode} requires {PlaybackFormat.SampleRate} Hz, " +
                $"but the source is {sourceFormat.SampleRate} Hz.");
        }
        if (ShareMode == AudioClientShareMode.Exclusive &&
            (sourceFormat.BitsPerSample != PlaybackFormat.BitsPerSample ||
                sourceFormat.Channels != PlaybackFormat.Channels))
        {
            throw new InvalidOperationException(
                $"WASAPI Exclusive source format ({sourceFormat}) does not match " +
                $"the requested endpoint format ({PlaybackFormat}).");
        }
    }

    private void Initialize(IWaveProvider source)
    {
        if (initialized)
        {
            return;
        }
        streamFormat = ShareMode == AudioClientShareMode.Exclusive
            ? PlaybackFormat
            : source.WaveFormat;
        long duration = bufferMilliseconds * ReferenceTimesPerMillisecond;
        AudioClientStreamFlags flags = AudioClientStreamFlags.EventCallback;
        if (ShareMode == AudioClientShareMode.Shared)
        {
            flags |= AudioClientStreamFlags.AutoConvertPcm |
                AudioClientStreamFlags.SrcDefaultQuality;
            audioClient.Initialize(ShareMode, flags, duration, 0, streamFormat, Guid.Empty);
        }
        else
        {
            audioClient.Initialize(ShareMode, flags, duration, duration, streamFormat, Guid.Empty);
        }
        audioClient.SetEventHandle(bufferReady.SafeWaitHandle.DangerousGetHandle());
        ActualBufferFrames = audioClient.BufferSize;
        initialized = true;
    }

    private void RenderLoop()
    {
        Exception? failure = null;
        bool clientStarted = false;
        try
        {
            AudioRenderClient render = audioClient.AudioRenderClient;
            bool sourceEnded = !FillBuffer(render, ActualBufferFrames);
            audioClient.Start();
            clientStarted = true;
            while (!stopRequested)
            {
                bufferReady.WaitOne(bufferMilliseconds * 3);
                if (stopRequested)
                {
                    break;
                }

                int padding = audioClient.CurrentPadding;
                if (sourceEnded)
                {
                    if (padding == 0)
                    {
                        break;
                    }
                    continue;
                }
                TimeSpan sinceLastFill = Stopwatch.GetElapsedTime(lastBufferFillTimestamp);
                TimeSpan bufferDuration = TimeSpan.FromSeconds(
                    (double)ActualBufferFrames / (streamFormat?.SampleRate ?? 1));
                if (WasapiRenderTiming.IsUnderrun(
                    padding,
                    sourceEnded,
                    sinceLastFill,
                    bufferDuration))
                {
                    RenderUnderruns++;
                }
                int availableFrames = ActualBufferFrames - padding;
                if (availableFrames > 0)
                {
                    sourceEnded = !FillBuffer(render, availableFrames);
                }
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            if (clientStarted)
            {
                try
                {
                    audioClient.Stop();
                    audioClient.Reset();
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                }
            }
            renderThread = null;
            if (failure == null)
            {
                playbackEnded?.TrySetResult(true);
            }
            else
            {
                playbackEnded?.TrySetException(failure);
            }
        }
    }

    private bool FillBuffer(AudioRenderClient render, int frames)
    {
        WaveFormat format = streamFormat ??
            throw new InvalidOperationException("WASAPI render is not initialized.");
        int byteCount = checked(frames * format.BlockAlign);
        var buffer = new byte[byteCount];
        int bytesRead = initializedSource?.Read(buffer, 0, byteCount) ?? 0;
        IntPtr destination = render.GetBuffer(frames);
        try
        {
            if (bytesRead > 0)
            {
                // The last source read can be shorter than the WASAPI buffer.
                // Copy the zero-initialized tail too so no stale device-buffer
                // contents are rendered after the final source frame.
                Marshal.Copy(buffer, 0, destination, byteCount);
            }
            RenderCallbacks++;
            lastBufferFillTimestamp = Stopwatch.GetTimestamp();
        }
        finally
        {
            render.ReleaseBuffer(
                frames,
                bytesRead == 0 ? AudioClientBufferFlags.Silent : AudioClientBufferFlags.None);
        }
        return bytesRead == byteCount;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            disposed = true;
            bufferReady.Dispose();
            audioClient.Dispose();
            enumerator.Dispose();
        }
    }
}
