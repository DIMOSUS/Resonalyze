using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The linear-phase crossover designer behind the FIR Constructor. What a user relies
/// on is checked on the kernels themselves: that they are linear-phase (symmetric,
/// odd, a constant group delay of half their length), that a matched low/high pair
/// sums back to a pure delay, and that the IIR-magnitude method actually lands on the
/// slope it names once the kernel is long enough.
/// </summary>
public sealed class FirCrossoverDesignTests
{
    private const int Rate = 48_000;

    private static FirCrossoverDesign Design(
        CrossoverKind kind = CrossoverKind.LowPass,
        double lowPassHz = 2_000,
        double highPassHz = 2_000,
        FirCrossoverMethod method = FirCrossoverMethod.IirMagnitude,
        FirWindow window = FirWindow.Kaiser,
        double beta = 8,
        int taps = 1_023,
        int rate = Rate,
        CrossoverFilterFamily family = CrossoverFilterFamily.LinkwitzRiley,
        int slope = 24) =>
        new(
            kind,
            new CrossoverEdge(family, lowPassHz, slope),
            new CrossoverEdge(family, highPassHz, slope),
            method,
            window,
            beta,
            taps,
            rate);

    [Theory]
    [InlineData(FirCrossoverMethod.IirMagnitude, FirWindow.Kaiser)]
    [InlineData(FirCrossoverMethod.IirMagnitude, FirWindow.Rectangular)]
    [InlineData(FirCrossoverMethod.WindowedSinc, FirWindow.Blackman)]
    [InlineData(FirCrossoverMethod.WindowedSinc, FirWindow.Hann)]
    public void AMatchedLowAndHighPass_SumToAPureDelay(FirCrossoverMethod method, FirWindow window)
    {
        // The reason to build a crossover linear-phase at all: the two branches meet
        // with no phase turn, so their sum is the input, late by the kernel's centre.
        // Exact whatever the length — the window is 1 at the centre and multiplies
        // ideal responses that add to a unit impulse.
        FirFilter low = Design(CrossoverKind.LowPass, method: method, window: window, taps: 255).Build();
        FirFilter high = Design(CrossoverKind.HighPass, method: method, window: window, taps: 255).Build();

        for (int i = 0; i < low.Length; i++)
        {
            double expected = i == 127 ? 1.0 : 0.0;
            Assert.Equal(expected, low.Taps[i] + high.Taps[i], 1e-9);
        }
    }

    [Theory]
    [InlineData(CrossoverKind.LowPass, FirCrossoverMethod.IirMagnitude)]
    [InlineData(CrossoverKind.HighPass, FirCrossoverMethod.IirMagnitude)]
    [InlineData(CrossoverKind.BandPass, FirCrossoverMethod.IirMagnitude)]
    [InlineData(CrossoverKind.BandPass, FirCrossoverMethod.WindowedSinc)]
    public void EveryKernel_IsLinearPhase_WithItsDelayAtTheCentre(
        CrossoverKind kind, FirCrossoverMethod method)
    {
        FirCrossoverDesign design = Design(kind, lowPassHz: 3_000, highPassHz: 300, method: method, taps: 2_047);
        FirFilter kernel = design.Build();

        Assert.Equal(2_047, kernel.Length);
        Assert.Equal(1_023, design.LatencySamples);
        Assert.Equal(1_023 * 1_000.0 / Rate, design.LatencyMs, 12);
        Assert.Equal(Rate, kernel.DeclaredSampleRateHz);
        for (int i = 0; i < kernel.Length; i++)
        {
            Assert.Equal(kernel.Taps[i], kernel.Taps[kernel.Length - 1 - i], 12);
        }

        // A constant group delay of exactly the centre, in the passband and out of it.
        foreach (double frequency in new[] { 100.0, 1_000.0, 5_000.0 })
        {
            Complex z1 = Complex.FromPolarCoordinates(1, -Math.Tau * frequency / Rate);
            Assert.Equal(1_023, kernel.GroupDelaySamples(z1), 6);
        }
    }

