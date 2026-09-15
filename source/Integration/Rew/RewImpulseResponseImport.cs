using System.Text.Json.Serialization;

namespace Resonalyze.Integration.Rew;

/// <summary>Body of REW's <c>POST /import/impulse-response-data</c>; names are REW's, stated explicitly so a rename cannot change the wire.</summary>
internal sealed class RewImpulseResponseData
{
    [JsonPropertyName("identifier")]
    public required string Identifier { get; init; }

    /// <summary>Seconds of the first sample; negative for the pre-roll so t = 0 is the loopback reference.</summary>
    [JsonPropertyName("startTime")]
    public required double StartTime { get; init; }

    [JsonPropertyName("sampleRate")]
    public required double SampleRate { get; init; }

    /// <summary>Omitted when null: a placeholder would claim a level nothing measured.</summary>
    [JsonPropertyName("splOffset")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SplOffset { get; init; }

    /// <summary>Always false: true applies REW's currently loaded mic calibration (measured on 5.40 b132), which is not this measurement's mic.</summary>
    /// <remarks>Samples are uncalibrated, so REW gets the raw response (see REFERENCE.md, "Sending a measurement to REW").</remarks>
    [JsonPropertyName("applyCal")]
    public bool ApplyCal { get; init; }

    [JsonPropertyName("data")]
    public required string Data { get; init; }
}

internal sealed record RewImpulseResponseImport(
    RewImpulseResponseData Body,
    int PreRollSamples,
    double PeakTimeSeconds);
