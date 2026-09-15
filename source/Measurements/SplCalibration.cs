using System.Text.Json.Serialization;
using Resonalyze.Audio;

namespace Resonalyze;

/// <summary>Digital identity of a capture input; SPL anchors validate against the input that produced the result.</summary>
public readonly record struct MeasurementInputIdentity(
    AudioBackend Backend,
    int SampleRate,
    int Bits,
    int MicrophoneChannelOffset,
    int? InputDeviceNumber,
    string? WasapiCaptureEndpointId,
    string? AsioDriverName);

/// <summary>SPL anchor from an acoustic calibrator. See docs/tech/sweep-measurement.md#spl-calibration.</summary>
public sealed class SplCalibration
{
    public static readonly double[] StandardReferenceLevelsDb = [94.0, 104.0, 114.0];

    public double ReferenceLevelDbSpl { get; set; }

    public double MeasuredLevelDbFs { get; set; }

    public double ReferenceFrequencyHz { get; set; } = 1_000.0;

    public double MeasuredFrequencyHz { get; set; }

    public DateTimeOffset CapturedAtUtc { get; set; }

    // Digital half of the gain chain the anchor is pinned to.

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

    /// <summary><c>SPL = dBFS + OffsetDb</c>.</summary>
    [JsonIgnore]
    public double OffsetDb => ReferenceLevelDbSpl - MeasuredLevelDbFs;

    /// <summary>Digital input match only; the analog preamp gain is invisible to software.</summary>
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

    public bool MatchesInput(MeasurementInputIdentity identity) =>
        MatchesInput(
            identity.Backend,
            identity.SampleRate,
            identity.Bits,
            identity.MicrophoneChannelOffset,
            identity.InputDeviceNumber,
            identity.WasapiCaptureEndpointId,
            identity.AsioDriverName);

    /// <summary>Stands in for a loaded measurement's own input identity (validated when first saved).</summary>
    public MeasurementInputIdentity CaptureIdentity => new(
        Backend,
        SampleRate,
        Bits,
        MicrophoneChannelOffset,
        InputDeviceNumber,
        WasapiCaptureEndpointId,
        AsioDriverName);

    /// <summary>Structural sanity check on stored numbers, not live pass/fail policy.</summary>
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
