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
        catch (Exception exception) when (IsUnreadable(exception))
        {
            // A body that is not REW's error shape tells us nothing; the status does.
            // Classified the same way as the measurement list, so an unreadable answer
            // is unreadable wherever it arrives — an unusable charset raises
            // InvalidOperationException here exactly as it does there, and escaping
            // from this catch would turn REW refusing an import into a crash.
        }

        throw new RewApiException(
            string.IsNullOrWhiteSpace(reported)
                ? $"REW refused the import ({(int)response.StatusCode} {response.ReasonPhrase})."
                : $"REW refused the import: {reported}");
    }

    /// <summary>Every measurement REW currently holds, keyed by its index.</summary>
    /// <remarks>
    /// A body that is not the shape this expects is turned into a
    /// <see cref="RewApiException"/> rather than left to escape. This is the route
    /// where that matters: the export deliberately does not gate on REW's version —
    /// it is a moving beta — so a REW that still answers while having changed this
    /// shape is the expected way the design fails, and it has to arrive as a
    /// reported problem rather than as an unhandled exception.
    /// <para>
    /// The two types caught are the two this route can actually raise, which was
    /// measured rather than assumed (.NET 10.0.301). <see cref="JsonException"/>
    /// covers both a malformed body and a body of another kind entirely: the
    /// generic <c>ReadFromJsonAsync</c> does not reject a foreign content type, it
    /// parses the bytes anyway, so an HTML error page arrives as "'&lt;' is an
    /// invalid start of a value" rather than as the
    /// <see cref="NotSupportedException"/> one would expect.
    /// <see cref="InvalidOperationException"/> is the one that does come from the
    /// header — an unusable charset ("The character set provided in ContentType is
    /// invalid") — and it is excluded for <see cref="ObjectDisposedException"/>,
    /// which derives from it and means this application misused its own client
    /// rather than anything about REW.
    /// </para>
    /// </remarks>
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
                "REW answered with a measurement list this build could not read, so the " +
                "measurement that was just sent could not be found to check it. " +
                $"({exception.Message})");
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

    /// <summary>
    /// Whether a failure means "the answer was not something this can read" — the
    /// body, or the header that says how to decode it. Shared so the version probe
    /// and the measurement list agree on what an unreadable answer is; they only
    /// differ in what they do about it.
    /// </summary>
    private static bool IsUnreadable(Exception exception) =>
        exception is JsonException ||
        (exception is InvalidOperationException and not ObjectDisposedException);

    /// <summary>
    /// Whether a failure means "REW is not there", as opposed to the caller having
    /// cancelled. A timeout surfaces as a cancellation whose token is not ours, so
    /// the token has to be consulted rather than the exception type.
    /// </summary>
    private static bool IsUnreachable(Exception exception, CancellationToken cancellationToken) =>
        exception switch
        {
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            HttpRequestException => true,
            _ when IsUnreadable(exception) => true,
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
