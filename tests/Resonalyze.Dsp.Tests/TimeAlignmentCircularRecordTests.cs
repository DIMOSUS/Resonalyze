namespace Resonalyze.Dsp.Tests;

/// <summary>A complete deconvolved record is circular, so <see cref="TimeAlignmentAnalysis.Analyze"/> transforms it unpadded;
/// padding makes a seam whose envelope transient read as a front (field sub: 0.198 ms instead of 13.277 ms).</summary>
public sealed class TimeAlignmentCircularRecordTests
{
    private const int SampleRate = 96_000;
    private const int Length = 65_536;

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

    // Padding moves envelope samples by tens of percent near the seam.
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
        // One transform round trip fewer: agreement to 16 digits, not always 17.
        for (int i = 0; i < circular.Length; i++)
        {
            Assert.Equal(circular[i], result.EnvelopeSamples[i], tolerance: 1e-12);
        }
    }

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
