using OxyPlot;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

// Pins the dB reference of the Time Alignment "Envelope Around Peak" plot.
// Both curves must be drawn against ONE amplitude (the Main record's strongest
// peak). Normalizing each curve by its own first-arrival level — which the plot
// used to do — makes the axis mean something different per curve, so two
// equally loud records whose picks sit at different depths read as decades
// apart on screen.
public sealed class TimeAlignmentEnvelopePlotTests
{
    private const int SampleRate = 96_000;
    private const int Radius = 300;

    [Fact]
    public void EnvelopeSeries_PicksAtDifferentDepths_KeepEqualPeaksLevel()
    {
        // Same peak amplitude, wildly different first-arrival prominence: a
        // near-peak pick (-6 dB) against one on a broad leading edge (-25 dB).
        TimeAlignmentAnalysisResult main = MakeResult(peak: 1.0, arrivalBelowPeakDb: 6);
        TimeAlignmentAnalysisResult compare = MakeResult(peak: 1.0, arrivalBelowPeakDb: 25);

        DrawnCurve mainCurve = Draw(main, main.StrongestEnvelopePeak);
        DrawnCurve compareCurve = Draw(compare, main.StrongestEnvelopePeak);

        Assert.Equal(0.0, mainCurve.PointsMaxDb, 3);
        Assert.Equal(0.0, compareCurve.PointsMaxDb, 3);
    }

    [Fact]
    public void EnvelopeSeries_QuieterCompareRecord_ShowsItsTrueLevelOffset()
    {
        TimeAlignmentAnalysisResult main = MakeResult(peak: 1.0, arrivalBelowPeakDb: 6);
        // Half the amplitude, and a pick depth that differs from Main's on top
        // of it: only the level difference may reach the plot.
        TimeAlignmentAnalysisResult compare = MakeResult(peak: 0.5, arrivalBelowPeakDb: 25);

        DrawnCurve mainCurve = Draw(main, main.StrongestEnvelopePeak);
        DrawnCurve compareCurve = Draw(compare, main.StrongestEnvelopePeak);

        Assert.Equal(0.0, mainCurve.PointsMaxDb, 3);
        Assert.Equal(-6.02, compareCurve.PointsMaxDb, 2);
    }

    [Fact]
    public void PeakMarkers_ShareTheSeriesReference()
    {
        TimeAlignmentAnalysisResult main = MakeResult(peak: 1.0, arrivalBelowPeakDb: 6);
        TimeAlignmentAnalysisResult compare = MakeResult(peak: 0.5, arrivalBelowPeakDb: 25);
        double reference = main.StrongestEnvelopePeak;

        // A marker must land on its own curve: the compare peak marker at the
        // compare curve's maximum, the compare arrival marker at its prominence
        // below THAT — not at 0 dB, which is what a per-curve reference gave.
        double comparePeakDb = TimeAlignmentPanelController.GetPeakMarkerDecibels(
            compare, reference, compare.StrongestEnvelopePeakIndex);
        double compareArrivalDb = TimeAlignmentPanelController.GetPeakMarkerDecibels(
            compare, reference, compare.EnvelopePeakIndex);

        DrawnCurve compareCurve = Draw(compare, reference);
        double prominenceDb = 20.0 * Math.Log10(
            compare.EnvelopePeak / compare.StrongestEnvelopePeak);
        Assert.Equal(compareCurve.PointsMaxDb, comparePeakDb, 3);
        Assert.Equal(comparePeakDb + prominenceDb, compareArrivalDb, 3);
    }

    [Fact]
    public void EnvelopeSeries_MuchQuieterCompareRecord_KeepsItsOwnFloor()
    {
        // A sub against a tweeter: 40 dB apart. The shared reference must move
        // the Compare curve down, not flatten its lower 40 dB onto an absolute
        // floor 80 dB under Main's peak.
        TimeAlignmentAnalysisResult main = MakeResult(peak: 1.0, arrivalBelowPeakDb: 6);
        TimeAlignmentAnalysisResult compare = MakeResult(peak: 0.01, arrivalBelowPeakDb: 6);

        DrawnCurve mainCurve = Draw(main, main.StrongestEnvelopePeak);
        DrawnCurve compareCurve = Draw(compare, main.StrongestEnvelopePeak);

        Assert.Equal(0.0, mainCurve.PointsMaxDb, 3);
        Assert.Equal(-80.0, mainCurve.PointsMinDb, 1);
        Assert.Equal(-40.0, compareCurve.PointsMaxDb, 1);
        Assert.Equal(-120.0, compareCurve.PointsMinDb, 1);

        // The axis figures must describe the line that was actually drawn:
        // clamping only the reported minimum would leave the plot wrong while
        // every assertion above still passed.
        Assert.Equal(compareCurve.PointsMinDb, compareCurve.MinDb, 6);
        Assert.Equal(compareCurve.PointsMaxDb, compareCurve.MaxDb, 6);
    }

    private static DrawnCurve Draw(
        TimeAlignmentAnalysisResult result,
        double referenceAmplitude)
    {
        LineSeries series = TimeAlignmentPanelController.CreateEnvelopeSeries(
            result,
            referenceAmplitude,
            SampleRate,
            Radius,
            step: 1,
            xOffsetMilliseconds: 0.0,
            OxyColors.Yellow,
            strokeThickness: 2,
            out double maxDb,
            out double minDb);
        return new DrawnCurve(series, maxDb, minDb);
    }

    // The points are what the user sees; MaxDb/MinDb only size the axis. Every
    // assertion below reads the points, and the floor test also pins the two to
    // each other — a reported range that no longer described the drawn line
    // would leave the plot wrong with the axis still looking right.
    private sealed record DrawnCurve(LineSeries Series, double MaxDb, double MinDb)
    {
        public double PointsMaxDb => Series.Points.Max(point => point.Y);

        public double PointsMinDb => Series.Points.Min(point => point.Y);
    }

    // An envelope with two humps: the arrival at index 400 and the strongest
    // peak 100 samples later.
    private static TimeAlignmentAnalysisResult MakeResult(
        double peak,
        double arrivalBelowPeakDb)
    {
        const int arrivalIndex = 400;
        const int strongestIndex = 500;
        var envelope = new double[2048];
        double arrival = peak * Math.Pow(10.0, -arrivalBelowPeakDb / 20.0);
        for (int i = 0; i < envelope.Length; i++)
        {
            // Pedestal 100 dB under this record's own peak, so the 80 dB
            // curve floor is what the drawn minimum reports.
            envelope[i] =
                arrival * Hump(i - arrivalIndex) +
                peak * Hump(i - strongestIndex) +
                peak * 1e-5;
        }

        return new TimeAlignmentAnalysisResult(
            envelope,
            arrivalIndex,
            envelope[arrivalIndex],
            strongestIndex,
            envelope[strongestIndex],
            SignalToNoiseDecibels: 60.0,
            FirstArrivalProminenceDecibels: -arrivalBelowPeakDb,
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
            StrongestRefinedByPhat: true);
    }

    private static double Hump(int offset) =>
        Math.Exp(-(offset * offset) / 200.0);
}
