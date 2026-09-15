namespace Resonalyze.Dsp.Tests;

public sealed class SweepAnalysisTests
{
    [Fact]
    public void DeconvolveWithInverseFilter_ReportsThePeakIndexOfTheConvolution()
    {
        // A flatness test is shift-invariant, so a wrong peak search is only caught here.
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
        var recorded = new double[128];
        recorded[70] = 1.0;
        var inverseFilter = new double[64];
        inverseFilter[5] = 1.0;

        SweepDeconvolutionResult result = SweepAnalysis.DeconvolveWithInverseFilter(
            recorded, inverseFilter);

        Assert.Equal(75, result.PeakIndex);
    }
}
