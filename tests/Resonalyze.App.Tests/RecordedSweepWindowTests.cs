using Resonalyze.Audio;

namespace Resonalyze.App.Tests;

public sealed class RecordedSweepWindowTests : IDisposable
{
    private const int SampleRate = 48_000;

    private readonly ExponentialSineSweep sweep = new();

    public RecordedSweepWindowTests() => sweep.FillData(20, 20_000, 2.0, 24, SampleRate);

    public void Dispose() => sweep.Dispose();

    private float[] Sweep => sweep.SweepData;

    private int SweepSamples => sweep.SweepSamples;

    // Sweep plus 0.5 s lead-in and 2 s tail.
    private int Bound => SweepSamples + (int)(2.5 * SampleRate);

    private float[] Recording(
        int leadSilence,
        int trailingSilence,
        float sweepGain = 1.0f,
        float noise = 0.0f,
        int seed = 4242)
    {
        var samples = new float[leadSilence + SweepSamples + trailingSilence];
        var random = new Random(seed);
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)((random.NextDouble() - 0.5) * 2 * noise);
        }
        for (int i = 0; i < SweepSamples; i++)
        {
            samples[leadSilence + i] += Sweep[i] * sweepGain;
        }

        return samples;
    }

    private IReadOnlyList<RecordedSweepSpan> Locate(float[] samples) =>
        RecordedSweepWindow.LocateCandidates(samples, Sweep, SampleRate);

    [Fact]
    public void AShortRecordingStillReportsWhereTheExcitationBegins()
    {
        int preRoll = SampleRate / 2;
        float[] full = Recording(preRoll, 0, noise: 0.0005f);
        float[] samples = full[..(preRoll + (int)(SweepSamples * 0.85))];

        RecordedSweepSpan span = Locate(samples)[0];

        Assert.Equal(0, span.Start);
        Assert.Equal(samples.Length, span.Length);
        Assert.InRange(span.ExcitationStart, preRoll - 8, preRoll + 8);
        Assert.True(span.ExcitationLength < SweepSamples);
    }

    // Every match is offered so the caller can fall through to the complete take; each span excludes the other attempt.
    [Fact]
    public void AShortRecordingOffersEveryTakeItHolds()
    {
        int second = SweepSamples + SampleRate / 4;
        float[] samples = Recording(0, 2 * SampleRate, sweepGain: 0.2f, noise: 0.0005f);
        for (int i = 0; i < samples.Length - second; i++)
        {
            samples[second + i] += Sweep[i];
        }

        IReadOnlyList<RecordedSweepSpan> spans = Locate(samples);

        Assert.Contains(spans, span => span.ExcitationStart == 0);
        Assert.Contains(spans, span => span.ExcitationStart == second);
        RecordedSweepSpan complete = spans.First(span => span.ExcitationStart == 0);
        Assert.True(complete.ExcitationLength >= SweepSamples);
        Assert.Equal(0, complete.Start);
        Assert.Equal(second, complete.Start + complete.Length);
        RecordedSweepSpan truncated = spans.First(span => span.ExcitationStart == second);
        Assert.True(truncated.ExcitationLength < SweepSamples);
        Assert.Equal(SweepSamples, truncated.Start);
    }

    [Fact]
    public void ShortRecordingsAreAnalyzedWhole()
    {
        float[] samples = Recording(4_800, 4_800);

        RecordedSweepSpan span = Locate(samples)[0];

        Assert.Equal(0, span.Start);
        Assert.Equal(samples.Length, span.Length);
    }

    [Fact]
    public void LongSilenceAroundTheSweepIsCutAway()
    {
        const int lead = 60 * SampleRate;
        float[] samples = Recording(lead, 60 * SampleRate, noise: 0.001f);

        RecordedSweepSpan span = Locate(samples)[0];

        Assert.Equal(lead - SampleRate / 2, span.Start);
        Assert.Equal(lead, span.ExcitationStart);
        Assert.Equal(Bound, span.Length);
    }

    // Matched filtering concentrates a 2 s sweep into one peak worth about 46 dB.
    [Theory]
    [Trait("Category", "Slow")]
    [InlineData(0.3f)]
    [InlineData(0.03f)]
    [InlineData(0.003f)]
    public void ASweepUnderTheNoiseFloorIsStillFound(float sweepGain)
    {
        const int lead = 20 * SampleRate;
        float[] samples = Recording(lead, 20 * SampleRate, sweepGain, noise: 0.03f);

        RecordedSweepSpan span = Locate(samples)[0];

        Assert.Equal(lead, span.ExcitationStart);
        Assert.Equal(Bound, span.Length);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void AnIsolatedClickIsNotMistakenForTheSweep()
    {
        const int lead = 30 * SampleRate;
        float[] samples = Recording(lead, 30 * SampleRate, noise: 0.001f);
        samples[SampleRate] = 1.0f;
        samples[SampleRate + 1] = -1.0f;

        Assert.Equal(lead, Locate(samples)[0].ExcitationStart);
    }

    [Theory]
    [Trait("Category", "Slow")]
    [InlineData(10.0)]
    [InlineData(30.0)]
    public void InterferenceLouderThanTheSweepIsNotTheMatch(double interferenceOverSweepDb)
    {
        const int lead = 60 * SampleRate;
        float[] samples = Recording(lead, 10 * SampleRate, sweepGain: 0.4f, noise: 0.0002f);
        double interference = 0.4 * Math.Pow(10.0, interferenceOverSweepDb / 20.0);
        var random = new Random(5150);
        for (int i = 0; i < 4 * SampleRate; i++)
        {
            samples[2 * SampleRate + i] +=
                (float)((random.NextDouble() - 0.5) * 2 * interference);
        }

        Assert.Equal(lead, Locate(samples)[0].ExcitationStart);
    }

    // A bass crossed out 30 dB read as a late start under a level rule; matching costs coherence, not position.
    [Theory]
    [Trait("Category", "Slow")]
    [InlineData(true)]
    [InlineData(false)]
    public void AQuietEndOfTheBandDoesNotMoveTheWindow(bool quietHead)
    {
        const int lead = 30 * SampleRate;
        float[] samples = Recording(lead, 30 * SampleRate, noise: 0.0005f);
        int quiet = (int)(SweepSamples * 0.4);
        for (int i = 0; i < quiet; i++)
        {
            int at = quietHead ? lead + i : lead + SweepSamples - 1 - i;
            samples[at] *= 0.0316f;
        }

        RecordedSweepSpan span = Locate(samples)[0];

        Assert.Equal(lead, span.ExcitationStart);
        Assert.Equal(Bound, span.Length);
    }

    [Fact]
    public void ASilentRecordingFallsBackToTheBoundedHead()
    {
        var samples = new float[10 * 60 * SampleRate];

        RecordedSweepSpan span = Locate(samples)[0];

        Assert.Equal(0, span.Start);
        Assert.Equal(Bound, span.Length);
    }

    [Fact]
    public void ADegenerateSweepLeavesTheRecordingAlone()
    {
        float[] samples = Recording(0, 0);

        RecordedSweepSpan span =
            RecordedSweepWindow.LocateCandidates(samples, [], SampleRate)[0];

        Assert.Equal(0, span.Start);
        Assert.Equal(samples.Length, span.Length);
    }
}
