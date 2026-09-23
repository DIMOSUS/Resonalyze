using Resonalyze.Dsp;
using static Resonalyze.App.Tests.FirConstructorSessionTests;

namespace Resonalyze.App.Tests;

public sealed class FirConstructorReaderTests
{
    [Fact]
    public void TheSessionLine_SaysWhatIsEdited_AndWhenADesignIsRebuiltAtTheProcessorsRate()
    {
        var session = new FirConstructorSession();
        Assert.Equal("Standalone: design a kernel and export it to a file.", FirConstructorReadout.Session(session));

        session.BeginHandoff(Request("B", LowPass(rate: 96_000)), LowPass());
        Assert.Equal(
            "Editing Channel B, left side. The rate is the processor's. " +
            "It was designed at 96 kHz and is rebuilt here at 48 kHz.",
            FirConstructorReadout.Session(session));

        session.BeginHandoff(Request("C", LowPass(rate: 44_100), rate: 44_100), LowPass());
        Assert.Equal("Editing Channel C, left side. The rate is the processor's.", FirConstructorReadout.Session(session));

        session.EndHandoff();
        Assert.StartsWith("Standalone", FirConstructorReadout.Session(session));
    }

    [Fact]
    public void ADesign_ReadsItsLatencyAtItsRate_AndItsWorstDeviation()
    {
        var session = new FirConstructorSession();
        Assert.Empty(FirConstructorReadout.Latency(session));
        Assert.Empty(FirConstructorReadout.Deviation(session));

        FirCrossoverDesign design = LowPass(taps: 1_023, rate: 96_000);
        Land(session, session.Edit(design)!);

        Assert.Equal("Latency 5.32 ms (511 samples at 96 kHz)", FirConstructorReadout.Latency(session));
        Assert.Equal(
            FormattableString.Invariant($"Worst deviation from the target: {session.Rendering!.DeviationDb:0.00} dB above −30 dB"),
            FirConstructorReadout.Deviation(session));
    }

    [Fact]
    public void ABrickWall_HasNoSlopeToCompare()
    {
        var session = new FirConstructorSession();
        Land(session, session.Edit(LowPass() with { Method = FirCrossoverMethod.WindowedSinc })!);

        Assert.True(double.IsNaN(session.Rendering!.DeviationDb));
        Assert.Equal("A brick wall has no slope to compare with: read the plot.", FirConstructorReadout.Deviation(session));
    }

    [Fact]
    public void ABareKernel_ReadsItsNameAndLength_AndSaysAnEditReplacesIt()
    {
        var session = new FirConstructorSession();
        Land(session, session.ShowBare(new FirFilter([0.25, 0.5, 0.25]), "room.wav", 44_100));
        Assert.Equal("room.wav: 3 taps, shown as it is at 44.1 kHz", FirConstructorReadout.Latency(session));
        Assert.Equal("Any change to the controls designs a new kernel in its place.", FirConstructorReadout.Deviation(session));

        Land(session, session.ShowBare(new FirFilter([1.0]), null, 48_000));
        Assert.Equal("Kernel: 1 taps, shown as it is at 48 kHz", FirConstructorReadout.Latency(session));
    }

    [Theory]
    [InlineData(CrossoverKind.LowPass, FirCrossoverMethod.IirMagnitude, FirWindow.Kaiser, false, false, true, true, true)]
    [InlineData(CrossoverKind.HighPass, FirCrossoverMethod.IirMagnitude, FirWindow.Hann, true, true, false, false, false)]
    [InlineData(CrossoverKind.BandPass, FirCrossoverMethod.WindowedSinc, FirWindow.Blackman, true, false, true, false, false)]
    public void ADraft_TakesTheCornersOfItsType_TheShapeForIirMagnitude_AndBetaForKaiser(
        CrossoverKind kind, FirCrossoverMethod method, FirWindow window,
        bool high, bool highShape, bool low, bool lowShape, bool beta)
    {
        FirConstructorFields fields = FirConstructorAvailability.Fields(
            LowPass() with { Kind = kind, Method = method, Window = window });

        Assert.Equal(new FirConstructorFields(high, highShape, low, lowShape, beta), fields);
    }

    [Fact]
    public void Export_WaitsForARebuild_AndReturn_TakesOnlyALandedDesignInAHandoff()
    {
        var session = new FirConstructorSession();
        FirConstructorRebuild pending = session.Edit(LowPass())!;
        Assert.False(FirConstructorAvailability.CanExport(session));

        Land(session, pending);
        Assert.True(FirConstructorAvailability.CanExport(session));
        Assert.Null(FirConstructorAvailability.Return(session));

        FirConstructorHandoffRequest request = Request("B");
        session.BeginHandoff(request, LowPass());
        FirConstructorReturn back = FirConstructorAvailability.Return(session)!;
        Assert.Equal((request.Token, session.Kernel!, session.Design!), (back.Token, back.Kernel, back.Design));

        FirConstructorRebuild editing = session.Edit(LowPass(taps: 511))!;
        Assert.Null(FirConstructorAvailability.Return(session));
        Assert.False(FirConstructorAvailability.CanExport(session));

        Land(session, editing);
        Land(session, session.ShowBare(new FirFilter([1.0]), "room.wav", 48_000));
        Assert.True(FirConstructorAvailability.CanExport(session));
        Assert.Null(FirConstructorAvailability.Return(session));
    }

