namespace Resonalyze.Dsp.Tests;

public sealed class SweepAnalysisTests
{
    [Fact]
    public void ASharedFilterSpectrum_DeconvolvesEachRecordingAsTheFilterItselfDoes()
    {
        var random = new Random(9);
        float[] filter = [.. Enumerable.Range(0, 3_000).Select(_ => (float)(random.NextDouble() - 0.5))];
        var shared = new InverseFilterSpectrum(filter);

        foreach (int length in new[] { 5_000, 5_000, 4_500, 20_000, 5_000 })
        {
            float[] recorded = [.. Enumerable.Range(0, length).Select(_ => (float)(random.NextDouble() - 0.5))];

            SweepDeconvolutionResult own = SweepAnalysis.DeconvolveWithInverseFilter(recorded, filter, 0.25);
            SweepDeconvolutionResult sharing = SweepAnalysis.DeconvolveWithInverseFilter(recorded, shared, 0.25);

            Assert.Equal(own.PeakIndex, sharing.PeakIndex);
            Assert.Equal(
                own.ImpulseResponse.Select(BitConverter.DoubleToInt64Bits),
                sharing.ImpulseResponse.Select(BitConverter.DoubleToInt64Bits));
        }

        Assert.Same(shared.At(8_192), shared.At(8_192));
    }

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
