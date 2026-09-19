using System.Numerics;

namespace Resonalyze;

/// <summary>Written on the UI thread, read by plot-build workers: volatile reference to an immutable selection; snapshot once.</summary>
internal sealed class CompareSelection
{
    private volatile CompareMeasurementSelection? current;

    public event Action? Changed;

    public CompareMeasurementSelection? Current => current;

    public void Set(
        string displayName,
        string? sourceFilePath,
        MeasurementResult result)
    {
        current = new CompareMeasurementSelection(displayName, sourceFilePath, result);
        Changed?.Invoke();
    }

    public void Clear()
    {
        current = null;
        Changed?.Invoke();
    }

    public CompareAnalysisSource? GetAnalysisSource()
    {
        if (current is not { } selection ||
            selection.Result.Transfer is not { ImpulseResponse.Length: > 0 } transfer)
        {
            return null;
        }

        return new CompareAnalysisSource(
            selection.DisplayName,
            selection.Result.SampleRate,
            transfer.ImpulseResponse,
            transfer.PeakIndex,
            selection.Result.TransferCoherence,
            // Its own K: loopback levels differ unless both share a session.
            selection.Result.SplOffsetDb,
            selection.Result.TimingReference,
            selection.Result.MeasuredBand);
    }

    // Recorded-sweep imports cannot join Time Alignment: their arrival depends on when the recorder started.
    public TimeAlignmentCompareMeasurement? GetTimeAlignmentMeasurement() =>
        current is not { } selection ||
            selection.Result.TimingReference == TimingReference.RecordedSweep
            ? null
            : new TimeAlignmentCompareMeasurement(
                selection.DisplayName,
                selection.Result);
}

internal sealed record CompareMeasurementSelection(
    string DisplayName,
    string? SourceFilePath,
    MeasurementResult Result);

// Sample rate match is validated by consumers. SplOffsetDb null means no anchor: FR omits it in dB SPL.
public readonly record struct CompareAnalysisSource(
    string DisplayName,
    int SampleRate,
    Complex[] TransferImpulseResponse,
    int TransferPeakIndex,
    double[]? TransferCoherence = null,
    double? SplOffsetDb = null,
    TimingReference TimingReference = TimingReference.SynchronizedLoopback,
    MeasuredBand Band = default);
