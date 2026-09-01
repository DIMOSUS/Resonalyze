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

    [Fact]
    public async Task SendAsync_ReportsAMeasurementListItCannotRead()
    {
        // The export deliberately does not gate on REW's version, so a beta that
        // still answers while having changed this shape is the way the design is
        // expected to fail. It has to arrive as a reported problem, not as a
        // JsonException through the application's global unexpected-error handler.
        var rew = new FakeRew { MeasurementsBody = "{\"1\":{\"title\":" };

        RewApiException exception = await Assert.ThrowsAsync<RewApiException>(
            () => SendAsync(rew, PeakSeconds(PeakIndex)));

        Assert.Contains("could not read", exception.Message);
        // The list is read before the import, so nothing was left behind in REW.
        Assert.DoesNotContain("/import/impulse-response-data", rew.Paths);
    }

    [Fact]
    public async Task SendAsync_ReportsAMeasurementListThatIsNotJsonAtAll()
    {
        // Something is on the port and it is not REW. Measured (.NET 10.0.301):
        // ReadFromJsonAsync does NOT reject the foreign content type, it parses the
        // bytes regardless, so this arrives as a JsonException about '<' — not as
        // the NotSupportedException the media type would suggest.
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
        // The other half, and the one that is a header fault rather than a body
        // fault: a charset HttpClient cannot resolve raises InvalidOperationException
        // from ReadFromJsonAsync, which no JsonException catch would have covered.
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
        // The same failure on the polling read, where the import has already gone
        // through: still a reported problem rather than an unhandled exception.
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
        // Something is listening on 4735 and it is not REW. That is the same news
        // as nothing listening at all, and must not throw at the button — this runs
        // from an async void handler, so an escape is a crash rather than a message.
        // The charset case is the one that was NOT already covered: it raises
        // InvalidOperationException, which the old "unreachable" rule did not list.
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
        // REW truncates a long title as it files it and reports the short one back
        // (measured on 5.40 Beta 132: 54 characters came back as 48, 64 as 45).
        // Requiring the name to match exactly made every export of a long name wait
        // out the filing timeout and then report that REW had not filed it.
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

        // Filed whole, and filed shortened to a length REW could have cut it to.
        Assert.True(RewMeasurementExport.IsFiledAs(identifier, identifier));
        Assert.True(RewMeasurementExport.IsFiledAs(identifier[..45], identifier));
        Assert.True(RewMeasurementExport.IsFiledAs(identifier[..40], identifier));

        // A SHORT name that merely begins the same way is somebody else's measurement.
        Assert.False(RewMeasurementExport.IsFiledAs("Resonalyze", identifier));
        Assert.False(RewMeasurementExport.IsFiledAs(identifier[..39], identifier));

        // An empty title is a prefix of everything, so it must not count as one.
        Assert.False(RewMeasurementExport.IsFiledAs("", identifier));
        Assert.False(RewMeasurementExport.IsFiledAs(null, identifier));

        // Longer than what was sent, another name, or a different case: not ours.
        Assert.False(RewMeasurementExport.IsFiledAs(identifier + "x", identifier));
        Assert.False(RewMeasurementExport.IsFiledAs("something else entirely, and long enough", identifier));
        Assert.False(RewMeasurementExport.IsFiledAs(identifier[..45].ToUpperInvariant(), identifier));

        // A short name is still matched when REW filed it WHOLE - the floor only
        // governs what may pass as a shortening.
        Assert.True(RewMeasurementExport.IsFiledAs("probe", "probe"));
    }

    [Fact]
    public async Task SendAsync_RefusesToGuessWhenTwoNewMeasurementsShareTheName()
    {
        // REW lets two measurements share a title, and both are new since the
        // snapshot. Nothing separates them, so the export must say so rather than
        // verify whichever the dictionary happened to yield first.
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
            // Filed first, and a prefix of ours: the user's own measurement.
            Concurrent = new FakeMeasurement("Resonalyze", "someone-elses-uuid", 0.5)
        };

        RewExportResult result = await SendAsync(rew, PeakSeconds(PeakIndex), identifier);

        // Verified against OURS — 0.5 s would have been reported as a huge disagreement
        // had the concurrent one been picked.
        Assert.True(result.Verified);
        Assert.Null(result.Problem);
    }

    [Fact]
    public async Task SendAsync_RefusesToGuessBetweenTwoEquallyGoodTruncations()
    {
        // Two new measurements, both shortened to the same length, both a prefix of
        // what was sent. Waiting cannot separate them and neither can a rule, so the
        // export says so instead of verifying one of them at random.
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
        // A real truncation: long enough that REW could have cut a title to it.
        Assert.True(RewMeasurementExport.IsFiledAs(identifier[..45], identifier));
        Assert.True(RewMeasurementExport.IsFiledAs(identifier, identifier));
        // Someone else's short name that happens to start the same way.
        Assert.False(RewMeasurementExport.IsFiledAs("Resonalyze", identifier));
        Assert.False(RewMeasurementExport.IsFiledAs(identifier[..39], identifier));
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
        // The GUID leads, so that what REW keeps of the name is still unique to this
        // run: REW shortens a long title, and a discriminator at the end is the part
        // it drops. Cleanup deletes by this name, so the cost of getting it wrong is
        // deleting a measurement belonging to whoever is using REW.
        string identifier = $"{Guid.NewGuid():N} Resonalyze round trip";

        IReadOnlyDictionary<string, RewMeasurementSummary> before =
            await client.GetMeasurementsAsync(CancellationToken.None);
        RewExportResult result = await new RewMeasurementExport(client).SendAsync(
            new RewExportRequest(Arrival(), PeakIndex, SampleRate, identifier, null),
            CancellationToken.None);

        // Clean up BEFORE asserting, and report the two failures separately: a
        // cleanup thrown from a finally block replaces the assertion that matters
        // with the news that a leftover could not be removed.
        string? cleanup = await DeleteMeasurementsAddedSinceAsync(
            http, baseAddress!, client, before, identifier);

        Assert.Null(result.Problem);
        Assert.Null(cleanup);
    }

    /// <summary>
    /// Removes what THIS test added, and nothing else. Rule of the house for anything
    /// that touches a live REW: it may be mid-session, so put back everything you
    /// added — and, just as strictly, touch nothing you did not. "New since the
    /// snapshot" is not that test: a measurement the user makes while this runs is
    /// also new, and deleting it would destroy their work. The identifier this test
    /// invented is unique, so it names its own import exactly.
    /// </summary>
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
            // The same prefix rule the export itself uses: REW truncates a long
            // title as it files it, and an equality test here would walk past the
            // measurement this test created and leave it in the user's session.
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
        // The case a refused connection does not cover. Before this, the probe's own
        // timeout cancelled the token it was watching, so the cancellation was not
        // read as "unreachable" and escaped through an async void handler.
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
        // The other half of the same fix: a caller who gives up must not be told
        // "REW is not answering", which is a different fact about a different thing.
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
        // REW stays usable during a send. A measurement that appears meanwhile is new
        // since the snapshot exactly as ours is, so UUID alone would let this export
        // verify a stranger's timing — and report a fault that belongs to neither.
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

        /// <summary>
        /// An address that accepts the connection and then says nothing — a firewall
        /// that drops packets, or a machine that is up with REW closed. This is the
        /// case a refused connection does NOT cover: nothing throws, the wait simply
        /// runs to whatever deadline is watching it.
        /// </summary>
        public bool Silent { get; set; }
        public HttpStatusCode ImportStatus { get; set; } = HttpStatusCode.Accepted;
        public FakeMeasurement? Concurrent { get; set; }

        /// <summary>
        /// What /measurements answers instead of the list, when set: a REW that is
        /// still there and still answering, having changed or malformed the shape.
        /// The export does not gate on REW's version, so this is the case that
        /// stands in for a future beta.
        /// </summary>
        public string? MeasurementsBody { get; set; }

        /// <summary>The content type it answers with, for a body that is not JSON at all.</summary>
        public string MeasurementsContentType { get; set; } = "application/json";

        /// <summary>
        /// Answer /measurements normally until the import has gone through, then
        /// stop being readable — the polling read, not the snapshot.
        /// </summary>
        public bool BreakMeasurementsAfterImport { get; set; }

        /// <summary>
        /// How many characters of a title REW keeps when it files one, or 0 for all
        /// of them. REW really does shorten a long name and report the short one
        /// back, which is why the export matches on a prefix.
        /// </summary>
        public int TitleLimit { get; set; }

        /// <summary>The name the export sent, which is what REW files it under.</summary>
        public string SentIdentifier { get; set; } = "probe";

        /// <summary>The same, for /version — the route the probe reads.</summary>
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

            // The stranger is listed BEFORE ours on purpose. Whichever candidate the
            // export happens to meet first must not be the one it keeps, and a fake
            // that always yields ours first cannot show the difference.
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

            // Someone at the keyboard in REW while this send was being filed. It has
            // to appear only once the import is under way: listed from the start it
            // would be in the caller's own before-snapshot, which is to say KNOWN, and
            // a test meaning to exercise the concurrency would quietly exercise
            // nothing.
            return "{" + string.Join(",", entries) + "}";
        }

        private static string Entry(string index, FakeMeasurement measurement) =>
            FormattableString.Invariant(
                $"\"{index}\":{{\"title\":\"{measurement.Title}\",\"uuid\":\"{measurement.Uuid}\",\"timeOfIRPeakSeconds\":{measurement.PeakSeconds:R}}}");

        /// <summary>
        /// The header goes on unvalidated so a test can send one REW could send and
        /// HttpClient cannot decode — an unusable charset, which is a header fault
        /// rather than a body fault and raises a different exception.
        /// </summary>
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
