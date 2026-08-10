namespace Resonalyze.App.Tests;

/// <summary>
/// Locating the excitation inside a recording that is mostly silence — the shape
/// of every file made by starting a recorder, walking to the seat, playing the
/// sweep and walking back.
/// </summary>
public sealed class RecordedSweepWindowTests
{
    private const int SampleRate = 48_000;
    private const int SweepSamples = 96_000;

    // The window is the sweep plus 0.5 s of lead-in and 2 s of tail.
    private const int Bound = SweepSamples + (int)(2.5 * SampleRate);

    private static float[] Recording(
        int leadSilence,
        int excitation,
        int trailingSilence,
        float noise = 0.0f)
    {
        var samples = new float[leadSilence + excitation + trailingSilence];
        var random = new Random(4242);
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)((random.NextDouble() - 0.5) * 2 * noise);
        }
        for (int i = 0; i < excitation; i++)
        {
            samples[leadSilence + i] += (float)(Math.Sin(i * 0.02) * 0.4);
        }

        return samples;
    }

    // A pre-roll followed by a sweep the file cuts short: the span is the whole
    // (short) recording, but what is left of the excitation inside it is what
    // decides whether the take is usable.
    [Fact]
    public void AShortRecordingStillReportsWhereTheExcitationBegins()
    {
        int preRoll = SampleRate / 2;
        float[] samples = Recording(preRoll, (int)(SweepSamples * 0.85), 0, noise: 0.0005f);

        RecordedSweepSpan span =
            RecordedSweepWindow.LocateCandidates(samples, SampleRate, SweepSamples)[0];

        Assert.Equal(0, span.Start);
        Assert.Equal(samples.Length, span.Length);
        Assert.InRange(span.ExcitationStart, preRoll - 480, preRoll + 480);
        Assert.True(span.ExcitationLength < SweepSamples);
    }

    [Fact]
    public void ShortRecordingsAreAnalyzedWhole()
    {
        float[] samples = Recording(4_800, SweepSamples, 4_800);

        RecordedSweepSpan span = RecordedSweepWindow.LocateCandidates(samples, SampleRate, SweepSamples)[0];

        Assert.Equal(0, span.Start);
        Assert.Equal(samples.Length, span.Length);
    }

    // The case that motivated the window: a minute of silence, a sweep, another
    // minute of silence. Without it every FFT is sized by the two minutes.
    [Fact]
    public void LongSilenceAroundTheSweepIsCutAway()
    {
        const int lead = 60 * SampleRate;
        float[] samples = Recording(lead, SweepSamples, 60 * SampleRate, noise: 0.001f);

        RecordedSweepSpan span = RecordedSweepWindow.LocateCandidates(samples, SampleRate, SweepSamples)[0];

        // Half a second of lead-in is kept before the excitation.
        Assert.InRange(span.Start, lead - SampleRate / 2 - 480, lead - SampleRate / 2 + 480);
        Assert.Equal(Bound, span.Length);
        Assert.True(span.Start + span.Length <= samples.Length);
        // The whole excitation is inside the window.
        Assert.True(span.Start + span.Length >= lead + SweepSamples);
    }

    // A door slam or a knock on the recorder is one window wide; the onset has to
    // be the sustained rise, or the analysis would start before the real sweep
    // and cut its end off.
    [Fact]
    public void AnIsolatedClickBeforeTheSweepIsNotTheOnset()
    {
        const int lead = 30 * SampleRate;
        float[] samples = Recording(lead, SweepSamples, 30 * SampleRate, noise: 0.001f);
        samples[SampleRate] = 1.0f;
        samples[SampleRate + 1] = -1.0f;

        RecordedSweepSpan span = RecordedSweepWindow.LocateCandidates(samples, SampleRate, SweepSamples)[0];

        Assert.InRange(span.Start, lead - SampleRate, lead);
    }

    // Speech or handling noise before the sweep is loud AND sustained, so no
    // level rule can rank it away with certainty — but the sweep must at least be
    // among the candidates the caller then tries.
    [Fact]
    public void SustainedNoiseBeforeTheSweepStillOffersTheSweep()
    {
        const int lead = 60 * SampleRate;
        float[] samples = Recording(lead, SweepSamples, 10 * SampleRate, noise: 0.001f);
        // Four seconds of interference — longer than the sweep itself — starting
        // one second in, at a level the sweep only just exceeds.
        var random = new Random(99);
        for (int i = 0; i < 4 * SampleRate; i++)
        {
            samples[SampleRate + i] += (float)((random.NextDouble() - 0.5) * 0.6);
        }

        IReadOnlyList<RecordedSweepSpan> candidates =
            RecordedSweepWindow.LocateCandidates(samples, SampleRate, SweepSamples);

        Assert.Contains(candidates, span =>
            span.Start <= lead && span.Start + span.Length >= lead + SweepSamples);
    }

    // The system under test barely reproduces one end of the band — a car whose
    // bass is crossed out drops its first octaves by 30 dB — so the excitation is
    // only heard from part-way in. The window must still hold ALL of it: analyzing
    // from where the level rose would build the result on an excitation whose
    // beginning is outside the window, and nothing downstream would notice.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AQuietEndOfTheBandDoesNotPushTheExcitationOutOfTheWindow(bool quietHead)
    {
        const int lead = 30 * SampleRate;
        float[] samples = Recording(lead, SweepSamples, 30 * SampleRate, noise: 0.0005f);
        int quiet = (int)(SweepSamples * 0.4);
        for (int i = 0; i < quiet; i++)
        {
            int at = quietHead ? lead + i : lead + SweepSamples - 1 - i;
            samples[at] *= 0.0316f;
        }

        RecordedSweepSpan span =
            RecordedSweepWindow.LocateCandidates(samples, SampleRate, SweepSamples)[0];

        Assert.True(span.Start <= lead, $"the window starts at {span.Start}, after the excitation at {lead}");
        Assert.True(
            span.Start + span.Length >= lead + SweepSamples,
            $"the window ends at {span.Start + span.Length}, before the excitation ends at {lead + SweepSamples}");
        // Widened, but still bounded by the sweep's own length.
        Assert.True(span.Length <= 2 * SweepSamples + (int)(2.5 * SampleRate));
    }

    // The case a threshold hung off the loudest thing in the file cannot survive:
    // a door, a voice or a knock on the recorder 30 dB ABOVE a quiet measurement
    // sweep. Twenty decibels under that interference is still well over the sweep,
    // so the excitation never becomes a candidate at all and no amount of retrying
    // reaches it. Measured against the noise floor instead, both are candidates.
    [Theory]
    [InlineData(10.0)]
    [InlineData(30.0)]
    public void InterferenceLouderThanTheSweepDoesNotHideIt(double interferenceOverSweepDb)
    {
        const int lead = 60 * SampleRate;
        float[] samples = Recording(lead, SweepSamples, 10 * SampleRate, noise: 0.0002f);
        double interference = 0.4 * Math.Pow(10.0, interferenceOverSweepDb / 20.0);
        var random = new Random(5150);
        for (int i = 0; i < SampleRate; i++)
        {
            samples[2 * SampleRate + i] +=
                (float)((random.NextDouble() - 0.5) * 2 * interference);
        }

        IReadOnlyList<RecordedSweepSpan> candidates =
            RecordedSweepWindow.LocateCandidates(samples, SampleRate, SweepSamples);

        Assert.Contains(candidates, span =>
            span.Start <= lead && span.Start + span.Length >= lead + SweepSamples);
    }

    [Fact]
    public void ASilentRecordingFallsBackToTheBoundedHead()
    {
        var samples = new float[10 * 60 * SampleRate];

        RecordedSweepSpan span = RecordedSweepWindow.LocateCandidates(samples, SampleRate, SweepSamples)[0];

        Assert.Equal(0, span.Start);
        Assert.Equal(Bound, span.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ADegenerateSweepLengthLeavesTheRecordingAlone(int sweepSamples)
    {
        float[] samples = Recording(0, 1_024, 0);

        RecordedSweepSpan span = RecordedSweepWindow.LocateCandidates(samples, SampleRate, sweepSamples)[0];

        Assert.Equal(0, span.Start);
        Assert.Equal(samples.Length, span.Length);
    }
}
