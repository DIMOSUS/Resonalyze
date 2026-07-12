using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Resonalyze;

public sealed class WasapiCaptureDevice : IAudioCaptureDevice
{
    private readonly MMDeviceEnumerator enumerator;
    private readonly MMDevice endpoint;
    private readonly WasapiCapture capture;
    private readonly string endpointId;
    private readonly string friendlyName;
    private TaskCompletionSource<bool>? stopped;
    private bool disposed;

    public WasapiCaptureDevice(string endpointId, int bufferMilliseconds = 100)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferMilliseconds);
        enumerator = new MMDeviceEnumerator();
        endpoint = enumerator.GetDevice(endpointId);
        if (endpoint.DataFlow != DataFlow.Capture)
        {
            throw new ArgumentException("The endpoint is not a capture device.", nameof(endpointId));
        }
        this.endpointId = endpoint.ID;
        friendlyName = endpoint.FriendlyName;

        capture = new WasapiCapture(endpoint, useEventSync: true, bufferMilliseconds);
        capture.DataAvailable += HandleDataAvailable;
        capture.RecordingStopped += HandleRecordingStopped;
        CaptureFormat = capture.WaveFormat;
    }

    public event EventHandler<AudioCaptureDataEventArgs>? DataAvailable;
    public event EventHandler<AudioDeviceStoppedEventArgs>? Stopped;

    public WaveFormat CaptureFormat { get; }
    public int ChannelCount => CaptureFormat.Channels;
    public string EndpointId => endpointId;
    public string FriendlyName => friendlyName;
    public long CapturePackets { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        stopped = SampleWaiterRegistry.NewSignal();
        CapturePackets = 0;
        capture.StartRecording();
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (disposed || stopped == null || stopped.Task.IsCompleted)
        {
            return;
        }
        await AudioCaptureStop.StopAndWaitAsync(
            capture.StopRecording,
            stopped,
            stopped.Task,
            $"WASAPI capture endpoint '{FriendlyName}'").ConfigureAwait(false);
    }

    private void HandleDataAvailable(object? sender, WaveInEventArgs args)
    {
        CapturePackets++;
        DataAvailable?.Invoke(
            this,
            new AudioCaptureDataEventArgs
            {
                Buffer = args.Buffer.AsMemory(0, args.BytesRecorded),
                BytesRecorded = args.BytesRecorded,
                Format = CaptureFormat
            });
    }

    private void HandleRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception == null)
        {
            stopped?.TrySetResult(true);
        }
        else
        {
            stopped?.TrySetException(args.Exception);
        }
        Stopped?.Invoke(this, new AudioDeviceStoppedEventArgs(args.Exception));
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
            capture.DataAvailable -= HandleDataAvailable;
            capture.RecordingStopped -= HandleRecordingStopped;
            capture.Dispose();
            // MMDevice.Dispose forces ReleaseComObject. A later lookup of this
            // endpoint can receive the disconnected cached RCW and fail with
            // E_NOINTERFACE, so leave the lightweight endpoint wrapper to CLR.
            enumerator.Dispose();
        }
    }
}
