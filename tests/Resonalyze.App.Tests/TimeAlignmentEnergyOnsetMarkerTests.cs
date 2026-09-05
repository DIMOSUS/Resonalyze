using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

// The energy onset on the Time Alignment envelope plot: a third callout
// beside the two peaks, placed where the analysis put the onset — relative to
// the Main first arrival like every other marker — and read off the envelope
// through the same wrap a complete record's signed positions need.
public sealed class TimeAlignmentEnergyOnsetMarkerTests
{
    private const int SampleRate = 96_000;

    private static TimeAlignmentAnalysisResult MakeResult(
        int arrivalIndex,
        int strongestIndex,
        double onsetSample)
    {
        var envelope = new double[2048];
        for (int i = 0; i < envelope.Length; i++)
        {
            envelope[i] =
                0.5 * Math.Exp(-((i - arrivalIndex) * (i - arrivalIndex)) / 200.0) +
                Math.Exp(-((i - strongestIndex) * (i - strongestIndex)) / 200.0) +
                1e-5;
        }

        return new TimeAlignmentAnalysisResult(
            envelope,
            arrivalIndex,
            envelope[arrivalIndex],
            strongestIndex,
            envelope[strongestIndex],
            SignalToNoiseDecibels: 60.0,
            FirstArrivalProminenceDecibels: -6.0,
            FirstArrivalPeakSample: arrivalIndex,
            FirstArrivalDelayMilliseconds: arrivalIndex * 1000.0 / SampleRate,
            StrongestPeakSample: strongestIndex,
            StrongestDelayMilliseconds: strongestIndex * 1000.0 / SampleRate,
            StrongestPeakSeparationMilliseconds:
                (strongestIndex - arrivalIndex) * 1000.0 / SampleRate,
            StrongestPeakIsSeparateArrival: true,
            FirstArrivalConfidence: 0.5,
            FirstArrivalRefinedByPhat: true,
            StrongestConfidence: 0.5,
            StrongestRefinedByPhat: true,
            EnergyOnsetSample: onsetSample,
            EnergyOnsetDelayMilliseconds: onsetSample * 1000.0 / SampleRate);
    }

    [Fact]
    public void GetEnergyOnsetIndex_RoundsAndWrapsTheSignedSample()
    {
        Assert.Equal(370, TimeAlignmentPanelController.GetEnergyOnsetIndex(
            MakeResult(400, 500, 370.4)));
        // A complete record reports a position past its midpoint as a
        // negative delay; the envelope index wraps back into the buffer.
        Assert.Equal(2048 - 30, TimeAlignmentPanelController.GetEnergyOnsetIndex(
            MakeResult(400, 500, -30.0)));
    }

    [Fact]
    public void MainMarkers_PlaceTheOnsetRelativeToTheFirstArrival()
    {
        TimeAlignmentAnalysisResult result = MakeResult(400, 500, 370.0);
        var model = new PlotModel();

        TimeAlignmentPanelController.AddMainPeakMarkers(
            model, result, result.StrongestEnvelopePeak);

        PlotCalloutMarkerAnnotation onset = Assert.Single(
            model.Annotations.OfType<PlotCalloutMarkerAnnotation>(),
            annotation => annotation.Text == "M Onset");
        Assert.Equal((370.0 - 400.0) * 1000.0 / SampleRate, onset.AnchorPoint.X, 9);
        Assert.Equal(
            TimeAlignmentPanelController.GetPeakMarkerDecibels(
                result, result.StrongestEnvelopePeak, 370),
            onset.AnchorPoint.Y,
            9);
        Assert.Contains(
            model.Annotations.OfType<PlotCalloutMarkerAnnotation>(),
            annotation => annotation.Text == "M First");
    }

    [Fact]
    public void CompareMarkers_PlaceBothOnsetsAgainstTheMainFirstArrival()
    {
        TimeAlignmentAnalysisResult main = MakeResult(400, 500, 370.0);
        TimeAlignmentAnalysisResult compare = MakeResult(450, 550, 425.0);
        var model = new PlotModel();

        TimeAlignmentPanelController.AddComparePeakMarkers(
            model, main, compare, main.StrongestEnvelopePeak,
            compare.FirstArrivalDelayMilliseconds - main.FirstArrivalDelayMilliseconds);

        PlotCalloutMarkerAnnotation mainOnset = Assert.Single(
            model.Annotations.OfType<PlotCalloutMarkerAnnotation>(),
            annotation => annotation.Text == "M Onset");
        PlotCalloutMarkerAnnotation compareOnset = Assert.Single(
            model.Annotations.OfType<PlotCalloutMarkerAnnotation>(),
            annotation => annotation.Text == "C Onset");
        Assert.Equal((370.0 - 400.0) * 1000.0 / SampleRate, mainOnset.AnchorPoint.X, 9);
        Assert.Equal((425.0 - 400.0) * 1000.0 / SampleRate, compareOnset.AnchorPoint.X, 9);
    }
}
