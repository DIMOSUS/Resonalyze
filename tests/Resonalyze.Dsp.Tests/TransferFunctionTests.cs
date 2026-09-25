using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp.Tests;

public sealed class TransferFunctionTests
{
    [Fact]
    public void MeasureSingleFrameCompactness_MatchesJudgingEachTargetOnItsOwn()
    {
        // Gate and regularization depend on the reference alone, so the batched call must be bit-identical.
        var random = new Random(20260828);
        int length = 4096;
        var reference = new double[length];
        for (int i = 0; i < length; i++)
        {
            reference[i] = random.NextDouble() - 0.5;
        }

        var targets = new List<IReadOnlyList<double>>();
        foreach ((int delay, double scale) in new[] { (37, 0.7), (91, 0.25) })
        {
            var target = new double[length];
            for (int i = delay; i < length; i++)
            {
                target[i] = reference[i - delay] * scale;
            }

            targets.Add(target);
        }

        var noise = new double[length];
        for (int i = 0; i < length; i++)
        {
            noise[i] = (random.NextDouble() - 0.5) * 0.05;
        }

        targets.Add(noise);

        TransferIrCompactness?[] batched = TransferFunction.MeasureSingleFrameCompactness(
            reference, targets, ExcitationBandGate.FullBand, 48_000);

        Assert.Equal(targets.Count, batched.Length);
        for (int i = 0; i < targets.Count; i++)
        {
            (_, Complex[]? alone) = TransferFunction.ComputeAveragedMagnitudeAndIr(
                [new TransferFunctionFrame(reference, targets[i])],
                ExcitationBandGate.FullBand);
            TransferIrCompactness expected = Assert.IsType<TransferIrCompactness>(
                TransferIrDiagnostics.MeasureCompactness(
                    Assert.IsType<Complex[]>(alone), 48_000));
            TransferIrCompactness actual =
                Assert.IsType<TransferIrCompactness>(batched[i]);
            Assert.Equal(expected.InsideOutsideDb, actual.InsideOutsideDb, 12);
            Assert.Equal(expected.PeakDelayMs, actual.PeakDelayMs, 12);
        }
    }

    [Fact]
    public void TheRelativeIrAndMagnitudeTogether_AreEachAsComputedAlone()
    {
        var random = new Random(3);
        var frames = new List<TransferFunctionFrame>();
        for (int run = 0; run < 3; run++)
        {
            var reference = new double[512];
            var target = new double[512];
            for (int i = 0; i < reference.Length; i++)
            {
                reference[i] = random.NextDouble() - 0.5;
                target[i] = (i >= 5 ? 0.7 * reference[i - 5] : 0.0) + (random.NextDouble() - 0.5) * 0.01;
            }

            frames.Add(new TransferFunctionFrame(reference, target));
        }

        (TransferEstimateResult transfer, TransferMagnitudeEstimate magnitude) =
            TransferFunction.ComputeAveragedRelativeIrAndMagnitude(frames, ExcitationBandGate.FullBand);

        TransferEstimateResult transferAlone = TransferFunction.ComputeAveragedRelativeIr(frames, ExcitationBandGate.FullBand);
        Assert.Equal(transferAlone.ImpulseResponse, transfer.ImpulseResponse);
        Assert.Equal(transferAlone.PeakIndex, transfer.PeakIndex);
        Assert.Equal(transferAlone.Coherence, transfer.Coherence);
        Assert.Equal(
            TransferFunction.ComputeAveragedMagnitude(frames, ExcitationBandGate.FullBand).Magnitude,
            magnitude.Magnitude);
    }

    [Fact]
    public void MeasureSingleFrameCompactness_LeavesAnUnusableTargetNull()
    {
        var reference = new double[512];
        for (int i = 0; i < reference.Length; i++)
        {
            reference[i] = Math.Sin(i * 0.11);
        }

        // Silence is not 'usable but shapeless': it cannot be measured either.
        var usable = new double[512];
        for (int i = 7; i < usable.Length; i++)
        {
            usable[i] = reference[i - 7] * 0.6;
        }

        TransferIrCompactness?[] results = TransferFunction.MeasureSingleFrameCompactness(
            reference,
            [usable, new double[16], []],
            ExcitationBandGate.FullBand,
            48_000);

        Assert.NotNull(results[0]);
        Assert.Null(results[1]);
        Assert.Null(results[2]);
    }

