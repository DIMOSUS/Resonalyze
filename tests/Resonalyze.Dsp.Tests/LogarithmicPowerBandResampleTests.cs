namespace Resonalyze.Dsp.Tests;

public sealed class LogarithmicPowerBandResampleTests
{
    private const int SampleRate = 48_000;

    [Fact]
    public void PowerBandLevels_AreFftLengthInvariant_ForTheSameNoise()
    {
        // The same noise must read the same band level at any FFT size (the amplitude path lost ~3 dB per doubling).
        float[] signal = WhiteNoise(2_048 * 200, seed: 20260718);

        List<SignalPoint> bands2048 = BandLevels(signal, 2_048);
        List<SignalPoint> bands4096 = BandLevels(signal, 4_096);

        // Grids differ per FFT size: compare at matching frequencies over a band with several bins per band.
        double sum2048 = 0.0;
        double sum4096 = 0.0;
        double maxAbsDiff = 0.0;
        int count = 0;
        // From 1 kHz the 1/12-octave band is wider than the rectangular main lobe at both sizes.
        for (double frequency = 1_000.0; frequency <= 6_000.0; frequency *= 1.05)
        {
            double level2048 = NearestLevel(bands2048, frequency);
            double level4096 = NearestLevel(bands4096, frequency);
            sum2048 += level2048;
            sum4096 += level4096;
            maxAbsDiff = Math.Max(maxAbsDiff, Math.Abs(level2048 - level4096));
            count++;
        }

        double meanDiff = Math.Abs(sum2048 / count - sum4096 / count);
        // 0.5 dB leaves room for the scatter of two independent noise runs.
        Assert.True(meanDiff < 0.5, $"mean band level differed by {meanDiff:0.000} dB across FFT sizes");
        Assert.True(maxAbsDiff < 2.0, $"a band differed by {maxAbsDiff:0.000} dB across FFT sizes");
    }

    [Theory]
    // Lower bounds are the N=2048 main-lobe crossovers with margin: Hann ~1.6 kHz, Flat Top ~4.1 kHz.
    [InlineData(WindowType.Hann, 1_800.0)]
    [InlineData(WindowType.FlatTop, 4_400.0)]
    public void PowerBandLevels_AreFftLengthInvariant_AboveResolution_ForWideLobeWindows(
        WindowType windowType,
        double lowerFrequency)
    {
        float[] signal = WhiteNoise(4_096 * 220, seed: 909_090);

        List<SignalPoint> bands2048 = BandLevels(signal, 2_048, windowType);
        List<SignalPoint> bands4096 = BandLevels(signal, 4_096, windowType);

        double sum2048 = 0.0;
        double sum4096 = 0.0;
        double maxAbsDiff = 0.0;
        int count = 0;
        for (double frequency = lowerFrequency; frequency <= 8_000.0; frequency *= 1.05)
        {
            double level2048 = NearestLevel(bands2048, frequency);
            double level4096 = NearestLevel(bands4096, frequency);
            sum2048 += level2048;
            sum4096 += level4096;
            maxAbsDiff = Math.Max(maxAbsDiff, Math.Abs(level2048 - level4096));
            count++;
        }

        double meanDiff = Math.Abs(sum2048 / count - sum4096 / count);
        Assert.True(meanDiff < 0.5, $"{windowType}: mean band level differed by {meanDiff:0.000} dB across FFT sizes");
        Assert.True(maxAbsDiff < 2.0, $"{windowType}: a band differed by {maxAbsDiff:0.000} dB across FFT sizes");
    }

    [Fact]
    public void PowerBandLevels_AreResolutionLimited_BelowTheMainLobeCrossover()
    {
        // Below the crossover the band IS the main lobe, so ~3 dB per doubling is the resolution limit, not a bug.
        float[] signal = WhiteNoise(4_096 * 220, seed: 515_151);

        double level2048 = NearestLevel(BandLevels(signal, 2_048, WindowType.Hann), 300.0);
        double level4096 = NearestLevel(BandLevels(signal, 4_096, WindowType.Hann), 300.0);

        double drop = level2048 - level4096;
        Assert.True(
            drop is > 1.5 and < 4.5,
            $"expected a ~3 dB resolution-limited drop below the crossover, got {drop:0.00} dB");
    }

