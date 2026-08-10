using Resonalyze.Audio;

namespace Resonalyze.App.Tests;

/// <summary>
/// Locating the excitation inside a recording that is mostly something else — the
/// shape of every file made by starting a recorder, walking to the seat, playing
/// the sweep and walking back. The sweep is found by matching it, so these build
/// recordings out of the real excitation rather than a stand-in tone.
/// </summary>
public sealed class RecordedSweepWindowTests : IDisposable
{
    private const int SampleRate = 48_000;

    private readonly ExponentialSineSweep sweep = new();

    public RecordedSweepWindowTests() => sweep.FillData(20, 20_000, 2.0, 24, SampleRate);

    public void Dispose() => sweep.Dispose();

    private float[] Sweep => sweep.SweepData;

    private int SweepSamples => sweep.SweepSamples;

    // The window is the sweep plus 0.5 s of lead-in and 2 s of tail.
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

    // A pre-roll followed by a sweep the file cuts short: the span is the whole
    // (short) recording, but what is left of the excitation inside it is what
    // decides whether the take is usable.
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

    [Fact]
    public void ShortRecordingsAreAnalyzedWhole()
    {
        float[] samples = Recording(4_800, 4_800);

        RecordedSweepSpan span = Locate(samples)[0];

        Assert.Equal(0, span.Start);
        Assert.Equal(samples.Length, span.Length);
    }

    // The case that motivated the window: a minute of silence, a sweep, another
    // minute of silence. Without it every FFT is sized by the two minutes.
    [Fact]
    public void LongSilenceAroundTheSweepIsCutAway()
    {
        const int lead = 60 * SampleRate;
        float[] samples = Recording(lead, 60 * SampleRate, noise: 0.001f);

        RecordedSweepSpan span = Locate(samples)[0];

        // Matched to the sample, so the lead-in is exactly what was asked for.
        Assert.Equal(lead - SampleRate / 2, span.Start);
        Assert.Equal(lead, span.ExcitationStart);
        Assert.Equal(Bound, span.Length);
    }

    // What the level detector could never do: find a sweep that is QUIETER than
    // the noise it was recorded in. Matching concentrates the whole excitation
    // into one peak, which is worth about 46 dB over two seconds — so a take a
    // listener would call empty still resolves to the sample.
    [Theory]
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

    // A door slam, a knock on the recorder: loud, and nothing like the sweep.
    [Fact]
    public void AnIsolatedClickIsNotMistakenForTheSweep()
    {
        const int lead = 30 * SampleRate;
        float[] samples = Recording(lead, 30 * SampleRate, noise: 0.001f);
        samples[SampleRate] = 1.0f;
        samples[SampleRate + 1] = -1.0f;

        Assert.Equal(lead, Locate(samples)[0].ExcitationStart);
    }

    // Speech or handling noise before the sweep is loud and sustained, and here it
    // is far louder than the sweep — the case a threshold hung off the loudest
    // thing in the file cannot survive at all. Matching does not care how loud the
    // interference is, only that it is not this sweep.
    [Theory]
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

    // The system under test barely reproduces one end of the band — a car whose
    // bass is crossed out drops its first octaves by 30 dB, which a level rule
    // read as the sweep starting a second late. The match is made against the
    // whole waveform, so the quiet end costs coherence, not position.
    [Theory]
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
        // And no widening to cover which end went quiet: the span is the ordinary
        // bound, where the level detector had to grow it by a quarter.
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