    [Theory]
    [InlineData(50.35)]
    [InlineData(128.6)]
    public void ComputePhaseTransformFromResponse_RecoversDelayFromTheIrAlone(
        double trueDelay)
    {
        // A transfer IR's spectrum already carries the cross-phase, so whitening it matches two-channel GCC-PHAT.
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

        // The whitened refinement must be polarity-blind like the envelope path.
        Assert.True(result.Refined);
        Assert.True(result.PeakCorrelation > 0.5);
        Assert.InRange(result.LagSamples, trueDelay - 0.02, trueDelay + 0.02);
    }

    [Fact]
    public void ComputePhaseTransformFromResponse_PadsAnOddLengthToAPowerOfTwo()
    {
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

        // Peak one sample outside the window: the edge maximum is not trusted.
        PhaseTransformDelay result = TransferFunction
            .ComputePhaseTransformFromResponse(impulseResponse)
            .RefineAround(coarseLagSamples: 44, searchRadiusSamples: 3);

        Assert.False(result.Refined);
    }

    [Fact]
    public void RefineAround_ReportsWeakPeakForAnUnrelatedAnchor()
    {
        double[] impulseResponse = BandLimitedPulse(4096, 40.0);

        PhaseTransformDelay result = TransferFunction
            .ComputePhaseTransformFromResponse(impulseResponse)
            .RefineAround(coarseLagSamples: 400, searchRadiusSamples: 3);

        Assert.True(result.PeakCorrelation < 0.2);
    }

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
    public void ComputeAveragedRelativeIr_SingleFrameIdenticalSignalsProducesUnitImpulse()
    {
        double[] reference = CreateImpulse(128);

        double[] ir = TransferFunction.ComputeAveragedRelativeIr(
            [new TransferFunctionFrame(reference, reference)]).ImpulseResponse;

        Assert.Equal(256, ir.Length);
        Assert.Equal(1.0, ir[0], precision: 9);
        Assert.All(
            ir.Skip(1).Take(127),
            sample => Assert.Equal(0.0, sample, precision: 8));
    }

    [Fact]
    public void ComputeAveragedRelativeIr_SingleFrameRecoversRelativeDelay()
    {
        const int delay = 17;
        double[] reference = CreateImpulse(128);
        double[] target = Delay(reference, delay);

        TransferEstimateResult result = TransferFunction.ComputeAveragedRelativeIr(
            [new TransferFunctionFrame(reference, target)]);

        Assert.Equal(delay, result.PeakIndex);
        Assert.Equal(1.0, result.ImpulseResponse[delay], precision: 9);
    }

    [Fact]
    public void ComputeAveragedRelativeIr_SingleFrameRecoversRelativeGain()
    {
        const double gain = 0.375;
        double[] reference = CreateImpulse(128);
        double[] target = reference.Select(sample => sample * gain).ToArray();

        double[] ir = TransferFunction.ComputeAveragedRelativeIr(
            [new TransferFunctionFrame(reference, target)]).ImpulseResponse;

        Assert.Equal(gain, ir[0], precision: 9);
        Assert.All(
            ir.Skip(1).Take(127),
            sample => Assert.Equal(0.0, sample, precision: 8));
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
        for (int bin = 1; bin < result.Coherence!.Length - 1; bin++)
        {
            Assert.InRange(result.Coherence[bin], 0.999, 1.0 + 1e-9);
        }
    }

