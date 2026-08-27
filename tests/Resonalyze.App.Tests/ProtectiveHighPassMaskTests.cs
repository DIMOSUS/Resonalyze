using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
using Resonalyze.Dsp;
using Resonalyze.History;

namespace Resonalyze.App.Tests;

/// <summary>
/// Which responses stop where the protective high-pass stopped them, and which
/// carry on.
/// </summary>
/// <remarks>
/// Only a response the filter was REMOVED from goes quiet: a loopback transfer had
/// it divided out, and below the point that became unrecoverable the compensation
/// zeroed those bins. A sweep deconvolution still CARRIES the filter, so its low
/// end is signal the loudspeaker really produced and masking it would delete a
/// measurement rather than a phantom.
/// </remarks>
public sealed class ProtectiveHighPassMaskTests
{
    private const int SampleRate = 96_000;

    private static readonly ProtectiveHighPassConfiguration Tweeter =
        new(ProtectiveHighPassKind.Butterworth, 1_000, 48);

    private static double Limit =>
        ProtectiveHighPassCompensation.LowestRecoverableFrequencyHz(
            Tweeter.ToEdge(),
            SampleRate,
            ProtectiveHighPassConfiguration.MaximumCompensationBoostDb);

    [Fact]
    public void AnUnknownFilterMasksNothing()
    {
        // Null is "nobody recorded what this response passed through", which is a
        // different answer from Off. Breaking such a curve would put a boundary on
        // it from a filter it may never have seen.
        Assert.Equal(
            0.0,
            ProtectiveHighPassConfiguration.LowestMeasuredFrequencyHz(null, SampleRate));
    }

    [Fact]
    public void AnOffFilterMasksNothing() =>
        Assert.Equal(
            0.0,
            ProtectiveHighPassConfiguration.LowestMeasuredFrequencyHz(
                ProtectiveHighPassConfiguration.Off,
                SampleRate));

    [Fact]
    public void AnEnabledFilterStopsWhereTheCompensationDoes()
    {
        double limit = ProtectiveHighPassConfiguration.LowestMeasuredFrequencyHz(
            Tweeter, SampleRate);

        Assert.Equal(Limit, limit, 6);
        Assert.InRange(limit, 500.0, 600.0);
    }

    [Fact]
    public void ATransferPreviewStopsAtTheLimit()
    {
        MeasurementHistoryPreview preview = MeasurementHistoryPreviewBuilder.Build(
            Impulse(),
            sweepPeakIndex: 0,
            SampleRate,
            SweepMeasurementMode.LoopbackTransfer,
            CompensatedTransfer(),
            transferPeakIndex: TransferPeak(),
            Tweeter);

        // The thumbnail drops the broken bands, so it simply begins where the
        // measurement does.
        Assert.NotEmpty(preview.Frequencies);
        Assert.True(
            preview.Frequencies[0] >= Limit,
            $"the preview starts at {preview.Frequencies[0]:0.0} Hz, below {Limit:0.0} Hz");
    }

    [Fact]
    public void ASweepDeconvolutionPreviewKeepsItsLowEnd()
    {
        MeasurementHistoryPreview preview = MeasurementHistoryPreviewBuilder.Build(
            Impulse(),
            sweepPeakIndex: 0,
            SampleRate,
            SweepMeasurementMode.SweepDeconvolution,
            CompensatedTransfer(),
            transferPeakIndex: TransferPeak(),
            Tweeter);

        // Nothing divided the filter out of this one, so its rolloff is the
        // loudspeaker's own and belongs on screen.
        Assert.True(
            preview.Frequencies[0] < Limit,
            $"the preview starts at {preview.Frequencies[0]:0.0} Hz, at or above {Limit:0.0} Hz");
    }

    private static Complex[] Impulse()
    {
        var impulse = new Complex[32_768];
        impulse[0] = Complex.One;
        return impulse;
    }

    // A response measured through the filter and corrected for it, the same two
    // steps a measurement performs.
    private static Complex[] CompensatedTransfer()
    {
        var spectrum = new Complex[32_768];
        CrossoverSpec spec = new(CrossoverKind.HighPass, HighPassEdge: Tweeter.ToEdge());
        for (int bin = 0; bin < spectrum.Length; bin++)
        {
            int signedBin = bin <= spectrum.Length / 2 ? bin : bin - spectrum.Length;
            spectrum[bin] = CrossoverFilter.Response(
                spec, (double)signedBin * SampleRate / spectrum.Length, SampleRate);
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);
        return ProtectiveHighPassCompensation.RemoveFromImpulseResponse(
            spectrum,
            Tweeter.ToEdge(),
            SampleRate,
            ProtectiveHighPassConfiguration.MaximumCompensationBoostDb).ImpulseResponse;
    }

    private static int TransferPeak()
    {
        Complex[] transfer = CompensatedTransfer();
        int peak = 0;
        for (int i = 1; i < transfer.Length; i++)
        {
            if (transfer[i].Magnitude > transfer[peak].Magnitude)
            {
                peak = i;
            }
        }

        return peak;
    }
}
