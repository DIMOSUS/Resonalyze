using OxyPlot;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

// Both curves share ONE dB reference (Main's strongest peak); per-curve arrival normalization made equal records read decades apart.
public sealed class TimeAlignmentEnvelopePlotTests
{
    private const int SampleRate = 96_000;
    private const int Radius = 300;

    [Fact]
    public void EnvelopeSeries_PicksAtDifferentDepths_KeepEqualPeaksLevel()
    {
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

        double comparePeakDb = TimeAlignmentPreviews.GetPeakMarkerDecibels(
            compare, reference, compare.StrongestEnvelopePeakIndex);
        double compareArrivalDb = TimeAlignmentPreviews.GetPeakMarkerDecibels(
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
        // 40 dB apart: the shared reference must shift Compare down, not flatten it onto the 80 dB floor.
        TimeAlignmentAnalysisResult main = MakeResult(peak: 1.0, arrivalBelowPeakDb: 6);
        TimeAlignmentAnalysisResult compare = MakeResult(peak: 0.01, arrivalBelowPeakDb: 6);

        DrawnCurve mainCurve = Draw(main, main.StrongestEnvelopePeak);
        DrawnCurve compareCurve = Draw(compare, main.StrongestEnvelopePeak);

        Assert.Equal(0.0, mainCurve.PointsMaxDb, 3);
        Assert.Equal(-80.0, mainCurve.PointsMinDb, 1);
        Assert.Equal(-40.0, compareCurve.PointsMaxDb, 1);
        Assert.Equal(-120.0, compareCurve.PointsMinDb, 1);

        Assert.Equal(compareCurve.PointsMinDb, compareCurve.MinDb, 6);
        Assert.Equal(compareCurve.PointsMaxDb, compareCurve.MaxDb, 6);
    }

    private static DrawnCurve Draw(
        TimeAlignmentAnalysisResult result,
        double referenceAmplitude)
    {
        LineSeries series = TimeAlignmentPreviews.CreateEnvelopeSeries(
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

    private sealed record DrawnCurve(LineSeries Series, double MaxDb, double MinDb)
    {
        public double PointsMaxDb => Series.Points.Max(point => point.Y);

        public double PointsMinDb => Series.Points.Min(point => point.Y);
    }

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