    [Fact]
    public void TheIirMagnitudeMethod_LandsOnTheSlopeItNames_GivenTheLength()
    {
        // LR24 at 2 kHz: −6 dB at the corner, and the kernel within a tenth of a dB of
        // the IIR magnitude everywhere that magnitude is above −30 dB.
        FirCrossoverDesign design = Design(taps: 4_095);
        FirFilter kernel = design.Build();

        double atCorner = 20 * Math.Log10(kernel.Response(2_000, Rate).Magnitude);
        Assert.Equal(-6.02, atCorner, 1);
        Assert.Equal(1.0, kernel.Response(20, Rate).Magnitude, 3);
        Assert.True(design.WorstDeviationDb(kernel) < 0.1);

        // And the readout does its job: the same slope a decade lower cannot be
        // resolved by a short kernel, and the deviation says so.
        FirCrossoverDesign tooShort = Design(lowPassHz: 100, taps: 63);
        Assert.True(tooShort.WorstDeviationDb(tooShort.Build()) > 3);
    }

    [Fact]
    public void AHighPass_PassesNyquist_AndBlocksDc()
    {
        FirFilter kernel = Design(CrossoverKind.HighPass, highPassHz: 500, taps: 4_095).Build();

        Assert.Equal(1.0, kernel.Response(Rate / 2.0, Rate).Magnitude, 3);
        Assert.True(kernel.Response(0, Rate).Magnitude < 1e-3);
    }

    [Fact]
    public void TheWindowedSinc_IsHalfwayAtItsCorner()
    {
        FirFilter kernel = Design(method: FirCrossoverMethod.WindowedSinc, window: FirWindow.Blackman, taps: 4_095)
            .Build();

        Assert.Equal(0.5, kernel.Response(2_000, Rate).Magnitude, 2);
        Assert.True(kernel.Response(3_000, Rate).Magnitude < 1e-3);
        Assert.False(Design(method: FirCrossoverMethod.WindowedSinc).HasTargetMagnitude);
        Assert.True(double.IsNaN(Design(method: FirCrossoverMethod.WindowedSinc)
            .WorstDeviationDb(kernel)));
    }

    [Fact]
    public void TheLongestKernel_Builds()
    {
        // A 40 Hz corner at the tap ceiling: the lowest crossover a car system asks
        // for, resolved to within a quarter of a dB of its LR24 shape.
        FirCrossoverDesign design = Design(lowPassHz: 40, taps: FirCrossoverDesign.MaximumTapCount);
        FirFilter kernel = design.Build();

        Assert.Equal(FirCrossoverDesign.MaximumTapCount, kernel.Length);
        Assert.True(design.WorstDeviationDb(kernel) < 0.25, design.WorstDeviationDb(kernel).ToString());
    }

    [Theory]
    [InlineData(CrossoverFilterFamily.LinkwitzRiley)]
    [InlineData(CrossoverFilterFamily.Butterworth)]
    [InlineData(CrossoverFilterFamily.Bessel)]
    public void TheClosedFormMagnitude_IsTheCrossoversOwn_AtEverySlopeTheHardwareHas(
        CrossoverFilterFamily family)
    {
        // The constructor reads Linkwitz-Riley and Butterworth magnitudes in closed
        // form so it can go past the section lists; at every slope a section list
        // does carry, the two must be the same numbers.
        foreach (int slope in CrossoverFilter.SupportedSlopes(family))
        {
            foreach (CrossoverKind kind in new[] { CrossoverKind.LowPass, CrossoverKind.HighPass })
            {
                FirCrossoverDesign design = Design(kind, lowPassHz: 1_500, highPassHz: 1_500, family: family, slope: slope);
                var edge = new CrossoverEdge(family, 1_500, slope);
                var spec = kind == CrossoverKind.LowPass
                    ? new CrossoverSpec(kind, LowPassEdge: edge)
                    : new CrossoverSpec(kind, HighPassEdge: edge);
                foreach (double frequency in new[] { 0.0, 20.0, 700.0, 1_500.0, 3_100.0, 15_000.0, 24_000.0 })
                {
                    double expected = CrossoverFilter.Response(spec, frequency, Rate).Magnitude;
                    Assert.Equal(expected, design.TargetMagnitude(frequency), 1e-9);
                }
            }
        }
    }

