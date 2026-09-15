namespace Resonalyze.Dsp.Tests;

/// <summary>A zero-phase bandpass builds a textured shelf ahead of a reverberant record (field sub: -20 dB, above the per-peak
/// ring ceiling); RisesOutOfItsApproach must not read its ripple as a front (field read 0.02 ms for 27 ms).</summary>
public sealed class AcausalPedestalTests
{
    private const int SampleRate = 96_000;

    [Fact]
    public void RippleOnAFlatShelf_IsNotReadAsAnArrival()
    {
        const int length = 8_192;
        const int shelfEnd = 600;
        const int peakIndex = 1_000;
        const double shelfLevel = 0.1;

        var envelope = new double[length];
        for (int i = 0; i < shelfEnd; i++)
        {
            // 1% relief every 15 samples, far more than the field's ripple.
            envelope[i] = shelfLevel * (i % 15 == 7 ? 1.01 : 1.0);
        }
        for (int i = shelfEnd; i <= peakIndex; i++)
        {
            envelope[i] = shelfLevel +
                (1.0 - shelfLevel) * (i - shelfEnd) / (peakIndex - shelfEnd);
        }
        for (int i = peakIndex + 1; i < length; i++)
        {
            envelope[i] = Math.Max(1e-6, Math.Exp(-(i - peakIndex) / 150.0));
        }

        // Field geometry: kernel ring -30.7 dB at that distance against a -20 dB shelf.
        var kernelEnvelope = new double[length];
        for (int d = 0; d < length; d++)
        {
            kernelEnvelope[d] = Math.Exp(-d / 60.0);
        }

        PeakSearchResult result = SignalEnvelope.FindPeak(
            envelope,
            SampleRate,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.FirstArrival,
                AnalysisKernelEnvelope = kernelEnvelope
            });

        Assert.True(
            result.SelectedIndex >= shelfEnd,
            $"the search selected index {result.SelectedIndex} — a ripple on " +
            "the flat shelf ahead of the arrival, not a front that rises");
        Assert.Equal(peakIndex, result.StrongestIndex);
    }

    // A soft front 6 dB proud of the shelf must still be read: the 25 dB search depth exists for such fronts.
    [Fact]
    public void SoftFrontProudOfTheShelf_IsStillRead()
    {
        const int length = 8_192;
        const int shelfEnd = 900;
        const int frontIndex = 700;
        const int peakIndex = 1_500;
        const double shelfLevel = 0.1;
        const double frontLevel = 0.2;

        var envelope = new double[length];
        for (int i = 0; i < shelfEnd; i++)
        {
            envelope[i] = shelfLevel * (i % 15 == 7 ? 1.01 : 1.0);
        }
        for (int i = frontIndex - 120; i <= frontIndex + 120; i++)
        {
            double x = (i - frontIndex) / 120.0;
            envelope[i] = Math.Max(
                envelope[i], shelfLevel + (frontLevel - shelfLevel) *
                    0.5 * (1 + Math.Cos(Math.PI * x)));
        }
        for (int i = shelfEnd; i <= peakIndex; i++)
        {
            envelope[i] = shelfLevel +
                (1.0 - shelfLevel) * (i - shelfEnd) / (peakIndex - shelfEnd);
        }
        for (int i = peakIndex + 1; i < length; i++)
        {
            envelope[i] = Math.Max(1e-6, Math.Exp(-(i - peakIndex) / 150.0));
        }

        var kernelEnvelope = new double[length];
        for (int d = 0; d < length; d++)
        {
            kernelEnvelope[d] = Math.Exp(-d / 60.0);
        }

        PeakSearchResult result = SignalEnvelope.FindPeak(
            envelope,
            SampleRate,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.FirstArrival,
                AnalysisKernelEnvelope = kernelEnvelope
            });

        Assert.Equal(frontIndex, result.SelectedIndex);
        Assert.Equal(peakIndex, result.StrongestIndex);
    }
}
