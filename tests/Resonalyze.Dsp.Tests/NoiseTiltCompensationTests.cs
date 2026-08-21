namespace Resonalyze.Dsp.Tests;

public sealed class NoiseTiltCompensationTests
{
    private const int FftLength = 2048;
    private const int SampleRate = 48_000;
    private const double PinkSlope = -3.0102999566398120; // -10·log10(2)

    [Fact]
    public void BinCompensation_MirrorsTheSlopeAroundThePivot()
    {
        // Pink falls 3.01 dB/octave on the per-bin display, so the compensation
        // rises by exactly that per octave, zero at the pivot.
        Assert.Equal(0.0, NoiseTiltCompensation.BinCompensationDb(PinkSlope, 1000.0), 12);
        Assert.Equal(
            -PinkSlope,
            NoiseTiltCompensation.BinCompensationDb(PinkSlope, 2000.0),
            precision: 9);
        Assert.Equal(
            PinkSlope,
            NoiseTiltCompensation.BinCompensationDb(PinkSlope, 500.0),
            precision: 9);
        // White is flat on the per-bin display: identity.
        Assert.Equal(0.0, NoiseTiltCompensation.BinCompensationDb(0.0, 20.0), 12);
        Assert.Equal(0.0, NoiseTiltCompensation.BinCompensationDb(0.0, 20_000.0), 12);
    }

    [Fact]
    public void BandCompensation_AlignsWithTheDisplayGridAndPinsThePivot()
    {
        double[] compensation = BandCompensation(PinkSlope);
        List<SignalPoint> grid = DisplayGrid();

        // Same resampler, same parameters: the compensation must line up with the
        // displayed band curve index for index.
        Assert.Equal(grid.Count, compensation.Length);

        // Exactly zero at the grid point nearest the pivot, so switching the
        // compensation on rotates the curve instead of shifting its level.
        int pivot = NearestIndex(grid, NoiseTiltCompensation.PivotFrequency);
        Assert.Equal(0.0, compensation[pivot], precision: 12);
    }

    [Fact]
    public void BandCompensation_IsFlatForPinkWhereBandsAreConstantRelative()
    {
        // In the constant-relative-bandwidth region the band-power display renders
        // pink flat on its own — the compensation there must be (near) zero, not the
        // per-bin +3 dB/octave line. This is the assertion that distinguishes the
        // band-law compensation from naively reusing the per-bin straight line.
        double[] compensation = BandCompensation(PinkSlope);
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
        // A flat white PSD tilts +3.01 dB/octave on the band-power display (band
        // power grows with bandwidth), so its compensation must FALL by that per
        // octave in the constant-relative region — even though the PSD slope is zero.
        double[] compensation = BandCompensation(0.0);
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
        // Below the corner where the window main lobe is wider than the reference
        // band, the integrator switches to constant ABSOLUTE bandwidth and pink
        // renders rising toward LF again (+6 dB per two octaves) — the compensation
        // must mirror that, falling toward LF instead of staying flat.
        double[] compensation = BandCompensation(PinkSlope);
        List<SignalPoint> grid = DisplayGrid();

        double at40 = compensation[NearestIndex(grid, 40.0)];
        double at160 = compensation[NearestIndex(grid, 160.0)];
        Assert.True(
            at160 - at40 > 3.0,
            $"pink band compensation should fall toward LF below the resolution " +
            $"corner, got {at40:0.000} at 40 Hz vs {at160:0.000} at 160 Hz");
    }

    private static double[] BandCompensation(double slope) =>
        NoiseTiltCompensation.BandCompensationDb(
            slope,
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

    // The very grid the display's band resampler produces for the same parameters,
    // rendered from an arbitrary spectrum — only the frequencies matter here.
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
