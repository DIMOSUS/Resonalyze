using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonalyze.Integration.Rew;

/// <summary>The only HTTP path to REW; the HttpClient is injected so tests drive a fake handler.</summary>
internal sealed class RewApiClient
{
    public const string DefaultBaseUrl = "http://localhost:4735/";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient http;
    private readonly Uri baseAddress;

    public RewApiClient(HttpClient http, Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(baseAddress);
        this.http = http;
        this.baseAddress = ConnectableAddress(baseAddress);
    }

    /// <summary>"localhost" as 127.0.0.1: REW listens on IPv4 only, and "localhost" resolves to ::1 first, whose refused
    /// connect took 2037 ms on Windows (REW 5.40 b134) — longer than the probe waits.</summary>
    internal static Uri ConnectableAddress(Uri baseAddress) =>
        string.Equals(baseAddress.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            ? new UriBuilder(baseAddress) { Host = "127.0.0.1" }.Uri
            : baseAddress;

    /// <summary>False for anything but an absolute http(s) URL, so a mistyped setting is refused at the dialog.</summary>
    public static bool TryParseBaseAddress(string? url, out Uri? baseAddress)
    {
        baseAddress = null;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        string trimmed = url.Trim();
        if (!trimmed.EndsWith('/'))
        {
            trimmed += "/";
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        baseAddress = parsed;
        return true;
    }

    /// <summary>REW version, or null when not answering within <paramref name="timeout"/>.</summary>
    /// <remarks>Owns its deadline token: a just-cancelled caller token is indistinguishable from a timeout and escaped unhandled on the UI thread.</remarks>
    public async Task<string?> ProbeVersionAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var deadline =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            return await TryGetVersionAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Null when REW does not answer; never throws for an absent REW.</summary>
    public async Task<string?> TryGetVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            RewApiMessage? message = await GetAsync<RewApiMessage>(
                "version",
                cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(message?.Message) ? null : message.Message;
        }
        catch (Exception exception) when (IsUnreachable(exception, cancellationToken))
        {
            return null;
        }
    }

    public async Task ImportImpulseResponseAsync(
        RewImpulseResponseData body,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http
            .PostAsJsonAsync(
                new Uri(baseAddress, "import/impulse-response-data"),
                body,
                cancellationToken)
            .ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? reported = await TryReadMessageAsync(response, cancellationToken).ConfigureAwait(false);
        throw new RewApiException(
            reported == null
                ? $"REW refused the import ({(int)response.StatusCode} {response.ReasonPhrase})."
                : $"REW refused the import: {reported}");
    }

    /// <remarks>Unexpected shapes become <see cref="RewApiException"/> (REW's version is not gated). Measured on .NET 10.0.301: ReadFromJsonAsync
    /// parses any content type, so an HTML page is a <see cref="JsonException"/>; a bad charset is <see cref="InvalidOperationException"/>
    /// (excluding <see cref="ObjectDisposedException"/>, our own misuse).</remarks>
    public async Task<IReadOnlyDictionary<string, RewMeasurementSummary>> GetMeasurementsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            Dictionary<string, RewMeasurementSummary>? measurements =
                await GetAsync<Dictionary<string, RewMeasurementSummary>>(
                    "measurements",
                    cancellationToken).ConfigureAwait(false);
            return measurements ?? [];
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
            throw new RewApiException(
                $"REW answered with a measurement list this build could not read. ({exception.Message})");
        }
    }

    /// <summary>Null for any failure or unexpected shape: the selection only preselects a row.</summary>
    public async Task<string?> TryGetSelectedMeasurementUuidAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await http
                .GetAsync(new Uri(baseAddress, "measurements/selected-uuid"), cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            JsonElement body = await response.Content
                .ReadFromJsonAsync<JsonElement>(SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
            string? uuid = body.ValueKind switch
            {
                JsonValueKind.String => body.GetString(),
                JsonValueKind.Object when body.TryGetProperty("message", out JsonElement message) &&
                    message.ValueKind == JsonValueKind.String => message.GetString(),
                _ => null
            };
            return string.IsNullOrWhiteSpace(uuid) ? null : uuid;
        }
        catch (Exception exception) when (IsUnreachable(exception, cancellationToken))
        {
            return null;
        }
    }

    /// <summary>REW's current measurement level in whatever unit REW is set to, or null when it could not be read.</summary>
    /// <remarks>A setting, not a property of any measurement: REW's summary carries no level (REW 5.40 b134).</remarks>
    public async Task<RewLevel?> TryGetMeasurementLevelAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await GetAsync<RewLevel>("measure/level", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsUnreachable(exception, cancellationToken))
        {
            return null;
        }
    }

    /// <summary>The raw response in percent of full scale: <c>normalised=false</c> keeps the level (REW scales the peak to one otherwise);
    /// unwindowed is REW's default.</summary>
    public async Task<RewImpulseResponseBody> GetImpulseResponseAsync(
        string uuid,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uuid);
        using HttpResponseMessage response = await http
            .GetAsync(
                new Uri(baseAddress, $"measurements/{Uri.EscapeDataString(uuid)}/impulse-response?unit=percent&normalised=false"),
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // A measurement without an IR answers 400 "... does not have an impulse response" (REW 5.40 b134).
            string? reported = await TryReadMessageAsync(response, cancellationToken).ConfigureAwait(false);
            throw new RewApiException(
                reported != null
                    ? $"REW could not give this measurement's impulse response: {reported}"
                    : response.StatusCode == HttpStatusCode.NotFound
                        ? "REW no longer holds this measurement. Refresh the list and choose again."
                        : $"REW could not give this measurement's impulse response ({(int)response.StatusCode} {response.ReasonPhrase}).");
        }

        try
        {
            RewImpulseResponseBody? body = await response.Content
                .ReadFromJsonAsync<RewImpulseResponseBody>(SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
            return body ?? throw new RewApiException("REW answered with an empty impulse response.");
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
            throw new RewApiException(
                $"REW answered with an impulse response this build could not read. ({exception.Message})");
        }
    }

    /// <summary>REW's one-line reason from a refusal; null when the body is not that shape (an escaping read would turn a refusal into a crash).</summary>
    private static async Task<string?> TryReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            RewApiMessage? message = await response.Content
                .ReadFromJsonAsync<RewApiMessage>(SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(message?.Message) ? null : message.Message;
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
            return null;
        }
    }

    private async Task<T?> GetAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http
            .GetAsync(new Uri(baseAddress, relativePath), cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync<T>(SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsUnreadable(Exception exception) =>
        exception is JsonException ||
        (exception is InvalidOperationException and not ObjectDisposedException);

    /// <summary>A timeout surfaces as a cancellation whose token is not ours, so the token is consulted, not the type.</summary>
    private static bool IsUnreachable(Exception exception, CancellationToken cancellationToken) =>
        exception switch
        {
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            HttpRequestException => true,
            _ when IsUnreadable(exception) => true,
            _ => false
        };
}

internal sealed class RewApiException : Exception
{
    public RewApiException(string message)
        : base(message)
    {
    }
}

internal sealed class RewApiMessage
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

internal sealed class RewLevel
{
    [JsonPropertyName("value")]
    public double? Value { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }
}

internal sealed class RewMeasurementSummary
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    /// <summary>REW finds the peak itself, so this combines the sent start time with the same peak sample.</summary>
    [JsonPropertyName("timeOfIRPeakSeconds")]
    public double? TimeOfIRPeakSeconds { get; set; }

    /// <summary>Shown as REW sends it; its format is not relied on.</summary>
    [JsonPropertyName("date")]
    public JsonElement? Date { get; set; }

    [JsonPropertyName("sampleRate")]
    public double? SampleRate { get; set; }

    /// <summary>REW's response range: a 0–20 kHz sweep at 96 kHz lists 0.366 Hz (one bin) to 20,000.244 Hz (REW 5.40 b134).</summary>
    [JsonPropertyName("startFreq")]
    public double? StartFrequencyHz { get; set; }

    [JsonPropertyName("endFreq")]
    public double? EndFrequencyHz { get; set; }

    /// <summary>Total IR shift applied in REW; already inside the start time and the peak time.</summary>
    [JsonPropertyName("cumulativeIRShiftSeconds")]
    public double? CumulativeIRShiftSeconds { get; set; }

    /// <summary>"Loopback" for a loopback-referenced sweep (REW 5.40 b134); absent on a measurement imported into REW.</summary>
    [JsonPropertyName("timingReference")]
    public string? TimingReference { get; set; }

    /// <summary>Seconds; the timing offset REW measured with, absent where REW has none to state.</summary>
    [JsonPropertyName("timingOffset")]
    public double? TimingOffsetSeconds { get; set; }
}

/// <summary>The body of <c>GET /measurements/{id}/impulse-response</c>.</summary>
internal sealed class RewImpulseResponseBody
{
    /// <summary>Seconds; the time of the first sample on REW's axis.</summary>
    [JsonPropertyName("startTime")]
    public double? StartTime { get; set; }

    [JsonPropertyName("sampleRate")]
    public double? SampleRate { get; set; }

    [JsonPropertyName("sampleInterval")]
    public double? SampleInterval { get; set; }

    [JsonPropertyName("timingReference")]
    public string? TimingReference { get; set; }

    /// <summary>"percent" or "dBFS"; percent is REW's full scale times 100.</summary>
    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    /// <summary>Base64 of big-endian 32-bit floats.</summary>
    [JsonPropertyName("data")]
    public string? Data { get; set; }
}
