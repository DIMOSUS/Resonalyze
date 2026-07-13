using NAudio.Wave;

namespace Resonalyze.Audio;

internal interface ISweepCaptureSession
{
    int ReadSamples { get; }
    Task StartAsync(CancellationToken cancellationToken);
    void Reset();
    Task WaitForSamplesAsync(int sampleCount, CancellationToken cancellationToken);
    float[][] GetSamplesSnapshot();
}

internal sealed class SweepRunAudioOrchestrator
{
    private readonly ISweepCaptureSession capture;
    private readonly IAudioPlaybackDevice playback;
    private bool captureStarted;

    public SweepRunAudioOrchestrator(
        ISweepCaptureSession capture,
        IAudioPlaybackDevice playback)
    {
        this.capture = capture;
        this.playback = playback;
    }

    public async Task<float[][]> CaptureAsync(
        IWaveProvider source,
        int sweepSamples,
        int tailSamples,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sweepSamples);
        ArgumentOutOfRangeException.ThrowIfNegative(tailSamples);

        if (!captureStarted)
        {
            await capture.StartAsync(cancellationToken).ConfigureAwait(false);
            captureStarted = true;
        }
        else
        {
            capture.Reset();
        }

        int recordingStart = capture.ReadSamples;
        await AudioPlaybackRunner.PlayToEndAsync(playback, source, cancellationToken)
            .ConfigureAwait(false);
        int requiredSamples = checked(recordingStart + sweepSamples + tailSamples);
        await capture.WaitForSamplesAsync(requiredSamples, cancellationToken)
            .ConfigureAwait(false);
        return capture.GetSamplesSnapshot();
    }
}