    [Fact]
    public void ComputeAveragedRelativeIr_IncoherentFramesDropCoherenceBelowOne()
    {
        // Alternating-sign spikes cancel in H1 but inflate the target auto-spectrum, so coherence must drop well below one.
        const int delay = 9;
        double[] reference = CreateImpulse(128);
        double[] target = Delay(reference, delay);

        var frames = new List<TransferFunctionFrame>();
        double[] signs = [1.0, -1.0, 1.0, -1.0];
        foreach (double sign in signs)
        {
            double[] noisy = (double[])target.Clone();
            noisy[40] += sign * 0.5; // uncorrelated with the reference impulse
            frames.Add(new TransferFunctionFrame(reference, noisy));
        }

        TransferEstimateResult result = TransferFunction.ComputeAveragedRelativeIr(frames);

        Assert.NotNull(result.Coherence);
        double maxCoherence = 0;
        for (int bin = 1; bin < result.Coherence!.Length - 1; bin++)
        {
            maxCoherence = Math.Max(maxCoherence, result.Coherence[bin]);
        }
        Assert.True(maxCoherence < 0.95, $"Expected coherence below 1 everywhere, peak was {maxCoherence:0.###}.");

        Assert.Equal(delay, result.PeakIndex);
        Assert.Equal(1.0, result.ImpulseResponse[delay], precision: 9);
    }

    [Fact]
    public void ComputeAveragedRelativeIr_GatesOutBinsAtTheReferenceNoiseFloor()
    {
        // Where the reference is only electrical noise Gxy/Gxx is a noise ratio that rang back as broadband garbage; those bins must read zero.
        const int delay = 25;
        double[] sweep = MiniSweep(4096, octaves: 5);
        double[] reference = AddNoise(sweep, 1e-6, seed: 1);
        double[] target = AddNoise(Delay(sweep, delay), 1e-3, seed: 2);

        TransferEstimateResult result = TransferFunction.ComputeAveragedRelativeIr(
            [new TransferFunctionFrame(reference, target)]);

        Assert.Equal(delay, result.PeakIndex);
        Assert.InRange(result.ImpulseResponse[delay], 0.8, 1.05);
        Assert.True(
            WorstOutsideWindow(result.ImpulseResponse, delay, 64) < 0.02,
            "Reference-noise-floor bins leaked into the IR.");
    }

    [Fact]
    public void ComputeAveragedRelativeIr_ExcitationEdgeCutsRumbleTheFloorGateCannot()
    {
        // Sweep leakage skirts keep reference power at -40..-20 dB below the sweep start, beyond any floor gate, while the mic
        // picks up rumble there: only the explicit excitation edge can cut it. Hann-shaped tones keep leakage compact.
        const int delay = 25;
        const int octaves = 3; // sweep spans Nyquist/8..Nyquist
        double[] sweep = MiniSweep(4096, octaves);
        double[] reference = AddNoise(sweep, 1e-6, seed: 5);
        double[] target = AddNoise(Delay(sweep, delay), 1e-5, seed: 6);
        for (int i = 0; i < reference.Length; i++)
        {
            double phase = 2.0 * Math.PI * i / 128.0;
            double window = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / reference.Length);
            reference[i] += 0.0005 * window * Math.Sin(phase);
            target[i] += 0.05 * window * Math.Sin(phase + 1.0);
        }
        var frames = new[] { new TransferFunctionFrame(reference, target) };

        TransferEstimateResult unmasked = TransferFunction.ComputeAveragedRelativeIr(frames);
        TransferEstimateResult masked = TransferFunction.ComputeAveragedRelativeIr(
            frames, excitationLowNyquistFraction: Math.Pow(2.0, -octaves));