    [Fact]
    public void PowerBandLevel_ReadsAFullScaleToneAtItsCalibratedLevel()
    {
        // On-bin full-scale tone must read 0 dBFS, the level tone calibration is anchored to.
        const int length = 2_048;
        const int bin = 256; // 256 * 48000 / 2048 = 6000 Hz
        double toneFrequency = (double)bin * SampleRate / length;

        // Smoothing would dilute a spike toward the surrounding silence.
        List<SignalPoint> bands = BandLevels(
            CreateSine(length, bin), length, smoothingOctaves: 0.0);

        double peak = double.NegativeInfinity;
        foreach (SignalPoint point in bands)
        {
            if (point.X >= toneFrequency * 0.98 && point.X <= toneFrequency * 1.02)
            {
                peak = Math.Max(peak, point.Y);
            }
        }

        Assert.InRange(peak, -0.3, 0.3);
    }

    [Fact]
    public void PowerBandLevel_WindowEnbwRemovesTheNoiseOverEstimate()
    {
        // Hann over-states a noise band by ENBW (~1.5).
        float[] signal = WhiteNoise(4_096 * 120, seed: 4242);

        double rectangular = MeanBandLevel(BandLevels(signal, 4_096, WindowType.Rectangular));
        double hann = MeanBandLevel(BandLevels(signal, 4_096, WindowType.Hann));

        Assert.True(
            Math.Abs(rectangular - hann) < 0.75,
            $"Hann vs rectangular mid-band level differed by {Math.Abs(rectangular - hann):0.000} dB");
    }

    [Fact]
    public void PowerBandLevels_SmoothingDoesNotLiftTheLevel()
    {
        // Smoothing once set the integration width and lifted the curve up to ~20 dB.
        float[] signal = WhiteNoise(2_048 * 250, seed: 33_221);

        double off = MeanBandLevel(BandLevels(signal, 2_048, smoothingOctaves: 0.0));
        double sixth = MeanBandLevel(BandLevels(signal, 2_048, smoothingOctaves: 1.0 / 6.0));
        double wide = MeanBandLevel(BandLevels(signal, 2_048, smoothingOctaves: 1.0));

        Assert.True(Math.Abs(off - sixth) < 1.0, $"1/6-octave smoothing shifted the level {off - sixth:0.00} dB");
        Assert.True(Math.Abs(off - wide) < 1.0, $"1-octave smoothing shifted the level {off - wide:0.00} dB");
    }

    [Fact]
    public void PowerBandLevels_DoNotRollOffAtTheTopEdgeOnAFlatSpectrum()
    {
        // A band straddling Nyquist would be half-empty and dip.
        const int sampleRate = 32_000;
        const int fftLength = 2_048;
        var amplitude = new double[fftLength / 2];
        Array.Fill(amplitude, 0.1);

        List<SignalPoint> bands = DataHelper.LogarithmicPowerBandResample(
            amplitude,
            fftLength,
            sampleRate,
            windowEnbwBins: 1.0,
            windowMainLobeBins: 2.0,
            start: 20,
            stop: 20_000,
            steps: 1024,
            smoothingOctaves: 1.0 / 6.0);

        double top = bands[^1].Y;
        double octaveBelow = NearestLevel(bands, bands[^1].X / 2.0);
        Assert.True(
            top >= octaveBelow,
            $"top band {top:0.00} dB dipped below the octave-below {octaveBelow:0.00} dB");
    }

    [Fact]
    public void PowerBandLevels_HaveNoAlignmentJumpBelowTheFftResolution()
    {
        // The whole-bin rule made adjacent fine-grid bands differ by ~5.4 dB.
        float[] signal = WhiteNoise(2_048 * 300, seed: 71755);
        List<SignalPoint> bands = BandLevels(signal, 2_048, smoothingOctaves: 0.0);

        double maxAdjacentJump = 0.0;
        for (int i = 1; i < bands.Count; i++)
        {
            if (bands[i].X is >= 800.0 and <= 1_200.0)
            {
                maxAdjacentJump = Math.Max(
                    maxAdjacentJump,
                    Math.Abs(bands[i].Y - bands[i - 1].Y));
            }
        }

        Assert.True(maxAdjacentJump < 2.0, $"adjacent sub-bin bands jumped {maxAdjacentJump:0.00} dB");
    }

