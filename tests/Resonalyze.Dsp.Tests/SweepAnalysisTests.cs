namespace Resonalyze.Dsp.Tests;

public sealed class SweepAnalysisTests
{
    [Fact]
    public void DeconvolveWithInverseFilter_ReportsThePeakIndexOfTheConvolution()
    {
        // recorded = delta at 30, inverse filter = delta at 20. Their convolution is a
        // single delta at 30 + 20 = 50, so the reported peak index must be 50. The
        // flatness test only checks a shift-invariant magnitude spectrum, so a wrong
        // peak search (off-by-one, min-vs-max flip) is invisible there but caught here.
        var recorded = new double[128];
        recorded[30] = 1.0;
        var inverseFilter = new double[64];
        inverseFilter[20] = 1.0;

        SweepDeconvolutionResult result = SweepAnalysis.DeconvolveWithInverseFilter(
            recorded, inverseFilter);

        Assert.Equal(50, result.PeakIndex);
        Assert.True(
            Math.Abs(result.ImpulseResponse[50]) >= result.ImpulseResponse.Max(Math.Abs) - 1e-12,
            "The reported peak index must hold the largest-magnitude sample.");
    }

    [Fact]
    public void DeconvolveWithInverseFilter_PeakTracksTheDelay()
    {
        // Shift the recorded delta and the peak index must move with it.
        var recorded = new double[128];
        recorded[70] = 1.0;
        var inverseFilter = new double[64];
        inverseFilter[5] = 1.0;

        SweepDeconvolutionResult result = SweepAnalysis.DeconvolveWithInverseFilter(
            recorded, inverseFilter);

        Assert.Equal(75, result.PeakIndex);
    }
}
