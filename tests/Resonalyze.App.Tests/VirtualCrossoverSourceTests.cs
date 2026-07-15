using System.Numerics;
using Resonalyze.History;

namespace Resonalyze.App.Tests;

/// <summary>
/// Characterization tests for the shared Virtual DSP source pipeline
/// (<see cref="ResolvedVirtualDspSource"/>): the loopback-transfer-IR requirement,
/// the transfer-peak clamp and the write into a channel side's runtime state —
/// the logic the file, history and restore paths used to each hand-roll.
/// </summary>
public sealed class VirtualCrossoverSourceTests
{
    private static MeasurementHistorySnapshot Snapshot(
        Complex[]? transferIr,
        int? transferPeak = null,
        int sampleRate = 48_000,
        double[]? coherence = null) =>
        new()
        {
            SampleRate = sampleRate,
            TransferImpulseResponse = transferIr,
            TransferPeakIndex = transferPeak,
            TransferCoherence = coherence,
            // Present but with no sweep metadata (Octaves defaults to 0), so the
            // distortion curve resolves to null.
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

    [Theory]
    [InlineData(10, 3)]   // beyond the end → last sample
    [InlineData(-5, 0)]   // before the start → first sample
    [InlineData(2, 2)]    // in range → unchanged
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
        // No sweep metadata on the snapshot, so no distortion curve.
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
        // Writing the IR arms the write-once processing snapshot the coordinator reads.
        Assert.NotNull(state.ProcessingSource);
    }
}
