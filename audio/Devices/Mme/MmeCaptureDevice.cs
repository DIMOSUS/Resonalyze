using NAudio.Wave;

namespace Resonalyze.Audio;

internal sealed class MmeCaptureDevice : IAudioCaptureDevice
{
    private readonly WaveInEvent source;
    private TaskCompletionSource<bool>? stopped;
    private bool disposed;

    public MmeCaptureDevice(int deviceNumber, WaveFormat format)
    {
        CaptureFormat = format ?? throw new ArgumentNullException(nameof(format));
        source = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = format
        };
        source.DataAvailable += HandleDataAvailable;
        source.RecordingStopped += HandleRecordingStopped;
    }

    public event EventHandler<AudioCaptureDataEventArgs>? DataAvailable;
    public event EventHandler<AudioDeviceStoppedEventArgs>? Stopped;

    public WaveFormat CaptureFormat { get; }
    public int ChannelCount => CaptureFormat.Channels;
    public int MaximumPacketBytes
    {
        get
        {
            int unaligned = checked(CaptureFormat.AverageBytesPerSecond * source.BufferMilliseconds / 1000);
            return Math.Max(
                CaptureFormat.BlockAlign,
                unaligned / CaptureFormat.BlockAlign * CaptureFormat.BlockAlign);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        stopped = SampleWaiterRegistry.NewSignal();
        source.StartRecording();
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (disposed || stopped == null)
        {
            return;
        }

        await AudioCaptureStop.StopAndWaitAsync(
            source.StopRecording,
            stopped,
            stopped.Task,
            "The MME audio input").ConfigureAwait(false);
    }

    private void HandleDataAvailable(object? sender, WaveInEventArgs args)
    {
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
            source.DataAvailable -= HandleDataAvailable;
            source.RecordingStopped -= HandleRecordingStopped;
            source.Dispose();
        }
    }
}
