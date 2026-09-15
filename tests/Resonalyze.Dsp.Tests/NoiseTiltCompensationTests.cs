using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp.Tests;

public sealed class NoiseTiltCompensationTests
{
    private const int FftLength = 2048;
    private const int SampleRate = 48_000;
    private const double PinkSlope = -3.0102999566398120; // -10·log10(2)

    private static readonly NoiseSpectralModel Pink =
        NoiseSpectralModel.PowerLaw(PinkSlope);
    private static readonly NoiseSpectralModel White =
        NoiseSpectralModel.PowerLaw(0.0);

    [Fact]
    public void BinCompensation_MirrorsThePowerLawAroundThePivot()
    {
        Assert.Equal(
            0.0, NoiseTiltCompensation.BinCompensationDb(Pink, 1000.0, SampleRate), 12);
        Assert.Equal(
            -PinkSlope,
            NoiseTiltCompensation.BinCompensationDb(Pink, 2000.0, SampleRate),
            precision: 9);
        Assert.Equal(
            PinkSlope,
            NoiseTiltCompensation.BinCompensationDb(Pink, 500.0, SampleRate),
            precision: 9);
        Assert.Equal(
            0.0, NoiseTiltCompensation.BinCompensationDb(White, 20.0, SampleRate), 12);
        Assert.Equal(
            0.0, NoiseTiltCompensation.BinCompensationDb(White, 20_000.0, SampleRate), 12);
    }

    [Fact]
    public void BandCompensation_AlignsWithTheDisplayGridAndPinsThePivot()
    {
        double[] compensation = BandCompensation(Pink);
        List<SignalPoint> grid = DisplayGrid();

        Assert.Equal(grid.Count, compensation.Length);

        // Zero at the pivot: switching compensation on rotates the curve, not its level.
        int pivot = NearestIndex(grid, NoiseTiltCompensation.PivotFrequency);
        Assert.Equal(0.0, compensation[pivot], precision: 12);
    }

    [Fact]
    public void BandCompensation_IsFlatForPinkWhereBandsAreConstantRelative()
    {
        // In the constant-relative-bandwidth region band power renders pink flat: compensation ~0, not the per-bin line.
        double[] compensation = BandCompensation(Pink);
        List<SignalPoint> grid = DisplayGrid();

        double at2k = compensation[NearestIndex(grid, 2000.0)];
        double at8k = compensation[NearestIndex(grid, 8000.0)];
        Assert.True(
            Math.Abs(at8k - at2k) < 0.3,
            $"pink band compensation should be flat 2k..8k, drifted {at8k - at2k:0.000} dB");
    }

    [Fact]
    public void BandCompensation_UndoesTheBandLawTiltForWhite()
    {
        // White tilts +3.01 dB/oct on band power, so its compensation falls though the PSD slope is zero.
        double[] compensation = BandCompensation(White);
        List<SignalPoint> grid = DisplayGrid();

        double at2k = compensation[NearestIndex(grid, 2000.0)];
        double at8k = compensation[NearestIndex(grid, 8000.0)];
        double perOctave = (at8k - at2k) / 2.0;
        Assert.True(
            Math.Abs(perOctave - PinkSlope) < 0.15,
            $"white band compensation should fall ~3.01 dB/octave, got {perOctave:0.000}");
    }

    [Fact]
    public void BandCompensation_FollowsTheResolutionCornerAtLowFrequencies()
    {
        // Below the main-lobe corner the integrator is constant-absolute-bandwidth and pink rises toward LF again.
        double[] compensation = BandCompensation(Pink);
        List<SignalPoint> grid = DisplayGrid();

        double at40 = compensation[NearestIndex(grid, 40.0)];
        double at160 = compensation[NearestIndex(grid, 160.0)];
        Assert.True(
            at160 - at40 > 3.0,
            $"pink band compensation should fall toward LF below the resolution " +
            $"corner, got {at40:0.000} at 40 Hz vs {at160:0.000} at 160 Hz");
    }

