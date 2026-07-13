using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Resonalyze.Audio;

internal sealed class WasapiPlaybackDevice : IAudioPlaybackDevice, IRenderDiagnosticsSource
{
    private readonly MMDeviceEnumerator enumerator;
    private readonly MMDevice endpoint;
    private AudioClient audioClient;
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
        if (shareMode == AudioClientShareMode.Exclusive && requestedFormat == null)
        {
            throw new ArgumentNullException(
                nameof(requestedFormat),
                "WASAPI Exclusive playback requires an explicit format.");
        }

        var createdEnumerator = new MMDeviceEnumerator();
        AudioClient? createdAudioClient = null;
        try
        {
            MMDevice createdEndpoint = createdEnumerator.GetDevice(endpointId);
            if (createdEndpoint.DataFlow != DataFlow.Render)
            {
                throw new ArgumentException(
                    "The endpoint is not a render device.",
                    nameof(endpointId));
            }
            createdAudioClient = createdEndpoint.AudioClient;
            WaveFormat playbackFormat = shareMode == AudioClientShareMode.Exclusive
                ? requestedFormat!
                : createdAudioClient.MixFormat;

            enumerator = createdEnumerator;
            endpoint = createdEndpoint;
            audioClient = createdAudioClient;
            this.endpointId = createdEndpoint.ID;
            friendlyName = createdEndpoint.FriendlyName;
            this.bufferMilliseconds = bufferMilliseconds;
            PlaybackFormat = playbackFormat;
            ShareMode = shareMode;
        }
        catch
        {
            createdAudioClient?.Dispose();
            createdEnumerator.Dispose();
            throw;
        }
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
        (long bufferDuration, long periodicity) =
            WasapiStreamConfiguration.GetDurations(ShareMode, bufferMilliseconds);
        AudioClientStreamFlags flags = AudioClientStreamFlags.EventCallback;
        if (ShareMode == AudioClientShareMode.Shared)
        {
            flags |= AudioClientStreamFlags.AutoConvertPcm |
                AudioClientStreamFlags.SrcDefaultQuality;
            audioClient.Initialize(
                ShareMode,
                flags,
                bufferDuration,
                periodicity,
                streamFormat,
                Guid.Empty);
        }
        else
        {
            InitializeExclusive(flags, bufferDuration, periodicity);
        }
        audioClient.SetEventHandle(bufferReady.SafeWaitHandle.DangerousGetHandle());
        ActualBufferFrames = audioClient.BufferSize;
        initialized = true;
    }

    private void InitializeExclusive(
        AudioClientStreamFlags flags,
        long bufferDuration,
        long periodicity)
    {
        try
        {
            audioClient.Initialize(
                ShareMode,
                flags,
                bufferDuration,
                periodicity,
                streamFormat!,
                Guid.Empty);
        }
        catch (COMException exception) when (
            WasapiStreamConfiguration.IsBufferSizeNotAligned(exception))
        {
            int alignedFrames = audioClient.BufferSize;
            long alignedDuration = WasapiStreamConfiguration.GetAlignedExclusiveDuration(
                alignedFrames,
                streamFormat!.SampleRate);
            audioClient.Dispose();
            audioClient = endpoint.AudioClient;
            audioClient.Initialize(
                ShareMode,
                flags,
                alignedDuration,
                alignedDuration,
                streamFormat,
                Guid.Empty);
        }
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
            if (ShareMode == AudioClientShareMode.Exclusive)
            {
                RunExclusiveRenderLoop(render, sourceEnded);
            }
            else
            {
                RunSharedRenderLoop(render, sourceEnded);
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

    private void RunExclusiveRenderLoop(AudioRenderClient render, bool finalBufferQueued)
    {
        TimeSpan bufferDuration = GetBufferDuration();
        int eventTimeoutMilliseconds = GetEventTimeoutMilliseconds();
        while (!stopRequested)
        {
            bool signaled = bufferReady.WaitOne(eventTimeoutMilliseconds);
            if (stopRequested)
            {
                break;
            }
            if (!signaled)
            {
                RenderUnderruns++;
                continue;
            }
            if (finalBufferQueued)
            {
                break;
            }
            if (Stopwatch.GetElapsedTime(lastBufferFillTimestamp) > bufferDuration * 1.5)
            {
                RenderUnderruns++;
            }
            finalBufferQueued = !FillBuffer(render, ActualBufferFrames);
        }
    }

    private void RunSharedRenderLoop(AudioRenderClient render, bool sourceEnded)
    {
        TimeSpan bufferDuration = GetBufferDuration();
        int eventTimeoutMilliseconds = GetEventTimeoutMilliseconds();
        while (!stopRequested)
        {
            bool signaled = bufferReady.WaitOne(eventTimeoutMilliseconds);
            if (stopRequested)
            {
                break;
            }
            if (!signaled)
            {
                RenderUnderruns++;
                continue;
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
            if (WasapiRenderTiming.IsUnderrun(
                padding,
                sourceEnded,
                Stopwatch.GetElapsedTime(lastBufferFillTimestamp),
                bufferDuration))
            {
                RenderUnderruns++;
            }
            int availableFrames = WasapiStreamConfiguration.GetRenderFrames(
                ShareMode,
                ActualBufferFrames,
                padding);
            if (availableFrames > 0)
            {
                sourceEnded = !FillBuffer(render, availableFrames);
            }
        }
    }

    private TimeSpan GetBufferDuration() => TimeSpan.FromSeconds(
        (double)ActualBufferFrames / (streamFormat?.SampleRate ?? 1));

    private int GetEventTimeoutMilliseconds() =>
        WasapiStreamConfiguration.GetEventTimeoutMilliseconds(
            ActualBufferFrames,
            streamFormat?.SampleRate ?? 1);

    private bool FillBuffer(AudioRenderClient render, int frames)
    {
        WaveFormat format = streamFormat ??
            throw new InvalidOperationException("WASAPI render is not initialized.");
        int byteCount = checked(frames * format.BlockAlign);
        var buffer = new byte[byteCount];
        IWaveProvider source = initializedSource ??
            throw new InvalidOperationException("WASAPI render source is not initialized.");
        AudioRenderBufferRead read = AudioRenderBufferReader.Fill(source, buffer);
        IntPtr destination = render.GetBuffer(frames);
        try
        {
            if (read.BytesRead > 0)
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
                read.BytesRead == 0 ? AudioClientBufferFlags.Silent : AudioClientBufferFlags.None);
        }
        return !read.SourceEnded;
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
