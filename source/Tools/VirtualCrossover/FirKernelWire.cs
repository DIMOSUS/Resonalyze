using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Session wire form of a FIR kernel: base64 of little-endian float64 taps (lossless) plus the source file's declared rate.
/// See docs/tech/virtual-dsp-session-file.md#fir-kernel-storage.
/// </summary>
public sealed class FirKernelWire
{
    // Cached per kernel instance: the base64 of a long kernel is ~1 MB and the autosave asks on every knob turn.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FirFilter, FirKernelWire>
        WireForms = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SampleRateHz { get; set; }

    public string? Taps { get; set; }

    public static FirKernelWire From(FirFilter fir)
    {
        ArgumentNullException.ThrowIfNull(fir);
        return WireForms.GetValue(fir, Encode);
    }

    private static FirKernelWire Encode(FirFilter fir)
    {
        ReadOnlySpan<double> taps = fir.Taps;
        var bytes = new byte[taps.Length * sizeof(double)];
        for (int index = 0; index < taps.Length; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(index * sizeof(double)), taps[index]);
        }

        return new FirKernelWire
        {
            SampleRateHz = fir.DeclaredSampleRateHz,
            Taps = Convert.ToBase64String(bytes)
        };
    }

    /// <summary>Throws <see cref="JsonException"/> for a block that is not a valid kernel, failing the load like any malformed field.</summary>
    public FirFilter ToFilter()
    {
        if (string.IsNullOrEmpty(Taps))
        {
            throw new JsonException("The FIR kernel carries no taps.");
        }
        if (SampleRateHz is <= 0)
        {
            throw new JsonException($"The FIR kernel declares an impossible sample rate ({SampleRateHz}).");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(Taps);
        }
        catch (FormatException exception)
        {
            throw new JsonException("The FIR tap block is not base64.", exception);
        }

        if (bytes.Length == 0 || bytes.Length % sizeof(double) != 0)
        {
            throw new JsonException("The FIR tap block is not a whole number of float64 values.");
        }

        var taps = new double[bytes.Length / sizeof(double)];
        for (int index = 0; index < taps.Length; index++)
        {
            taps[index] = BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(index * sizeof(double)));
        }

        try
        {
            return new FirFilter(taps, SampleRateHz);
        }
        catch (ArgumentException exception)
        {
            throw new JsonException($"The FIR kernel is not usable: {exception.Message}", exception);
        }
    }
}
