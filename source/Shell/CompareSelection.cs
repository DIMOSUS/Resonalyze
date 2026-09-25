using System.Numerics;

namespace Resonalyze;

/// <summary>Written on the UI thread, read by plot-build workers: volatile reference to an immutable selection; snapshot once.</summary>
internal sealed class CompareSelection
{
    private volatile CompareMeasurementSelection? current;
    // UI thread only: the newest load, Set or Clear; an older load that finishes later does not land.
    private long latest;

    public event Action? Changed;

    public CompareMeasurementSelection? Current => current;

    /// <summary>Taken before a load's first await; the load lands through <see cref="TrySet"/> with it.</summary>
    public long BeginLoad() => ++latest;

    /// <summary>Whether <paramref name="load"/> is still the newest choice, so its failure is still worth reporting.</summary>
    public bool IsCurrent(long load) => load == latest;

    public void Set(
        string displayName,
        string? sourceFilePath,
        MeasurementResult result) =>
        TrySet(BeginLoad(), displayName, sourceFilePath, result);

    /// <returns>False when a newer load, <see cref="Set"/> or <see cref="Clear"/> came after <paramref name="load"/>: nothing changes.</returns>
    public bool TrySet(
        long load,
        string displayName,
        string? sourceFilePath,
        MeasurementResult result)
    {
        if (load != latest)
        {
            return false;
        }

        current = new CompareMeasurementSelection(displayName, sourceFilePath, result);
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        ++latest;
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

    // A result without absolute time cannot join Time Alignment: its arrival is not the tract's delay.
    public TimeAlignmentCompareMeasurement? GetTimeAlignmentMeasurement() =>
        current is not { } selection ||
            !selection.Result.TimingReference.HasAbsoluteTime()
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
