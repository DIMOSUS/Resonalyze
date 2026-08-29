using System.Net;
using System.Text;
using System.Text.Json;
using Resonalyze.Integration.Rew;

namespace Resonalyze.App.Tests;

/// <summary>
/// The send, driven by a fake handler standing in for REW. What is under test is
/// the conversation: which routes are called, how the new measurement is picked out
/// of the ones REW already holds, and what the round-trip check does with the number
/// it reads back.
/// </summary>
public sealed class RewMeasurementExportTests
{
    private const int SampleRate = 48_000;
    private const int PeakIndex = 1_000;
    private const string Version = "5.40 Beta 132 API 0.9.6";

    [Fact]
    public async Task SendAsync_PostsTheImportToRewsDataRoute()
    {
        var rew = new FakeRew();

        RewExportResult result = await SendAsync(rew, PeakSeconds(PeakIndex));

        Assert.True(result.Verified);
        Assert.Contains("/import/impulse-response-data", rew.Paths);
        JsonElement body = JsonDocument.Parse(rew.ImportBody!).RootElement;
        Assert.Equal("probe", body.GetProperty("identifier").GetString());
        Assert.Equal(SampleRate, body.GetProperty("sampleRate").GetDouble());
        Assert.True(body.GetProperty("startTime").GetDouble() < 0.0);
        Assert.False(body.GetProperty("applyCal").GetBoolean());
    }

    [Fact]
    public async Task SendAsync_SaysNothingWhenRewsCopyLandsWhereItWasSent()
    {
        RewExportResult result = await SendAsync(new FakeRew(), PeakSeconds(PeakIndex));

        Assert.True(result.Verified);
        Assert.Null(result.Problem);
    }

    [Fact]
    public async Task SendAsync_ToleratesTheRoundingOfADoubleButNotASample()
    {
        // The measured disagreement on a real import was around 1e-18 s; anything
        // that survives to the sample grid is a different sample and a real fault.
        RewExportResult withinRounding =
            await SendAsync(new FakeRew(), PeakSeconds(PeakIndex) + 1e-15);
        RewExportResult offByOneSample =
            await SendAsync(new FakeRew(), PeakSeconds(PeakIndex + 1));

        Assert.True(withinRounding.Verified);
        Assert.False(offByOneSample.Verified);
    }

    [Fact]
    public async Task SendAsync_ReportsADisagreementInSamplesAndNamesTheVersionItSpokeTo()
    {
        RewExportResult result = await SendAsync(new FakeRew(), PeakSeconds(PeakIndex + 64));

        Assert.False(result.Verified);
        Assert.Contains("64", result.Problem!);
        Assert.Contains("samples", result.Problem!);
        // The version is not gated on, but a report of this has to say what it was
        // talking to — REW's API is a moving beta.
        Assert.Contains(Version, result.Problem!);
    }

    [Fact]
    public async Task SendAsync_ReportsARewThatIsNotAnswering()
    {
        var rew = new FakeRew { Unreachable = true };

        RewExportResult result = await SendAsync(rew, PeakSeconds(PeakIndex));

        Assert.False(result.Verified);
        Assert.Contains("not answering", result.Problem!);
        // Nothing was sent, so nothing has to be undone in REW.
        Assert.DoesNotContain("/import/impulse-response-data", rew.Paths);
    }

    [Fact]
    public async Task SendAsync_PicksTheNewMeasurementByUuidRatherThanByName()
    {
        // REW lets two measurements share a title, so a second send of the same name
        // would otherwise be verified against the first one's numbers.
        var rew = new FakeRew
        {
            Existing =
            {
                ["1"] = new FakeMeasurement("probe", "old-uuid", PeakSeconds(PeakIndex + 500))
            }
        };

        RewExportResult result = await SendAsync(rew, PeakSeconds(PeakIndex));

        Assert.True(result.Verified);
    }

    [Fact]
    public async Task SendAsync_ReportsAnImportRewRefused()
    {
        var rew = new FakeRew { ImportStatus = HttpStatusCode.BadRequest };

        RewApiException exception = await Assert.ThrowsAsync<RewApiException>(
            () => SendAsync(rew, PeakSeconds(PeakIndex)));

        Assert.Contains("refused", exception.Message);
    }

    /// <summary>
    /// The whole path against a running REW, which is the only thing that can prove
    /// the fields are REW's and the start time survives. It creates one measurement
    /// and deletes it again.
    /// </summary>
    [RewFact]
    [Trait("Category", "Hardware")]
    public async Task SendAsync_RoundTripsThroughARunningRew()
    {
        Assert.True(RewApiClient.TryParseBaseAddress(RewFactAttribute.ApiUrl(), out Uri? baseAddress));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var client = new RewApiClient(http, baseAddress!);
        string identifier = $"Resonalyze round trip {Guid.NewGuid():N}";

        IReadOnlyDictionary<string, RewMeasurementSummary> before =
            await client.GetMeasurementsAsync(CancellationToken.None);
        RewExportResult result = await new RewMeasurementExport(client).SendAsync(
            new RewExportRequest(Arrival(), PeakIndex, SampleRate, identifier, null),
            CancellationToken.None);

        // Clean up BEFORE asserting, and report the two failures separately: a
        // cleanup thrown from a finally block replaces the assertion that matters
        // with the news that a leftover could not be removed.
        string? cleanup = await DeleteMeasurementsAddedSinceAsync(
            http, baseAddress!, client, before);

        Assert.Null(result.Problem);
        Assert.Null(cleanup);
    }

