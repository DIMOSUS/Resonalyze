using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>What a mode settings panel reads of the open measurement; with nothing open, the rate is the one the next
/// run is configured for.</summary>
internal sealed record ModeSettingsMeasurement(MeasurementResult? Result, int ConfiguredSampleRate)
{
    public int SampleRate => Result?.SampleRate ?? ConfiguredSampleRate;

    /// <summary>The result's own anchor; a loaded file carries its own.</summary>
    public bool SplAvailable => Result?.SplOffsetDb != null;

    /// <summary>The transfer IR's band-limited first-arrival front, memoized per IR.</summary>
    public double? TransferStartMs() =>
        Result is { Transfer.ImpulseResponse.Length: > 0, SampleRate: > 0 } result
            ? TransferIrStartCache.ResolveStartMs(
                result.Transfer.ImpulseResponse,
                result.SampleRate,
                result.Transfer.PeakIndex)
            : null;
}
