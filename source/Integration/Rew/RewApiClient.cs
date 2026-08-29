using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonalyze.Integration.Rew;

/// <summary>
/// The only place this application speaks HTTP to REW. Everything above it works
/// in payloads and summaries, so the transport can be driven by a fake handler in
/// tests and REW is never needed to prove the code that builds a request.
/// </summary>
/// <remarks>
/// The <see cref="HttpClient"/> is supplied rather than created here for that
/// reason; the shell hands it the one it owns.
/// </remarks>
internal sealed class RewApiClient
{
    /// <summary>Where REW's API listens when nobody has moved it.</summary>
    public const string DefaultBaseUrl = "http://localhost:4735/";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient http;
    private readonly Uri baseAddress;

    public RewApiClient(HttpClient http, Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(baseAddress);
        this.http = http;
        this.baseAddress = baseAddress;
    }

    /// <summary>
    /// Turns a typed address into one that can be combined with a relative path.
    /// Returns false for anything that is not an absolute http(s) URL, so a
    /// mistyped setting is a refusal at the dialog rather than an exception later.
    /// </summary>
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

    /// <summary>
    /// The version string REW announces, or null when it does not answer. Never
    /// throws for an absent REW: not running is the ordinary case, not a fault.
    /// </summary>
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

    /// <summary>Posts one impulse response. The import itself runs asynchronously in REW.</summary>
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

        string? reported = null;
        try
        {
            RewApiMessage? message = await response.Content
                .ReadFromJsonAsync<RewApiMessage>(SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
            reported = message?.Message;
        }
        catch (JsonException)
        {
            // A body that is not REW's error shape tells us nothing; the status does.
        }

        throw new RewApiException(
            string.IsNullOrWhiteSpace(reported)
                ? $"REW refused the import ({(int)response.StatusCode} {response.ReasonPhrase})."
                : $"REW refused the import: {reported}");
    }

    /// <summary>Every measurement REW currently holds, keyed by its index.</summary>
    public async Task<IReadOnlyDictionary<string, RewMeasurementSummary>> GetMeasurementsAsync(
        CancellationToken cancellationToken)
    {
        Dictionary<string, RewMeasurementSummary>? measurements =
            await GetAsync<Dictionary<string, RewMeasurementSummary>>(
                "measurements",
                cancellationToken).ConfigureAwait(false);
        return measurements ?? [];
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

    /// <summary>
    /// Whether a failure means "REW is not there", as opposed to the caller having
    /// cancelled. A timeout surfaces as a cancellation whose token is not ours, so
    /// the token has to be consulted rather than the exception type.
    /// </summary>
    private static bool IsUnreachable(Exception exception, CancellationToken cancellationToken) =>
        exception switch
        {
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            HttpRequestException or JsonException => true,
            _ => false
        };
}

/// <summary>REW answered, and said no.</summary>
internal sealed class RewApiException : Exception
{
    public RewApiException(string message)
        : base(message)
    {
    }
}

/// <summary>REW's one-line reply shape, used by <c>/version</c> and the import routes.</summary>
internal sealed class RewApiMessage
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// The part of REW's measurement summary this export reads back. The whole summary
/// is much larger; the fields here are the ones the round-trip check needs — which
/// measurement is the new one, and where REW put its peak.
/// </summary>
internal sealed class RewMeasurementSummary
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    /// <summary>
    /// The arrival's time on REW's axis. This is the field that proves the start
    /// time survived: REW finds the peak itself, so the number combines what it
    /// was told (the start time) with what it found (the same peak sample).
    /// </summary>
    [JsonPropertyName("timeOfIRPeakSeconds")]
    public double? TimeOfIRPeakSeconds { get; set; }
}
