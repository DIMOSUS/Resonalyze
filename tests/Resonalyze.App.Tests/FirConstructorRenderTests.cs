using OxyPlot;
using Resonalyze.Dsp;
using static Resonalyze.App.Tests.FirConstructorSessionTests;

namespace Resonalyze.App.Tests;

public sealed class FirConstructorRenderTests
{
    [Fact]
    public void ADesign_IsDrawnAgainstItsTarget_WithTheLatencyTakenOutOfThePhase()
    {
        FirCrossoverDesign design = LowPass(taps: 1_023);
        FirFilter kernel = design.Build();

        FirConstructorRendering rendering = FirConstructorRender.Draw(kernel, design, 48_000, CancellationToken.None);

        Assert.Same(kernel, rendering.Kernel);
        Assert.Equal(801, rendering.Magnitude.Length);
        Assert.Equal(801, rendering.Target.Length);
        Assert.Equal(20, rendering.Magnitude[0].X, 9);
        Assert.Equal(20_000, rendering.Magnitude[^1].X, 6);
        Assert.Equal(design.WorstDeviationDb(kernel), rendering.DeviationDb);
        // Passband phase is flat once the (N-1)/2 delay is removed.
        Assert.All(rendering.Phase.Where(point => point.X < 500), point => Assert.True(Math.Abs(point.Y) < 1e-6, $"{point}"));
        // The stop band far below the peak hides its phase.
        Assert.Contains(rendering.Phase, point => double.IsNaN(point.Y));
    }

    [Fact]
    public void AnEvenSymmetricKernel_ReadsFlatPhase_AndABareKernelHasNoTargetNorDeviation()
    {
        var even = new FirFilter([0.1, 0.4, 0.4, 0.1], 48_000);

        FirConstructorRendering rendering = FirConstructorRender.Draw(even, null, 48_000, CancellationToken.None);

        Assert.Empty(rendering.Target);
        Assert.True(double.IsNaN(rendering.DeviationDb));
        Assert.All(
            rendering.Phase.Where(point => !double.IsNaN(point.Y)),
            point => Assert.True(Math.Abs(point.Y) < 1e-6 || Math.Abs(Math.Abs(point.Y) - 180) < 1e-6, $"{point}"));
    }

    [Fact]
    public void TheImpulse_RunsFromThePeak_AndItsDecibelsStopAt120BelowIt()
    {
        var kernel = new FirFilter([0.0, 0.5, 1.0, -0.25], 1_000);

        FirConstructorRendering rendering = FirConstructorRender.Draw(kernel, null, 1_000, CancellationToken.None);

        Assert.Equal([new DataPoint(-2, 0), new DataPoint(-1, 0.5), new DataPoint(0, 1), new DataPoint(1, -0.25)], rendering.Impulse);
        Assert.Equal(-120, rendering.ImpulseDb[0].Y);
        Assert.Equal(0, rendering.ImpulseDb[2].Y);
        Assert.Equal(20 * Math.Log10(0.25), rendering.ImpulseDb[3].Y, 12);
    }

    [Fact]
    public void AtALowRate_TheResponseStopsAtNyquist()
    {
        FirConstructorRendering rendering =
            FirConstructorRender.Draw(new FirFilter([1.0]), null, 16_000, CancellationToken.None);

        Assert.Equal(8_000, rendering.Magnitude[^1].X, 6);
    }

    [Fact]
    public void ARebuild_BuildsItsDesign_OrShowsItsBareKernel()
    {
        var session = new FirConstructorSession();
        using FirConstructorRebuild designed = session.Edit(LowPass(taps: 255))!;
        Assert.Equal(255, FirConstructorRender.Run(designed, CancellationToken.None).Kernel.Length);

        var bare = new FirFilter([0.25, 0.5, 0.25]);
        using FirConstructorRebuild shown = session.ShowBare(bare, null, 48_000);
        Assert.Same(bare, FirConstructorRender.Run(shown, CancellationToken.None).Kernel);
    }

    [Fact]
    public void ACancelledRebuild_StopsDrawing()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            FirConstructorRender.Draw(new FirFilter([1.0]), null, 48_000, cancelled.Token));
    }
}
