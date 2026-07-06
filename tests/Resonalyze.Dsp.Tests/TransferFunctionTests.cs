using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp.Tests;

public sealed class TransferFunctionTests
{
    [Theory]
    [InlineData(20.37)]
    [InlineData(33.62)]
    [InlineData(128.5)]
    public void EstimatePhaseTransformDelay_RecoversFractionalDelay(double trueDelay)
    {
        double[] reference = BandLimitedBurst(4096, seed: 1234);
        double[] target = FractionalDelay(reference, trueDelay);
        int coarse = (int)Math.Round(trueDelay);

        PhaseTransformDelay result = TransferFunction.EstimatePhaseTransformDelay(
            reference, target, coarse, searchRadiusSamples: 4);

        Assert.True(result.Refined);
        Assert.True(result.PeakCorrelation > 0.5);
        // The whitened peak with the sinc + parabolic finish resolves the delay to
        // a hundredth of a sample, far tighter than the whole-sample envelope
        // estimate it replaces.
        Assert.InRange(result.LagSamples, trueDelay - 0.01, trueDelay + 0.01);
    }

    [Fact]
    public void EstimatePhaseTransformDelay_RecoversDelayForInvertedPolarity()
    {
        const double trueDelay = 27.4;
        double[] reference = BandLimitedBurst(4096, seed: 55);
        double[] target = FractionalDelay(reference, trueDelay)
            .Select(sample => -sample)
            .ToArray();
        int coarse = (int)Math.Round(trueDelay);

        PhaseTransformDelay result = TransferFunction.EstimatePhaseTransformDelay(
            reference, target, coarse, searchRadiusSamples: 4);

        // The envelope path is polarity-blind; the whitened refinement must be too,
        // finding the delay in the negative trough rather than a positive side lobe.
        Assert.True(result.Refined);
        Assert.True(result.PeakCorrelation > 0.5);
        Assert.InRange(result.LagSamples, trueDelay - 0.05, trueDelay + 0.05);
    }

    [Fact]
    public void RefineAround_ReusesOneTransformForMultipleLags()
    {
        double[] reference = BandLimitedBurst(4096, seed: 321);
        double[] target = FractionalDelay(reference, 45.3);

        PhaseTransformCorrelation correlation =
            TransferFunction.ComputePhaseTransform(reference, target);
        PhaseTransformDelay shared = correlation.RefineAround(45, searchRadiusSamples: 4);
        PhaseTransformDelay oneShot = TransferFunction.EstimatePhaseTransformDelay(
            reference, target, 45, searchRadiusSamples: 4);

        Assert.Equal(oneShot.LagSamples, shared.LagSamples, precision: 12);
        Assert.Equal(oneShot.PeakCorrelation, shared.PeakCorrelation, precision: 12);
    }

    [Fact]
    public void EstimatePhaseTransformDelay_FlagsUntrustedWhenPeakOutsideWindow()
    {
        double[] reference = BandLimitedBurst(4096, seed: 99);
        double[] target = FractionalDelay(reference, 40.0);

        // Anchor just past the true delay so the peak sits one sample outside the
        // window: the in-window maximum lands on the edge and is not trusted.
        PhaseTransformDelay result = TransferFunction.EstimatePhaseTransformDelay(
            reference, target, coarseLagSamples: 44, searchRadiusSamples: 3);

        Assert.False(result.Refined);
    }

    [Fact]
    public void EstimatePhaseTransformDelay_ReportsWeakPeakForAnUnrelatedAnchor()
    {
        double[] reference = BandLimitedBurst(4096, seed: 7);
        double[] target = FractionalDelay(reference, 40.0);

        // Far from any arrival the whitened correlation is just noise, so the peak
        // height stays low — the signal the caller uses to keep its coarse estimate.
        PhaseTransformDelay result = TransferFunction.EstimatePhaseTransformDelay(
            reference, target, coarseLagSamples: 400, searchRadiusSamples: 3);

        Assert.True(result.PeakCorrelation < 0.2);
    }

    // A band-limited noise burst windowed to zero at both ends, so a circular
    // fractional shift of it matches the linear delay the estimator sees after
    // its internal zero-padding.
    private static double[] BandLimitedBurst(int length, int seed)
    {
        var random = new Random(seed);
        var spectrum = new Complex[length];
        int maxBin = length * 2 / 5;
        for (int k = 1; k <= maxBin; k++)
        {
            Complex bin = Complex.FromPolarCoordinates(
                1.0, random.NextDouble() * 2.0 * Math.PI);
            spectrum[k] = bin;
            spectrum[length - k] = Complex.Conjugate(bin);
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);
        var signal = new double[length];
        for (int i = 0; i < length; i++)
        {
            double window = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (length - 1));
            signal[i] = spectrum[i].Real * window;
        }

