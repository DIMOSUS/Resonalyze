namespace Resonalyze.Dsp.Tests;

/// <summary>The calibrator tone anchors the SPL scale: accurate across sub-bin offsets (flat-top window) and rejected unless clean and dominant.</summary>
public sealed class SplToneAnalysisTests
{
    private const int SampleRate = 48_000;
    private const int FrameLength = 16_384;
    private const double BinWidthHz = (double)SampleRate / FrameLength;

    private static readonly SplToneCriteria Criteria = SplToneCriteria.Default;

    [Fact]
    public void Analyze_CleanTone_IsClearAndOnFrequency()
    {
        double[] spectrum = PowerSpectrumOf(Tone(frequencyHz: 1_000.0, amplitude: 0.1));

        SplToneReading reading = SplToneAnalysis.Analyze(spectrum, BinWidthHz, Criteria);

        Assert.True(reading.HasClearPeak, $"prominence {reading.ProminenceDb:0.0} dB");
        Assert.True(reading.WithinFrequencyTolerance);
        Assert.Equal(1_000.0, reading.PeakFrequencyHz, BinWidthHz);
        Assert.Equal(-20.0, reading.LevelDbFs, 0.3); // 0.1 amplitude = -20 dBFS
        Assert.True(reading.ProminenceDb > 40.0, $"prominence {reading.ProminenceDb:0.0} dB");
    }

    [Theory]
    // Bin width ~2.93 Hz; Hann or rectangular would read 1.4–3.9 dB low. Pins the flat-top choice.
    [InlineData(1_000.0)]
    [InlineData(1_000.7)]
    [InlineData(1_001.5)]
    [InlineData(999.3)]
    public void Analyze_ToneLevelIsAccurateAcrossSubBinOffsets(double frequencyHz)
    {
        const double amplitude = 0.25; // -12.04 dBFS
        double expectedDbFs = 20.0 * Math.Log10(amplitude);

        double[] spectrum = PowerSpectrumOf(Tone(frequencyHz, amplitude));
        SplToneReading reading = SplToneAnalysis.Analyze(spectrum, BinWidthHz, Criteria);

        Assert.True(reading.HasClearPeak);
        Assert.Equal(expectedDbFs, reading.LevelDbFs, 0.3);
    }

    [Fact]
    public void Analyze_MeasuredLevelTracksTheReferenceOffset()
    {
        double quiet = SplToneAnalysis.Analyze(
            PowerSpectrumOf(Tone(1_000.0, 0.02)), BinWidthHz, Criteria).LevelDbFs;
        double loud = SplToneAnalysis.Analyze(
            PowerSpectrumOf(Tone(1_000.0, 0.2)), BinWidthHz, Criteria).LevelDbFs;

        Assert.Equal(20.0, loud - quiet, 0.1);
    }

    [Fact]
    public void Analyze_NoiseOnly_HasNoClearPeak()
    {
        double[] spectrum = AveragedPowerSpectrum(
            frameCount: 32, frame => Noise(seed: frame, amplitude: 0.05));

        SplToneReading reading = SplToneAnalysis.Analyze(spectrum, BinWidthHz, Criteria);

        Assert.False(reading.HasClearPeak, $"prominence {reading.ProminenceDb:0.0} dB");
    }

    [Fact]
    public void Analyze_OffFrequencyTone_FailsTolerance()
    {
        double[] spectrum = PowerSpectrumOf(Tone(frequencyHz: 1_500.0, amplitude: 0.1));

        SplToneReading reading = SplToneAnalysis.Analyze(spectrum, BinWidthHz, Criteria);

        Assert.False(reading.WithinFrequencyTolerance);
        Assert.False(reading.HasClearPeak);
        Assert.Equal(1_500.0, reading.PeakFrequencyHz, BinWidthHz);
    }

    [Fact]
    public void Analyze_LowFrequencyRumbleDoesNotOutrankTheTone()
    {
        // Sub-100 Hz rumble is below the analysis floor, so the 1 kHz tone still wins.
        float[] rumbleAndTone = Sum(
            Tone(frequencyHz: 50.0, amplitude: 0.8),
            Tone(frequencyHz: 1_000.0, amplitude: 0.1));

        SplToneReading reading = SplToneAnalysis.Analyze(
            PowerSpectrumOf(rumbleAndTone), BinWidthHz, Criteria);

        Assert.True(reading.HasClearPeak);
        Assert.Equal(1_000.0, reading.PeakFrequencyHz, BinWidthHz);
    }

    [Fact]
    public void Analyze_RejectsInvalidArguments()
    {
        double[] spectrum = PowerSpectrumOf(Tone(1_000.0, 0.1));

        Assert.Throws<ArgumentNullException>(
            () => SplToneAnalysis.Analyze(null!, BinWidthHz, Criteria));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SplToneAnalysis.Analyze(spectrum, 0.0, Criteria));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SplToneAnalysis.Analyze(spectrum, double.NaN, Criteria));
    }

    private static double[] PowerSpectrumOf(float[] samples) =>
        SpectrumAnalysis.ComputePowerSpectrum(samples, WindowType.FlatTop);

    private static double[] AveragedPowerSpectrum(int frameCount, Func<int, float[]> buildFrame)
    {
        double[]? accumulated = null;
        for (int frame = 0; frame < frameCount; frame++)
        {
            double[] power = PowerSpectrumOf(buildFrame(frame));
            accumulated ??= new double[power.Length];
            for (int i = 0; i < power.Length; i++)
            {
                accumulated[i] += power[i] / frameCount;
            }
        }

        return accumulated!;
    }

    private static float[] Tone(double frequencyHz, double amplitude)
    {
        var samples = new float[FrameLength];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(amplitude * Math.Sin(2.0 * Math.PI * frequencyHz * i / SampleRate));
        }

        return samples;
    }

    private static float[] Noise(int seed, double amplitude)
    {
        var random = new Random(seed + 1);
        var samples = new float[FrameLength];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(amplitude * (random.NextDouble() * 2.0 - 1.0));
        }

        return samples;
    }

    private static float[] Sum(float[] a, float[] b)
    {
        var samples = new float[a.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = a[i] + b[i];
        }

        return samples;
    }
}
