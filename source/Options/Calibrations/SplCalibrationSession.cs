using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>One SPL calibration dialog: the reference level, the listen in flight and what the last one found. A new
/// listen starts without a result, so Save keeps only the last listen. See
/// docs/tech/sweep-measurement.md#calibration-dialogs-code-map.</summary>
internal sealed class SplCalibrationSession
{
    // ~5.9 Hz bins at 48 kHz: isolates the 1 kHz tone with enough frames in a few seconds.
    public const int FrameLength = 8_192;
    public static readonly TimeSpan DefaultCaptureDuration = TimeSpan.FromSeconds(4);
    public static readonly SplToneCriteria Criteria = SplToneCriteria.Default;

    private readonly Func<DateTimeOffset> now;
    private readonly TimeSpan captureDuration;
    private CancellationTokenSource? cancellation;
    private int runs;

    public SplCalibrationSession(
        AudioSessionRequest request,
        SplCalibration? existing,
        Func<DateTimeOffset>? now = null,
        TimeSpan? captureDuration = null)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        this.captureDuration = captureDuration ?? DefaultCaptureDuration;
        ReferenceIndex = existing != null
            ? Math.Max(0, Array.IndexOf(SplCalibration.StandardReferenceLevelsDb, existing.ReferenceLevelDbSpl))
            : 0;
    }

    /// <summary>The input being configured, so the anchor is pinned to that tract.</summary>
    public AudioSessionRequest Request { get; }

    public static IReadOnlyList<double> Levels => SplCalibration.StandardReferenceLevelsDb;

    public int ReferenceIndex { get; private set; }

    public double ReferenceLevelDbSpl => Levels[Math.Clamp(ReferenceIndex, 0, Levels.Count - 1)];

    public bool Running { get; private set; }

    /// <summary>A close asked for while listening waits until the listen unwinds.</summary>
    public bool CloseRequested { get; private set; }

    public SplCalibration? Result { get; private set; }

    /// <summary>Null until the first listen: the dialog keeps its own idle line.</summary>
    public SplStatus? Status { get; private set; }

    public int ProgressPercent { get; private set; }

    public void SelectReference(int index) => ReferenceIndex = index;

    public void Stop() => cancellation?.Cancel();

    /// <summary>True when the dialog may close now; otherwise the listen is stopped and the close waits for it.</summary>
    public bool RequestClose()
    {
        if (!Running)
        {
            return true;
        }

        CloseRequested = true;
        cancellation?.Cancel();
        return false;
    }

    /// <summary>The reference level is read before the listen starts; a progress report arriving after it ended is
    /// dropped. <paramref name="changed"/> hears each progress report on the caller's context.</summary>
    public async Task RunAsync(IAudioSessionFactory factory, Action? changed = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (Running)
        {
            return;
        }

        double reference = ReferenceLevelDbSpl;
        int run = ++runs;
        Running = true;
        Result = null;
        ProgressPercent = 0;
        Status = SplCalibrationReport.Listening;
        var progress = new Progress<SplCalibrationProgress>(report =>
        {
            if (!Running || run != runs)
            {
                return;
            }

            ProgressPercent = SplCalibrationReport.Percent(report, captureDuration);
            Status = SplCalibrationReport.Progress(report);
            changed?.Invoke();
        });

        using var runCancellation = new CancellationTokenSource();
        cancellation = runCancellation;
        SplCalibrationCaptureResult? capture = null;
        string? error = null;
        bool cancelled = false;
        try
        {
            capture = await new SplCalibrationListener(factory)
                .CaptureAsync(Request, FrameLength, Criteria, captureDuration, progress, runCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }
        finally
        {
            cancellation = null;
            Running = false;
        }

        if (cancelled)
        {
            Status = SplCalibrationReport.Cancelled;
        }
        else if (error != null)
        {
            Status = SplCalibrationReport.OpenFailed(error);
        }
        else if (capture is { } result)
        {
            Finish(result, reference);
        }
    }

    private void Finish(SplCalibrationCaptureResult result, double reference)
    {
        SplCalibrationFailure failure = SplCalibrationListener.Evaluate(result);
        if (failure != SplCalibrationFailure.None)
        {
            Result = null;
            Status = SplCalibrationReport.Failed(failure, result, Criteria);
            return;
        }

        Result = SplCalibrationReport.Calibration(Request, result, reference, Criteria, now());
        Status = SplCalibrationReport.Succeeded(result, Result);
    }
}
