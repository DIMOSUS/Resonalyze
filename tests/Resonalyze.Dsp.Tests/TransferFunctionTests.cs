using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp.Tests;

public sealed class TransferFunctionTests
{
    [Theory]
    [InlineData(50.35)]
    [InlineData(128.6)]
    public void ComputePhaseTransformFromResponse_RecoversDelayFromTheIrAlone(
        double trueDelay)
    {
        // A transfer IR's spectrum already carries the cross-phase, so whitening it
        // recovers the same delay a two-channel GCC-PHAT would.
        double[] impulseResponse = BandLimitedPulse(4096, trueDelay);
        int coarse = (int)Math.Round(trueDelay);

        PhaseTransformCorrelation correlation =
            TransferFunction.ComputePhaseTransformFromResponse(impulseResponse);
        PhaseTransformDelay result = correlation.RefineAround(coarse, searchRadiusSamples: 4);

        Assert.True(result.Refined);
        Assert.True(result.PeakCorrelation > 0.5);
        Assert.InRange(result.LagSamples, trueDelay - 0.02, trueDelay + 0.02);
    }

    [Fact]
    public void ComputePhaseTransformFromResponse_RecoversDelayForInvertedPolarity()
    {
        const double trueDelay = 27.4;
        double[] impulseResponse = BandLimitedPulse(4096, trueDelay)
            .Select(sample => -sample)
            .ToArray();
        int coarse = (int)Math.Round(trueDelay);

        PhaseTransformDelay result = TransferFunction
            .ComputePhaseTransformFromResponse(impulseResponse)
            .RefineAround(coarse, searchRadiusSamples: 4);

        // The envelope path is polarity-blind; the whitened refinement must be too,
        // finding the delay in the negative trough rather than a positive side lobe.
        Assert.True(result.Refined);
        Assert.True(result.PeakCorrelation > 0.5);
        Assert.InRange(result.LagSamples, trueDelay - 0.02, trueDelay + 0.02);
    }

    [Fact]
    public void ComputePhaseTransformFromResponse_PadsAnOddLengthToAPowerOfTwo()
    {
        // A non-power-of-two IR takes the padded radix-2 path; the correlation
        // stays index-aligned with the impulse response, so the peak still lands
        // on the pulse position.
        const double trueDelay = 41.3;
        double[] impulseResponse = BandLimitedPulse(4096, trueDelay)
            .Take(4095)
            .ToArray();

        PhaseTransformDelay result = TransferFunction
            .ComputePhaseTransformFromResponse(impulseResponse)
            .RefineAround(41, searchRadiusSamples: 4);

        Assert.True(result.Refined);
        Assert.InRange(result.LagSamples, trueDelay - 0.1, trueDelay + 0.1);
    }

    [Fact]
    public void RefineAround_FlagsUntrustedWhenPeakOutsideWindow()
    {
        double[] impulseResponse = BandLimitedPulse(4096, 40.0);

        // Anchor just past the true delay so the peak sits one sample outside the
        // window: the in-window maximum lands on the edge and is not trusted.
        PhaseTransformDelay result = TransferFunction
            .ComputePhaseTransformFromResponse(impulseResponse)
            .RefineAround(coarseLagSamples: 44, searchRadiusSamples: 3);

        Assert.False(result.Refined);
    }

    [Fact]
    public void RefineAround_ReportsWeakPeakForAnUnrelatedAnchor()
    {
        double[] impulseResponse = BandLimitedPulse(4096, 40.0);

        // Far from any arrival the whitened correlation is just noise, so the peak
        // height stays low — the signal the caller uses to keep its coarse estimate.
        PhaseTransformDelay result = TransferFunction
            .ComputePhaseTransformFromResponse(impulseResponse)
            .RefineAround(coarseLagSamples: 400, searchRadiusSamples: 3);

        Assert.True(result.PeakCorrelation < 0.2);
    }

    // A band-limited pulse at a fractional position, built from a flat-magnitude
    // linear-phase spectrum — a stand-in transfer IR whose delay is known exactly.
    private static double[] BandLimitedPulse(int length, double delaySamples)
    {
        var spectrum = new Complex[length];
        spectrum[0] = Complex.One;
        int maxBin = length * 2 / 5;
        for (int k = 1; k <= maxBin; k++)
        {
            double angle = -2.0 * Math.PI * k * delaySamples / length;
            Complex bin = Complex.FromPolarCoordinates(1.0, angle);
            spectrum[k] = bin;
            spectrum[length - k] = Complex.Conjugate(bin);
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);
        var pulse = new double[length];
        for (int i = 0; i < length; i++)
        {
            pulse[i] = spectrum[i].Real;
        }

        return pulse;
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