    [Fact]
    public void LeakyIntegratorModel_MatchesTheSynthesisRecurrence()
    {
        // The brown model is the synthesis filter (leak·value + (1−leak)·white), not an ideal −6 dB/oct line.
        const double CornerHz = 76.0;
        double leak = 1.0 - 2.0 * Math.PI * CornerHz / SampleRate;
        int n = 1 << 17;
        var response = new Complex[n];
        double value = 0.0;
        for (int i = 0; i < n; i++)
        {
            value = leak * value + (1.0 - leak) * (i == 0 ? 1.0 : 0.0);
            response[i] = value;
        }

        Fourier.Forward(response, FourierOptions.NoScaling);

        var model = NoiseSpectralModel.LeakyIntegrator(CornerHz);
        for (int bin = 1; bin < n / 2; bin *= 2)
        {
            double frequency = bin * (double)SampleRate / n;
            double measured = response[bin].Magnitude;
            double modelled = model.AmplitudeAt(frequency, SampleRate);
            Assert.Equal(0.0, 20.0 * Math.Log10(measured / modelled), precision: 3);
        }
    }

    [Fact]
    public void KellettPinkModel_MatchesTheSynthesisRecurrence()
    {
        // Pink uses the synthesis Kellett bank, which flattens below its lowest pole (~35 Hz at 192 kHz).
        foreach (int sampleRate in new[] { SampleRate, 192_000 })
        {
            int n = 1 << 17;
            var response = new Complex[n];
            var states = new double[KellettPinkFilter.Poles.Count];
            double delayed = 0.0;
            for (int i = 0; i < n; i++)
            {
                double white = i == 0 ? 1.0 : 0.0;
                double pink = KellettPinkFilter.DirectGain * white + delayed;
                for (int pole = 0; pole < states.Length; pole++)
                {
                    (double a, double g) = KellettPinkFilter.Poles[pole];
                    states[pole] = a * states[pole] + g * white;
                    pink += states[pole];
                }
                delayed = KellettPinkFilter.DelayedGain * white;
                response[i] = pink;
            }

            Fourier.Forward(response, FourierOptions.NoScaling);

            for (int bin = 1; bin < n / 2; bin *= 2)
            {
                double frequency = bin * (double)sampleRate / n;
                double measured = response[bin].Magnitude;
                double modelled =
                    NoiseSpectralModel.KellettPink.AmplitudeAt(frequency, sampleRate);
                Assert.Equal(0.0, 20.0 * Math.Log10(measured / modelled), precision: 3);
            }
        }
    }

    [Fact]
    public void BrownCompensation_FlattensBelowTheFilterCorner()
    {
        // Below the 76 Hz leaky-integrator corner the excitation flattens; an ideal slope would print a fake 11.6 dB bass roll-off.
        var brown = NoiseSpectralModel.LeakyIntegrator(76.0);
        double at20 = NoiseTiltCompensation.BinCompensationDb(brown, 20.0, SampleRate);
        double at76 = NoiseTiltCompensation.BinCompensationDb(brown, 76.0, SampleRate);

        Assert.True(at76 < -15.0, $"expected a deep brown compensation at 76 Hz, got {at76:0.0}");
        Assert.True(
            at20 - at76 > -4.0,
            $"compensation must flatten below the corner: {at20:0.0} at 20 Hz vs {at76:0.0} at 76 Hz");
    }

    private static double[] BandCompensation(NoiseSpectralModel model) =>
        NoiseTiltCompensation.BandCompensationDb(
            model,
            (FftLength / 2) + 1,
            FftLength,
            SampleRate,
            Windowing.EquivalentNoiseBandwidthBins(WindowType.Hann, FftLength),
            Windowing.MainLobeWidthBins(WindowType.Hann),
            20,
            20_000,
            1024,
            smoothingOctaves: 1.0 / 6.0,
            psychoacoustic: false);

    private static List<SignalPoint> DisplayGrid()
    {
        var flat = new double[(FftLength / 2) + 1];
        Array.Fill(flat, 1.0);
        return DataHelper.LogarithmicPowerBandResample(
            flat,
            FftLength,
            SampleRate,
            Windowing.EquivalentNoiseBandwidthBins(WindowType.Hann, FftLength),
            Windowing.MainLobeWidthBins(WindowType.Hann),
            20,
            20_000,
            1024,
            smoothingOctaves: 1.0 / 6.0,
            psychoacoustic: false);
    }

    private static int NearestIndex(List<SignalPoint> points, double frequency)
    {
        int nearest = 0;
        for (int i = 1; i < points.Count; i++)
        {
            if (Math.Abs(Math.Log2(points[i].X / frequency)) <
                Math.Abs(Math.Log2(points[nearest].X / frequency)))
            {
                nearest = i;
            }
        }

        return nearest;
    }
}