    [Fact]
    public void PowerBandLevels_StopAtNyquist_AndDoNotFabricateAboveTheLastBin()
    {
        // At 32 kHz the grid must stop at the highest resolved bin, not invent 16-20 kHz.
        const int sampleRate = 32_000;
        const int fftLength = 2_048;
        double nyquist = (fftLength / 2 - 1) * ((double)sampleRate / fftLength);

        var amplitude = new double[fftLength / 2];
        Array.Fill(amplitude, 0.1);

        List<SignalPoint> bands = DataHelper.LogarithmicPowerBandResample(
            amplitude,
            fftLength,
            sampleRate,
            windowEnbwBins: 1.0,
            windowMainLobeBins: 2.0,
            start: 20,
            stop: 20_000,
            steps: 1024,
            smoothingOctaves: 1.0 / 6.0);

        Assert.NotEmpty(bands);
        Assert.All(bands, point => Assert.True(
            point.X <= nyquist + 1e-6,
            $"band at {point.X:0} Hz is above Nyquist {nyquist:0} Hz"));
        Assert.True(bands[^1].X > nyquist * 0.9);
    }

    [Theory]
    [InlineData(WindowType.Hann, 0.0, 6_000.0)]        // Hann, smoothing off, on-bin
    [InlineData(WindowType.FlatTop, 0.0, 6_000.0)]     // Flat Top, smoothing off
    [InlineData(WindowType.FlatTop, 1.0 / 48.0, 6_000.0)] // Flat Top, 1/48 octave
    [InlineData(WindowType.Hann, 0.0, 1_000.0)]        // Hann, off-bin (~42.7) near 1 kHz
    public void PowerBandLevel_ReadsAToneAcrossWindowsAndFineSmoothing(
        WindowType windowType,
        double smoothingOctaves,
        double frequencyHz)
    {
        // The floor is the main lobe, not ENBW (an ENBW floor left a bin-centred Hann tone ~1.25 dB low).
        double peak = TonePeakLevel(frequencyHz, windowType, smoothingOctaves, fftLength: 2_048);
        Assert.InRange(peak, -1.0, 0.4);
    }

    private static double TonePeakLevel(
        double frequencyHz,
        WindowType windowType,
        double smoothingOctaves,
        int fftLength)
    {
        var sine = new float[fftLength];
        for (int i = 0; i < fftLength; i++)
        {
            sine[i] = (float)Math.Sin(2.0 * Math.PI * frequencyHz * i / SampleRate);
        }

        double[] amplitude = SpectrumAnalysis.ComputeInputMagnitudeSpectrum(
            SpectrumAnalysis.ComputeAutoPowerSpectrumFrame(sine, windowType),
            windowType,
            fftLength);
        List<SignalPoint> bands = DataHelper.LogarithmicPowerBandResample(
            amplitude,
            fftLength,
            SampleRate,
            Windowing.EquivalentNoiseBandwidthBins(windowType, fftLength),
            Windowing.MainLobeWidthBins(windowType),
            start: 20,
            stop: 20_000,
            steps: 1024,
            smoothingOctaves: smoothingOctaves);

        double peak = double.NegativeInfinity;
        foreach (SignalPoint point in bands)
        {
            if (point.X >= frequencyHz * 0.98 && point.X <= frequencyHz * 1.02)
            {
                peak = Math.Max(peak, point.Y);
            }
        }

        return peak;
    }

    // 1-5 kHz: the reference band exceeds every window's main lobe.
    private static double MeanBandLevel(List<SignalPoint> bands)
    {
        double sum = 0.0;
        int count = 0;
        foreach (SignalPoint point in bands)
        {
            if (point.X is >= 1_000.0 and <= 5_000.0)
            {
                sum += point.Y;
                count++;
            }
        }

        return sum / count;
    }

