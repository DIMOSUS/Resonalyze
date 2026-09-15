namespace Resonalyze.App.Tests;

public sealed class RecordedSweepDetectorTests : IDisposable
{
    private const int SampleRate = 48_000;

    private readonly ExponentialSineSweep sweep = new();

    public RecordedSweepDetectorTests() => sweep.FillData(20, 20_000, 1.0, 24, SampleRate);

    public void Dispose() => sweep.Dispose();

    private float[] Excitation => sweep.SweepData;

    private float[] Take(int start, int trailing, float gain = 1.0f, float noise = 0.0f, int seed = 1)
    {
        var samples = new float[start + Excitation.Length + trailing];
        var random = new Random(seed);
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)((random.NextDouble() - 0.5) * 2 * noise);
        }
        for (int i = 0; i < Excitation.Length; i++)
        {
            samples[start + i] += Excitation[i] * gain;
        }

        return samples;
    }

    [Fact]
    public void FindsTheSweepToTheSample()
    {
        const int start = 37_123;

        IReadOnlyList<SweepMatch> matches =
            RecordedSweepDetector.FindSweeps(Take(start, SampleRate), Excitation, 3);

        Assert.NotEmpty(matches);
        Assert.Equal(start, matches[0].Start);
        Assert.Equal(1.0, matches[0].Quality, tolerance: 0.001);
    }

    // 20 dB under the noise; matching one second of sweep gives about 43 dB of gain.
    [Fact]
    public void FindsASweepBuriedUnderNoise()
    {
        const int start = 12_345;
        float[] samples = Take(start, SampleRate, gain: 0.003f, noise: 0.03f);

        IReadOnlyList<SweepMatch> matches =
            RecordedSweepDetector.FindSweeps(samples, Excitation, 1);

        Assert.Equal(start, matches[0].Start);
    }

    // Quality is a correlation, so a quieter take is not a worse match.
    [Fact]
    public void ReportsEveryTakeInTheRecording()
    {
        int second = Excitation.Length + 3 * SampleRate;
        float[] samples = Take(SampleRate, second + SampleRate);
        for (int i = 0; i < Excitation.Length; i++)
        {
            samples[SampleRate + second + i] += Excitation[i] * 0.5f;
        }

        IReadOnlyList<SweepMatch> matches =
            RecordedSweepDetector.FindSweeps(samples, Excitation, 2);

        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, match => match.Start == SampleRate);
        Assert.Contains(matches, match => match.Start == SampleRate + second);
        Assert.All(matches, match => Assert.Equal(1.0, match.Quality, tolerance: 0.01));
    }

    [Fact]
    public void RanksTheCleanerTakeFirst()
    {
        int second = Excitation.Length + 3 * SampleRate;
        float[] samples = Take(SampleRate, second + SampleRate);
        var random = new Random(11);
        for (int i = 0; i < Excitation.Length; i++)
        {
            samples[SampleRate + second + i] +=
                Excitation[i] * 0.5f + (float)((random.NextDouble() - 0.5) * 0.8);
        }

        IReadOnlyList<SweepMatch> matches =
            RecordedSweepDetector.FindSweeps(samples, Excitation, 2);

        Assert.Equal(SampleRate, matches[0].Start);
        Assert.True(
            matches[0].Quality > matches[1].Quality,
            $"clean {matches[0].Quality:0.000} against noisy {matches[1].Quality:0.000}");
    }

    // The true start of a cut-off take is among placements running off the file's end, so those are searched.
    [Fact]
    public void FindsASweepTheRecordingRanOutOn()
    {
        const int start = 24_000;
        float[] full = Take(start, 0);
        float[] truncated = full[..(start + (int)(Excitation.Length * 0.7))];

        IReadOnlyList<SweepMatch> matches =
            RecordedSweepDetector.FindSweeps(truncated, Excitation, 1);

        Assert.Equal(start, matches[0].Start);
        Assert.True(
            matches[0].Start + Excitation.Length > truncated.Length,
            "the match has to show the sweep running past the end of the file");
    }

    [Fact]
    public void QualityJudgesTheShapeRatherThanTheLevel()
    {
        float[] quietSweep = Take(SampleRate, SampleRate, gain: 0.01f);
        var loudHum = new float[quietSweep.Length];
        for (int i = 0; i < loudHum.Length; i++)
        {
            loudHum[i] = (float)(0.5 * Math.Sin(2.0 * Math.PI * 50.0 * i / SampleRate));
        }

        double sweepQuality = RecordedSweepDetector
            .FindSweeps(quietSweep, Excitation, 1)[0].Quality;
        double humQuality = RecordedSweepDetector
            .FindSweeps(loudHum, Excitation, 1)[0].Quality;

        Assert.True(
            sweepQuality > humQuality * 4,
            $"sweep {sweepQuality:0.000} against hum {humQuality:0.000}");
    }

    // Decimated search: comparing a coarse start with a refined one mixes scales and merged the two takes.
    [Fact]
    public void ReportsEveryTakeOnceTheSearchDecimates()
    {
        const int rate = 48_000;
        const int first = 12_000;
        using var shortSweep = new ExponentialSineSweep();
        shortSweep.FillData(20, 20_000, 0.25, 24, rate);
        float[] excitation = shortSweep.SweepData;
        // 2 x (first / decimation) is exactly the second take's coarse index.
        int second = first + excitation.Length;
        var samples = new float[1_200_000];
        var random = new Random(99);
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)((random.NextDouble() - 0.5) * 1e-4);
        }
        for (int i = 0; i < excitation.Length; i++)
        {
            samples[first + i] += excitation[i];
            samples[second + i] +=
                excitation[i] * 0.5f + (float)((random.NextDouble() - 0.5) * 0.05);
        }

        IReadOnlyList<SweepMatch> matches =
            RecordedSweepDetector.FindSweeps(samples, excitation, 3);

        Assert.Contains(matches, match => match.Start == first);
        Assert.Contains(matches, match => match.Start == second);
    }

    [Fact]
    public void ReportsNothingWhenThereIsNothingToMatch()
    {
        Assert.Empty(RecordedSweepDetector.FindSweeps([], Excitation, 1));
        Assert.Empty(RecordedSweepDetector.FindSweeps(new float[1_000], [], 1));
        Assert.Empty(RecordedSweepDetector.FindSweeps(
            new float[Excitation.Length / 2], Excitation, 1));
    }
}
