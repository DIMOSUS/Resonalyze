using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Resonalyze;

public sealed class WasapiCaptureDevice : IAudioCaptureDevice
{
    private readonly MMDeviceEnumerator enumerator;
    private readonly MMDevice endpoint;
    private AudioClient audioClient;
    private readonly EventWaitHandle packetReady = new(false, EventResetMode.AutoReset);
    private readonly string endpointId;
    private readonly string friendlyName;
    private readonly int bufferMilliseconds;
    private Thread? captureThread;
    private TaskCompletionSource<bool>? stopped;
    private volatile bool stopRequested;
    private bool initialized;
    private bool startedOnce;
    private bool disposed;

    public WasapiCaptureDevice(
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
                "WASAPI Exclusive capture requires an explicit format.");
        }

        var createdEnumerator = new MMDeviceEnumerator();
        AudioClient? createdAudioClient = null;
        try
        {
            MMDevice createdEndpoint = createdEnumerator.GetDevice(endpointId);
            if (createdEndpoint.DataFlow != DataFlow.Capture)
            {
                throw new ArgumentException(
                    "The endpoint is not a capture device.",
                    nameof(endpointId));
            }
            createdAudioClient = createdEndpoint.AudioClient;
            WaveFormat captureFormat = shareMode == AudioClientShareMode.Exclusive
                ? requestedFormat!
                : createdAudioClient.MixFormat;

            enumerator = createdEnumerator;
            endpoint = createdEndpoint;
            audioClient = createdAudioClient;
            this.endpointId = createdEndpoint.ID;
            friendlyName = createdEndpoint.FriendlyName;
            this.bufferMilliseconds = bufferMilliseconds;
            ShareMode = shareMode;
            CaptureFormat = captureFormat;
        }
        catch
        {
            createdAudioClient?.Dispose();
            createdEnumerator.Dispose();
            throw;
        }
    }

    public event EventHandler<AudioCaptureDataEventArgs>? DataAvailable;
    public event EventHandler<AudioDeviceStoppedEventArgs>? Stopped;

    public WaveFormat CaptureFormat { get; }
    public int ChannelCount => CaptureFormat.Channels;
    public string EndpointId => endpointId;
    public string FriendlyName => friendlyName;
    public AudioClientShareMode ShareMode { get; }
    public long CapturePackets { get; private set; }
    public long Discontinuities { get; private set; }
    public long SilentPackets { get; private set; }
    public long TimestampErrors { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (captureThread != null)
        {
            throw new InvalidOperationException("Capture is already running.");
        }
        if (startedOnce)
        {
            throw new InvalidOperationException(
                "A WASAPI capture device cannot be restarted; keep it running and reset the session accumulator.");
        }

        Initialize();
        startedOnce = true;
        stopRequested = false;
        stopped = SampleWaiterRegistry.NewSignal();
        CapturePackets = 0;
        Discontinuities = 0;
        SilentPackets = 0;
        TimestampErrors = 0;
        captureThread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = "Resonalyze WASAPI capture"
        };
        captureThread.Start();
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (disposed || captureThread == null || stopped == null)
        {
            return;
        }
        stopRequested = true;
        packetReady.Set();
        await stopped.Task.WaitAsync(AudioCaptureStop.StopTimeout).ConfigureAwait(false);
    }

    private void Initialize()
    {
        if (initialized)
        {
            return;
        }
        (long bufferDuration, long periodicity) =
            WasapiStreamConfiguration.GetDurations(ShareMode, bufferMilliseconds);
        AudioClientStreamFlags flags = AudioClientStreamFlags.EventCallback;
        if (ShareMode == AudioClientShareMode.Shared)
        {
            flags |= AudioClientStreamFlags.AutoConvertPcm |
                AudioClientStreamFlags.SrcDefaultQuality;
            audioClient.Initialize(ShareMode, flags, 0, 0, CaptureFormat, Guid.Empty);
        }
        else
        {
            try
            {
                audioClient.Initialize(
                    ShareMode,
                    flags,
                    bufferDuration,
                    periodicity,
                    CaptureFormat,
                    Guid.Empty);
            }
            catch (COMException exception) when (
                WasapiStreamConfiguration.IsBufferSizeNotAligned(exception))
            {
                int alignedFrames = audioClient.BufferSize;
                long alignedDuration = WasapiStreamConfiguration.GetAlignedExclusiveDuration(
                    alignedFrames,
                    CaptureFormat.SampleRate);
                audioClient.Dispose();
                audioClient = endpoint.AudioClient;
                audioClient.Initialize(
                    ShareMode,
                    flags,
                    alignedDuration,
                    alignedDuration,
                    CaptureFormat,
                    Guid.Empty);
            }
        }
        audioClient.SetEventHandle(packetReady.SafeWaitHandle.DangerousGetHandle());
        initialized = true;
    }

    private void CaptureLoop()
    {
        Exception? failure = null;
        try
        {
            AudioCaptureClient capture = audioClient.AudioCaptureClient;
            audioClient.Start();
            while (!stopRequested)
            {
                packetReady.WaitOne(bufferMilliseconds * 3);
                if (!stopRequested)
                {
                    ReadAvailablePackets(capture);
                }
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            try
            {
                audioClient.Stop();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
            captureThread = null;
            if (failure == null)
            {
                stopped?.TrySetResult(true);
            }
            else
            {
                stopped?.TrySetException(failure);
            }
            Stopped?.Invoke(this, new AudioDeviceStoppedEventArgs(failure));
        }
    }

    private void ReadAvailablePackets(AudioCaptureClient capture)
    {
        while (capture.GetNextPacketSize() != 0)
        {
            IntPtr source = capture.GetBuffer(
                out int frames,
                out AudioClientBufferFlags flags,
                out long devicePosition,
                out long qpcPosition);
            try
            {
                int byteCount = checked(frames * CaptureFormat.BlockAlign);
                var buffer = new byte[byteCount];
                bool silent = (flags & AudioClientBufferFlags.Silent) != 0;
                bool discontinuity = (flags & AudioClientBufferFlags.DataDiscontinuity) != 0;
                bool timestampError = (flags & AudioClientBufferFlags.TimestampError) != 0;
                if (!silent)
                {
                    Marshal.Copy(source, buffer, 0, byteCount);
                }
                CapturePackets++;
                if (silent)
                {
                    SilentPackets++;
                }
                if (discontinuity)
                {
                    Discontinuities++;
                }
                if (timestampError)
                {
                    TimestampErrors++;
                }
                DataAvailable?.Invoke(
                    this,
                    new AudioCaptureDataEventArgs
                    {
                        Buffer = buffer,
                        BytesRecorded = byteCount,
                        Format = CaptureFormat,
                        DevicePositionFrames = devicePosition,
                        QpcPosition = qpcPosition,
                        Discontinuity = discontinuity,
                        Silent = silent,
                        TimestampError = timestampError
                    });
            }
            finally
            {
                capture.ReleaseBuffer(frames);
            }
        }
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
            packetReady.Dispose();
            audioClient.Dispose();
            enumerator.Dispose();
        }
    }
}
