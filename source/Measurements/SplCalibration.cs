using System.Text.Json.Serialization;
using Resonalyze.Audio;

namespace Resonalyze;

/// <summary>
/// The digital identity of a capture input — backend, format and channel routing.
/// A measurement carries one (a snapshot of the input it ran on, or the input a
/// loaded file was measured on) so its SPL anchor is validated against the input
/// that actually produced the result, not the app's current configuration.
/// </summary>
public readonly record struct MeasurementInputIdentity(
    AudioBackend Backend,
    int SampleRate,
    int Bits,
    int MicrophoneChannelOffset,
    int? InputDeviceNumber,
    string? WasapiCaptureEndpointId,
    string? AsioDriverName);

/// <summary>
/// A sound-pressure-level calibration anchor: the fixed relationship between the
/// microphone's digital level and absolute SPL, established by holding an acoustic
/// calibrator (a known 94/104/114 dB tone at 1 kHz) over the capsule and reading
/// the level it produces.
/// <para>
/// It stores the raw ingredients, not a pre-baked "shift the response by N dB"
/// number. The displayed frequency response is a loopback-referenced transfer
/// function (microphone ÷ loopback), so placing it on an SPL axis needs this
/// microphone-side anchor <em>and</em> each measurement's own loopback level; the
/// per-measurement combination is done where the curve is drawn, from these
/// fields plus the impulse response's stored loopback levels.
/// </para>
/// <para>
/// The anchor is valid only while the capture gain chain is unchanged. The digital
/// half of that chain is recorded here so a mismatched input can be detected; the
/// analog preamp gain cannot be seen from software, so the standing rule is to
/// leave the input gain untouched between calibration and measurement.
/// </para>
/// </summary>
public sealed class SplCalibration
{
    /// <summary>The reference levels a standard IEC 60942 calibrator emits, in dB SPL.</summary>
    public static readonly double[] StandardReferenceLevelsDb = [94.0, 104.0, 114.0];

    /// <summary>The calibrator's stated output level at the reference frequency, in dB SPL.</summary>
    public double ReferenceLevelDbSpl { get; set; }

    /// <summary>
    /// The microphone level actually captured at the reference frequency, in dBFS.
    /// The measured half of the anchor.
    /// </summary>
    public double MeasuredLevelDbFs { get; set; }

    /// <summary>The calibrator's nominal tone frequency, in Hz (1 kHz for IEC 60942).</summary>
    public double ReferenceFrequencyHz { get; set; } = 1_000.0;

    /// <summary>The frequency the dominant peak was actually found at, in Hz (provenance).</summary>
    public double MeasuredFrequencyHz { get; set; }

    /// <summary>When the calibration was captured.</summary>
    public DateTimeOffset CapturedAtUtc { get; set; }

    // --- Capture identity: the digital half of the gain chain the anchor is
    // pinned to, so a later measurement on a different input can be flagged. ---

    public AudioBackend Backend { get; set; }
    public int SampleRate { get; set; }
    public int Bits { get; set; }
    public int MicrophoneChannelOffset { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? InputDeviceNumber { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WasapiCaptureEndpointId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AsioDriverName { get; set; }

    /// <summary>
    /// The microphone-side offset that turns a captured dBFS level into dB SPL:
    /// <c>SPL = dBFS + OffsetDb</c>. Derived, so it never disagrees with the stored
    /// reference and measured levels.
    /// </summary>
    [JsonIgnore]
    public double OffsetDb => ReferenceLevelDbSpl - MeasuredLevelDbFs;

    /// <summary>
    /// Whether this anchor was captured on the same digital input as the supplied
    /// configuration. The analog preamp gain is invisible to software and is not
    /// part of this check — the anchor can still be wrong if that knob moved.
    /// </summary>
    public bool MatchesInput(
        AudioBackend backend,
        int sampleRate,
        int bits,
        int microphoneChannelOffset,
        int? inputDeviceNumber,
        string? wasapiCaptureEndpointId,
        string? asioDriverName)
    {
        if (Backend != backend ||
            SampleRate != sampleRate ||
            Bits != bits ||
            MicrophoneChannelOffset != microphoneChannelOffset)
        {
            return false;
        }

        return backend switch
        {
            AudioBackend.Asio =>
                string.Equals(AsioDriverName, asioDriverName, StringComparison.Ordinal),
            AudioBackend.WasapiShared or AudioBackend.WasapiExclusive =>
                string.Equals(WasapiCaptureEndpointId, wasapiCaptureEndpointId, StringComparison.Ordinal),
            _ => InputDeviceNumber == inputDeviceNumber
        };
    }

    /// <summary>Same check against an <see cref="MeasurementInputIdentity"/>.</summary>
    public bool MatchesInput(MeasurementInputIdentity identity) =>
        MatchesInput(
            identity.Backend,
            identity.SampleRate,
            identity.Bits,
            identity.MicrophoneChannelOffset,
            identity.InputDeviceNumber,
            identity.WasapiCaptureEndpointId,
            identity.AsioDriverName);

    /// <summary>
    /// The input this calibration was captured on, as an identity. For a loaded
    /// measurement (whose anchor was validated when its file was first saved) this
    /// stands in for the result's own input identity.
    /// </summary>
    public MeasurementInputIdentity CaptureIdentity => new(
        Backend,
        SampleRate,
        Bits,
        MicrophoneChannelOffset,
        InputDeviceNumber,
        WasapiCaptureEndpointId,
        AsioDriverName);

    /// <summary>
    /// Throws when the anchor is structurally invalid (used when loading a
    /// persisted file). This is a sanity check on the stored numbers, not the
    /// pass/fail policy of a live calibration.
    /// </summary>
    public void Validate()
    {
        if (!double.IsFinite(ReferenceLevelDbSpl) || ReferenceLevelDbSpl is < 0.0 or > 200.0)
        {
            throw new InvalidDataException("The SPL calibration reference level is out of range.");
        }
        if (!double.IsFinite(MeasuredLevelDbFs) || MeasuredLevelDbFs is < -250.0 or > 24.0)
        {
            throw new InvalidDataException("The SPL calibration measured level is out of range.");
        }
        if (!double.IsFinite(ReferenceFrequencyHz) || ReferenceFrequencyHz is <= 0.0 or > 200_000.0 ||
            !double.IsFinite(MeasuredFrequencyHz) || MeasuredFrequencyHz < 0.0 || MeasuredFrequencyHz > 200_000.0)
        {
            throw new InvalidDataException("The SPL calibration frequency is out of range.");
        }
        if (!Enum.IsDefined(Backend))
        {
            throw new InvalidDataException("The SPL calibration backend is invalid.");
        }
        if (SampleRate is < 8_000 or > 768_000)
        {
            throw new InvalidDataException("The SPL calibration sample rate is out of range.");
        }
        if (Bits is not (16 or 24))
        {
            throw new InvalidDataException("The SPL calibration bit depth is unsupported.");
        }
        if (MicrophoneChannelOffset < 0)
        {
            throw new InvalidDataException("The SPL calibration microphone channel is invalid.");
        }
    }
}
