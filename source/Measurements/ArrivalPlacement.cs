using System.Numerics;

namespace Resonalyze;

/// <summary>Transfer IRs without absolute time. See docs/tech/sweep-measurement.md#arrival-ahead-of-the-loopback.</summary>
internal static class ArrivalPlacement
{
    /// <summary>Not zero; see docs/tech/sweep-measurement.md#imported-arrival-placement.</summary>
    public const double PlacedArrivalSeconds = 0.010;

    public static int PlacedArrivalIndex(int sampleRate, int length) =>
        Math.Min((int)Math.Round(PlacedArrivalSeconds * sampleRate), length - 1);

    /// <summary>Circular, because the transfer IR's acausal pre-ringing lives at the buffer's far end.</summary>
    public static Complex[] RotateTo(Complex[] impulseResponse, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        int shift = from - to;
        if (shift == 0)
        {
            return impulseResponse;
        }

        int length = impulseResponse.Length;
        var rotated = new Complex[length];
        for (int i = 0; i < length; i++)
        {
            rotated[i] = impulseResponse[((i + shift) % length + length) % length];
        }

        return rotated;
    }

    /// <summary>Ms the arrival led the loopback, or null: such an arrival wraps to the far half, and no tract delays by half a record.</summary>
    public static double? AheadOfLoopbackMs(int peakIndex, int length, int sampleRate) =>
        sampleRate > 0 && length > 0 && peakIndex > length / 2
            ? (length - peakIndex) * 1_000.0 / sampleRate
            : null;

    /// <summary><see cref="Judge"/> for a stored result, only when it carries its loopback level: every run recorded here writes
    /// one, and imports (REW, external tools) do not, whose buffers are not laid out as ours.</summary>
    public static MeasurementResult JudgeStored(MeasurementResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Levels.Loopback.Available ? Judge(result) : result;
    }

    /// <summary>A loopback result whose arrival led the loopback, re-filed non-causal and placed like an import.</summary>
    public static MeasurementResult Judge(MeasurementResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.TimingReference != TimingReference.SynchronizedLoopback ||
            result.Transfer is not { ImpulseResponse.Length: > 0 } transfer ||
            AheadOfLoopbackMs(transfer.PeakIndex, transfer.ImpulseResponse.Length, result.SampleRate) == null)
        {
            return result;
        }

        int arrival = PlacedArrivalIndex(result.SampleRate, transfer.ImpulseResponse.Length);
        return result with
        {
            TimingReference = TimingReference.NonCausalLoopback,
            Transfer = new MeasurementImpulseResponse(
                RotateTo(transfer.ImpulseResponse, transfer.PeakIndex, arrival),
                arrival)
        };
    }
}