    /// <summary>
    /// Leaves the user's REW as it was found. Rule of the house for anything that
    /// touches a live REW: it may be mid-session, so put back everything you added.
    /// </summary>
    private static async Task<string?> DeleteMeasurementsAddedSinceAsync(
        HttpClient http,
        Uri baseAddress,
        RewApiClient client,
        IReadOnlyDictionary<string, RewMeasurementSummary> before)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (RewMeasurementSummary summary in before.Values)
        {
            if (!string.IsNullOrEmpty(summary.Uuid))
            {
                known.Add(summary.Uuid);
            }
        }

        IReadOnlyDictionary<string, RewMeasurementSummary> after =
            await client.GetMeasurementsAsync(CancellationToken.None);
        foreach (RewMeasurementSummary summary in after.Values)
        {
            if (string.IsNullOrEmpty(summary.Uuid) || known.Contains(summary.Uuid))
            {
                continue;
            }

            if (await TryDeleteAsync(http, baseAddress, summary.Uuid) is { } problem)
            {
                return problem;
            }
        }

        return null;
    }

    /// <summary>
    /// REW can still be finishing with a measurement it has already listed, and
    /// refuses the delete while it is; a couple of retries is the difference between
    /// leaving the user's session clean and leaving a probe in it.
    /// </summary>
    private static async Task<string?> TryDeleteAsync(HttpClient http, Uri baseAddress, string uuid)
    {
        HttpStatusCode status = HttpStatusCode.Unused;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            using HttpResponseMessage response = await http.DeleteAsync(
                new Uri(baseAddress, $"measurements/{uuid}"),
                CancellationToken.None);
            if (response.IsSuccessStatusCode)
            {
                return null;
            }

            status = response.StatusCode;
            await Task.Delay(TimeSpan.FromMilliseconds(400), CancellationToken.None);
        }

        return $"REW refused to delete the measurement this test created ({uuid}): {status}.";
    }

    private static Task<RewExportResult> SendAsync(FakeRew rew, double reportedPeakSeconds)
    {
        rew.ReportedPeakSeconds = reportedPeakSeconds;
        using var http = new HttpClient(rew);
        var export = new RewMeasurementExport(
            new RewApiClient(http, new Uri("http://localhost:4735/")));
        return export.SendAsync(
            new RewExportRequest(Arrival(), PeakIndex, SampleRate, "probe", null),
            CancellationToken.None);
    }

    private static double PeakSeconds(int peakIndex) => peakIndex / (double)SampleRate;

    private static double[] Arrival()
    {
        var samples = new double[8_192];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = Math.Sin(i * 0.01) * 0.01;
        }

        samples[PeakIndex] = 1.0;
        return samples;
    }

    private sealed record FakeMeasurement(string Title, string Uuid, double PeakSeconds);

    /// <summary>
    /// REW as far as this export is concerned: a version, a measurement list that
    /// gains one entry when the import route is called, and the peak time it reports
    /// for it.
    /// </summary>
    private sealed class FakeRew : HttpMessageHandler
    {
        private bool imported;

        public List<string> Paths { get; } = [];
        public Dictionary<string, FakeMeasurement> Existing { get; } = [];
        public string? ImportBody { get; private set; }
        public double ReportedPeakSeconds { get; set; }
        public bool Unreachable { get; set; }
        public HttpStatusCode ImportStatus { get; set; } = HttpStatusCode.Accepted;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Unreachable)
            {
                throw new HttpRequestException("Connection refused.");
            }

            string path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);

            switch (path)
            {
                case "/version":
                    return Json(HttpStatusCode.OK, $"{{\"message\":\"{Version}\"}}");

                case "/import/impulse-response-data":
                    ImportBody = request.Content == null
                        ? null
                        : await request.Content.ReadAsStringAsync(cancellationToken);
                    if (ImportStatus != HttpStatusCode.Accepted &&
                        ImportStatus != HttpStatusCode.OK)
                    {
                        return Json(ImportStatus, "{\"message\":\"bad request\"}");
                    }

                    imported = true;
                    return Json(ImportStatus, "{\"message\":\"in progress\"}");

                case "/measurements":
                    return Json(HttpStatusCode.OK, BuildMeasurements());

                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        private string BuildMeasurements()
        {
            var entries = new List<string>();
            foreach ((string index, FakeMeasurement measurement) in Existing)
            {
                entries.Add(Entry(index, measurement));
            }

            if (imported)
            {
                entries.Add(Entry(
                    (Existing.Count + 1).ToString(),
                    new FakeMeasurement("probe", "new-uuid", ReportedPeakSeconds)));
            }

            return "{" + string.Join(",", entries) + "}";
        }

        private static string Entry(string index, FakeMeasurement measurement) =>
            FormattableString.Invariant(
                $"\"{index}\":{{\"title\":\"{measurement.Title}\",\"uuid\":\"{measurement.Uuid}\",\"timeOfIRPeakSeconds\":{measurement.PeakSeconds:R}}}");

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
    }
}
