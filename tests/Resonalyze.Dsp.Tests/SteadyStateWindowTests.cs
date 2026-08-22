using Resonalyze.Dsp;

namespace Resonalyze.Dsp.Tests;

// The steady-state magnitude window: one definition in milliseconds for every
// magnitude curve the Virtual DSP tool and the EQ Wizard draw, realized in samples
// with the same clamp-and-trim the gated carve applies (ResolveGatePlacement).
public sealed class SteadyStateWindowTests
{
    [Fact]
    public void At48k_TheFullWindowFits()
    {
        (int window, int left, int right) =
            FrequencyResponseOptions.SteadyStateWindowSamples(48_000);

        // 2 + 500 + 180 ms at 48 kHz — under the 32768-sample FFT, so nothing trims.
        Assert.Equal(32_736, window);
        Assert.Equal(96, left);
        Assert.Equal(8_640, right);
    }

    [Fact]
    public void AtHighRates_TheCarveClampGovernsAndTheFadesStillFit()
    {
        // 682 ms at 192 kHz would be 130944 samples; the carve holds at most
        // GatedFftLength, and the fades must be cut to fit INSIDE the clamped
        // window the same way ResolveGatePlacement cuts them — a right fade longer
        // than the window would be an invalid Tukey.
        (int window, int left, int right) =
            FrequencyResponseOptions.SteadyStateWindowSamples(192_000);

        Assert.Equal(DataHelper.GatedFftLength, window);
        Assert.Equal(384, left);
        Assert.True(right <= window - left);
        Assert.True(left + right <= window);
        // Even clamped, the window stays a steady-state one: ~171 ms — dozens of
        // times the junction gate it replaced, enough for a bass band's ringing.
        Assert.True(window * 1_000.0 / 192_000 > 150);
    }
}
