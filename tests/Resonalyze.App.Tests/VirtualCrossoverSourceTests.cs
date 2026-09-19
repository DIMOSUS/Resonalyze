using System.Numerics;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverSourceTests
{
    private static MeasurementResult Result(
        Complex[]? transferIr,
        int? transferPeak = null,
        int sampleRate = 48_000,
        double[]? coherence = null,
        TimingReference timingReference = TimingReference.SynchronizedLoopback) =>
        new()
        {
            SampleRate = sampleRate,
            Bits = 24,
            TimingReference = timingReference,
            Transfer = transferIr == null
                ? null
                : new MeasurementImpulseResponse(transferIr, transferPeak ?? 0),
            TransferCoherence = coherence,
            SweepDeconvolution = new MeasurementImpulseResponse([Complex.One], 0)
        };

    [Fact]
    public void FromResult_ReturnsNull_WhenThereIsNoTransferIr()
    {
        Assert.Null(ResolvedVirtualDspSource.FromResult(Result(null)));
        Assert.Null(ResolvedVirtualDspSource.FromResult(Result([])));
    }

    // An imported recording's arrival is set by when the recorder started, so it cannot be summed.
    [Fact]
    public void FromResult_ReturnsNull_ForAnImportedRecording()
    {
        Complex[] transferIr = [Complex.One, Complex.Zero];

        Assert.Null(ResolvedVirtualDspSource.FromResult(
            Result(transferIr, timingReference: TimingReference.RecordedSweep)));
        Assert.NotNull(ResolvedVirtualDspSource.FromResult(
            Result(transferIr, timingReference: TimingReference.SynchronizedLoopback)));
    }

    [Theory]
    [InlineData(10, 3)]
    [InlineData(-5, 0)]
    [InlineData(2, 2)]
    public void FromResult_ClampsTransferPeakIndexIntoTheIr(int rawPeak, int expected)
    {
        Complex[] ir = [Complex.One, Complex.Zero, Complex.Zero, Complex.Zero];

        ResolvedVirtualDspSource? resolved =
            ResolvedVirtualDspSource.FromResult(Result(ir, rawPeak));

        Assert.NotNull(resolved);
        Assert.Equal(expected, resolved.TransferPeakIndex);
    }

    [Fact]
    public void FromResult_DefaultsPeakToZero_AndCarriesRateAndCoherence()
    {
        Complex[] ir = [Complex.One, Complex.Zero];
        double[] coherence = [1.0, 0.5];

        ResolvedVirtualDspSource? resolved = ResolvedVirtualDspSource.FromResult(
            Result(ir, transferPeak: null, sampleRate: 44_100, coherence: coherence));

        Assert.NotNull(resolved);
        Assert.Equal(0, resolved.TransferPeakIndex);
        Assert.Equal(44_100, resolved.SampleRate);
        Assert.Same(coherence, resolved.TransferCoherence);
        Assert.Null(resolved.DistortionCurve);
    }

    [Fact]
    public void ApplyTo_WritesTheMeasurementIntoTheSideStateAndArmsProcessingSource()
    {
        Complex[] ir = [Complex.One, Complex.Zero, Complex.Zero];
        ResolvedVirtualDspSource resolved =
            ResolvedVirtualDspSource.FromResult(Result(ir, transferPeak: 1))!;
        var state = new VirtualCrossoverChannelState();

        resolved.ApplyTo(state);

        Assert.Same(ir, state.TransferImpulseResponse);
        Assert.Equal(1, state.TransferPeakIndex);
        Assert.Equal(48_000, state.SampleRate);
        Assert.NotNull(state.ProcessingSource);
    }
}
