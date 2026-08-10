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

    [Fact]
    public void ShortRecordingsAreAnalyzedWhole()
    {
        float[] samples = Recording(4_800, SweepSamples, 4_800);

        RecordedSweepSpan span = RecordedSweepWindow.Locate(samples, SampleRate, SweepSamples);

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

        RecordedSweepSpan span = RecordedSweepWindow.Locate(samples, SampleRate, SweepSamples);

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

        RecordedSweepSpan span = RecordedSweepWindow.Locate(samples, SampleRate, SweepSamples);

        Assert.InRange(span.Start, lead - SampleRate, lead);
    }

    [Fact]
    public void ASilentRecordingFallsBackToTheBoundedHead()
    {
        var samples = new float[10 * 60 * SampleRate];

        RecordedSweepSpan span = RecordedSweepWindow.Locate(samples, SampleRate, SweepSamples);

        Assert.Equal(0, span.Start);
        Assert.Equal(Bound, span.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ADegenerateSweepLengthLeavesTheRecordingAlone(int sweepSamples)
    {
        float[] samples = Recording(0, 1_024, 0);

        RecordedSweepSpan span = RecordedSweepWindow.Locate(samples, SampleRate, sweepSamples);

        Assert.Equal(0, span.Start);
        Assert.Equal(samples.Length, span.Length);
    }
}
