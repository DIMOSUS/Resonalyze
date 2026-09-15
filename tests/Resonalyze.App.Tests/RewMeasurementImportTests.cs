using System.Buffers.Binary;
using System.Net;
using System.Text;
using Resonalyze.Integration.Rew;
using Xunit.Abstractions;

namespace Resonalyze.App.Tests;

public sealed class RewMeasurementImportTests(ITestOutputHelper output)
{
    private const int SampleRate = 48_000;
    private const int Length = 8_192;
    private const double TimeZeroIndex = 1_200.25;
    private const int PeakIndex = 1_296;
    private const string Uuid = "ba2da346-0f31-4d9d-bbeb-2bfcd07e1cb9";
    private const string Version = "5.40 Beta 132 API 0.9.6";
    private static readonly Uri BaseAddress = new("http://localhost:4735/");

    [Fact]
    public async Task ListAsync_KeepsRewsOrderAndItsSelection()
    {
        var rew = new FakeRew
        {
            MeasurementsBody =
                "{\"10\":{\"title\":\"ten\",\"uuid\":\"u10\"},\"2\":{\"title\":\"two\",\"uuid\":\"u2\"},\"1\":{\"title\":\"one\",\"uuid\":\"u1\"}}",
            SelectedBody = "\"u2\""
        };

        RewMeasurementCatalog catalog = await ListAsync(rew);

        Assert.Equal(Version, catalog.Version);
        Assert.Equal(["one", "two", "ten"], catalog.Measurements.Select(m => m.Title));
        Assert.Equal("u2", catalog.SelectedUuid);
    }

    [Fact]
    public async Task ListAsync_ReadsTheSelectionFromAMessageObject()
    {
        var rew = new FakeRew { SelectedBody = $"{{\"message\":\"{Uuid}\"}}" };

        RewMeasurementCatalog catalog = await ListAsync(rew);

        Assert.Equal(Uuid, catalog.SelectedUuid);
    }

    [Fact]
    public async Task ListAsync_AnUnreadableSelectionOnlyLosesThePreselection()
    {
        var rew = new FakeRew { SelectedBody = "[1,2]" };

        RewMeasurementCatalog catalog = await ListAsync(rew);

        Assert.Null(catalog.SelectedUuid);
        Assert.Single(catalog.Measurements);
    }

