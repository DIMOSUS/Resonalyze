namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The SPL calibration listens for an acoustic calibrator's reference tone and
/// anchors the whole SPL scale to the level it reads there, so two things must
/// hold: the tone level must be accurate to a fraction of a dB wherever the tone
/// falls between bins (why the flat-top window is used), and the verdict must
/// reject anything that is not a clean, dominant tone at the target frequency.
/// </summary>
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
    // Bin-centred, and three tones that fall progressively further between bins
    // (bin width ~2.93 Hz). A Hann or rectangular window would read 1.4–3.9 dB
    // low here; the flat-top window is what keeps every one within a fraction of
    // a dB — this is the test that pins that choice.
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
        // The number the whole feature depends on: two calibrator levels 20 dB
        // apart must read 20 dB apart, so ref - measured is a stable offset.
        double quiet = SplToneAnalysis.Analyze(
            PowerSpectrumOf(Tone(1_000.0, 0.02)), BinWidthHz, Criteria).LevelDbFs;
        double loud = SplToneAnalysis.Analyze(
            PowerSpectrumOf(Tone(1_000.0, 0.2)), BinWidthHz, Criteria).LevelDbFs;

        Assert.Equal(20.0, loud - quiet, 0.1);
    }

    [Fact]
    public void Analyze_NoiseOnly_HasNoClearPeak()
    {
        // Broadband noise averaged over many frames is flat: no bin stands 20 dB
        // above the background, so there is no calibrator tone to lock onto.
        double[] spectrum = AveragedPowerSpectrum(
            frameCount: 32, frame => Noise(seed: frame, amplitude: 0.05));

        SplToneReading reading = SplToneAnalysis.Analyze(spectrum, BinWidthHz, Criteria);

        Assert.False(reading.HasClearPeak, $"prominence {reading.ProminenceDb:0.0} dB");
    }

    [Fact]
    public void Analyze_OffFrequencyTone_FailsTolerance()
    {
        // A clean tone, but at 1.5 kHz: prominent, yet not the calibrator's tone.
        double[] spectrum = PowerSpectrumOf(Tone(frequencyHz: 1_500.0, amplitude: 0.1));

        SplToneReading reading = SplToneAnalysis.Analyze(spectrum, BinWidthHz, Criteria);

        Assert.False(reading.WithinFrequencyTolerance);
        Assert.False(reading.HasClearPeak);
        Assert.Equal(1_500.0, reading.PeakFrequencyHz, BinWidthHz);
    }

    [Fact]
    public void Analyze_LowFrequencyRumbleDoesNotOutrankTheTone()
    {
        // A large sub-100 Hz rumble sits below the analysis floor, so the 1 kHz
        // calibrator tone still wins the dominant-peak search even though the
        // rumble carries far more energy.
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