        return signal;
    }

    // An exact fractional delay via a frequency-domain linear-phase ramp.
    private static double[] FractionalDelay(double[] input, double delaySamples)
    {
        int length = input.Length;
        var spectrum = new Complex[length];
        for (int i = 0; i < length; i++)
        {
            spectrum[i] = new Complex(input[i], 0.0);
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);
        for (int k = 0; k < length; k++)
        {
            double frequency = k <= length / 2 ? k : k - length;
            double angle = -2.0 * Math.PI * frequency * delaySamples / length;
            spectrum[k] *= Complex.FromPolarCoordinates(1.0, angle);
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);
        var output = new double[length];
        for (int i = 0; i < length; i++)
        {
            output[i] = spectrum[i].Real;
        }

        return output;
    }

    [Fact]
    public void ComputeRelativeIr_IdenticalSignalsProducesUnitImpulse()
    {
        double[] reference = CreateImpulse(128);

        double[] ir = TransferFunction.ComputeRelativeIr(reference, reference);

        Assert.Equal(256, ir.Length);
        Assert.Equal(1.0, ir[0], precision: 9);
        Assert.All(
            ir.Skip(1).Take(127),
            sample => Assert.Equal(0.0, sample, precision: 8));
    }

    [Fact]
    public void ComputeRelativeIr_RecoversRelativeDelay()
    {
        const int delay = 17;
        double[] reference = CreateImpulse(128);
        double[] target = Delay(reference, delay);

        double[] ir = TransferFunction.ComputeRelativeIr(reference, target);

        int peakIndex = FindPeakIndex(ir);
        Assert.Equal(delay, peakIndex);
        Assert.Equal(1.0, ir[delay], precision: 9);
    }

    [Fact]
    public void ComputeRelativeIr_RecoversRelativeGain()
    {
        const double gain = 0.375;
        double[] reference = CreateImpulse(128);
        double[] target = reference.Select(sample => sample * gain).ToArray();

        double[] ir = TransferFunction.ComputeRelativeIr(reference, target);

        Assert.Equal(gain, ir[0], precision: 9);
        Assert.All(
            ir.Skip(1).Take(127),
            sample => Assert.Equal(0.0, sample, precision: 8));
    }

    [Fact]
    public void ComputeRelativeIr_RejectsMismatchedLengths()
    {
        Assert.Throws<ArgumentException>(() =>
            TransferFunction.ComputeRelativeIr([1.0, 2.0], [1.0]));
    }

    [Fact]
    public void ComputeAveragedRelativeIr_SingleRunDoesNotReportCoherence()
    {
        double[] reference = CreateImpulse(128);

        TransferEstimateResult result = TransferFunction.ComputeAveragedRelativeIr(
            [new TransferFunctionFrame(reference, reference)]);

        Assert.Null(result.Coherence);
        Assert.Equal(0, result.PeakIndex);
        Assert.Equal(1.0, result.ImpulseResponse[0], precision: 9);
    }

    [Fact]
    public void ComputeAveragedRelativeIr_MultipleRunsReportsCoherence()
    {
        const int delay = 9;
        double[] reference = CreateImpulse(128);
        double[] target = Delay(reference, delay);

        TransferEstimateResult result = TransferFunction.ComputeAveragedRelativeIr(
            [
                new TransferFunctionFrame(reference, target),
                new TransferFunctionFrame(reference, target),
                new TransferFunctionFrame(reference, target)
            ]);

        Assert.NotNull(result.Coherence);
        Assert.Equal(delay, result.PeakIndex);
        Assert.Equal(1.0, result.ImpulseResponse[delay], precision: 9);
        Assert.All(result.Coherence!, value => Assert.InRange(value, 0.0, 1.0));
    }

    private static double[] CreateImpulse(int length)
    {
        var impulse = new double[length];
        impulse[0] = 1.0;
        return impulse;
    }

    private static double[] Delay(double[] input, int delay)
    {
        var output = new double[input.Length];
        for (int i = 0; i + delay < input.Length; i++)
        {
            output[i + delay] = input[i];
        }

        return output;
    }

    private static int FindPeakIndex(IReadOnlyList<double> samples)
    {
        int peakIndex = 0;
        double peakMagnitude = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            double magnitude = Math.Abs(samples[i]);
            if (magnitude > peakMagnitude)
            {
                peakMagnitude = magnitude;
                peakIndex = i;
            }
        }

        return peakIndex;
    }
}