    private static List<SignalPoint> BandLevels(
        float[] signal,
        int fftLength,
        WindowType windowType = WindowType.Rectangular,
        double smoothingOctaves = 1.0 / 6.0) =>
        BandLevelsAt(signal, fftLength, SampleRate, windowType, smoothingOctaves);

    private static List<SignalPoint> BandLevelsAt(
        float[] signal,
        int fftLength,
        int sampleRate,
        WindowType windowType = WindowType.Rectangular,
        double smoothingOctaves = 1.0 / 6.0)
    {
        double[] targetPower = AccumulateTargetPower(signal, fftLength, windowType);
        double[] amplitude = SpectrumAnalysis.ComputeInputMagnitudeSpectrum(
            targetPower, windowType, fftLength);
        return DataHelper.LogarithmicPowerBandResample(
            amplitude,
            fftLength,
            sampleRate,
            Windowing.EquivalentNoiseBandwidthBins(windowType, fftLength),
            Windowing.MainLobeWidthBins(windowType),
            start: 20,
            stop: 20_000,
            steps: 1024,
            smoothingOctaves: smoothingOctaves);
    }

    private static double NearestLevel(List<SignalPoint> bands, double frequency)
    {
        double bestDistance = double.MaxValue;
        double level = double.NaN;
        foreach (SignalPoint point in bands)
        {
            double distance = Math.Abs(point.X - frequency);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                level = point.Y;
            }
        }

