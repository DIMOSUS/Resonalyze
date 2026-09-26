using System.Numerics;
using Resonalyze.Audio;

namespace Resonalyze.App.Tests;

public sealed class PlotDrawInputsTests
{
    [Fact]
    public void NoChange_DrawsNothing_AndANewNameOnlyRetitles()
    {
        AnalyzerDocument document = Open(Measurement(), "first.json");
        PlotDrawInputs drawn = PlotDrawInputs.Read(Mode.PhaseResponse, document, includesCurves: true, compare: null);

        Assert.Equal(PlotRedraw.None, PlotDrawInputs.Read(Mode.PhaseResponse, document, true, null).RedrawFrom(drawn));
        document.Rename("saved.json");
        Assert.Equal(PlotRedraw.Retitle, PlotDrawInputs.Read(Mode.PhaseResponse, document, true, null).RedrawFrom(drawn));
    }

    [Fact]
    public void ANewResult_TheCurvesComingOrGoing_OrAnotherMode_Rebuild()
    {
        AnalyzerDocument document = Open(Measurement(), "first.json");
        PlotDrawInputs drawn = PlotDrawInputs.Read(Mode.PhaseResponse, document, includesCurves: true, compare: null);

        Assert.Equal(PlotRedraw.Rebuild, PlotDrawInputs.Read(Mode.PhaseResponse, document, false, null).RedrawFrom(drawn));
        Assert.Equal(PlotRedraw.Rebuild, PlotDrawInputs.Read(Mode.GroupDelay, document, true, null).RedrawFrom(drawn));
        Assert.Equal(PlotRedraw.Rebuild, PlotDrawInputs.Read(Mode.PhaseResponse, document, true, null).RedrawFrom(null));
        document.TryBegin()!.Install(Measurement(), "first.json");
        Assert.Equal(PlotRedraw.Rebuild, PlotDrawInputs.Read(Mode.PhaseResponse, document, true, null).RedrawFrom(drawn));
    }

    [Theory]
    [InlineData(Mode.FrequencyResponse, true, true)]
    [InlineData(Mode.PhaseResponse, true, true)]
    [InlineData(Mode.GroupDelay, true, true)]
    [InlineData(Mode.ImpulseResponse, true, true)]
    [InlineData(Mode.CumulativeSpectrumDecay, true, false)]
    [InlineData(Mode.BurstDecay, true, false)]
    [InlineData(Mode.Autocorrelation, true, false)]
    [InlineData(Mode.FrequencyResponse, false, false)]
    public void ACompareChoice_RebuildsOnlyWhereTheDrawShowsIt(Mode mode, bool includesCurves, bool rebuilds)
    {
        AnalyzerDocument document = Open(Measurement(), "first.json");
        PlotDrawInputs drawn = PlotDrawInputs.Read(mode, document, includesCurves, compare: null);
        var compare = new CompareMeasurementSelection("reference", null, Measurement());

        Assert.Equal(
            rebuilds ? PlotRedraw.Rebuild : PlotRedraw.None,
            PlotDrawInputs.Read(mode, document, includesCurves, compare).RedrawFrom(drawn));
    }

    private static AnalyzerDocument Open(MeasurementResult result, string name)
    {
        var document = new AnalyzerDocument();
        Assert.True(document.TryBegin()!.Install(result, name));
        return document;
    }

    private static MeasurementResult Measurement()
    {
        var impulse = new Complex[1_024];
        impulse[100] = Complex.One;
        return TestMeasurementResults.Restored(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000,
            sampleRate: 48_000,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: impulse,
            sweepDeconvolutionPeakIndex: 100,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: impulse,
            transferPeakIndex: 100);
    }
}
