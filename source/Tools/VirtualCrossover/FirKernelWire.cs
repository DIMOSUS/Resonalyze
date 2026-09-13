using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// A channel's FIR kernel as the session file carries it:
/// <c>{ "sampleRateHz": 48000, "taps": "&lt;base64&gt;" }</c>. The kernel is part of
/// the tune the way the PEQ bands are, not a file the tune points at, so a session
/// travels with its kernels and never has one to relink. This is the wire shape
/// only; the tune works with <see cref="FirFilter"/>, which the serializer never
/// sees (its span-typed <c>Taps</c> is not a property System.Text.Json can map).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Taps"/> is the base64 of the little-endian float64 values: a kernel is
/// a DESIGNED set of numbers, not a measurement with a noise floor under it, and a
/// text kernel from rePhase carries sixteen digits that an export from here has to
/// give back unchanged — so, unlike the impulse responses (float32, see
/// <see cref="Float32SampleArrayJsonConverter"/>), nothing is rounded. At the tap
/// ceiling that is 1.4 MB per side, which a session carries once. Byte order is
/// fixed by contract, not by host.
/// </para>
/// <para>
/// <see cref="SampleRateHz"/> is the rate the kernel's source FILE declared, kept for
/// the block's warning only; absent when the file declared none. The taps are
/// convolved at the processor's rate regardless — see <see cref="FirFilter"/>.
/// </para>
/// <para>
/// The session is rewritten by the debounced autosave on every knob turn, and the
/// spatial average was deliberately kept OUT of it for that reason (see
/// <see cref="VirtualCrossoverChannelSettings.SpatialAveragePath"/>). The kernel is
/// in, by decision — it is the tune, not a measurement of it — so its cost is kept
/// to the write itself: the wire form is built ONCE per kernel and reused for every
/// save after, which an immutable kernel makes safe. What remains is the file's
/// size, 1.4 MB per side at the tap ceiling and a few hundred kilobytes for the
/// kernels car processors actually run.
/// </para>
/// </remarks>
public sealed class FirKernelWire
{
    // One wire form per kernel instance, for as long as the kernel lives: the
    // base64 of a long kernel is a megabyte, and the autosave asks for it on every
    // knob turn.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FirFilter, FirKernelWire>
        WireForms = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SampleRateHz { get; set; }

    public string? Taps { get; set; }

    /// <summary>
    /// The wire form of <paramref name="fir"/> — the same instance for the same kernel
    /// every time, built on the first call.
    /// </summary>
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

    /// <summary>
    /// The kernel this wire form describes. Throws <see cref="JsonException"/> for a
    /// block that is not a kernel — no taps, a tap block that is not whole float64s,
    /// a kernel the DSP refuses — so a garbled session fails its load the way any
    /// other malformed field does, rather than opening with some other filter.
    /// </summary>
    public FirFilter ToFilter()
    {
        if (string.IsNullOrEmpty(Taps))
        {
            throw new JsonException("The FIR kernel carries no taps.");
        }
        if (SampleRateHz is <= 0)
        {
            // A rate the file could not have declared: a damaged block, not "none".
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