        return level;
    }

    private static double[] AccumulateTargetPower(float[] signal, int fftLength, WindowType windowType)
    {
        int frames = signal.Length / fftLength;
        double[]? accumulated = null;
        var frame = new float[fftLength];
        for (int f = 0; f < frames; f++)
        {
            Array.Copy(signal, f * fftLength, frame, 0, fftLength);
            TransferSpectrumFrame spectrum =
                SpectrumAnalysis.ComputeTransferSpectrumFrame(frame, frame, windowType);
            accumulated ??= new double[spectrum.TargetPowerSpectrum.Length];
            for (int i = 0; i < accumulated.Length; i++)
            {
                accumulated[i] += spectrum.TargetPowerSpectrum[i];
            }
        }

        for (int i = 0; i < accumulated!.Length; i++)
        {
            accumulated[i] /= frames;
        }

        return accumulated;
    }

    [Theory]
    [InlineData(1.0 / 6.0, false)]
    [InlineData(1.0 / 3.0, false)]
    [InlineData(1.0, false)]
    [InlineData(SpectrumSmoothing.PsychoacousticBaseInverseOctaves / 6.0, true)]
    public void SmoothBandLevels_ReplaysExactlyWhatTheResamplerWouldHaveDrawn(
        double smoothingOctaves,
        bool psychoacoustic)
    {
        // Re-smoothing stored band levels must reproduce the analyzer's curve exactly: the EQ is fitted against it.
        const int sampleRate = 48_000;
        const int fftLength = 4_096;
        var rng = new Random(11);
        var amplitude = new double[fftLength / 2];
        for (int i = 0; i < amplitude.Length; i++)
        {
            amplitude[i] = 0.01 + (rng.NextDouble() * 0.02);
            if (i is 300 or 301 or 900)
            {
                amplitude[i] = 0.5;
            }
        }

        List<SignalPoint> Resample(double octaves, bool psycho) =>
            DataHelper.LogarithmicPowerBandResample(
                amplitude,
                fftLength,
                sampleRate,
                windowEnbwBins: 1.5,
                windowMainLobeBins: 3.0,
                start: 20,
                stop: 20_000,
                steps: 1024,
                smoothingOctaves: octaves,
                psychoacoustic: psycho);

        List<SignalPoint> drawn = Resample(smoothingOctaves, psychoacoustic);
        List<SignalPoint> replayed = DataHelper.SmoothBandLevels(
            Resample(0.0, false), smoothingOctaves, psychoacoustic);

        Assert.Equal(drawn.Count, replayed.Count);
        double worst = 0;
        for (int i = 0; i < drawn.Count; i++)
        {
            Assert.Equal(drawn[i].X, replayed[i].X, 9);
            worst = Math.Max(worst, Math.Abs(drawn[i].Y - replayed[i].Y));
        }

        Assert.True(worst < 1e-9, $"Replayed smoothing differs by up to {worst:0.#####} dB.");
    }

    [Fact]
    public void SmoothBandLevels_KeepsUnmeasuredBandsAsGapsAndDoesNotSpreadThem()
    {
        var levels = new List<SignalPoint>();
        for (int i = 0; i < 200; i++)
        {
            double f = 100 * Math.Pow(2, i / 40.0);
            levels.Add(new SignalPoint(f, i == 100 ? double.NaN : 80));
        }

        List<SignalPoint> smoothed = DataHelper.SmoothBandLevels(levels, 1.0 / 3.0, false);

        Assert.True(double.IsNaN(smoothed[100].Y));
        Assert.Equal(80, smoothed[99].Y, 6);
        Assert.Equal(80, smoothed[101].Y, 6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmoothRatioLevels_AveragesDecibelsWithoutTheMagnitudeBias(bool psychoacoustic)
    {
        // A ratio curve averages decibels (~-3 dB at a -6 dB step); a power mean would read ~-2.0 dB.
        var ratio = new List<SignalPoint>();
        for (int i = 0; i < 400; i++)
        {
            double f = 100 * Math.Pow(2, i / 40.0);
            ratio.Add(new SignalPoint(f, i == 300 ? double.NaN : i < 200 ? 0 : -6));
        }

        List<SignalPoint> smoothed =
            DataHelper.SmoothRatioLevels(ratio, 1.0 / 3.0, psychoacoustic);

        Assert.InRange(smoothed[199].Y, -3.2, -2.4);
        Assert.Equal(0, smoothed[100].Y, 6);
        Assert.Equal(-6, smoothed[380].Y, 6);
        Assert.True(double.IsNaN(smoothed[300].Y));
        Assert.Equal(-6, smoothed[299].Y, 6);
        Assert.Equal(-6, smoothed[301].Y, 6);
    }

    [Fact]
    public void SmoothRatioLevels_KeepsThePsychoacousticBandwidth()
    {
        // The psychoacoustic width schedule (1/3 oct below 100 Hz to 1/6 by 1 kHz) must survive dropping the cubic weighting.
        List<SignalPoint> WithNotchAt(double frequency)
        {
            var curve = new List<SignalPoint>();
            for (int i = 0; i < 600; i++)
            {
                double f = 20 * Math.Pow(2, i / 60.0);
                curve.Add(new SignalPoint(
                    f, Math.Abs(Math.Log2(f / frequency)) < 1.0 / 120 ? -12 : 0));
            }

            return curve;
        }

        static double DepthAt(List<SignalPoint> curve, double frequency, bool psychoacoustic)
        {
            List<SignalPoint> smoothed = DataHelper.SmoothRatioLevels(
                curve,
                psychoacoustic
                    ? SpectrumSmoothing.SmoothingOctaves(SpectrumSmoothing.PsychoacousticCode)
                    : 1.0 / 6.0,
                psychoacoustic);
            return smoothed
                .Where(point => Math.Abs(Math.Log2(point.X / frequency)) < 1.0 / 120)
                .Min(point => point.Y);
        }

        List<SignalPoint> bassNotch = WithNotchAt(50);
        List<SignalPoint> trebleNotch = WithNotchAt(4_000);

        Assert.True(
            DepthAt(bassNotch, 50, psychoacoustic: true) >
            DepthAt(bassNotch, 50, psychoacoustic: false) + 0.3,
            "the psychoacoustic mode must smooth wider than 1/6 octave in the bass");

        Assert.Equal(
            DepthAt(trebleNotch, 4_000, psychoacoustic: false),
            DepthAt(trebleNotch, 4_000, psychoacoustic: true),
            1);
    }

    private static float[] CreateSine(int length, int bin)
    {
        var samples = new float[length];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)Math.Sin(2.0 * Math.PI * bin * i / length);
        }

        return samples;
    }

    private static float[] WhiteNoise(int length, int seed)
    {
        var rng = new Random(seed);
        var samples = new float[length];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
        }

        return samples;
    }
}
