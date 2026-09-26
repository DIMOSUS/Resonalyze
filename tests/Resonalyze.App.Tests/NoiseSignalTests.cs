using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class NoiseSignalTests
{
    [Fact]
    public void FillData_Silent_CreatesZeroPlaybackBuffer()
    {
        using var signal = new NoiseSignal();

        signal.FillData(
            requestedDuration: 2048.0 / 48_000,
            sampleRate: 48_000,
            noiseColor: NoiseColor.Silent,
            periodLength: 2048);

        Assert.Equal(2048, signal.FloatData.Length);
        Assert.All(signal.FloatData, sample => Assert.Equal(0.0f, sample));
    }

    [Fact]
    public void FillData_PinkPeriodic_TilesOnePeriodAtItsPeak()
    {
        const int period = 2048;
        using var signal = new NoiseSignal();

        signal.FillData(
            requestedDuration: 4.0 * period / 48_000,
            sampleRate: 48_000,
            noiseColor: NoiseColor.PinkPeriodic,
            periodLength: period);

        Assert.Equal(4 * period, signal.FloatData.Length);
        for (int i = period; i < signal.FloatData.Length; i++)
        {
            Assert.Equal(signal.FloatData[i % period], signal.FloatData[i]);
        }
        Assert.Equal(0.25, signal.FloatData.Max(Math.Abs), 6);
        // The random-phase period it replaced had a ~13 dB crest.
        Assert.InRange(PeriodicNoiseSynthesis.CrestFactorDb(signal.FloatData.Select(sample => (double)sample).ToArray()), 0.0, 3.0);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void PinkPeriod_IsExactlyPinkInsideItsBandAndSilentOutside()
    {
        // 5.86 Hz bins at 192 kHz put the first bin below the low edge and most of the top above the high one.
        const int length = 32_768;
        const int sampleRate = 192_000;

        double[] period = NoiseSignal.SynthesizePinkPeriod(length, sampleRate);

        var spectrum = period.Select(sample => new Complex(sample, 0.0)).ToArray();
        Fourier.Forward(spectrum, FourierOptions.NoScaling);
        double binWidth = (double)sampleRate / length;
        double inBandPowerTimesBin = double.NaN;
        for (int k = 1; k <= length / 2; k++)
        {
            double frequency = k * binWidth;
            double power = spectrum[k].Magnitude * spectrum[k].Magnitude;
            if (frequency < NoiseSignal.PeriodicPinkLowHz || frequency > NoiseSignal.PeriodicPinkHighHz)
            {
                Assert.True(power < 1e-20, $"{frequency:0.0} Hz carries {power:E1}");
                continue;
            }

            // Pink: power per bin falls as 1/k, so k·|X_k|² is one constant.
            if (double.IsNaN(inBandPowerTimesBin))
            {
                inBandPowerTimesBin = k * power;
            }
            Assert.Equal(1.0, k * power / inBandPowerTimesBin, 9);
        }
    }
}
