namespace Resonalyze.Dsp.Tests;

public sealed class LogarithmicPowerBandResampleTests
{
    private const int SampleRate = 48_000;

    [Fact]
    public void PowerBandLevels_AreFftLengthInvariant_ForTheSameNoise()
    {
        // The whole point of the power-integrated RTA: the same acoustic noise must
        // read the same absolute band level whatever the FFT size. The old amplitude
        // path dropped a broadband level ~3.01 dB per doubling of N; this must not.
        float[] signal = WhiteNoise(2_048 * 200, seed: 20260718);

        List<SignalPoint> bands2048 = BandLevels(signal, 2_048);
        List<SignalPoint> bands4096 = BandLevels(signal, 4_096);

        // The two FFT sizes clamp their grids to their own resolved range, so the log
        // grids no longer share a frequency per index — compare at matching frequencies
        // (nearest point in each), over a mid band where every band holds several bins
        // so the estimate is stable and the comparison is about scale, not variance.
        double sum2048 = 0.0;
        double sum4096 = 0.0;
        double maxAbsDiff = 0.0;
        int count = 0;
        // Compare from 1 kHz up, where the fixed 1/12-octave band is wider than the
        // rectangular main lobe at both FFT sizes, so the band (and level) is
        // FFT-independent rather than resolution-limited.
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
        // A 3 dB FFT-size error would blow straight past this; 0.5 dB leaves room for
        // the finite-average estimation scatter of two independent noise runs.
        Assert.True(meanDiff < 0.5, $"mean band level differed by {meanDiff:0.000} dB across FFT sizes");
        Assert.True(maxAbsDiff < 2.0, $"a band differed by {maxAbsDiff:0.000} dB across FFT sizes");
    }

    [Theory]
    // Above each window's main-lobe crossover (where the fixed 1/12-octave band already
    // exceeds the lobe at BOTH FFT sizes) the band is FFT-independent. The lower bound is
    // the N=2048 crossover with margin: Hann (4-bin lobe) ~1.6 kHz, Flat Top (10-bin lobe)
    // ~4.1 kHz. The old invariance test only exercised Rectangular from 1 kHz, whose lobe
    // crosses at ~0.97 kHz, so it never caught a wide-lobe window in its resolved region.
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
        // The documented cost of the main-lobe floor: below the crossover the band width
        // IS the main lobe (mainLobeBins·Fs/N), which halves when N doubles, so a broadband
        // level drops ~3 dB per doubling — the resolution limit of a single FFT, not a bug.
        // Pin it so a future change cannot silently claim invariance it does not have here.
        // At 300 Hz the Hann lobe (~94 Hz at 2048, ~47 Hz at 4096) dwarfs the 1/12-octave
        // band (~17 Hz), so both FFT sizes are floored to their lobe.
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
        // A power sum over a band containing a full-scale on-bin tone (rectangular,
        // leakage-free, ENBW 1) must still read 0 dBFS — the same level the tone
        // calibration is anchored to, so the SPL offset stays valid for tones.
        const int length = 2_048;
        const int bin = 256; // 256 * 48000 / 2048 = 6000 Hz
        double toneFrequency = (double)bin * SampleRate / length;

        // Smoothing off, so the tone reads its band level directly; smoothing would
        // (correctly) dilute a spike toward the surrounding silence.
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
        // Under a Hann window the coherent-gain-normalized bin power over-states a
        // noise band by the window ENBW (~1.5). Dividing by ENBW must bring the Hann
        // band level back onto the rectangular one for the same noise.
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
        // The bug: smoothing was the integration band width, so wider smoothing swept
        // more power into each band and lifted the whole curve (up to ~20 dB on a quiet
        // signal). With a fixed reference band and level-preserving averaging, the mid
        // band level must stay put from Smoothing Off through the widest setting.
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
        // At 32 kHz the 20 kHz request sits above Nyquist, so the grid clamps below it.
        // For a perfectly flat spectrum the 1/6-octave band power rises monotonically
        // with frequency (wider bands hold more power), so the last emitted band must be
        // ABOVE the band an octave below it. A band straddling Nyquist would instead be
        // half-empty and dip below it — the roll-off artifact.
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
        // With smoothing off, the 1024-point log grid is far finer than the FFT bins
        // near 1 kHz. The old whole-bin-or-fraction rule made a band that caught a bin
        // centre read ~5.4 dB above its neighbour that did not. Fractional-overlap
        // integration plus the resolution floor must keep adjacent bands within a
        // fraction of that, dominated only by the noise estimate's own scatter.
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
        // At a 32 kHz sample rate Nyquist is 16 kHz, but the plot still asks for 20 kHz.
        // The grid must stop at the highest resolved bin instead of reusing it to
        // invent a 16–20 kHz region.
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
        // The grid should actually reach up toward Nyquist, not stop far short.
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
        // The resolution floor is the window's main lobe, not its ENBW, so a full-scale
        // tone keeps its whole main lobe and reads its calibrated 0 dBFS level even with
        // narrow smoothing and a wide-lobe window — an ENBW floor left a bin-centred Hann
        // tone ~1.25 dB low.
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

    // Mean over 1–5 kHz, where the fixed 1/12-octave reference band is wider than any
    // window's main lobe, so every window integrates the same band (isolating the ENBW
    // correction / smoothing under test from the low-frequency resolution limit).
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

    // Averages the mic auto-power over non-overlapping frames, matching the live
    // analyzer's accumulation, so the amplitude spectrum is a stable noise estimate.
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
