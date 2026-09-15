using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
using Resonalyze.Dsp;
using Resonalyze.History;

namespace Resonalyze.App.Tests;

/// <summary>
/// Only a loopback transfer goes quiet outside the measured band; a sweep deconvolution carries the filter,
/// so its edges are real loudspeaker output.
/// </summary>
public sealed class MeasuredBandTests
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
        // Null (nobody recorded the filter) differs from Off and must not break the curve.
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
            MeasuredBand.Resolve(Tweeter, 0, 0, SampleRate));

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
            MeasuredBand.Resolve(Tweeter, 0, 0, SampleRate));

        Assert.True(
            preview.Frequencies[0] < Limit,
            $"the preview starts at {preview.Frequencies[0]:0.0} Hz, at or above {Limit:0.0} Hz");
    }

    [Fact]
    public void ASweepThatNeverReachedLowNarrowsTheBandOnItsOwn()
    {
        // Band sweep from 800 Hz: below 565 Hz the gate zeroed the bins, and a windowed zero drew 495 of 1024 points as rolloff.
        MeasuredBand band = MeasuredBand.Resolve(
            measurementFilter: null,
            measuredLowHz: 800,
            measuredHighHz: 20_000,
            SampleRate);

        Assert.Equal(800, band.LowEdgeHz, 6);
        Assert.Equal(20_000, band.HighEdgeHz, 6);
    }

    [Fact]
    public void TheGuardBandIsNotMeasured()
    {
        // The fade guard bands (half an octave each side) are down-weighted by validity (−13.0 dB at 400 Hz on 500-5000),
        // so the readable band is the full-amplitude one.
        MeasuredBand honest = MeasuredBand.Resolve(
            measurementFilter: null, measuredLowHz: 500, measuredHighHz: 5_000, SampleRate);
        Assert.Equal(500, honest.LowEdgeHz, 6);
        Assert.Equal(5_000, honest.HighEdgeHz, 6);
        Assert.False(honest.Contains(400));
        Assert.False(honest.Contains(6_300));
        Assert.True(honest.Contains(1_000));
    }

    [Fact]
    public void AFilterAndASweepBothNarrowIt_TheWiderLimitWins()
    {
        MeasuredBand sweptLower = MeasuredBand.Resolve(Tweeter, 200, 20_000, SampleRate);
        Assert.Equal(Limit, sweptLower.LowEdgeHz, 6);

        MeasuredBand sweptHigher = MeasuredBand.Resolve(Tweeter, 2_000, 20_000, SampleRate);
        Assert.Equal(2_000, sweptHigher.LowEdgeHz, 6);
    }

    [Fact]
    public void AMeasurementThatRecordedNoSweepBandIsJudgedOnItsFilterAlone()
    {
        MeasuredBand band = MeasuredBand.Resolve(Tweeter, 0, 0, SampleRate);

        Assert.Equal(Limit, band.LowEdgeHz, 6);
        Assert.True(double.IsPositiveInfinity(band.HighEdgeHz));
    }

    [Fact]
    public void ADefaultBandMeansEverything()
    {
        // A default band has a high edge of zero, which read literally would blank everything above DC.
        MeasuredBand band = default;

        Assert.Equal(0.0, band.LowEdgeHz);
        Assert.True(double.IsPositiveInfinity(band.HighEdgeHz));
        Assert.Equal(MeasuredBand.Everything.LowEdgeHz, band.LowEdgeHz);
        Assert.Equal(MeasuredBand.Everything.HighEdgeHz, band.HighEdgeHz);
    }

    [Fact]
    public void ANothingnessAtEitherEndIsRefusedRatherThanTrusted()
    {
        MeasuredBand reversed = MeasuredBand.Resolve(null, 20_000, 20, SampleRate);
        Assert.Equal(0.0, reversed.LowEdgeHz);
        Assert.True(double.IsPositiveInfinity(reversed.HighEdgeHz));

        MeasuredBand absent = MeasuredBand.Resolve(null, 0, 0, SampleRate);
        Assert.Equal(0.0, absent.LowEdgeHz);
        Assert.True(double.IsPositiveInfinity(absent.HighEdgeHz));
    }

    [Fact]
    public void ATransferPreviewAlsoStopsWhereTheSweepDid()
    {
        MeasurementHistoryPreview preview = MeasurementHistoryPreviewBuilder.Build(
            Impulse(),
            sweepPeakIndex: 0,
            SampleRate,
            SweepMeasurementMode.LoopbackTransfer,
            CompensatedTransfer(),
            transferPeakIndex: TransferPeak(),
            MeasuredBand.Resolve(null, 2_000, 20_000, SampleRate));

        Assert.NotEmpty(preview.Frequencies);
        Assert.True(
            preview.Frequencies[0] >= 2_000,
            $"the preview starts at {preview.Frequencies[0]:0.0} Hz, below 2000 Hz");
        Assert.True(
            preview.Frequencies[^1] <= 20_000,
            $"the preview ends at {preview.Frequencies[^1]:0.0} Hz, above 20000 Hz");
    }

    private static Complex[] Impulse()
    {
        var impulse = new Complex[32_768];
        impulse[0] = Complex.One;
        return impulse;
    }

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
