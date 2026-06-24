using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonalyze;

internal static class GitHubReleaseChecker
{
    public const string ReleasesPageUrl = "https://github.com/DIMOSUS/Resonalyze/releases";
    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/DIMOSUS/Resonalyze/releases/latest";

    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static async Task<ReleaseCheckResult?> CheckForUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        using HttpResponseMessage response = await HttpClient
            .SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using Stream stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        ReleaseApiModel? latest = await JsonSerializer.DeserializeAsync<ReleaseApiModel>(
            stream,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (latest == null || string.IsNullOrWhiteSpace(latest.TagName))
        {
            return null;
        }

        string releaseUrl = string.IsNullOrWhiteSpace(latest.HtmlUrl)
            ? ReleasesPageUrl
            : latest.HtmlUrl;
        bool updateAvailable = ApplicationVersionInfo.IsOlderThan(latest.TagName);
        return new ReleaseCheckResult(
            latest.TagName,
            releaseUrl,
            updateAvailable);
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression =
                DecompressionMethods.GZip |
                DecompressionMethods.Deflate |
                DecompressionMethods.Brotli
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Resonalyze", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    internal sealed record ReleaseCheckResult(
        string TagName,
        string ReleaseUrl,
        bool UpdateAvailable);

    private sealed class ReleaseApiModel
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }
}
