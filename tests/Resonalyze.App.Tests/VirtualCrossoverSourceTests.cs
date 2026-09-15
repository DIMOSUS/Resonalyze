using System.Numerics;
using Resonalyze.History;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverSourceTests
{
    private static MeasurementHistorySnapshot Snapshot(
        Complex[]? transferIr,
        int? transferPeak = null,
        int sampleRate = 48_000,
        double[]? coherence = null,
        TimingReference timingReference = TimingReference.SynchronizedLoopback) =>
        new()
        {
            SampleRate = sampleRate,
            TimingReference = timingReference,
            TransferImpulseResponse = transferIr,
            TransferPeakIndex = transferPeak,
            TransferCoherence = coherence,
            SweepDeconvolutionImpulseResponse = [Complex.One],
            MeterSnapshot = InputLevelMeterSnapshot.Empty,
            Preview = new MeasurementHistoryPreview()
        };

    [Fact]
    public void FromSnapshot_ReturnsNull_WhenThereIsNoTransferIr()
    {
        Assert.Null(ResolvedVirtualDspSource.FromSnapshot(Snapshot(null)));
        Assert.Null(ResolvedVirtualDspSource.FromSnapshot(Snapshot([])));
    }

    // An imported recording's arrival is set by when the recorder started, so it cannot be summed.
    [Fact]
    public void FromSnapshot_ReturnsNull_ForAnImportedRecording()
    {
        Complex[] transferIr = [Complex.One, Complex.Zero];

        Assert.Null(ResolvedVirtualDspSource.FromSnapshot(
            Snapshot(transferIr, timingReference: TimingReference.RecordedSweep)));
        Assert.NotNull(ResolvedVirtualDspSource.FromSnapshot(
            Snapshot(transferIr, timingReference: TimingReference.SynchronizedLoopback)));
    }

    [Theory]
    [InlineData(10, 3)]
    [InlineData(-5, 0)]
    [InlineData(2, 2)]
    public void FromSnapshot_ClampsTransferPeakIndexIntoTheIr(int rawPeak, int expected)
    {
        Complex[] ir = [Complex.One, Complex.Zero, Complex.Zero, Complex.Zero];

        ResolvedVirtualDspSource? resolved =
            ResolvedVirtualDspSource.FromSnapshot(Snapshot(ir, rawPeak));

        Assert.NotNull(resolved);
        Assert.Equal(expected, resolved.TransferPeakIndex);
    }

    [Fact]
    public void FromSnapshot_DefaultsPeakToZero_AndCarriesRateAndCoherence()
    {
        Complex[] ir = [Complex.One, Complex.Zero];
        double[] coherence = [1.0, 0.5];

        ResolvedVirtualDspSource? resolved = ResolvedVirtualDspSource.FromSnapshot(
            Snapshot(ir, transferPeak: null, sampleRate: 44_100, coherence: coherence));

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
            ResolvedVirtualDspSource.FromSnapshot(Snapshot(ir, transferPeak: 1))!;
        var state = new VirtualCrossoverChannelState();

        resolved.ApplyTo(state);

        Assert.Same(ir, state.TransferImpulseResponse);
        Assert.Equal(1, state.TransferPeakIndex);
        Assert.Equal(48_000, state.SampleRate);
        Assert.NotNull(state.ProcessingSource);
    }
}
