using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class SpectrumAnalysisTests
{
    [Fact]
    public void ComputeTransferMagnitudeSpectrum_IdenticalImpulseIsFlatUnity()
    {
        double[] spectrum = SpectrumAnalysis.ComputeTransferMagnitudeSpectrum(
            CreateImpulse(128),
            CreateImpulse(128));

        Assert.All(spectrum, magnitude => Assert.Equal(1.0, magnitude, precision: 9));
    }

    [Fact]
    public void ComputeTransferMagnitudeSpectrum_RecoversRelativeGain()
    {
        const double gain = 0.25;
        float[] reference = CreateImpulse(128);
        float[] target = reference.Select(sample => (float)(sample * gain)).ToArray();

        double[] spectrum = SpectrumAnalysis.ComputeTransferMagnitudeSpectrum(reference, target);

        Assert.All(spectrum, magnitude => Assert.Equal(gain, magnitude, precision: 9));
    }

    [Fact]
    public void ComputeTransferMagnitudeSpectrum_TracksFrequencyDependentGain()
    {
        const int length = 1024;
        const int lowBin = 12;
        const int highBin = 120;
        const double highFrequencyGain = 0.2;

        float[] low = CreateSine(length, lowBin);
        float[] high = CreateSine(length, highBin);
        float[] reference = Mix(low, high, highGain: 1.0);
        float[] target = Mix(low, high, highGain: highFrequencyGain);

        double[] spectrum = SpectrumAnalysis.ComputeTransferMagnitudeSpectrum(reference, target);

        Assert.InRange(spectrum[lowBin], 0.95, 1.05);
        Assert.InRange(spectrum[highBin], highFrequencyGain - 0.03, highFrequencyGain + 0.03);
    }

    [Fact]
    public void ComputeH1MagnitudeSpectrum_AveragingSuppressesUncorrelatedTargetNoise()
    {
        const int length = 1024;
        const int bin = 24;
        const double gain = 0.5;
        const int frameCount = 64;

        Complex[]? accumulatedCross = null;
        double[]? accumulatedPower = null;
        float[] reference = CreateSine(length, bin);

        for (int frame = 0; frame < frameCount; frame++)
        {
            float[] target = AddDeterministicNoise(
                reference.Select(sample => (float)(sample * gain)).ToArray(),
                frame);
            TransferSpectrumFrame spectrumFrame =
                SpectrumAnalysis.ComputeTransferSpectrumFrame(reference, target);

            accumulatedCross ??= new Complex[spectrumFrame.CrossSpectrum.Length];
            accumulatedPower ??= new double[spectrumFrame.ReferencePowerSpectrum.Length];
            for (int i = 0; i < accumulatedCross.Length; i++)
            {
                accumulatedCross[i] += spectrumFrame.CrossSpectrum[i];
                accumulatedPower[i] += spectrumFrame.ReferencePowerSpectrum[i];
            }
        }

        double[] spectrum = SpectrumAnalysis.ComputeH1MagnitudeSpectrum(
            accumulatedCross!,
            accumulatedPower!);

        Assert.InRange(spectrum[bin], gain - 0.03, gain + 0.03);
    }

    [Fact]
    public void ComputePowerSpectrum_DcLevelIsWindowInvariant()
    {
        const int length = 128;
        var ones = new float[length];
        Array.Fill(ones, 1.0f);

        double[] power = SpectrumAnalysis.ComputePowerSpectrum(ones);

        // Tone-calibrated (dBFS) scale: a DC level of 1.0 reads amplitude 1.0
        // regardless of the FFT length.
        Assert.Equal(1.0, power[0], precision: 3);
    }

    [Theory]
    [InlineData(WindowType.Hann)]
    [InlineData(WindowType.FlatTop)]
    [InlineData(WindowType.BlackmanHarris)]
    [InlineData(WindowType.Rectangular)]
    public void ComputePowerSpectrum_DcLevelIsWindowInvariantForAllWindows(WindowType windowType)
    {
        const int length = 128;
        var ones = new float[length];
        Array.Fill(ones, 1.0f);

        double[] power = SpectrumAnalysis.ComputePowerSpectrum(ones, windowType);

        // Coherent-gain normalization cancels the window sum, so DC always
        // reads the true 1.0 level regardless of window.
        Assert.Equal(1.0, power[0], precision: 3);
    }

    [Fact]
    public void CreateWindow_RectangularIsUnity()
    {
        double[] window = Windowing.CreateAnalysisWindow(WindowType.Rectangular, 16);

        Assert.All(window, value => Assert.Equal(1.0, value, precision: 12));
    }

    [Fact]
    public void CreateWindow_HannHasZeroEndpointsAndUnityCenter()
    {
        double[] window = Windowing.CreateAnalysisWindow(WindowType.Hann, 65);

        Assert.Equal(0.0, window[0], precision: 9);
        Assert.Equal(0.0, window[^1], precision: 9);
        Assert.Equal(1.0, window[32], precision: 9);
    }

    [Theory]
    [InlineData(WindowType.FlatTop)]
    [InlineData(WindowType.BlackmanHarris)]
    public void ComputePowerSpectrum_PeaksAtToneBinForWindow(WindowType windowType)
    {
        const int length = 512;
        const int bin = 40;

        double[] power = SpectrumAnalysis.ComputePowerSpectrum(
            CreateSine(length, bin),
            windowType);

        int peakBin = 0;
        double peak = double.NegativeInfinity;
        for (int i = 1; i < power.Length; i++)
        {
            if (power[i] > peak)
            {
                peak = power[i];
                peakBin = i;
            }
        }

        Assert.Equal(bin, peakBin);
    }

    [Fact]
    public void ComputePowerSpectrum_PeaksAtToneBin()
    {
        const int length = 256;
        const int bin = 20;

        double[] power = SpectrumAnalysis.ComputePowerSpectrum(CreateSine(length, bin));

        int peakBin = 0;
        double peak = double.NegativeInfinity;
        for (int i = 1; i < power.Length; i++)
        {
            if (power[i] > peak)
            {
                peak = power[i];
                peakBin = i;
            }
        }

        Assert.Equal(bin, peakBin);
    }

    [Theory]
    [InlineData(1_024)]
    [InlineData(4_096)]
    public void ComputeInputMagnitudeSpectrum_ToneLevelIsFftLengthInvariant(int length)
    {
        // The same full-scale tone must read amplitude 1.0 whatever the FFT
        // size — the RTA level used to jump 6.02 dB per doubling, on the same
        // dB axis as the length-invariant H1 transfer gain.
        int bin = length / 16;
        float[] signal = CreateSine(length, bin);

        TransferSpectrumFrame frame =
            SpectrumAnalysis.ComputeTransferSpectrumFrame(signal, signal, WindowType.Hann);
        double[] magnitude = SpectrumAnalysis.ComputeInputMagnitudeSpectrum(
            frame.TargetPowerSpectrum,
            WindowType.Hann,
            length);

        Assert.InRange(magnitude[bin], 0.98, 1.02);
    }

    [Fact]
    public void ComputePowerSpectrum_RejectsEmpty()
    {
        Assert.Throws<ArgumentException>(
            () => SpectrumAnalysis.ComputePowerSpectrum(Array.Empty<float>()));
    }

    [Fact]
    public void ComputeTransferSpectrumFrame_TargetPowerTracksGain()
    {
        const double gain = 0.25;
        float[] reference = CreateImpulse(128);
        float[] target = reference.Select(sample => (float)(sample * gain)).ToArray();

        TransferSpectrumFrame frame =
            SpectrumAnalysis.ComputeTransferSpectrumFrame(reference, target);

        for (int i = 0; i < frame.ReferencePowerSpectrum.Length; i++)
        {
            Assert.Equal(
                frame.ReferencePowerSpectrum[i] * gain * gain,
                frame.TargetPowerSpectrum[i],
                precision: 9);
        }
    }

    [Fact]
    public void ComputeCoherence_FullyCorrelatedIsUnityPartialIsLess()
    {
        Complex[] cross = [new Complex(2.0, 0.0), new Complex(1.0, 0.0), new Complex(10.0, 0.0)];
        double[] referencePower = [2.0, 4.0, 1.0];
        double[] targetPower = [2.0, 4.0, 1.0];

        double[] coherence = SpectrumAnalysis.ComputeCoherence(
            cross,
            referencePower,
            targetPower);

        Assert.Equal(1.0, coherence[0], precision: 9);
        Assert.Equal(0.0625, coherence[1], precision: 9);
        Assert.Equal(1.0, coherence[2], precision: 9); // clamped from 100
    }

    [Fact]
    public void ComputeCoherence_HighAtCorrelatedSignalBinUnderNoise()
    {
        const int length = 1024;
        const int bin = 24;
        const double gain = 0.5;
        const int frameCount = 64;

        Complex[]? cross = null;
        double[]? referencePower = null;
        double[]? targetPower = null;
        float[] reference = CreateSine(length, bin);

        for (int frame = 0; frame < frameCount; frame++)
        {
            float[] target = AddDeterministicNoise(
                reference.Select(sample => (float)(sample * gain)).ToArray(),
                frame);
            TransferSpectrumFrame spectrumFrame =
                SpectrumAnalysis.ComputeTransferSpectrumFrame(reference, target);

            cross ??= new Complex[spectrumFrame.CrossSpectrum.Length];
            referencePower ??= new double[spectrumFrame.ReferencePowerSpectrum.Length];
            targetPower ??= new double[spectrumFrame.TargetPowerSpectrum.Length];
            for (int i = 0; i < cross.Length; i++)
            {
                cross[i] += spectrumFrame.CrossSpectrum[i];
                referencePower[i] += spectrumFrame.ReferencePowerSpectrum[i];
                targetPower[i] += spectrumFrame.TargetPowerSpectrum[i];
            }
        }

        double[] coherence = SpectrumAnalysis.ComputeCoherence(
            cross!,
            referencePower!,
            targetPower!);

        Assert.All(coherence, value => Assert.InRange(value, 0.0, 1.0));
        Assert.True(coherence[bin] > 0.9, $"coherence at signal bin was {coherence[bin]}");
    }

    [Fact]
    public void DebiasCoherence_MapsTheNullExpectationToZero()
    {
        // The raw MSC over K averages reads 1/K for pure noise — at K = 2 that
        // is 0.5, exactly at the trust thresholds downstream. The correction
        // must send the null expectation to 0, keep 1 at 1, and collapse the
        // no-estimate case (K = 1) entirely.
        Assert.Equal(0.0, SpectrumAnalysis.DebiasCoherence([0.5], 2)[0], 9);
        Assert.Equal(0.0, SpectrumAnalysis.DebiasCoherence([0.25], 4)[0], 9);
        Assert.Equal(1.0, SpectrumAnalysis.DebiasCoherence([1.0], 2)[0], 9);
        Assert.Equal(1.0, SpectrumAnalysis.DebiasCoherence([0.9], 1)[0] + 1.0, 9);
        // Below the null expectation clamps at zero rather than going negative.
        Assert.Equal(0.0, SpectrumAnalysis.DebiasCoherence([0.1], 2)[0], 9);
        // Large K leaves an honest estimate nearly untouched.
        Assert.Equal(0.9, SpectrumAnalysis.DebiasCoherence([0.9], 1000)[0], 3);
    }

    [Fact]
    public void ComputeAveragedRelativeIr_TwoNoiseFramesReadMostlyIncoherent()
    {
        // Two frames whose targets are unrelated noise: the raw two-average MSC
        // averages ~0.5 across the band (estimator bias, not information) and
        // used to sit exactly at the unwrap trust floor. The stored coherence
        // is debiased, so it must average well below that.
        const int length = 2_048;
        float[] reference = CreateSine(length, bin: 24);
        var frames = new List<TransferFunctionFrame>();
        for (int frame = 0; frame < 2; frame++)
        {
            var target = new double[length];
            for (int i = 0; i < length; i++)
            {
                // Deterministic pseudo-noise, different per frame.
                target[i] = Math.Sin(i * (12.9898 + frame * 3.7) + frame * 78.233)
                    * Math.Sin(i * 0.7301 + frame);
            }
            frames.Add(new TransferFunctionFrame(
                Array.ConvertAll(reference, sample => (double)sample),
                target));
        }

        TransferEstimateResult result =
            TransferFunction.ComputeAveragedRelativeIr(frames);

        Assert.NotNull(result.Coherence);
        double mean = result.Coherence!.Average();
        Assert.True(
            mean < 0.3,
            $"debiased two-average noise coherence averaged {mean:0.000}");
    }

    [Fact]
    public void ComputeCoherence_RejectsMismatchedLengths()
    {
        Assert.Throws<ArgumentException>(() =>
            SpectrumAnalysis.ComputeCoherence(
                [Complex.One],
                [1.0],
                [1.0, 2.0]));
    }

    [Fact]
    public void ComputeTransferMagnitudeSpectrum_RejectsMismatchedLengths()
    {
        Assert.Throws<ArgumentException>(() =>
            SpectrumAnalysis.ComputeTransferMagnitudeSpectrum([1.0f], [1.0f, 0.0f]));
    }

    [Fact]
    public void ComputeH1MagnitudeSpectrum_RejectsMismatchedLengths()
    {
        Assert.Throws<ArgumentException>(() =>
            SpectrumAnalysis.ComputeH1MagnitudeSpectrum(
                [Complex.One],
                [1.0, 2.0]));
    }

    [Theory]
    [InlineData(WindowType.Hann)]
    [InlineData(WindowType.FlatTop)]
    [InlineData(WindowType.BlackmanHarris)]
    [InlineData(WindowType.Rectangular)]
    public void ComputeInputMagnitudeSpectrum_EqualsSqrtOfPowerSpectrum(WindowType windowType)
    {
        const int length = 512;
        const int bin = 40;
        float[] signal = CreateSine(length, bin);

        TransferSpectrumFrame frame =
            SpectrumAnalysis.ComputeTransferSpectrumFrame(signal, signal, windowType);
        double[] magnitude = SpectrumAnalysis.ComputeInputMagnitudeSpectrum(
            frame.TargetPowerSpectrum,
            windowType,
            length);
        double[] power = SpectrumAnalysis.ComputePowerSpectrum(signal, windowType);

        // The RTA magnitude is coherent-gain-normalized just like the trusted
        // single-block power spectrum, so it must equal its square root exactly
        // (same window, same |FFT|², same scale).
        Assert.Equal(power.Length, magnitude.Length);
        for (int i = 0; i < magnitude.Length; i++)
        {
            Assert.Equal(Math.Sqrt(power[i]), magnitude[i], precision: 9);
        }
    }

    [Theory]
    [InlineData(WindowType.Hann)]
    [InlineData(WindowType.FlatTop)]
    [InlineData(WindowType.BlackmanHarris)]
    [InlineData(WindowType.Rectangular)]
    public void ComputeInputMagnitudeSpectrum_ToneLevelIsWindowInvariant(WindowType windowType)
    {
        const int length = 512;
        const int bin = 40;
        // A full-scale tone sitting exactly on a bin: coherent-gain
        // normalization must recover the same peak amplitude for every window.
        float[] signal = CreateSine(length, bin);

        TransferSpectrumFrame frame =
            SpectrumAnalysis.ComputeTransferSpectrumFrame(signal, signal, windowType);
        double[] magnitude = SpectrumAnalysis.ComputeInputMagnitudeSpectrum(
            frame.TargetPowerSpectrum,
            windowType,
            length);

        // Tone calibration: a full-scale on-bin sine reads amplitude 1.0 for
        // every window AND every FFT length (the level used to jump 6 dB per
        // FFT-size doubling). Allow 2% for the tiny double-frequency leakage.
        Assert.InRange(magnitude[bin], 0.98, 1.02);
    }

    [Fact]
    public void ComputeInputMagnitudeSpectrum_RejectsTooShortFrame()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SpectrumAnalysis.ComputeInputMagnitudeSpectrum([1.0], WindowType.Hann, 1));
    }

    [Theory]
    [InlineData(WindowType.Hann)]
    [InlineData(WindowType.Rectangular)]
    public void ComputeAutoPowerSpectrumFrame_MatchesTheTransferFrameTargetPower(WindowType windowType)
    {
        // The mic-only RTA path accumulates ComputeAutoPowerSpectrumFrame; it must
        // produce exactly the target auto-power the dual-channel transfer frame does,
        // so a reference-free capture reads the same RTA as one with a loopback.
        const int length = 512;
        float[] signal = CreateSine(length, bin: 40);

        double[] autoPower = SpectrumAnalysis.ComputeAutoPowerSpectrumFrame(signal, windowType);
        TransferSpectrumFrame frame =
            SpectrumAnalysis.ComputeTransferSpectrumFrame(signal, signal, windowType);

        Assert.Equal(frame.TargetPowerSpectrum.Length, autoPower.Length);
        for (int i = 0; i < autoPower.Length; i++)
        {
            Assert.Equal(frame.TargetPowerSpectrum[i], autoPower[i], precision: 9);
        }
    }

    [Fact]
    public void ComputeAutoPowerSpectrumFrame_RejectsEmpty()
    {
        Assert.Throws<ArgumentException>(
            () => SpectrumAnalysis.ComputeAutoPowerSpectrumFrame(Array.Empty<float>()));
    }

    private static float[] CreateImpulse(int length)
    {
        var impulse = new float[length];
        impulse[length / 2] = 1.0f;
        return impulse;
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

    private static float[] Mix(
        IReadOnlyList<float> low,
        IReadOnlyList<float> high,
        double highGain)
    {
        var samples = new float[low.Count];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(low[i] + high[i] * highGain);
        }

        return samples;
    }

    private static float[] AddDeterministicNoise(float[] samples, int frame)
    {
        var noisy = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            double noise =
                0.25 * Math.Sin(2.0 * Math.PI * (37 + frame % 11) * i / samples.Length + frame) +
                0.15 * Math.Sin(2.0 * Math.PI * (113 + frame % 7) * i / samples.Length);
            noisy[i] = (float)(samples[i] + noise);
        }

        return noisy;
    }
}
