using System.Numerics;
using Resonalyze.History;

namespace Resonalyze.App.Tests;

public sealed class ArrivalPlacementTests
{
    private const int SampleRate = 48_000;
    private const int Length = 65_536;

    [Fact]
    public void AnArrivalAheadOfTheLoopback_IsFiledWithoutAbsoluteTime_AndPlacedLikeAnImport()
    {
        // 50 ms ahead: the arrival wrapped to the far end of the circular IR, its tail to the start.
        int peak = Length - 2_400;
        MeasurementResult judged = ArrivalPlacement.Judge(Result(peak, TimingReference.SynchronizedLoopback));

        int placed = (int)Math.Round(ArrivalPlacement.PlacedArrivalSeconds * SampleRate);
        Assert.Equal(TimingReference.NonCausalLoopback, judged.TimingReference);
        Assert.False(judged.TimingReference.HasAbsoluteTime());
        Assert.Equal(placed, judged.Transfer!.PeakIndex);
        Assert.Equal(1.0, judged.Transfer.ImpulseResponse[placed].Real);
        Assert.Equal(0.5, judged.Transfer.ImpulseResponse[placed + 2_400 + 10].Real);
        Assert.Equal(50.0, ArrivalPlacement.AheadOfLoopbackMs(peak, Length, SampleRate)!.Value, 9);
    }

    [Theory]
    [InlineData(TimingReference.SynchronizedLoopback, 480)]
    [InlineData(TimingReference.SynchronizedLoopback, Length / 2)]
    [InlineData(TimingReference.RecordedSweep, Length - 2_400)]
    public void AnythingElse_IsLeftAsItIs(TimingReference reference, int peak)
    {
        MeasurementResult result = Result(peak, reference);

        Assert.Same(result, ArrivalPlacement.Judge(result));
    }

    [Fact]
    public async Task AFileWhoseArrivalLedItsLoopback_ReadsBackWithoutAbsoluteTime_AndSavesAsIt()
    {
        string first = Path.Combine(Path.GetTempPath(), $"resonalyze-ahead-{Guid.NewGuid():N}.json");
        string second = Path.Combine(Path.GetTempPath(), $"resonalyze-ahead-{Guid.NewGuid():N}.json");
        try
        {
            await ImpulseResponseFile.From(Result(Length - 2_400, TimingReference.SynchronizedLoopback))
                .SaveAsync(first);

            MeasurementResult loaded = (await ImpulseResponseFile.LoadAsync(first)).ToResult();
            await ImpulseResponseFile.From(loaded).SaveAsync(second);
            MeasurementResult reloaded = (await ImpulseResponseFile.LoadAsync(second)).ToResult();

            Assert.Equal(TimingReference.NonCausalLoopback, loaded.TimingReference);
            Assert.Equal(TimingReference.NonCausalLoopback, reloaded.TimingReference);
            Assert.Equal(loaded.Transfer!.PeakIndex, reloaded.Transfer!.PeakIndex);
            Assert.Null(ResolvedVirtualDspSource.FromResult(reloaded));
            Assert.Contains("before the loopback did", VirtualCrossoverSourceRules.DescribeUnsummable(reloaded));
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact]
    public void TheEqWizardsFileSource_ReadsTheArrivalWhereTheFileLoadPlacesIt()
    {
        ImpulseResponseFile file = ImpulseResponseFile.From(
            Result(Length - 2_400, TimingReference.SynchronizedLoopback));

        EqWizardCurveSource source = EqWizardSourceResolver.CreateFromImpulseResponse(file, "ahead", "ahead");

        Assert.Equal(file.ToResult().Transfer!.PeakIndex, source.Measurement!.PeakIndex);
        Assert.Equal(1.0, source.Measurement.ImpulseResponse![source.Measurement.PeakIndex].Real);
    }

    [Fact]
    public void AHistoryRowOfAReFiledFile_DrawsItsPreviewFromTheReFiledImpulseResponse()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"resonalyze-ahead-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            // The stored preview was drawn with the arrival at the far end.
            ImpulseResponseFile file = ImpulseResponseFile.From(
                Result(Length - 2_400, TimingReference.SynchronizedLoopback));
            MeasurementResult result = file.ToResult();
            var service = new MeasurementHistoryService(
                new MeasurementHistoryPersistence(Path.Combine(directory, "history.json")));

            Guid id = service.AddOrUpdateLoadedFile(
                Path.Combine(directory, "ahead.json"), file, result, new MeasurementSessionSnapshot());

            Assert.Equal(
                MeasurementHistoryPreviewBuilder.Build(result).MagnitudesDb,
                service.FindById(id)!.Preview.MagnitudesDb);
            Assert.NotEqual(file.ToPreview()!.MagnitudesDb, service.FindById(id)!.Preview.MagnitudesDb);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TheCaptureNotice_SaysHowFarAheadAndWhatToDo()
    {
        string text = new SweepResultCaution(PreArrivalDb: null, AheadOfLoopbackMs: 43.9).Describe();

        Assert.Contains("43.9 ms BEFORE the loopback did", text);
        Assert.Contains("one audio device", text);
        Assert.DoesNotContain("unusual energy", text);
    }

    // A REW import states no levels: its buffer is REW's, so a delay past half of it is no evidence.
    [Fact]
    [Trait("Category", "Slow")]
    public async Task AStoredResultWithoutALoopbackLevel_KeepsItsTimeAndItsArrival()
    {
        string path = Path.Combine(Path.GetTempPath(), $"resonalyze-rew-{Guid.NewGuid():N}.json");
        try
        {
            MeasurementResult imported = Result(Length - 2_400, TimingReference.SynchronizedLoopback) with
            {
                Levels = InputLevelMeterSnapshot.Empty
            };
            await ImpulseResponseFile.From(imported).SaveAsync(path);

            MeasurementResult loaded = (await ImpulseResponseFile.LoadAsync(path)).ToResult();

            Assert.Equal(TimingReference.SynchronizedLoopback, loaded.TimingReference);
            Assert.Equal(Length - 2_400, loaded.Transfer!.PeakIndex);
            Assert.Equal(imported.Transfer!.ImpulseResponse, loaded.Transfer.ImpulseResponse);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MeasurementResult Result(int peak, TimingReference reference)
    {
        var transfer = new Complex[Length];
        transfer[peak] = Complex.One;
        transfer[(peak + 2_400 + 10) % Length] = new Complex(0.5, 0);
        var sweep = new Complex[1_024];
        sweep[16] = Complex.One;
        return new MeasurementResult
        {
            SampleRate = SampleRate,
            Bits = 24,
            LowFrequencyHz = 20,
            HighFrequencyHz = 20_000,
            AchievedLowFrequencyHz = 20,
            AchievedHighFrequencyHz = 20_000,
            SweepDurationSeconds = 1.0,
            MeasuredAtUtc = DateTimeOffset.UnixEpoch.AddDays(1),
            MeasurementMode = SweepMeasurementMode.LoopbackTransfer,
            TimingReference = reference,
            SweepDeconvolution = new MeasurementImpulseResponse(sweep, 16),
            Transfer = new MeasurementImpulseResponse(transfer, peak),
            Levels = new InputLevelMeterSnapshot(
                new InputLevelMeterEntry(true, -40.0, -55.0, false, false),
                new InputLevelMeterEntry(true, -6.0, -12.0, false, true))
        };
    }
}
