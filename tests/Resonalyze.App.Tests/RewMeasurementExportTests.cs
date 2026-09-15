using System.Net;
using System.Text;
using System.Text.Json;
using Resonalyze.Integration.Rew;

namespace Resonalyze.App.Tests;

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
        // Measured disagreement on a real import was ~1e-18 s; anything reaching the sample grid is a real fault.
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
        Assert.Contains(Version, result.Problem!);
    }

    [Fact]
    public async Task SendAsync_ReportsARewThatIsNotAnswering()
    {
        var rew = new FakeRew { Unreachable = true };

        RewExportResult result = await SendAsync(rew, PeakSeconds(PeakIndex));

        Assert.False(result.Verified);
        Assert.Contains("not answering", result.Problem!);
        Assert.DoesNotContain("/import/impulse-response-data", rew.Paths);
    }

    [Fact]
    public async Task SendAsync_PicksTheNewMeasurementByUuidRatherThanByName()
    {
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

    [Fact]
    public async Task SendAsync_ReportsAMeasurementListItCannotRead()
    {
        // The export does not gate on REW's version, so a changed shape must be a reported problem, not a JsonException.
        var rew = new FakeRew { MeasurementsBody = "{\"1\":{\"title\":" };

        RewApiException exception = await Assert.ThrowsAsync<RewApiException>(
            () => SendAsync(rew, PeakSeconds(PeakIndex)));

        Assert.Contains("could not read", exception.Message);
        Assert.DoesNotContain("/import/impulse-response-data", rew.Paths);
    }

    [Fact]
    public async Task SendAsync_ReportsAMeasurementListThatIsNotJsonAtAll()
    {
        // Measured (.NET 10.0.301): ReadFromJsonAsync ignores a foreign content type and throws JsonException about '<'.
        var rew = new FakeRew
        {
            MeasurementsBody = "<html><body>not REW</body></html>",
            MeasurementsContentType = "text/html; charset=utf-8"
        };

        RewApiException exception = await Assert.ThrowsAsync<RewApiException>(
            () => SendAsync(rew, PeakSeconds(PeakIndex)));

        Assert.Contains("could not read", exception.Message);
    }

    [Fact]
    public async Task SendAsync_ReportsAMeasurementListItCannotEvenDecode()
    {
        // An unresolvable charset raises InvalidOperationException, which a JsonException catch misses.
        var rew = new FakeRew
        {
            MeasurementsContentType = "application/json; charset=utf-9"
        };

        RewApiException exception = await Assert.ThrowsAsync<RewApiException>(
            () => SendAsync(rew, PeakSeconds(PeakIndex)));

        Assert.Contains("could not read", exception.Message);
    }

    [Fact]
    public async Task SendAsync_ReportsAMeasurementListThatGoesWrongAfterTheImport()
    {
        var rew = new FakeRew();

        RewApiException exception = await Assert.ThrowsAsync<RewApiException>(async () =>
        {
            using var http = new HttpClient(rew);
            var export = new RewMeasurementExport(
                new RewApiClient(http, new Uri("http://localhost:4735/")));
            rew.BreakMeasurementsAfterImport = true;
            await export.SendAsync(
                new RewExportRequest(Arrival(), PeakIndex, SampleRate, "probe", null),
                CancellationToken.None);
        });

        Assert.Contains("could not read", exception.Message);
        Assert.Contains("/import/impulse-response-data", rew.Paths);
    }

    [Fact]
    public async Task ProbeAsync_TreatsAPortThatAnswersSomethingElseAsNotRew()
    {
        // Runs from an async void handler, so any escape is a crash.
        var rew = new FakeRew
        {
            VersionContentType = "application/json; charset=utf-9"
        };
        using var http = new HttpClient(rew);
        var export = new RewMeasurementExport(
            new RewApiClient(http, new Uri("http://localhost:4735/")));

        string? version = await export.ProbeAsync(
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        Assert.Null(version);
    }

    [Fact]
    public async Task SendAsync_FindsItsMeasurementWhenRewShortensTheName()
    {
        // REW truncates long titles (5.40 Beta 132: 54 chars -> 48, 64 -> 45), hence prefix matching.
        const string identifier = "Resonalyze 2026-09-01 12-00-00 export probe name";
        var rew = new FakeRew { TitleLimit = 45 };

        RewExportResult result = await SendAsync(rew, PeakSeconds(PeakIndex), identifier);

        Assert.True(result.Verified);
        Assert.Null(result.Problem);
    }

    [Fact]
    public void IsFiledAs_AcceptsATruncationAndRefusesEverythingElse()
    {
        const string identifier = "Resonalyze 2026-09-01 12-00-00 export probe name";

        Assert.True(RewMeasurementExport.IsFiledAs(identifier, identifier));
        Assert.True(RewMeasurementExport.IsFiledAs(identifier[..45], identifier));
        Assert.True(RewMeasurementExport.IsFiledAs(identifier[..40], identifier));

        Assert.False(RewMeasurementExport.IsFiledAs("Resonalyze", identifier));
        Assert.False(RewMeasurementExport.IsFiledAs(identifier[..39], identifier));

        Assert.False(RewMeasurementExport.IsFiledAs("", identifier));
        Assert.False(RewMeasurementExport.IsFiledAs(null, identifier));

        Assert.False(RewMeasurementExport.IsFiledAs(identifier + "x", identifier));
        Assert.False(RewMeasurementExport.IsFiledAs("something else entirely, and long enough", identifier));
        Assert.False(RewMeasurementExport.IsFiledAs(identifier[..45].ToUpperInvariant(), identifier));

        Assert.True(RewMeasurementExport.IsFiledAs("probe", "probe"));
    }

    [Fact]
    public async Task SendAsync_RefusesToGuessWhenTwoNewMeasurementsShareTheName()
    {
        // Two new measurements share a title: nothing separates them, so the export must say so.
        var rew = new FakeRew
        {
            Concurrent = new FakeMeasurement("probe", "someone-elses-uuid", 0.5)
        };

        RewExportResult result = await SendAsync(rew, PeakSeconds(PeakIndex));

        Assert.False(result.Verified);
        Assert.Contains("more than one", result.Problem!);
    }

    [Fact]
    public async Task SendAsync_IgnoresAConcurrentMeasurementWhoseNameOnlyStartsLikeOurs()
    {
        const string identifier = "Resonalyze 2026-09-01 12-00-00 export probe name";
        var rew = new FakeRew
        {
            SentIdentifier = identifier,
            Concurrent = new FakeMeasurement("Resonalyze", "someone-elses-uuid", 0.5)
        };

        RewExportResult result = await SendAsync(rew, PeakSeconds(PeakIndex), identifier);

        Assert.True(result.Verified);
        Assert.Null(result.Problem);
    }

    [Fact]
    public async Task SendAsync_RefusesToGuessBetweenTwoEquallyGoodTruncations()
    {
        const string identifier = "Resonalyze 2026-09-01 12-00-00 export probe name";
        var rew = new FakeRew
        {
            SentIdentifier = identifier,
            TitleLimit = 44,
            Concurrent = new FakeMeasurement(identifier[..44], "another-new-uuid", 0.5)
        };

        RewExportResult result = await SendAsync(rew, PeakSeconds(PeakIndex), identifier);

        Assert.False(result.Verified);
        Assert.Contains("more than one", result.Problem!);
    }

    [Fact]
    public void IsFiledAs_RefusesAShortNameThatIsMerelyAPrefix()
    {
        const string identifier = "Resonalyze 2026-09-01 12-00-00 export probe name";
        Assert.True(RewMeasurementExport.IsFiledAs(identifier[..45], identifier));
        Assert.True(RewMeasurementExport.IsFiledAs(identifier, identifier));
        Assert.False(RewMeasurementExport.IsFiledAs("Resonalyze", identifier));
        Assert.False(RewMeasurementExport.IsFiledAs(identifier[..39], identifier));
    }

    /// <summary>Against a running REW: creates one measurement and deletes it again.</summary>
    [RewFact]
    [Trait("Category", "Hardware")]
    public async Task SendAsync_RoundTripsThroughARunningRew()
    {
        Assert.True(RewApiClient.TryParseBaseAddress(RewFactAttribute.ApiUrl(), out Uri? baseAddress));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var client = new RewApiClient(http, baseAddress!);
        // The GUID leads because REW drops the end of a long title, and cleanup deletes by this name.
        string identifier = $"{Guid.NewGuid():N} Resonalyze round trip";

        IReadOnlyDictionary<string, RewMeasurementSummary> before =
            await client.GetMeasurementsAsync(CancellationToken.None);
        RewExportResult result = await new RewMeasurementExport(client).SendAsync(
            new RewExportRequest(Arrival(), PeakIndex, SampleRate, identifier, null),
            CancellationToken.None);

        // Clean up before asserting: a throw from finally would replace the assertion that matters.
        string? cleanup = await DeleteMeasurementsAddedSinceAsync(
            http, baseAddress!, client, before, identifier);

        Assert.Null(result.Problem);
        Assert.Null(cleanup);
    }

    /// <summary>Deletes only what this test added: the user's REW may be mid-session.</summary>
    private static async Task<string?> DeleteMeasurementsAddedSinceAsync(
        HttpClient http,
        Uri baseAddress,
        RewApiClient client,
        IReadOnlyDictionary<string, RewMeasurementSummary> before,
        string identifier)
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
            if (string.IsNullOrEmpty(summary.Uuid) ||
                known.Contains(summary.Uuid) ||
                !RewMeasurementExport.IsFiledAs(summary.Title, identifier))
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

    /// <summary>REW refuses a delete while still finishing a listed measurement, hence retries.</summary>
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

    [Fact]
    public async Task ProbeAsync_ReadsTheVersionRewAnnounces()
    {
        using var http = new HttpClient(new FakeRew());
        var export = new RewMeasurementExport(
            new RewApiClient(http, new Uri("http://localhost:4735/")));

        string? version = await export.ProbeAsync(
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        Assert.Equal(Version, version);
    }

    [Fact]
    public async Task ProbeAsync_TreatsItsOwnDeadlineAsNotAnswering()
    {
        // The probe's own timeout cancelling its token used to escape through the async void handler.
        using var http = new HttpClient(new FakeRew { Silent = true });
        var export = new RewMeasurementExport(
            new RewApiClient(http, new Uri("http://localhost:4735/")));

        string? version = await export.ProbeAsync(
            TimeSpan.FromMilliseconds(50),
            CancellationToken.None);

        Assert.Null(version);
    }

    [Fact]
    public async Task ProbeAsync_StillPropagatesTheCallersOwnCancellation()
    {
        using var http = new HttpClient(new FakeRew { Silent = true });
        var export = new RewMeasurementExport(
            new RewApiClient(http, new Uri("http://localhost:4735/")));
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => export.ProbeAsync(TimeSpan.FromMinutes(5), caller.Token));
    }

    [Fact]
    public async Task SendAsync_IgnoresAMeasurementTheUserMadeWhileThisOneWasFiling()
    {
        // A measurement the user makes during the send is also new since the snapshot; UUID alone would verify a stranger.
        var rew = new FakeRew
        {
            Concurrent = new FakeMeasurement(
                "the user's own sweep", "someone-elses-uuid", PeakSeconds(PeakIndex + 500))
        };

        RewExportResult result = await SendAsync(rew, PeakSeconds(PeakIndex));

        Assert.True(result.Verified);
        Assert.Null(result.Problem);
    }

    private static Task<RewExportResult> SendAsync(
        FakeRew rew,
        double reportedPeakSeconds,
        string identifier = "probe")
    {
        rew.ReportedPeakSeconds = reportedPeakSeconds;
        rew.SentIdentifier = identifier;
        using var http = new HttpClient(rew);
        var export = new RewMeasurementExport(
            new RewApiClient(http, new Uri("http://localhost:4735/")));
        return export.SendAsync(
            new RewExportRequest(Arrival(), PeakIndex, SampleRate, identifier, null),
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

    private sealed class FakeRew : HttpMessageHandler
    {
        private bool imported;

        public List<string> Paths { get; } = [];
        public Dictionary<string, FakeMeasurement> Existing { get; } = [];
        public string? ImportBody { get; private set; }
        public double ReportedPeakSeconds { get; set; }
        public bool Unreachable { get; set; }

        /// <summary>Accepts the connection and says nothing (dropped packets): no throw, only a deadline.</summary>
        public bool Silent { get; set; }
        public HttpStatusCode ImportStatus { get; set; } = HttpStatusCode.Accepted;
        public FakeMeasurement? Concurrent { get; set; }

        public string? MeasurementsBody { get; set; }

        public string MeasurementsContentType { get; set; } = "application/json";

        public bool BreakMeasurementsAfterImport { get; set; }

        /// <summary>Title characters REW keeps when filing, 0 for all.</summary>
        public int TitleLimit { get; set; }

        public string SentIdentifier { get; set; } = "probe";

        public string? VersionBody { get; set; }

        public string VersionContentType { get; set; } = "application/json";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Unreachable)
            {
                throw new HttpRequestException("Connection refused.");
            }

            if (Silent)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            string path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);

            switch (path)
            {
                case "/version":
                    return Json(
                        HttpStatusCode.OK,
                        VersionBody ?? $"{{\"message\":\"{Version}\"}}",
                        VersionContentType);

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
                    if (BreakMeasurementsAfterImport && imported)
                    {
                        return Json(HttpStatusCode.OK, "{\"1\":{\"title\":");
                    }

                    return Json(
                        HttpStatusCode.OK,
                        MeasurementsBody ?? BuildMeasurements(),
                        MeasurementsContentType);

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

            // The stranger is listed first on purpose, so first-found cannot pass.
            if (imported && Concurrent is { } first)
            {
                entries.Add(Entry((Existing.Count + 2).ToString(), first));
            }

            if (imported)
            {
                string filed = SentIdentifier;
                if (TitleLimit > 0 && filed.Length > TitleLimit)
                {
                    filed = filed[..TitleLimit];
                }

                entries.Add(Entry(
                    (Existing.Count + 1).ToString(),
                    new FakeMeasurement(filed, "new-uuid", ReportedPeakSeconds)));
            }

            // Appears only after import starts; otherwise it would be in the before-snapshot and test nothing.
            return "{" + string.Join(",", entries) + "}";
        }

        private static string Entry(string index, FakeMeasurement measurement) =>
            FormattableString.Invariant(
                $"\"{index}\":{{\"title\":\"{measurement.Title}\",\"uuid\":\"{measurement.Uuid}\",\"timeOfIRPeakSeconds\":{measurement.PeakSeconds:R}}}");

        private static HttpResponseMessage Json(
            HttpStatusCode status,
            string body,
            string contentType = "application/json")
        {
            var content = new StringContent(body, Encoding.UTF8);
            content.Headers.Remove("Content-Type");
            content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            return new HttpResponseMessage(status) { Content = content };
        }
    }
}