    [Fact]
    public void AnExport_IsNamedForItsDesign_OrItsFile_AndCarriesTheDesignsDescription()
    {
        var session = new FirConstructorSession();
        Assert.Null(FirConstructorExport.Request(session));

        FirCrossoverDesign design = LowPass(taps: 255, rate: 96_000);
        Land(session, session.Edit(design)!);
        FirConstructorExportRequest designed = FirConstructorExport.Request(session)!;
        Assert.Equal(
            ("FIR LP 2 kHz", 96_000, (string?)null, FirCrossoverDescription.Long(design)),
            (designed.SuggestedFileName, designed.RateHz, designed.SourceName, designed.Description));

        Land(session, session.ShowBare(new FirFilter([0.25, 0.5, 0.25]), "room correction.wav", 44_100));
        FirConstructorExportRequest bare = FirConstructorExport.Request(session)!;
        Assert.Equal(
            ("room correction", 44_100, "room correction.wav", (string?)null),
            (bare.SuggestedFileName, bare.RateHz, bare.SourceName, bare.Description));

        Land(session, session.ShowBare(new FirFilter([1.0]), null, 48_000));
        Assert.Equal("FIR", FirConstructorExport.Request(session)!.SuggestedFileName);

        FirConstructorRebuild pending = session.Edit(design)!;
        Assert.Null(FirConstructorExport.Request(session));
        pending.Dispose();
    }

    [Fact]
    public void TheSavedText_KeepsTheRateTheNameAndTheDescription()
    {
        using var folder = new TemporaryDirectory();
        string path = folder.File("kernel.txt");
        var request = new FirConstructorExportRequest(new FirFilter([0.25, 0.5, 0.25]), 96_000, "room.wav", "Linear-phase test", "x");

        FirConstructorExport.Save(request, path);

        string[] lines = File.ReadAllLines(path);
        Assert.Equal(
            ["* FIR filter exported by Resonalyze (imported from room.wav)", "* Linear-phase test", "* Sample rate: 96000"],
            lines[..3]);
        Assert.Equal([0.25, 0.5, 0.25], FirFilterFiles.Load(path).Taps.ToArray());
    }

    [Fact]
    public void TheSlope_IsTheNearestTheFamilyOffers_AndAFamilyItDoesNotBecomesLinkwitzRiley()
    {
        Assert.Equal(48, FirConstructorChoices.NearestSlope(CrossoverFilterFamily.Bessel, 96));
        Assert.Equal(12, FirConstructorChoices.NearestSlope(CrossoverFilterFamily.LinkwitzRiley, 6));
        Assert.Equal(
            FirCrossoverDesign.SupportedSlopes(CrossoverFilterFamily.Butterworth).Select(slope => $"{slope} dB/oct"),
            FirConstructorChoices.Slopes(CrossoverFilterFamily.Butterworth).Select(choice => choice.Label));
        Assert.Equal(CrossoverFilterFamily.Bessel, FirConstructorChoices.OfferedFamily(CrossoverFilterFamily.Bessel));
        Assert.Equal(
            CrossoverFilterFamily.LinkwitzRiley, FirConstructorChoices.OfferedFamily(CrossoverFilterFamily.Chebyshev));
        Assert.Equal(["Linkwitz-Riley", "Butterworth", "Bessel"], FirConstructorChoices.Families.Select(family => family.Label));
    }

    [Theory]
    [InlineData(4_095, 4_095)]
    [InlineData(2_000, 2_001)]
    [InlineData(16_382, 16_383)]
    [InlineData(16_384, 16_383)]
    public void AnEvenTapCount_MovesToAnOddNeighbour(int typed, int odd) =>
        Assert.Equal(odd, FirConstructorChoices.OddTapCount(typed));

    [Fact]
    public void TheRates_AreListedAsTheDescriptionWritesThem()
    {
        Assert.Equal(
            ["44.1 kHz", "48 kHz", "88.2 kHz", "96 kHz", "176.4 kHz", "192 kHz"],
            FirConstructorChoices.SampleRates.Select(FirConstructorChoices.RateLabel));
        Assert.Contains(FirConstructorChoices.DefaultRateHz, FirConstructorChoices.SampleRates);
    }

    // As the panel runs one: land, let go, dispose.
    private static void Land(FirConstructorSession session, FirConstructorRebuild rebuild)
    {
        using (rebuild)
        {
            Assert.True(session.Land(rebuild, FirConstructorRender.Run(rebuild, CancellationToken.None)));
            session.Finish(rebuild);
        }
    }
}
