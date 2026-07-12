using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class CropSharedWindowTests
{
    [Fact]
    public void Crop_KeepsTheInterChannelTimingIntact()
    {
        var early = new Complex[100_000];
        var late = new Complex[100_000];
        early[40_000] = Complex.One;
        late[40_777] = Complex.One;

        Complex[][] cropped = VirtualCrossoverAnalysis.CropSharedDirectSoundWindow(
            [early, late], cropLength: 8_192, prePeakSamples: 1_000);

        int earlyPeak = VirtualCrossoverAnalysis.FindPeakIndex(cropped[0]);
        int latePeak = VirtualCrossoverAnalysis.FindPeakIndex(cropped[1]);
        Assert.Equal(1_000, earlyPeak);
        Assert.Equal(777, latePeak - earlyPeak);
        Assert.All(cropped, ir => Assert.Equal(8_192, ir.Length));
    }

    [Fact]
    public void Crop_ShortResponsesAndEarlyPeaksStayUsable()
    {
        var shortIr = new Complex[500];
        shortIr[10] = Complex.One;

        Complex[][] cropped = VirtualCrossoverAnalysis.CropSharedDirectSoundWindow(
            [shortIr], cropLength: 8_192, prePeakSamples: 1_000);

        // The peak sits before the pre-peak margin, so the crop starts at 0
        // and the short capture comes back whole.
        Assert.Equal(500, cropped[0].Length);
        Assert.Equal(10, VirtualCrossoverAnalysis.FindPeakIndex(cropped[0]));
    }
}