    [Fact]
    public async Task ListAsync_AsksNothingElseOfARewThatIsNotRunning()
    {
        var rew = new FakeRew { Unreachable = true };

        RewMeasurementCatalog catalog = await ListAsync(rew);

        Assert.Null(catalog.Version);
        Assert.Empty(catalog.Measurements);
        Assert.DoesNotContain(rew.Requests, request => request.StartsWith("/measurements", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListAsync_ReadsRewsPublishedSummary()
    {
        // The example from REW's API help, which also carries fields this build does not read.
        var rew = new FakeRew
        {
            MeasurementsBody =
                "{\"1\":{\"title\":\"Artist 3+Q2070Si\",\"notes\":\"no EQ\",\"date\":\"2014-May-05 14:12:40\"," +
                $"\"uuid\":\"{Uuid}\",\"groupName\":\"Listening position\",\"startFreq\":2.1972656,\"endFreq\":24027.117," +
                "\"inverted\":false,\"sampleRate\":48000,\"rewVersion\":\"V5.01 Beta 20\",\"splOffsetdB\":139.223," +
                "\"cumulativeIRShiftSeconds\":0,\"clockAdjustmentPPM\":0,\"timeOfIRStartSeconds\":-0.0000625," +
                "\"timeOfIRPeakSeconds\":-0.0000012207031250266454,\"micCalPath\":\"C:/cal.txt\"}}"
        };

        RewMeasurementSummary summary = Assert.Single((await ListAsync(rew)).Measurements);

        Assert.Equal("Artist 3+Q2070Si", summary.Title);
        Assert.Equal("2014-May-05 14:12:40", RewMeasurementImport.DescribeDate(summary.Date));
        Assert.Equal(48_000, summary.SampleRate);
        Assert.Equal(0.0, summary.CumulativeIRShiftSeconds);
        Assert.Equal(-0.0000012207031250266454, summary.TimeOfIRPeakSeconds);
    }

    [Fact]
    public async Task ListAsync_ReadsTheTimingRewRecordedForAMeasuredSweep()
    {
        // As REW 5.40 Beta 134 lists a loopback-referenced sweep.
        var rew = new FakeRew
        {
            MeasurementsBody =
                "{\"1\":{\"title\":\"test\",\"date\":\"2026-Sep-15 20:30:56\"," +
                $"\"uuid\":\"{Uuid}\",\"startFreq\":0.36621094,\"endFreq\":20000.244,\"inverted\":false," +
                "\"sampleRate\":96000.0,\"rewVersion\":\"V5.40 beta 134\",\"timingReference\":\"Loopback\"," +
                "\"delay\":4.012749044351588E-4,\"timingOffset\":0.004,\"signalToNoisedB\":46.06715675508752," +
                "\"splOffsetdB\":99.88561725616455,\"cumulativeIRShiftSeconds\":0.0," +
                "\"timeOfIRStartSeconds\":3.75E-4,\"timeOfIRPeakSeconds\":4.012749044352004E-4}}"
        };

        RewMeasurementSummary summary = Assert.Single((await ListAsync(rew)).Measurements);

        Assert.Equal("Loopback", summary.TimingReference);
        Assert.Equal(0.004, summary.TimingOffsetSeconds);
        Assert.Equal(96_000, summary.SampleRate);
    }

    [Fact]
    public void ConnectableAddress_UsesIpv4ForLocalhost()
    {
        Assert.Equal(
            new Uri("http://127.0.0.1:4735/"),
            RewApiClient.ConnectableAddress(new Uri("http://localhost:4735/")));
        Assert.Equal(
            new Uri("http://192.168.1.5:4735/"),
            RewApiClient.ConnectableAddress(new Uri("http://192.168.1.5:4735/")));
    }

    [Fact]
    public async Task PrepareAsync_AsksForTheUnnormalisedResponseInPercent()
    {
        var rew = new FakeRew();

        await PrepareAsync(rew, offsetSeconds: 0.0);

        Assert.Contains($"/measurements/{Uuid}/impulse-response?unit=percent&normalised=false", rew.Requests);
    }

    [Fact]
    public async Task PrepareAsync_ReadsPercentAsFractionsOfFullScale()
    {
        RewImportPreparation preparation = await PrepareAsync(new FakeRew(), offsetSeconds: 0.0);

        Assert.Equal(0.8, preparation.Import!.Samples[PeakIndex], 6);
    }

    [Fact]
    public async Task PrepareAsync_TakesTheSweepLevelBackOut()
    {
        RewImportPreparation preparation = await PrepareAsync(new FakeRew(), offsetSeconds: 0.0, sweepLevelDbfs: -12.0);

        RewPreparedImport import = preparation.Import!;
        Assert.Equal(0.8 * Math.Pow(10.0, 12.0 / 20.0), import.Samples[PeakIndex], 6);
        Assert.Equal(-12.0, import.SweepLevelDbfs);
    }

    [Theory]
    [InlineData(0.36621094, 20000.244, 0.36621094, 20000.244, true)]
    [InlineData(2.1972656, 24027.117, 2.1972656, 24000.0, true)]
    [InlineData(null, null, 20.0, 24000.0, false)]
    [InlineData(0.0, 20000.0, 20.0, 24000.0, false)]
    public void ResolveBand_TakesRewsRangeClampedToNyquist(
        double? startHz, double? endHz, double expectedLowHz, double expectedHighHz, bool expectedFromRew)
    {
        var measurement = new RewMeasurementSummary { StartFrequencyHz = startHz, EndFrequencyHz = endHz };

        (double lowHz, double highHz, bool fromRew) = RewMeasurementImport.ResolveBand(measurement, SampleRate);

        Assert.Equal(expectedLowHz, lowHz, 6);
        Assert.Equal(expectedHighHz, highHz, 6);
        Assert.Equal(expectedFromRew, fromRew);
    }

    [Fact]
    public async Task PrepareAsync_ReadsTheSweepRateFromTheHarmonicsInRewsPreRoll()
    {
        const int peak = 40_000;
        const double secondsPerNeper = 0.1;
        double[] samples = Noise(65_536, peak);
        samples[peak - (int)Math.Round(secondsPerNeper * Math.Log(2) * SampleRate)] = 4e-3;
        samples[peak - (int)Math.Round(secondsPerNeper * Math.Log(3) * SampleRate)] = 2e-3;
        var rew = new FakeRew { Samples = samples, StartTime = -(peak - 96.0) / SampleRate };

        RewPreparedImport import = (await PrepareAsync(rew, offsetSeconds: 0.0)).Import!;

        Assert.Equal([2, 3], import.SweepRate!.Orders);
        Assert.Equal(secondsPerNeper, import.SweepRate.SecondsPerNeper, 1.5 / SampleRate);
        Assert.Equal(
            secondsPerNeper * Math.Log(import.HighFrequencyHz / import.LowFrequencyHz) * SampleRate,
            import.SweepLengthSamples,
            12.0);
    }

    [Fact]
    public async Task PrepareAsync_WithoutHarmonics_KeepsTheResponsesOwnLength()
    {
        RewPreparedImport import = (await PrepareAsync(new FakeRew(), offsetSeconds: 0.0)).Import!;

        Assert.Null(import.SweepRate);
        Assert.Equal(Length, import.SweepLengthSamples);
    }

    [Fact]
    public async Task PrepareAsync_RefusesALevelAboveFullScale()
    {
        var rew = new FakeRew();

        RewImportPreparation preparation = await PrepareAsync(rew, offsetSeconds: 0.0, sweepLevelDbfs: 3.0);

        Assert.Contains("at or below 0", preparation.Problem!);
        Assert.DoesNotContain(rew.Requests, request => request.Contains("impulse-response", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{\"value\":-12.0,\"unit\":\"dBFS\"}", -12.0, "dBFS")]
    [InlineData("{\"value\":-3.5,\"unit\":\"dBu\"}", null, "dBu")]
    [InlineData("<html>404</html>", null, null)]
    public async Task ListAsync_ReadsRewsLevelSettingOnlyInDbfs(string body, double? expected, string? expectedUnit)
    {
        var rew = new FakeRew { LevelBody = body };

        RewMeasurementCatalog catalog = await ListAsync(rew);

        Assert.Equal(expected, catalog.LevelDbfs);
        Assert.Equal(expectedUnit, catalog.Level?.Unit);
    }

    [Fact]
    public async Task PrepareAsync_RefusesAUnitThatIsNotLinear()
    {
        var rew = new FakeRew { Unit = "dBFS" };

        RewImportPreparation preparation = await PrepareAsync(rew, offsetSeconds: 0.0);

        Assert.Contains("“dBFS”", preparation.Problem!);
    }

    [Fact]
    public async Task PrepareAsync_PutsTheLoopbackReferenceOnSampleZero()
    {
        RewImportPreparation preparation = await PrepareAsync(new FakeRew(), offsetSeconds: 0.0);

        RewPreparedImport import = Assert.IsType<RewPreparedImport>(preparation.Import);
        Assert.Null(preparation.Problem);
        Assert.Equal(TimingReference.SynchronizedLoopback, import.Plan.Reference);
        Assert.Equal(TimeZeroIndex, import.TimeZeroIndex, 9);
        Assert.Equal(PeakIndex - TimeZeroIndex, import.Plan.ArrivalSamples, 9);
        Assert.Equal(Length, import.Referenced.Length);
        Assert.Equal(96, RewMeasurementImport.PeakIndexOf(import.Referenced));
    }

    [Fact]
    public async Task PrepareAsync_TakesAStatedOffsetBackOut()
    {
        RewImportPreparation preparation = await PrepareAsync(new FakeRew(), offsetSeconds: 0.0005);

        RewPreparedImport import = Assert.IsType<RewPreparedImport>(preparation.Import);
        Assert.Equal(TimeZeroIndex - 24, import.Plan.ReferenceIndex, 9);
        Assert.Equal(PeakIndex - TimeZeroIndex + 24, import.Plan.ArrivalSamples, 9);
        Assert.Equal(120, RewMeasurementImport.PeakIndexOf(import.Referenced));
    }

    [Fact]
    public async Task PrepareAsync_TheOffsetRewRecordsPutsAnOffsetSweepBackOnItsArrival()
    {
        // As REW 5.40 Beta 134 serves a sweep taken with a 4 ms offset: the peak reads 4 ms early and timingOffset is +0.004.
        const double arrivalSamples = 19.2;
        var rew = new FakeRew { StartTime = -(PeakIndex - arrivalSamples + (0.004 * SampleRate)) / SampleRate };

        RewImportPreparation preparation = await PrepareAsync(rew, offsetSeconds: 0.004);

        Assert.Equal(arrivalSamples, preparation.Import!.Plan.ArrivalSamples, 6);
        Assert.Equal(TimingReference.SynchronizedLoopback, preparation.Import.Plan.Reference);
    }

    [Fact]
    public async Task PrepareAsync_TakesRewsIrShiftOutWithTheStatedOffset()
    {
        // As REW 5.40 Beta 134 serves a response after Offset t=0 by +2 ms: cumulativeIRShiftSeconds +0.002, axis 2 ms earlier.
        const double shift = 0.002;
        var shifted = new FakeRew { StartTime = -(TimeZeroIndex + (shift * SampleRate)) / SampleRate };

        RewPreparedImport import = (await PrepareAsync(shifted, offsetSeconds: 0.0, irShiftSeconds: shift)).Import!;

        Assert.Equal(PeakIndex - TimeZeroIndex, import.Plan.ArrivalSamples, 6);
        Assert.Equal(TimingReference.SynchronizedLoopback, import.Plan.Reference);
        Assert.Equal(shift, import.IrShiftSeconds);
        Assert.Equal(shift, import.Plan.OffsetSeconds, 12);
    }

    [Fact]
    public async Task PrepareAsync_WithAnUnknownOffset_StillClaimsNoPosition_WhateverTheShift()
    {
        RewPreparedImport import = (await PrepareAsync(new FakeRew(), offsetSeconds: null, irShiftSeconds: 0.002)).Import!;

        Assert.Equal(TimingReference.RecordedSweep, import.Plan.Reference);
    }

    [Fact]
    public async Task PrepareAsync_SaysWhenAMeasurementHasNoImpulseResponse()
    {
        // REW 5.40 Beta 134 answers a magnitude-only measurement with 400 and this message.
        var rew = new FakeRew
        {
            ImpulseResponseStatus = HttpStatusCode.BadRequest,
            ImpulseResponseBody = $"{{\"message\":\"Magn test0.0 at index 6 uuid {Uuid} does not have an impulse response\"}}"
        };

        RewApiException exception = await Assert.ThrowsAsync<RewApiException>(
            () => PrepareAsync(rew, offsetSeconds: 0.0));

        Assert.Contains("does not have an impulse response", exception.Message);
        Assert.DoesNotContain("no longer holds", exception.Message);
    }

    [Fact]
    public async Task PrepareAsync_FilesAnUnknownOffsetAsARecordedSweep()
    {
        RewImportPreparation preparation = await PrepareAsync(new FakeRew(), offsetSeconds: null);

        Assert.Equal(TimingReference.RecordedSweep, preparation.Import!.Plan.Reference);
    }

    [Fact]
    public async Task PrepareAsync_RefusesAnOffsetThatPutsTheArrivalBeforeTheReference()
    {
        RewImportPreparation preparation = await PrepareAsync(new FakeRew(), offsetSeconds: -0.003);

        Assert.Null(preparation.Import);
        Assert.Contains("cannot produce", preparation.Problem!);
    }

    [Fact]
    public async Task PrepareAsync_RefusesAMeasurementWithoutALoopbackReference()
    {
        var rew = new FakeRew { TimingReference = "No timing reference" };

        RewImportPreparation preparation = await PrepareAsync(rew, offsetSeconds: 0.0);

        Assert.Null(preparation.Import);
        Assert.Contains("\u201cNo timing reference\u201d", preparation.Problem!);
    }

    [Fact]
    public async Task PrepareAsync_RefusesAListedRateMismatchWithoutDownloading()
    {
        var rew = new FakeRew();

        RewImportPreparation preparation = await PrepareAsync(rew, offsetSeconds: 0.0, listedSampleRate: 44_100);

        Assert.Contains("44100 Hz", preparation.Problem!);
        Assert.DoesNotContain(rew.Requests, request => request.Contains("impulse-response", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PrepareAsync_RefusesTheRateTheResponseItselfStates()
    {
        var rew = new FakeRew { ServedSampleRate = 96_000 };

        RewImportPreparation preparation = await PrepareAsync(rew, offsetSeconds: 0.0, listedSampleRate: null);

        Assert.Contains("96000 Hz", preparation.Problem!);
    }

    [Fact]
    public async Task PrepareAsync_RefusesABufferThatDoesNotHoldTheReference()
    {
        var rew = new FakeRew { StartTime = 0.01 };

        RewImportPreparation preparation = await PrepareAsync(rew, offsetSeconds: null);

        Assert.Contains("outside the buffer", preparation.Problem!);
    }

    [Fact]
    public async Task PrepareAsync_ReportsAMeasurementRewNoLongerHolds()
    {
        var rew = new FakeRew { ImpulseResponseStatus = HttpStatusCode.NotFound };

        RewApiException exception = await Assert.ThrowsAsync<RewApiException>(
            () => PrepareAsync(rew, offsetSeconds: 0.0));

        Assert.Contains("no longer holds", exception.Message);
    }

    [Fact]
    public async Task PrepareAsync_TurnsAnUnreadableResponseIntoARewProblem()
    {
        var rew = new FakeRew { ImpulseResponseBody = "<html>busy</html>" };

        RewApiException exception = await Assert.ThrowsAsync<RewApiException>(
            () => PrepareAsync(rew, offsetSeconds: 0.0));

        Assert.Contains("could not read", exception.Message);
    }

    /// <summary>Against a running REW: sends a measurement, reads its impulse response back, then deletes it.</summary>
    [RewFact]
    [Trait("Category", "Hardware")]
    public async Task GetImpulseResponseAsync_ReadsBackTheSamplesAnExportSent()
    {
        Assert.True(RewApiClient.TryParseBaseAddress(RewFactAttribute.ApiUrl(), out Uri? baseAddress));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var client = new RewApiClient(http, baseAddress!);
        string identifier = $"{Guid.NewGuid():N} Resonalyze import";
        double[] sent = Spike(peakIndex: 3_000);

        IReadOnlyDictionary<string, RewMeasurementSummary> before =
            await client.GetMeasurementsAsync(CancellationToken.None);
        RewExportResult exported = await new RewMeasurementExport(client).SendAsync(
            new RewExportRequest(sent, 3_000, SampleRate, identifier, null),
            CancellationToken.None);
        RewMeasurementSummary? filed = (await client.GetMeasurementsAsync(CancellationToken.None)).Values
            .FirstOrDefault(summary => summary.Uuid != null &&
                !before.Values.Any(known => known.Uuid == summary.Uuid) &&
                RewMeasurementExport.IsFiledAs(summary.Title, identifier));
        RewImpulseResponseBody? body = filed == null
            ? null
            : await client.GetImpulseResponseAsync(filed.Uuid!, CancellationToken.None);

        string? cleanup = await RewMeasurementExportTests.DeleteMeasurementsAddedSinceAsync(
            http, baseAddress!, client, before, identifier);

        Assert.Null(exported.Problem);
        Assert.NotNull(body);
        output.WriteLine($"timingReference: {body.TimingReference}");
        RewImpulseResponseImport expected = RewImpulseResponsePayload.Build(sent, 3_000, SampleRate, identifier, null);
        double[] expectedSamples = RewImpulseResponsePayload.DecodeSamples(expected.Body.Data);
        double[] readSamples = RewImpulseResponsePayload.DecodeSamples(body.Data!);
        output.WriteLine(FormattableString.Invariant(
            $"read peak {readSamples.Max(Math.Abs):R} for a sent peak of {expectedSamples.Max(Math.Abs):R}"));
        Assert.Equal(expected.Body.StartTime, body.StartTime!.Value, 9);
        Assert.Equal("percent", body.Unit);
        Assert.Equal(expectedSamples.Length, readSamples.Length);
        for (int i = 0; i < readSamples.Length; i++)
        {
            Assert.Equal(expectedSamples[i], readSamples[i] / RewMeasurementImport.PercentOfFullScale, 6);
        }

        Assert.Null(cleanup);
    }

    /// <summary>Against a running REW: reads the list and REW's selected measurement without changing anything.</summary>
    [RewFact]
    [Trait("Category", "Hardware")]
    public async Task ListAsync_AndPrepareAsync_ReadRewsSelectedMeasurement()
    {
        Assert.True(RewApiClient.TryParseBaseAddress(RewFactAttribute.ApiUrl(), out Uri? baseAddress));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var import = new RewMeasurementImport(new RewApiClient(http, baseAddress!));

        RewMeasurementCatalog catalog = await import.ListAsync(TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.NotNull(catalog.Version);
        if (catalog.Measurements.Count == 0)
        {
            return;
        }

        Assert.NotNull(catalog.SelectedUuid);
        RewMeasurementSummary selected = Assert.Single(
            catalog.Measurements, measurement => measurement.Uuid == catalog.SelectedUuid);
        output.WriteLine($"REW level setting: {catalog.LevelDbfs} dBFS");
        RewImportPreparation preparation = await import.PrepareAsync(
            selected,
            selected.TimingOffsetSeconds,
            catalog.LevelDbfs ?? -12.0,
            (int)Math.Round(selected.SampleRate ?? SampleRate),
            CancellationToken.None);
        output.WriteLine($"{selected.Title}: {preparation.Problem ?? "importable"}");
        output.WriteLine(FormattableString.Invariant(
            $"rate {selected.SampleRate}, peak {selected.TimeOfIRPeakSeconds * 1000.0:0.####} ms, cumulative IR shift {selected.CumulativeIRShiftSeconds * 1000.0:0.####} ms"));
        if (preparation.Import is { } prepared)
        {
            output.WriteLine(FormattableString.Invariant(
                $"t0 at sample {prepared.TimeZeroIndex:0.###}, arrival {prepared.Plan.ArrivalSamples / prepared.SampleRate * 1000.0:0.###} ms"));
        }

        RewImpulseResponseBody body = await new RewApiClient(http, baseAddress!)
            .GetImpulseResponseAsync(selected.Uuid!, CancellationToken.None);
        output.WriteLine(FormattableString.Invariant(
            $"timingReference “{body.TimingReference}”, startTime {body.StartTime:R} s, sampleRate {body.SampleRate}"));
    }

    private static async Task<RewMeasurementCatalog> ListAsync(FakeRew rew)
    {
        using var http = new HttpClient(rew);
        return await new RewMeasurementImport(new RewApiClient(http, BaseAddress))
            .ListAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
    }

    private static async Task<RewImportPreparation> PrepareAsync(
        FakeRew rew,
        double? offsetSeconds,
        double? listedSampleRate = SampleRate,
        double sweepLevelDbfs = 0.0,
        double? irShiftSeconds = null)
    {
        using var http = new HttpClient(rew);
        var measurement = new RewMeasurementSummary
        {
            Title = "front left",
            Uuid = Uuid,
            SampleRate = listedSampleRate,
            CumulativeIRShiftSeconds = irShiftSeconds
        };
        return await new RewMeasurementImport(new RewApiClient(http, BaseAddress))
            .PrepareAsync(measurement, offsetSeconds, sweepLevelDbfs, SampleRate, CancellationToken.None);
    }

    private static double[] Spike(int peakIndex)
    {
        var samples = new double[Length];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = Math.Sin(i * 0.013) * 0.002;
        }

        samples[peakIndex] = 0.8;
        return samples;
    }

    private static double[] Noise(int length, int peakIndex)
    {
        var random = new Random(20260915);
        var samples = new double[length];
        for (int i = 0; i < length; i++)
        {
            samples[i] = (random.NextDouble() - 0.5) * 4e-5;
        }

        samples[peakIndex] = 0.8;
        return samples;
    }

    private static string Encode(double[] samples)
    {
        var bytes = new byte[samples.Length * sizeof(float)];
        for (int i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(i * sizeof(float)), (float)samples[i]);
        }

        return Convert.ToBase64String(bytes);
    }

    private sealed class FakeRew : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public bool Unreachable { get; set; }
        public string MeasurementsBody { get; set; } = $"{{\"1\":{{\"title\":\"front left\",\"uuid\":\"{Uuid}\"}}}}";
        public string? SelectedBody { get; set; } = $"\"{Uuid}\"";
        public HttpStatusCode ImpulseResponseStatus { get; set; } = HttpStatusCode.OK;
        public string? ImpulseResponseBody { get; set; }
        public string TimingReference { get; set; } = "Loopback";
        public string Unit { get; set; } = "percent";
        public string LevelBody { get; set; } = "{\"value\":-12.0,\"unit\":\"dBFS\"}";
        public double[]? Samples { get; set; }
        public double ServedSampleRate { get; set; } = SampleRate;
        public double StartTime { get; set; } = -TimeZeroIndex / SampleRate;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Unreachable)
            {
                throw new HttpRequestException("Connection refused.");
            }

            string path = request.RequestUri!.AbsolutePath;
            Requests.Add(request.RequestUri.PathAndQuery);
            HttpResponseMessage response = path switch
            {
                "/version" => Json(HttpStatusCode.OK, $"{{\"message\":\"{Version}\"}}"),
                "/measurements" => Json(HttpStatusCode.OK, MeasurementsBody),
                "/measure/level" => Json(HttpStatusCode.OK, LevelBody),
                "/measurements/selected-uuid" => SelectedBody == null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : Json(HttpStatusCode.OK, SelectedBody),
                _ when path == $"/measurements/{Uuid}/impulse-response" =>
                    Json(ImpulseResponseStatus, ImpulseResponseBody ?? BuildImpulseResponse()),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
            return Task.FromResult(response);
        }

        private string BuildImpulseResponse() =>
            FormattableString.Invariant(
                $"{{\"unit\":\"{Unit}\",\"startTime\":{StartTime:R},\"sampleInterval\":{1.0 / ServedSampleRate:R},\"sampleRate\":{ServedSampleRate:R},\"timingReference\":\"{TimingReference}\",\"timingRefTime\":0.0,\"timingOffset\":0.0,\"delay\":0.002,\"data\":\"{Encode((Samples ?? Spike(PeakIndex)).Select(sample => sample * RewMeasurementImport.PercentOfFullScale).ToArray())}\"}}");

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
