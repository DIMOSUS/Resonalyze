using System.Text.Json.Serialization;

namespace Resonalyze.Integration.Rew;

/// <summary>
/// The body of REW's <c>POST /import/impulse-response-data</c>, field for field.
/// </summary>
/// <remarks>
/// The names are REW's, so they are stated rather than derived from a naming
/// policy: this object exists to match a protocol, and a rename on our side must
/// not silently change what goes on the wire.
/// </remarks>
internal sealed class RewImpulseResponseData
{
    /// <summary>The name REW files the arriving measurement under.</summary>
    [JsonPropertyName("identifier")]
    public required string Identifier { get; init; }

    /// <summary>
    /// The time of the FIRST sample, in seconds. Negative for the pre-roll this
    /// export frames in, so t = 0 lands on the loopback reference.
    /// </summary>
    [JsonPropertyName("startTime")]
    public required double StartTime { get; init; }

    [JsonPropertyName("sampleRate")]
    public required double SampleRate { get; init; }

    /// <summary>
    /// The dB that turns this measurement's values into dB SPL, or null when the
    /// measurement carries no SPL anchor. Omitted from the JSON when null: REW
    /// reads a missing offset as none, and a placeholder would be a level claim
    /// nothing measured.
    /// </summary>
    [JsonPropertyName("splOffset")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SplOffset { get; init; }

    /// <summary>
    /// Always false. The samples already passed through this session's microphone
    /// calibration, and REW applying its own on top would count it twice.
    /// </summary>
    [JsonPropertyName("applyCal")]
    public bool ApplyCal { get; init; }

    /// <summary>Base64 of the samples as big-endian 32-bit floats.</summary>
    [JsonPropertyName("data")]
    public required string Data { get; init; }
}

/// <summary>
/// A built import: the body to post, plus the two numbers the round-trip check
/// reads it back against.
/// </summary>
/// <param name="Body">What goes on the wire.</param>
/// <param name="PreRollSamples">
/// How far the buffer was rolled, so the samples before t = 0 are the ones that
/// were wrapped into its tail.
/// </param>
/// <param name="PeakTimeSeconds">
/// Where the arrival should land on REW's time axis once the import is filed.
/// </param>
internal sealed record RewImpulseResponseImport(
    RewImpulseResponseData Body,
    int PreRollSamples,
    double PeakTimeSeconds);
