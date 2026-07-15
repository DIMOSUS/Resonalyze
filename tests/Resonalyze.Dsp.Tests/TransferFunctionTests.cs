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
        // Identical frames are perfectly coherent: every interior bin must read ~1,
        // not merely "somewhere in [0, 1]".
        for (int bin = 1; bin < result.Coherence!.Length - 1; bin++)
        {
            Assert.InRange(result.Coherence[bin], 0.999, 1.0 + 1e-9);
        }
    }

    [Fact]
    public void ComputeAveragedRelativeIr_IncoherentFramesDropCoherenceBelowOne()
    {
        // Reference is an impulse (flat spectrum); each target is the same delayed
        // impulse plus an uncorrelated spike whose sign alternates across frames. The
        // spikes sum to zero, so the averaged H1 still recovers the clean delay, but
        // their power inflates the target auto-spectrum and pushes coherence well
        // below one at every bin. A vacuous "in [0,1]" check would miss this.
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

        // The zero-mean spikes cancel in the average, so the delay is still clean.
        Assert.Equal(delay, result.PeakIndex);
        Assert.Equal(1.0, result.ImpulseResponse[delay], precision: 9);
    }

    [Fact]
    public void ComputeAveragedRelativeIr_GatesOutBinsAtTheReferenceNoiseFloor()
    {
        // The power-floor safety net: where the reference truly carries
        // nothing but its (tiny, electrical) noise, Gxy/Gxx is a noise ratio
        // of order target/reference — orders of magnitude above the in-band
        // response — and with the absolute epsilon it rang back through the
        // IFFT as broadband time-domain garbage. Those bins must read zero:
        // the recovered IR is then a clean band-limited pulse at the true
        // delay instead of noise swamping it.
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
        // The field failure mode on real capture lengths: the sweep's own
        // leakage skirts hold the reference power at only -40..-20 dB re max
        // all the way below the sweep start, so no data-driven floor gate can
        // mark that region — while the microphone picks up strong infrasonic
        // rumble there (vibration, wind) that the loopback never sees.
        // Gxy/Gxx then reads rumble-over-skirt, far above the honest in-band
        // response. Model the skirt as a reference tone below the sweep start
        // — well above the floor gate yet far below the passband — and the
        // rumble as a 40 dB louder target tone at the same frequency: only
        // the explicit excitation edge can cut it. Both tones are Hann-shaped
        // so their own leakage stays as compact relative to the edge as real
        // capture-length rumble is.
        const int delay = 25;
        const int octaves = 3; // sweep spans Nyquist/8..Nyquist
        double[] sweep = MiniSweep(4096, octaves);
        double[] reference = AddNoise(sweep, 1e-6, seed: 5);
        double[] target = AddNoise(Delay(sweep, delay), 1e-5, seed: 6);
        for (int i = 0; i < reference.Length; i++)
        {
            // Nyquist/64 — below the sweep start and below the edge's ramp.
            double phase = 2.0 * Math.PI * i / 128.0;
            double window = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / reference.Length);
            reference[i] += 0.0005 * window * Math.Sin(phase);
            target[i] += 0.05 * window * Math.Sin(phase + 1.0);
        }
        var frames = new[] { new TransferFunctionFrame(reference, target) };

        TransferEstimateResult unmasked = TransferFunction.ComputeAveragedRelativeIr(frames);
        TransferEstimateResult masked = TransferFunction.ComputeAveragedRelativeIr(
            frames, excitationLowNyquistFraction: Math.Pow(2.0, -octaves));

        // The rumble tone spreads as a sinusoid across the whole IR while the
        // pulse's own band-edge ringing decays away from it, so far-field RMS
        // separates the two. Without the edge the rumble-over-skirt garbage
        // dominates it (~0.09 here)...
        Assert.True(
            RmsOutsideWindow(unmasked.ImpulseResponse, delay, 256) > 0.03,
            "Test setup lost its teeth: the floor gate alone already cut the rumble.");
        // ...with it the pulse comes back clean, in place and full-size.
        Assert.Equal(delay, masked.PeakIndex);
        Assert.InRange(masked.ImpulseResponse[delay], 0.8, 1.05);
        Assert.True(
            RmsOutsideWindow(masked.ImpulseResponse, delay, 256) < 0.005,
            "Sub-excitation rumble leaked past the excitation edge.");
    }

    [Fact]
    public void ComputeAveragedRelativeIr_MaskedBinsDoNotScaleTheGateThresholds()
    {
        // PR-review finding: the peak scan anchoring gateHigh and λ used to
        // include bins the excitation edge later zeroes or attenuates. A loud
        // sub-edge reference component (hum below — or in the ramp of — a
        // narrow sweep's start) then scaled the power gate from an artifact
        // excluded from the estimate, fading the genuinely excited bins. The
        // hums here are deliberately absurd — 60+ dB over the sweep bins,
        // enough to zero the whole passband through the old scan — so the pin
        // is decisive: with the scan restricted to bins at FULL edge weight
        // the pulse must come back intact.
        const int delay = 25;
        const int octaves = 3; // sweep spans Nyquist/8..Nyquist
        double[] sweep = MiniSweep(4096, octaves);
        double[] reference = AddNoise(sweep, 1e-6, seed: 7);
        double[] target = AddNoise(Delay(sweep, delay), 1e-5, seed: 8);
        for (int i = 0; i < reference.Length; i++)
        {
            double window = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / reference.Length);
            // Nyquist/64 — below the excitation edge's ramp.
            reference[i] += 1000.0 * window * Math.Sin(2.0 * Math.PI * i / 128.0);
            // Nyquist * 3/32 — inside the edge's ramp (Nyquist/16..Nyquist/8),
            // where the bin is attenuated but not zeroed.
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
        // PR-review finding: coherence used to be returned unmasked. Below
        // the sweep start the sweep's own leakage — and any stationary rumble
        // — is deterministic across runs, so raw γ² reads ~1 exactly where
        // the estimate zeroes the bins as unexcited, and the consumers that
        // treat coherence as a reliability gate (phase unwrap, PHAT
        // weighting, the plotted curve) kept trusting them. Three identical
        // frames make raw γ² exactly 1 everywhere; the returned coherence
        // must still read the masked region as untrusted and keep full
        // in-band trust.
        const int octaves = 3; // sweep spans Nyquist/8..Nyquist
        double[] sweep = MiniSweep(4096, octaves);
        double[] reference = AddNoise(sweep, 1e-6, seed: 9);
        double[] target = AddNoise(Delay(sweep, 25), 1e-6, seed: 10);
        for (int i = 0; i < target.Length; i++)
        {
            // Deterministic rumble at Nyquist/64, below the edge's ramp.
            target[i] += 0.05 * Math.Sin(2.0 * Math.PI * i / 128.0);
        }
        var frame = new TransferFunctionFrame(reference, target);

        TransferEstimateResult result = TransferFunction.ComputeAveragedRelativeIr(
            [frame, frame, frame],
            excitationLowNyquistFraction: Math.Pow(2.0, -octaves));

        // Coherence covers 0..Nyquist in fftLength / 2 + 1 = 4097 bins; the
        // rumble sits at bin 64, the edge's ramp spans bins 256..512.
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
        // The cross- and auto-spectra are accumulated without normalization, so
        // an absolute epsilon regularized four accumulated runs four times more
        // weakly than one. The relative regularization scales with the sums:
        // repeating the identical frame must reproduce the identical estimate.
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

    // The app's exponential sweep in miniature: <paramref name="octaves"/>
    // octaves ending exactly at Nyquist, amplitude faded in linearly over the
    // first octave — so the spectrum has the same shape the excitation gates
    // see in a real loopback capture, leakage skirts below the start
    // frequency included.
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
            // Deterministic pseudo-noise, decorrelated across seeds.
            noisy[i] = signal[i] + amplitude
                * Math.Sin(i * (12.9898 + seed * 3.7) + seed * 78.233)
                * Math.Sin(i * 0.7301 + seed);
        }

        return noisy;
    }

    [Fact]
    public void RefineAround_DegenerateCorrelationReportsNoRefinementWithoutNaN()
    {
        // An all-zero IR whitens to an empty band (weightSum 0, normalizer 0), so the
        // refinement must bail out cleanly rather than divide by zero.
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