        // Far-field RMS separates spread rumble from the pulse's decaying edge ringing.
        Assert.True(
            RmsOutsideWindow(unmasked.ImpulseResponse, delay, 256) > 0.03,
            "Test setup lost its teeth: the floor gate alone already cut the rumble.");
        Assert.Equal(delay, masked.PeakIndex);
        Assert.InRange(masked.ImpulseResponse[delay], 0.8, 1.05);
        Assert.True(
            RmsOutsideWindow(masked.ImpulseResponse, delay, 256) < 0.005,
            "Sub-excitation rumble leaked past the excitation edge.");
    }

    [Fact]
    public void ComputeAveragedRelativeIr_HighExcitationEdgeCutsAboveBandPollution()
    {
        // Mirror of the low edge: a target-only tone above the sweep's top is cut only by the explicit high edge.
        const int delay = 25;
        double[] sweep = MiniSweepBand(4096, lowFraction: 0.125, highFraction: 0.5);
        double[] reference = AddNoise(sweep, 1e-6, seed: 5);
        double[] target = AddNoise(Delay(sweep, delay), 1e-5, seed: 6);
        for (int i = 0; i < reference.Length; i++)
        {
            double phase = Math.PI * 0.85 * i; // 0.85*Nyquist, above the sweep end
            double window = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / reference.Length);
            reference[i] += 0.0005 * window * Math.Sin(phase);
            target[i] += 0.05 * window * Math.Sin(phase + 1.0);
        }
        var frames = new[] { new TransferFunctionFrame(reference, target) };

        TransferEstimateResult unmasked = TransferFunction.ComputeAveragedRelativeIr(frames);
        TransferEstimateResult masked = TransferFunction.ComputeAveragedRelativeIr(
            frames,
            excitationLowNyquistFraction: 0.0,
            excitationHighNyquistFraction: 0.5);

        double unmaskedRms = RmsOutsideWindow(unmasked.ImpulseResponse, delay, 256);
        Assert.True(
            unmaskedRms > 0.02,
            "Test setup lost its teeth: the floor gate alone already cut the tone.");
        Assert.Equal(delay, masked.PeakIndex);
        Assert.True(
            RmsOutsideWindow(masked.ImpulseResponse, delay, 256) < 0.5 * unmaskedRms,
            "Above-excitation tone leaked past the high excitation edge.");
    }

    [Fact]
    public void ComputeAveragedRelativeIr_BandGateZeroesTheRampBelowTheAchievedEdge()
    {
        // The legacy ramp [edge/2, edge] lies entirely below the achieved sweep start and half-passes cabin noise.
        const int delay = 25;
        // Tone at 0.18·Nyquist: inside the legacy ramp [0.125, 0.25], below the achieved edge.
        double[] sweep = MiniSweepBand(4096, lowFraction: 0.25, highFraction: 0.5);
        double[] reference = AddNoise(sweep, 1e-6, seed: 5);
        double[] target = AddNoise(Delay(sweep, delay), 1e-5, seed: 6);
        for (int i = 0; i < reference.Length; i++)
        {
            double phase = Math.PI * 0.18 * i;
            double window = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / reference.Length);
            reference[i] += 0.0005 * window * Math.Sin(phase);
            target[i] += 0.05 * window * Math.Sin(phase + 1.0);
        }
        var frames = new[] { new TransferFunctionFrame(reference, target) };

        TransferEstimateResult legacy = TransferFunction.ComputeAveragedRelativeIr(
            frames,
            excitationLowNyquistFraction: 0.25,
            excitationHighNyquistFraction: 0.5);
        TransferEstimateResult banded = TransferFunction.ComputeAveragedRelativeIr(
            frames,
            new ExcitationBandGate(
                LowZeroNyquistFraction: 0.25,
                LowFullNyquistFraction: 0.30,
                HighFullNyquistFraction: 0.45,
                HighZeroNyquistFraction: 0.5));

        double legacyRms = RmsOutsideWindow(legacy.ImpulseResponse, delay, 256);
        Assert.True(
            legacyRms > 0.01,
            "Test setup lost its teeth: the legacy ramp did not leak the sub-edge tone.");
        Assert.Equal(delay, banded.PeakIndex);
        Assert.True(
            RmsOutsideWindow(banded.ImpulseResponse, delay, 256) < 0.2 * legacyRms,
            "Sub-edge tone leaked past the band gate.");
    }

    [Fact]
    public void ComputeAveragedRelativeIr_MaskedBinsDoNotScaleTheGateThresholds()
    {
        // The peak scan anchoring gateHigh and λ must use only full-edge-weight bins; absurd sub-edge hums make the pin decisive.
        const int delay = 25;
        const int octaves = 3; // sweep spans Nyquist/8..Nyquist
        double[] sweep = MiniSweep(4096, octaves);
        double[] reference = AddNoise(sweep, 1e-6, seed: 7);
        double[] target = AddNoise(Delay(sweep, delay), 1e-5, seed: 8);
        for (int i = 0; i < reference.Length; i++)
        {
            double window = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / reference.Length);
            reference[i] += 1000.0 * window * Math.Sin(2.0 * Math.PI * i / 128.0);
            // Nyquist * 3/32: inside the edge's ramp, attenuated but not zeroed.
            reference[i] += 1000.0 * window * Math.Sin(2.0 * Math.PI * 3.0 * i / 64.0);
        }

        TransferEstimateResult result = TransferFunction.ComputeAveragedRelativeIr(
            [new TransferFunctionFrame(reference, target)],
            excitationLowNyquistFraction: Math.Pow(2.0, -octaves));

        Assert.Equal(delay, result.PeakIndex);
        Assert.InRange(result.ImpulseResponse[delay], 0.8, 1.05);
    }

    [Fact]
    public void ComputeAveragedRelativeIr_CoherenceIsUntrustedWhereTheEstimateIsMasked()
    {
        // Raw γ² reads ~1 where deterministic leakage/rumble sits in unexcited bins; returned coherence must be masked there.
        const int octaves = 3; // sweep spans Nyquist/8..Nyquist
        double[] sweep = MiniSweep(4096, octaves);
        double[] reference = AddNoise(sweep, 1e-6, seed: 9);
        double[] target = AddNoise(Delay(sweep, 25), 1e-6, seed: 10);
        for (int i = 0; i < target.Length; i++)
        {
            target[i] += 0.05 * Math.Sin(2.0 * Math.PI * i / 128.0);
        }
        var frame = new TransferFunctionFrame(reference, target);

        TransferEstimateResult result = TransferFunction.ComputeAveragedRelativeIr(
            [frame, frame, frame],
            excitationLowNyquistFraction: Math.Pow(2.0, -octaves));

        // 4097 bins: rumble at bin 64, the edge's ramp spans bins 256..512.
        Assert.NotNull(result.Coherence);
        Assert.Equal(4097, result.Coherence!.Length);
        Assert.Equal(0.0, result.Coherence[64]);
        for (int bin = 0; bin < 256; bin++)
        {
            Assert.Equal(0.0, result.Coherence[bin]);
        }
        Assert.InRange(result.Coherence[1024], 0.999, 1.0 + 1e-9);
        Assert.InRange(result.Coherence[2048], 0.999, 1.0 + 1e-9);
    }

    [Fact]
    public void ComputeAveragedRelativeIr_EstimateDoesNotDependOnTheAverageCount()
    {
        // Spectra accumulate unnormalized, so regularization must be relative: repeating a frame reproduces the estimate.
        double[] sweep = MiniSweep(2048, octaves: 4);
        double[] reference = AddNoise(sweep, 1e-6, seed: 3);
        double[] target = AddNoise(Delay(sweep, 40), 1e-4, seed: 4);
        var frame = new TransferFunctionFrame(reference, target);

        TransferEstimateResult once = TransferFunction.ComputeAveragedRelativeIr([frame]);
        TransferEstimateResult four = TransferFunction.ComputeAveragedRelativeIr(
            [frame, frame, frame, frame]);

        Assert.Equal(once.PeakIndex, four.PeakIndex);
        for (int i = 0; i < once.ImpulseResponse.Length; i++)
        {
            Assert.Equal(once.ImpulseResponse[i], four.ImpulseResponse[i], precision: 12);
        }
    }

    // <summary>Miniature app sweep: <paramref name="octaves"/> octaves ending at Nyquist with a first-octave fade-in, leakage skirts included.</summary>
    private static double[] MiniSweep(int length, int octaves)
    {
        double frequencyRatio = Math.Pow(2.0, octaves);
        double logarithmicRatio = Math.Log(frequencyRatio);
        double phaseFactor = (Math.PI / frequencyRatio) / logarithmicRatio;
        double octaveLength = length / (double)octaves;
        var sweep = new double[length];
        for (int i = 0; i < length; i++)
        {
            double exponentialPosition = Math.Exp(i / (double)length * logarithmicRatio);
            sweep[i] = Math.Sin(phaseFactor * length * exponentialPosition)
                * Math.Min(i / octaveLength, 1.0);
        }

        return sweep;
    }

    private static double[] MiniSweepBand(int length, double lowFraction, double highFraction)
    {
        double logRatio = Math.Log(highFraction / lowFraction);
        double startPhase = lowFraction * Math.PI * length / logRatio;
        double octaveLength = length / Math.Log2(highFraction / lowFraction);
        var sweep = new double[length];
        for (int i = 0; i < length; i++)
        {
            double phase = startPhase * Math.Exp(i / (double)length * logRatio);
            sweep[i] = Math.Sin(phase) * Math.Min(i / octaveLength, 1.0);
        }

        return sweep;
    }

    private static double WorstOutsideWindow(double[] ir, int center, int halfWidth)
    {
        double worst = 0;
        for (int i = 0; i < ir.Length; i++)
        {
            if (Math.Abs(i - center) > halfWidth)
            {
                worst = Math.Max(worst, Math.Abs(ir[i]));
            }
        }

        return worst;
    }

    private static double RmsOutsideWindow(double[] ir, int center, int halfWidth)
    {
        double sum = 0;
        int count = 0;
        for (int i = 0; i < ir.Length; i++)
        {
            if (Math.Abs(i - center) > halfWidth)
            {
                sum += ir[i] * ir[i];
                count++;
            }
        }

        return Math.Sqrt(sum / Math.Max(1, count));
    }

    private static double[] AddNoise(double[] signal, double amplitude, int seed)
    {
        var noisy = new double[signal.Length];
        for (int i = 0; i < signal.Length; i++)
        {
            noisy[i] = signal[i] + amplitude
                * Math.Sin(i * (12.9898 + seed * 3.7) + seed * 78.233)
                * Math.Sin(i * 0.7301 + seed);
        }

        return noisy;
    }

    [Fact]
    public void RefineAround_DegenerateCorrelationReportsNoRefinementWithoutNaN()
    {
        // An all-zero IR whitens to an empty band; refinement must bail out, not divide by zero.
        PhaseTransformCorrelation correlation =
            TransferFunction.ComputePhaseTransformFromResponse(new double[128]);

        PhaseTransformDelay delay = correlation.RefineAround(coarseLagSamples: 10, searchRadiusSamples: 4);

        Assert.Equal(0.0, delay.PeakCorrelation);
        Assert.False(delay.Refined);
        Assert.Equal(10, delay.LagSamples);
        Assert.False(double.IsNaN(delay.PeakCorrelation) || double.IsInfinity(delay.PeakCorrelation));
    }

    private static double[] CreateImpulse(int length)
    {
        var impulse = new double[length];
        impulse[0] = 1.0;
        return impulse;
    }

    [Fact]
    public void ComputePhaseTransformFromSpectrum_MatchesTheResponseOverload()
    {
        const int length = 1_024;
        var response = new double[length];
        var random = new Random(4711);
        for (int i = 0; i < 64; i++)
        {
            response[200 + i] = Math.Exp(-i / 12.0) * Math.Sin(2.0 * Math.PI * i / 9.0);
        }

        for (int i = 0; i < length; i++)
        {
            response[i] += (random.NextDouble() - 0.5) * 1e-3;
        }

        var spectrum = new Complex[length];
        for (int i = 0; i < length; i++)
        {
            spectrum[i] = new Complex(response[i], 0.0);
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);

        PhaseTransformDelay expected = TransferFunction
            .ComputePhaseTransformFromResponse(response)
            .RefineAround(200, 8);
        PhaseTransformDelay actual = TransferFunction
            .ComputePhaseTransformFromSpectrum(spectrum)
            .RefineAround(200, 8);

        Assert.Equal(expected.Refined, actual.Refined);
        Assert.Equal(expected.LagSamples, actual.LagSamples, precision: 10);
        Assert.Equal(expected.PeakCorrelation, actual.PeakCorrelation, precision: 10);
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
}
