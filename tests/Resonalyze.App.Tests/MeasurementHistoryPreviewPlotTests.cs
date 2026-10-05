using OxyPlot;
using OxyPlot.Axes;
using Resonalyze.History;

namespace Resonalyze.App.Tests;

public sealed class MeasurementHistoryPreviewPlotTests
{
    [Fact]
    public void ThePreviewKeepsItsFixedScale_ButLiftsTheCeilingOverAPaddedLoopback()
    {
        Axis ordinary = DecibelAxis(PeakingAt(-5.0));
        Assert.Equal(PlotModelStyle.RelativeDecibelMinimum, ordinary.ActualMinimum);
        Assert.Equal(PlotModelStyle.RelativeDecibelMaximum, ordinary.ActualMaximum);

        Axis padded = DecibelAxis(PeakingAt(20.0));
        Assert.Equal(PlotModelStyle.RelativeDecibelMinimum, padded.ActualMinimum);
        Assert.True(padded.ActualMaximum > 20.0, $"the axis stops at {padded.ActualMaximum} dB under a +20 dB curve");
    }

    private static MeasurementHistoryPreview PeakingAt(double peakDb) => new()
    {
        Frequencies = [100.0, 1_000.0, 10_000.0],
        MagnitudesDb = [peakDb - 3.0, peakDb, peakDb - 1.0],
    };

    private static Axis DecibelAxis(MeasurementHistoryPreview preview)
    {
        PlotModel model = MeasurementHistoryPreviewPlot.Create(preview);
        ((IPlotModel)model).Update(true);
        return model.Axes.Single(axis => axis.Position == AxisPosition.Left);
    }
}