    [Theory]
    [InlineData(CrossoverFilterFamily.LinkwitzRiley, 96)]
    [InlineData(CrossoverFilterFamily.Butterworth, 96)]
    [InlineData(CrossoverFilterFamily.Butterworth, 66)]
    public void SlopesPastTheHardwareList_AreDesigned_AndSteep(CrossoverFilterFamily family, int slope)
    {
        FirCrossoverDesign low = Design(CrossoverKind.LowPass, family: family, slope: slope, taps: 4_095);
        FirFilter kernel = low.Build();

        // −3 dB (Butterworth) or −6 dB (Linkwitz-Riley) at the corner, and the kernel
        // follows the slope to within half a dB wherever it stands above −30 dB.
        double atCorner = 20 * Math.Log10(kernel.Response(2_000, Rate).Magnitude);
        Assert.Equal(family == CrossoverFilterFamily.LinkwitzRiley ? -6.02 : -3.01, atCorner, 1);
        Assert.True(low.WorstDeviationDb(kernel) < 0.5, low.WorstDeviationDb(kernel).ToString());
        // An octave up, the target sits at least the slope's own figure down — a few dB
        // more at 4 kHz, where the bilinear transform the crossover is built with
        // compresses the octave (measured 2.8 dB at 48 kHz).
        double octaveUpDb = 20 * Math.Log10(low.TargetMagnitude(4_000));
        Assert.InRange(octaveUpDb, -slope - 4, -slope);
    }

    [Fact]
    public void ALinkwitzRileyPair_SumsToADelay_AtTheSteepestSlopeToo()
    {
        FirFilter low = Design(CrossoverKind.LowPass, slope: 96, taps: 1_023).Build();
        FirFilter high = Design(CrossoverKind.HighPass, slope: 96, taps: 1_023).Build();

        for (int i = 0; i < low.Length; i++)
        {
            Assert.Equal(i == 511 ? 1.0 : 0.0, low.Taps[i] + high.Taps[i], 1e-9);
        }
    }

    [Fact]
    public void ADesignThatCannotBeBuilt_SaysWhy()
    {
        Assert.Null(Design().Problem());
        Assert.Contains("odd", Design(taps: 1_024).Problem());
        Assert.NotNull(Design(taps: 1).Problem());
        Assert.NotNull(Design(taps: FirCrossoverDesign.MaximumTapCount + 2).Problem());
        Assert.NotNull(Design(CrossoverKind.Off).Problem());
        Assert.NotNull(Design(lowPassHz: 24_000).Problem());
        Assert.NotNull(Design(CrossoverKind.BandPass, lowPassHz: 200, highPassHz: 2_000).Problem());
        Assert.NotNull(Design(family: CrossoverFilterFamily.Chebyshev).Problem());
        Assert.NotNull(Design(family: CrossoverFilterFamily.LinkwitzRiley, slope: 18).Problem());
        Assert.NotNull(Design(family: CrossoverFilterFamily.LinkwitzRiley, slope: 108).Problem());
        // Bessel's prototype table ends at 48 dB/oct.
        Assert.NotNull(Design(family: CrossoverFilterFamily.Bessel, slope: 96).Problem());
        Assert.Null(Design(family: CrossoverFilterFamily.Butterworth, slope: 90).Problem());
        Assert.NotNull(Design(beta: 25).Problem());
        Assert.NotNull(Design(rate: 0).Problem());

        // The windowed sinc reads only the corner: a family or slope it never uses
        // cannot make the design unbuildable.
        Assert.Null(Design(method: FirCrossoverMethod.WindowedSinc, slope: 18).Problem());
        Assert.Throws<ArgumentException>(() => Design(taps: 1_024).Build());
    }
}
