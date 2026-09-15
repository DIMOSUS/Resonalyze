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
            // Classified like the measurement list; letting InvalidOperationException escape here would turn a refused import into a crash.
        }

        throw new RewApiException(
            string.IsNullOrWhiteSpace(reported)
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

internal sealed class RewMeasurementSummary
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    /// <summary>REW finds the peak itself, so this combines the sent start time with the same peak sample.</summary>
    [JsonPropertyName("timeOfIRPeakSeconds")]
    public double? TimeOfIRPeakSeconds { get; set; }
}
