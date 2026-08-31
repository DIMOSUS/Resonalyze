namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The circular-record contract of <see cref="TimeAlignmentAnalysis.Analyze"/>:
/// a COMPLETE deconvolved record (<c>WrapPeakPositions</c>) is circular by
/// construction — its tail is continuous with its head — so its transforms run
/// unpadded, at the record's own length, where circular convolution is exact.
/// Zero padding such a record manufactures a seam where the tail no longer
/// meets the head, and the Hilbert envelope's edge transient at that seam
/// reads as structure.
/// </summary>
/// <remarks>
/// The field case (v5_exp subwoofer): a transfer IR carrying a DC shelf
/// (out-of-band deconvolution residue, −17 dB under the peak). Circularly the
/// shelf is a featureless constant the first-arrival search walks straight
/// past — the read is the 13.28 ms front. Padded, the envelope dipped 9 dB at
/// the record start and climbed back over the first millisecond, and the
/// search rightly accepted that climb as a front: 0.2 ms, before the driver
/// made a sound. The same padding turned the mid channel's smooth wrapped
/// skirt into a 25 dB step at the record seam on the panel's envelope view.
/// </remarks>
public sealed class TimeAlignmentCircularRecordTests
{
    private const int SampleRate = 96_000;
    private const int Length = 65_536;

    // A sub-like complete record: a DC shelf across the whole circular
    // buffer (the field record's out-of-band residue), a band-limited front
    // at 13 ms, measurement noise, and a distortion-products block near the
    // record end — where sweep deconvolution parks it.
    private static double[] CompleteRecord()
    {
        var record = new double[Length];
        int front = (int)(0.013 * SampleRate);
        var noise = new Random(20260831);
        for (int i = 0; i < Length; i++)
        {
            record[i] = -0.14 + 0.004 * (noise.NextDouble() * 2 - 1);
        }
        for (int i = front; i < Length; i++)
        {
            double t = (i - front) / (double)SampleRate;
            record[i] += (1 - Math.Exp(-t / 0.004)) * Math.Exp(-t / 0.08) *
                Math.Sin(2 * Math.PI * 60.0 * t);
        }
        int harmonics = Length - 2_000;
        for (int i = harmonics; i < Length; i++)
        {
            double t = (i - harmonics) / (double)SampleRate;
            record[i] += 0.1 * Math.Exp(-t / 0.004) *
                Math.Sin(2 * Math.PI * 200.0 * t);
        }
        return record;
    }

    // The falsifier: the envelope of a complete record must BE the record's
    // own circular envelope — the same samples the public primitive returns,
    // whose own contract (a bin-centred cosine comes back flat) is what
    // defines circular. The padded path differs by tens of percent near the
    // seam, so any padding creeping back into this branch turns this red.
    [Fact]
    public void CompleteRecord_EnvelopeIsTheCircularEnvelope()
    {
        double[] record = CompleteRecord();

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            record,
            SampleRate,
            new TimeAlignmentAnalysisOptions { WrapPeakPositions = true });

        double[] circular = SignalEnvelope.Envelope(record);
        Assert.Equal(circular.Length, result.EnvelopeSamples.Length);
        for (int i = 0; i < circular.Length; i++)
        {
            Assert.Equal(circular[i], result.EnvelopeSamples[i], precision: 12);
        }
    }

    // And the banded read of the same record: filtered at the record's own
    // length — exact for a circular signal — never on a padded copy.
    [Fact]
    public void CompleteRecord_BandedEnvelopeIsTheCircularOne()
    {
        double[] record = CompleteRecord();
        var options = new TimeAlignmentAnalysisOptions
        {
            WrapPeakPositions = true,
            UseBandpassWindow = true,
            BandpassCenterHz = 80,
            BandpassPassOctaves = 2,
            BandpassFadeOctaves = 0.5
        };

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            record, SampleRate, options);

        double[] window = BandpassWindow.Create(
            Length,
            SampleRate,
            options.BandpassCenterHz,
            options.BandpassPassOctaves,
            options.BandpassFadeOctaves);
        double[] circular = SignalEnvelope.Envelope(
            BandpassWindow.Apply(record, window));
        for (int i = 0; i < circular.Length; i++)
        {
            Assert.Equal(circular[i], result.EnvelopeSamples[i], precision: 12);
        }
    }

    // The outcome the contract exists for, stated on the fixture: nothing
    // plays before 13 ms, so nothing before it may be called an arrival. The
    // field verification is the v5_exp subwoofer itself, whose full-band read
    // moved from 0.198 ms (padded) to 13.277 ms with the circular contract.
    [Fact]
    public void CompleteRecord_DcShelfIsNotReadAsAnArrival()
    {
        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            CompleteRecord(),
            SampleRate,
            new TimeAlignmentAnalysisOptions { WrapPeakPositions = true });

        Assert.True(result.IsValid);
        Assert.True(
            result.FirstArrivalDelayMilliseconds > 10.0,
            $"the search read {result.FirstArrivalDelayMilliseconds:0.000} ms — " +
            "ahead of everything the driver played");
    }
}
