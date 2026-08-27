using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
using Resonalyze.Dsp;
using Resonalyze.History;

namespace Resonalyze.App.Tests;

/// <summary>
/// What a measurement carries a measurement at, and which responses stop there.
/// </summary>
/// <remarks>
/// Two things narrow it: a protective high-pass the compensation could not invert,
/// and a sweep that never excited part of the range. Both leave the response exactly
/// zero, and only a response those zeroes are IN goes quiet — a loopback transfer.
/// A sweep deconvolution still CARRIES the filter and is normalized by the
/// excitation rather than gated against a loopback, so its edges are signal the
/// loudspeaker really produced and masking them would delete a measurement rather
/// than a phantom.
/// </remarks>
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
            MeasuredBand.Resolve(Tweeter, 0, 0, SampleRate));

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
            MeasuredBand.Resolve(Tweeter, 0, 0, SampleRate));

        // Nothing divided the filter out of this one, so its rolloff is the
        // loudspeaker's own and belongs on screen.
        Assert.True(
            preview.Frequencies[0] < Limit,
            $"the preview starts at {preview.Frequencies[0]:0.0} Hz, at or above {Limit:0.0} Hz");
    }

    [Fact]
    public void ASweepThatNeverReachedLowNarrowsTheBandOnItsOwn()
    {
        // The owner's tweeters: a band sweep from 800 Hz that actually swept from
        // 565, with no protective high-pass anywhere. Below that nothing was played
        // at all, the excitation gate zeroed those bins, and a windowed spectrum of
        // a zero is the window — 495 of 1024 points drawn as a rolloff from -60 dB
        // down to -96, none of it measured.
        MeasuredBand band = MeasuredBand.Resolve(
            measurementFilter: null,
            achievedLowHz: 565,
            achievedHighHz: 28_299,
            SampleRate);

        Assert.Equal(565, band.LowEdgeHz, 6);
        Assert.Equal(28_299, band.HighEdgeHz, 6);
    }

    [Fact]
    public void AFilterAndASweepBothNarrowIt_TheWiderLimitWins()
    {
        // Both are true at once, so the response is silent wherever EITHER says so.
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
        // The trap this exists to close: a default-constructed band carries a HIGH
        // edge of zero, and read literally that would blank every frequency above DC
        // on any measurement whose band nobody set.
        MeasuredBand band = default;

        Assert.Equal(0.0, band.LowEdgeHz);
        Assert.True(double.IsPositiveInfinity(band.HighEdgeHz));
        Assert.Equal(MeasuredBand.Everything.LowEdgeHz, band.LowEdgeHz);
        Assert.Equal(MeasuredBand.Everything.HighEdgeHz, band.HighEdgeHz);
    }

    [Fact]
    public void ANothingnessAtEitherEndIsRefusedRatherThanTrusted()
    {
        // A reversed or absent sweep band says nothing about the measurement, so it
        // narrows nothing — the alternative is blanking a curve on a bad number.
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
