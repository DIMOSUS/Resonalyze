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

        // Coherent-gain normalization makes the windowed result match the
        // rectangular-window level: |X[0]| = N, so power = N^2.
        Assert.Equal((double)length * length, power[0], precision: 3);
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
