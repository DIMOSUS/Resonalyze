using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp.Tests;

public sealed class VirtualCrossoverAnalysisTests
{
    private const int SampleRate = 48_000;

    private static Complex[] UnitImpulse(int length, int position)
    {
        var ir = new Complex[length];
        ir[position] = Complex.One;
        return ir;
    }

    [Fact]
    public void MeasureSumLoss_LevelMatchedMinus60DbTail_HasNoDelayEvidence()
    {
        // A -60 dB residue lifted by the +30 dB level match sits exactly at the -30 dB gate:
        // observability must be judged on the RAW balance.
        Complex[] fixedIr = UnitImpulse(4_096, 100);
        var tail = new Complex[4_096];
        tail[120] = 0.001; // -60 dB, same broadband spectral shape.

        (double LossDb, double DipDb)? loss = VirtualCrossoverAnalysis.MeasureSumLoss(
            tail, [fixedIr], SampleRate, 500, 2_000,
            levelMatch: true, requireDelayEvidence: true);

        Assert.Null(loss);
    }

    [Fact]
    public void PredictedAverageSumLossDb_AlignedImpulsesSumWithoutLoss()
    {
        Complex[] a = UnitImpulse(4_096, 100);
        Complex[] b = UnitImpulse(4_096, 100);

        double? loss = VirtualCrossoverAnalysis.PredictedAverageSumLossDb(
            [a, b], SampleRate, 500, 2_000);

        Assert.NotNull(loss);
        Assert.InRange(loss!.Value, -0.05, 0.001);
    }

    [Fact]
    public void PredictedAverageSumLossDb_HalfPeriodOffsetLosesEnergy()
    {
        // 0.5 ms is half a period at 1 kHz: the band center cancels.
        Complex[] a = UnitImpulse(4_096, 100);
        Complex[] b = UnitImpulse(4_096, 100 + SampleRate / 2_000);

        double? loss = VirtualCrossoverAnalysis.PredictedAverageSumLossDb(
            [a, b], SampleRate, 700, 1_400);

        Assert.NotNull(loss);
        Assert.True(loss!.Value < -3.0, $"expected a clear loss, got {loss} dB");
    }

    [Fact]
    public void PredictedAverageSumLossDb_GainScaleShiftsTheBalance()
    {
        Complex[] a = UnitImpulse(4_096, 100);
        Complex[] b = UnitImpulse(4_096, 100 + SampleRate / 2_000);

        double? balanced = VirtualCrossoverAnalysis.PredictedAverageSumLossDb(
            [a, b], SampleRate, 700, 1_400);
        double? lopsided = VirtualCrossoverAnalysis.PredictedAverageSumLossDb(
            [a, b], SampleRate, 700, 1_400, [1.0, 0.01]);

        Assert.True(lopsided!.Value > balanced!.Value + 3.0);
    }

    [Fact]
    public void PredictedAverageSumLossDb_SingleChannelHasNoSum()
    {
        Assert.Null(VirtualCrossoverAnalysis.PredictedAverageSumLossDb(
            [UnitImpulse(4_096, 100)], SampleRate, 500, 2_000));
    }

    [Fact]
    public void EffectiveOverlapOctaves_IdenticalBroadbandDriversSpanTheWholeBand()
    {
        Complex[] a = UnitImpulse(4_096, 100);
        Complex[] b = UnitImpulse(4_096, 100);

        double octaves = VirtualCrossoverAnalysis.EffectiveOverlapOctaves(
            b, [a], SampleRate, 500, 2_000);

        Assert.InRange(octaves, 1.7, 2.05);
    }

    [Fact]
    public void EffectiveOverlapOctaves_GatesBandWhereBothChannelsAreFarBelowSignal()
    {
        // Same steep band-pass on both: equal levels outside the 500-700 Hz pass-band are not shared band.
        Complex[] bandPass = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(8_192, 100),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                LowPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.LinkwitzRiley, 700, 48),
                HighPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.LinkwitzRiley, 500, 48))),
            SampleRate,
            SampleRate);

        double octaves = VirtualCrossoverAnalysis.EffectiveOverlapOctaves(
            bandPass, [bandPass], SampleRate, 500, 2_000);

        Assert.InRange(octaves, 0.7, 1.4);
    }

    [Fact]
    public void EffectiveOverlapOctaves_DisjointDriversBarelyOverlap()
    {
        // Only one driver radiates across the band: the degenerate hand-over the trust floor catches.
        Complex[] fixedIr = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(4_096, 100),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.LowPass,
                LowPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.Butterworth, 125, 36))),
            SampleRate,
            SampleRate);
        Complex[] variableIr = UnitImpulse(4_096, 100);

        double octaves = VirtualCrossoverAnalysis.EffectiveOverlapOctaves(
            variableIr, [fixedIr], SampleRate, 500, 2_000);

        Assert.True(octaves < 0.1, $"expected near-zero overlap, got {octaves:0.000}");
    }

    [Fact]
    public void EffectiveOverlapOctaves_CrossoverPairKeepsAFractionOfTheBand()
    {
        Complex[] lower = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(4_096, 100),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.LowPass,
                LowPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.LinkwitzRiley, 1_000, 24))),
            SampleRate,
            SampleRate);
        Complex[] upper = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(4_096, 100),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.HighPass,
                HighPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.LinkwitzRiley, 1_000, 24))),
            SampleRate,
            SampleRate);

        double octaves = VirtualCrossoverAnalysis.EffectiveOverlapOctaves(
            upper, [lower], SampleRate, 500, 2_000);

        Assert.InRange(octaves, 0.3, 1.4);
    }

    // Reflection ~30 ms late: inside the time-sized gate at every rate, outside a fixed 4096-sample gate at 192 kHz.
    private static Complex[] LowJunctionArrival(int sampleRate, double arrivalMs)
    {
        int length = sampleRate / 2; // 0.5 s, room for the gate at any rate
        var ir = new Complex[length];
        int direct = (int)Math.Round(arrivalMs / 1000.0 * sampleRate);
        int reflection = (int)Math.Round((arrivalMs + 30.0) / 1000.0 * sampleRate);
        ir[direct] = Complex.One;
        if (reflection < length)
        {
            ir[reflection] += 0.5;
        }
        return VirtualCrossoverAnalysis.ApplyChain(
            ir,
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                LowPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.LinkwitzRiley, 160, 24),
                HighPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.LinkwitzRiley, 40, 24))),
            sampleRate,
            sampleRate);
    }

    private static double RecoveredJunctionDelayMs(int sampleRate, double knownDelayMs)
    {
        Complex[] fixedIr = LowJunctionArrival(sampleRate, 10.0);
        Complex[] variableIr = LowJunctionArrival(sampleRate, 10.0 + knownDelayMs);
        IReadOnlyList<AlignmentCandidate> candidates =
            VirtualCrossoverAnalysis.FindAlignmentCandidates(
                variableIr, [fixedIr], sampleRate, 40, 160, -2.0, 2.0);
        return AlignmentSelection.Select(candidates, 0.0).DelayMs;
    }

    [Fact]
    public void FindAlignmentCandidates_RecoversTheSameDelayAcrossSampleRates()
    {
        // 192 kHz discriminates: a fixed 4096-sample gate (21 ms) would cut the 30 ms reflection.
        const double knownDelayMs = 0.30;
        double at48k = RecoveredJunctionDelayMs(48_000, knownDelayMs);
        double at96k = RecoveredJunctionDelayMs(96_000, knownDelayMs);
        double at192k = RecoveredJunctionDelayMs(192_000, knownDelayMs);

        Assert.Equal(-knownDelayMs, at48k, 1);
        Assert.Equal(-knownDelayMs, at96k, 1);
        Assert.Equal(-knownDelayMs, at192k, 1);
        Assert.True(Math.Abs(at48k - at192k) < 0.05,
            $"rate-dependent delay: 48k={at48k:0.000} ms, 192k={at192k:0.000} ms");
        Assert.True(Math.Abs(at48k - at96k) < 0.05,
            $"rate-dependent delay: 48k={at48k:0.000} ms, 96k={at96k:0.000} ms");
    }

    [Fact]
    public void FindAlignmentCandidates_OneSidedSilence_ReturnsEmpty()
    {
        // One side silent: loss is flat 0 dB, so returning prior-shaped candidates would fabricate an alignment.
        Complex[] active = UnitImpulse(4_096, 100);
        var silent = new Complex[4_096];

        IReadOnlyList<AlignmentCandidate> silentVariable =
            VirtualCrossoverAnalysis.FindAlignmentCandidates(
                silent, [active], SampleRate, 500, 2_000, -3, 3,
                priorDelayMs: 0.5, priorSigmaMs: 1.0);
        IReadOnlyList<AlignmentCandidate> silentFixed =
            VirtualCrossoverAnalysis.FindAlignmentCandidates(
                active, [silent], SampleRate, 500, 2_000, -3, 3,
                priorDelayMs: 0.5, priorSigmaMs: 1.0);

        Assert.Empty(silentVariable);
        Assert.Empty(silentFixed);
    }

    [Fact]
    public void EffectiveOverlapOctaves_SilentVariableHasNoOverlap()
    {
        Complex[] fixedIr = UnitImpulse(4_096, 100);
        var silent = new Complex[4_096];

        double octaves = VirtualCrossoverAnalysis.EffectiveOverlapOctaves(
            silent, [fixedIr], SampleRate, 500, 2_000);

        Assert.Equal(0.0, octaves, 6);
    }

    [Fact]
    public void ApplyChain_IdentityKeepsTheImpulse()
    {
        Complex[] ir = UnitImpulse(2_048, 100);

        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            ir, DspChannelChain.Identity, SampleRate, SampleRate);

        Assert.Equal(100, VirtualCrossoverAnalysis.FindPeakIndex(processed));
        Assert.Equal(1.0, processed[100].Real, 9);
        Assert.Equal(0.0, processed[99].Magnitude, 9);
    }

    [Fact]
    public void ApplyChain_DelayMovesThePeakByWholeSamples()
    {
        Complex[] ir = UnitImpulse(2_048, 100);

        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            ir, new DspChannelChain(DelayMs: 1.0), SampleRate, SampleRate);

        Assert.Equal(148, VirtualCrossoverAnalysis.FindPeakIndex(processed));
        Assert.Equal(1.0, processed[148].Real, 9);
    }

    [Fact]
    public void ApplyChain_GainAndPolarityScaleTheImpulse()
    {
        Complex[] ir = UnitImpulse(1_024, 10);

        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            ir,
            new DspChannelChain(GainDb: -6.0206, InvertPolarity: true),
            SampleRate,
            SampleRate);

        Assert.Equal(-0.5, processed[10].Real, 6);
    }

    [Fact]
    public void ApplyChain_KeepsARealImpulseReal()
    {
        Complex[] ir = UnitImpulse(1_024, 50);

        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            ir,
            new DspChannelChain(
                DelayMs: 0.13,
                Crossover: new CrossoverSpec(
                    CrossoverKind.LowPass,
                    new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_000, 24))),
            SampleRate,
            SampleRate);

        double maxImaginary = processed.Max(sample => Math.Abs(sample.Imaginary));
        Assert.True(maxImaginary < 1e-9, $"Imaginary residue {maxImaginary} is too large.");
    }

    [Fact]
    public void ApplyChain_FullChainMatchesDirectFrequencyResponse()
    {
        Complex[] ir = UnitImpulse(2_048, 73);
        ir[120] = new Complex(0.35, 0);
        ir[511] = new Complex(-0.12, 0);
        var chain = new DspChannelChain(
            GainDb: -2.5,
            DelayMs: -0.37,
            InvertPolarity: true,
            Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                LowPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.LinkwitzRiley, 3_000, 24),
                HighPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.LinkwitzRiley, 250, 24)),
            Peq: new EqualizationCurve(
                [
                    new PeqBand(120, 1.4, -3.0),
                    new PeqBand(950, 2.2, 4.5),
                    new PeqBand(4_200, 0.8, -2.0)
                ],
                preampDb: -1.5));

        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            ir, chain, SampleRate, SampleRate);
        Complex[] expectedSpectrum = new Complex[processed.Length];
        Array.Copy(ir, expectedSpectrum, ir.Length);
        Fourier.Forward(expectedSpectrum, FourierOptions.Matlab);
        int half = expectedSpectrum.Length / 2;
        expectedSpectrum[0] *= chain.Response(0, SampleRate);
        for (int i = 1; i < half; i++)
        {
            Complex response = chain.Response(
                i * (double)SampleRate / expectedSpectrum.Length,
                SampleRate);
            expectedSpectrum[i] *= response;
            expectedSpectrum[expectedSpectrum.Length - i] *= Complex.Conjugate(response);
        }

        expectedSpectrum[half] *= chain.Response(SampleRate / 2.0, SampleRate).Real;

        Complex[] actualSpectrum = (Complex[])processed.Clone();
        Fourier.Forward(actualSpectrum, FourierOptions.Matlab);
        double maxError = expectedSpectrum
            .Zip(actualSpectrum, (expected, actual) => (expected - actual).Magnitude)
            .Max();
        Assert.True(maxError < 1e-9, $"Max spectrum error {maxError:e} is too large.");
    }

    [Fact]
    public void PreparedDspResponse_MatchesDspChannelChainResponse()
    {
        var chain = new DspChannelChain(
            GainDb: 1.75,
            DelayMs: 0.42,
            InvertPolarity: true,
            Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                LowPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.LinkwitzRiley, 3_200, 24),
                HighPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.Butterworth, 280, 18)),
            Peq: new EqualizationCurve(
                [
                    new PeqBand(85, 0.9, 2.5),
                    new PeqBand(740, 3.0, -5.0),
                    new PeqBand(6_500, 1.2, 1.8)
                ],
                preampDb: -0.75));
        PreparedDspResponse prepared = PreparedDspResponse.Create(chain, SampleRate);

        foreach (double frequency in EqualizationCurve.LogFrequencyGrid(20, 20_000, 128))
        {
            Complex expected = chain.Response(frequency, SampleRate);
            Complex actual = prepared.Response(frequency);
            Assert.True(
                (expected - actual).Magnitude < 1e-12,
                $"Response mismatch at {frequency:0.###} Hz.");
        }
    }

    [Fact]
    public void ApplyChain_DelayedTailDoesNotWrapAround()
    {
        // Impulse near the end: padding must absorb the delay instead of wrapping to sample 0.
        Complex[] ir = UnitImpulse(1_024, 1_000);

        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            ir, new DspChannelChain(DelayMs: 5.0), SampleRate, SampleRate);

        Assert.Equal(1_240, VirtualCrossoverAnalysis.FindPeakIndex(processed));
        Assert.True(processed.Length >= 1_240);
        Assert.Equal(0.0, processed[0].Magnitude, 9);
    }

    [Fact]
    public void SumImpulseResponses_CoherentChannelsAdd()
    {
        Complex[] a = UnitImpulse(512, 20);
        Complex[] b = UnitImpulse(256, 20);

        Complex[] sum = VirtualCrossoverAnalysis.SumImpulseResponses([a, b]);

        Assert.Equal(512, sum.Length);
        Assert.Equal(2.0, sum[20].Real, 12);
    }

    [Fact]
    public void SumImpulseResponses_InvertedChannelCancels()
    {
        Complex[] a = UnitImpulse(512, 20);
        Complex[] b = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(512, 20),
            new DspChannelChain(InvertPolarity: true),
            SampleRate,
            SampleRate);

        Complex[] sum = VirtualCrossoverAnalysis.SumImpulseResponses([a, b]);

        Assert.Equal(0.0, sum.Max(sample => sample.Magnitude), 9);
    }

    [Fact]
    public void StepResponse_OfAnImpulse_IsAUnitStep_SummedFromTheRecordsStart()
    {
        Complex[] ir = UnitImpulse(64, 20);
        ir[5] = new Complex(3.0, 0.0);

        double[] step = VirtualCrossoverAnalysis.StepResponse(ir, 10, 70);

        Assert.Equal(70, step.Length);
        Assert.All(step.Take(10), value => Assert.Equal(3.0, value));
        Assert.All(step.Skip(10), value => Assert.Equal(4.0, value));

        double[] whole = VirtualCrossoverAnalysis.StepResponse(ir, 0, 80);
        for (int i = 0; i < step.Length; i++)
        {
            Assert.Equal(whole[10 + i], step[i]);
        }
    }

    [Fact]
    public void StepResponse_OfASum_IsTheSumOfTheSteps()
    {
        Complex[] low = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(4_096, 200),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.LowPass,
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24))),
            SampleRate,
            SampleRate);
        Complex[] high = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(4_096, 200),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.HighPass,
                HighPassEdge: new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24))),
            SampleRate,
            SampleRate);
        Complex[] sum = VirtualCrossoverAnalysis.SumImpulseResponses([low, high]);

        double[] lowStep = VirtualCrossoverAnalysis.StepResponse(low, 150, 2_000);
        double[] highStep = VirtualCrossoverAnalysis.StepResponse(high, 150, 2_000);
        double[] sumStep = VirtualCrossoverAnalysis.StepResponse(sum, 150, 2_000);

        for (int i = 0; i < sumStep.Length; i++)
        {
            Assert.Equal(lowStep[i] + highStep[i], sumStep[i], 9);
        }

        Assert.Equal(0.0, highStep[^1], 3);
        Assert.Equal(1.0, lowStep[^1], 3);
        Assert.Equal(1.0, sumStep[^1], 3);
    }

    [Fact]
    public void LinkwitzRileySplit_SumsBackToTheOriginal()
    {
        // LR24 low + high sum to an allpass copy: unit total energy.
        Complex[] ir = UnitImpulse(4_096, 200);

        Complex[] lowBranch = VirtualCrossoverAnalysis.ApplyChain(
            ir,
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.LowPass,
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24))),
            SampleRate,
            SampleRate);
        Complex[] highBranch = VirtualCrossoverAnalysis.ApplyChain(
            ir,
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.HighPass,
                HighPassEdge: new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24))),
            SampleRate,
            SampleRate);

        Complex[] sum = VirtualCrossoverAnalysis.SumImpulseResponses([lowBranch, highBranch]);

        double energy = sum.Sum(sample => sample.Magnitude * sample.Magnitude);
        Assert.Equal(1.0, energy, 6);
    }

    [Fact]
    public void FindBestDelayMs_RecoversAWholeSampleOffset()
    {
        Complex[] variable = UnitImpulse(4_096, 100);
        Complex[] fixedIr = UnitImpulse(4_096, 148);

        double delay = VirtualCrossoverAnalysis.FindBestDelayMs(
            variable, [fixedIr], SampleRate, 200, 10_000);

        Assert.Equal(1.0, delay, 3);
    }

    [Fact]
    public void FindBestDelayMs_RecoversAFractionalOffset()
    {
        Complex[] variable = UnitImpulse(4_096, 100);
        Complex[] fixedIr = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(4_096, 100),
            new DspChannelChain(DelayMs: 0.7708),
            SampleRate,
            SampleRate);

        double delay = VirtualCrossoverAnalysis.FindBestDelayMs(
            variable, [fixedIr], SampleRate, 200, 10_000);

        Assert.Equal(0.7708, delay, 3);
    }

    [Fact]
    public void FindBestDelayMs_ReturnsNegative_WhenTheVariableChannelLags()
    {
        // Negative best delay tells the caller to delay the other channel.
        Complex[] variable = UnitImpulse(4_096, 124);
        Complex[] fixedIr = UnitImpulse(4_096, 100);

        double delay = VirtualCrossoverAnalysis.FindBestDelayMs(
            variable, [fixedIr], SampleRate, 200, 10_000);

        Assert.Equal(-0.5, delay, 3);
    }

    [Fact]
    public void FindBestDelayMs_AlignsCrossoverBranches()
    {
        Complex[] low = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(8_192, 300),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.LowPass,
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24))),
            SampleRate,
            SampleRate);
        Complex[] high = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(8_192, 300),
            new DspChannelChain(
                DelayMs: 0.4,
                Crossover: new CrossoverSpec(
                    CrossoverKind.HighPass,
                    HighPassEdge: new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24))),
            SampleRate,
            SampleRate);

        double delay = VirtualCrossoverAnalysis.FindBestDelayMs(
            low, [high], SampleRate, 500, 2_000);

        Assert.Equal(0.4, delay, 2);
    }

    [Fact]
    public void AverageSumLossDb_IsZeroForCoherentCurves_AndNegativeUnderCancellation()
    {
        var channel = new List<SignalPoint>
        {
            new(500, 0.0), new(1_000, 0.0), new(2_000, 0.0)
        };
        var coherentSum = new List<SignalPoint>
        {
            new(500, 6.0206), new(1_000, 6.0206), new(2_000, 6.0206)
        };
        var degradedSum = new List<SignalPoint>
        {
            new(500, 0.0), new(1_000, 0.0), new(2_000, 0.0)
        };

        double? zeroLoss = VirtualCrossoverAnalysis.AverageSumLossDb(
            VirtualCrossoverAnalysis.SumLossCurve(coherentSum, [channel, channel]),
            100,
            10_000);
        double? loss = VirtualCrossoverAnalysis.AverageSumLossDb(
            VirtualCrossoverAnalysis.SumLossCurve(degradedSum, [channel, channel]),
            100,
            10_000);

        Assert.NotNull(zeroLoss);
        Assert.Equal(0.0, zeroLoss.Value, 3);
        Assert.NotNull(loss);
        Assert.Equal(-6.0206, loss.Value, 3);
    }

    [Fact]
    public void MinimumSumLossDb_ReadsTheDeepestNotch()
    {
        var channel = new List<SignalPoint>
        {
            new(500, 0.0), new(1_000, 0.0), new(2_000, 0.0)
        };
        var sum = new List<SignalPoint>
        {
            new(500, 6.0206), new(1_000, -5.9794), new(2_000, 6.0206)
        };

        double? dip = VirtualCrossoverAnalysis.MinimumSumLossDb(
            VirtualCrossoverAnalysis.SumLossCurve(sum, [channel, channel]),
            100,
            10_000);

        Assert.NotNull(dip);
        Assert.Equal(-12.0, dip.Value, 3);
    }

    [Fact]
    public void AverageSumLossDb_IgnoresPointsOutsideTheWindow()
    {
        var channel = new List<SignalPoint> { new(100, 0.0), new(1_000, 0.0) };
        var sum = new List<SignalPoint> { new(100, -20.0), new(1_000, 6.0206) };

        double? loss = VirtualCrossoverAnalysis.AverageSumLossDb(
            VirtualCrossoverAnalysis.SumLossCurve(sum, [channel, channel]),
            500,
            2_000);

        Assert.NotNull(loss);
        Assert.Equal(0.0, loss.Value, 3);
    }

    [Fact]
    public void SumLossCurve_IsThePerPointComplexVsMagnitudeSumGap()
    {
        var channel = new List<SignalPoint>
        {
            new(500, 0.0), new(1_000, 0.0), new(2_000, 0.0)
        };
        var sum = new List<SignalPoint>
        {
            new(500, 6.0206), new(1_000, 0.0), new(2_000, 6.0206)
        };

        List<SignalPoint> loss = VirtualCrossoverAnalysis.SumLossCurve(sum, [channel, channel]);

        Assert.Equal(3, loss.Count);
        Assert.Equal(500, loss[0].X);
        Assert.Equal(0.0, loss[0].Y, 3);
        Assert.Equal(-6.0206, loss[1].Y, 3);
        Assert.Equal(0.0, loss[2].Y, 3);
    }

    [Fact]
    public void SumLossCurve_IsNotDrawnWhereEveryChannelIsInItsStopBand()
    {
        // Two FIR stop-band floors at comparable level: their phasor sum is noise, however loud locally.
        double[] frequencies = [60, 120, 250, 1_000, 4_000];
        var sub = new List<SignalPoint>();
        var woofer = new List<SignalPoint>();
        var sum = new List<SignalPoint>();
        double[] subDb = [0, -6, -60, -110, -112];
        double[] wooferDb = [-30, -6, -20, -111, -110];
        for (int i = 0; i < frequencies.Length; i++)
        {
            sub.Add(new SignalPoint(frequencies[i], subDb[i]));
            woofer.Add(new SignalPoint(frequencies[i], wooferDb[i]));
            // Cancelling at 1 and 4 kHz, where only the floors remain.
            sum.Add(new SignalPoint(frequencies[i], i >= 3 ? -130 : Math.Max(subDb[i], wooferDb[i])));
        }

        List<SignalPoint> loss = VirtualCrossoverAnalysis.SumLossCurve(sum, [sub, woofer]);

        Assert.All(loss.Take(3), point => Assert.True(double.IsFinite(point.Y)));
        Assert.All(loss.Skip(3), point => Assert.True(double.IsNaN(point.Y)));
    }

    [Fact]
    public void SumLossCurve_ReadsAChannelThatMeasuredNothingAsContributingNothing()
    {
        // NaN below a divided-out protective high-pass must not poison the sum.
        var woofer = new List<SignalPoint>
        {
            new(200, 0.0), new(500, 0.0), new(2_000, 0.0)
        };
        var tweeter = new List<SignalPoint>
        {
            new(200, double.NaN), new(500, double.NaN), new(2_000, 0.0)
        };
        var sum = new List<SignalPoint>
        {
            new(200, 0.0), new(500, 0.0), new(2_000, 6.0206)
        };

        List<SignalPoint> loss = VirtualCrossoverAnalysis.SumLossCurve(
            sum, [woofer, tweeter]);

        Assert.Equal(0.0, loss[0].Y, 3);
        Assert.Equal(0.0, loss[1].Y, 3);
        Assert.Equal(0.0, loss[2].Y, 3);
    }

    [Fact]
    public void SumLossCurve_BreaksWhereNoChannelMeasuredAnything()
    {
        var first = new List<SignalPoint> { new(200, double.NaN), new(2_000, 0.0) };
        var second = new List<SignalPoint> { new(200, double.NaN), new(2_000, 0.0) };
        var sum = new List<SignalPoint> { new(200, -20.0), new(2_000, 6.0206) };

        List<SignalPoint> loss = VirtualCrossoverAnalysis.SumLossCurve(
            sum, [first, second]);

        Assert.False(double.IsFinite(loss[0].Y));
        Assert.Equal(0.0, loss[1].Y, 3);
    }

    [Fact]
    public void SumLossCurve_SmoothsTheRatio_NotTheOperands()
    {
        // Ideal complementary 70 Hz split: flat sum, steep operands, so any dip is invented by smoothing order.
        static double Complementary(double frequency)
        {
            double ratio = Math.Pow(frequency / 70.0, 8);
            return 1.0 / (1.0 + ratio);
        }

        List<SignalPoint> lowPass = LinearBins(frequency => Math.Max(
            -120.0, DataHelper.AmplitudeToDecibels(Complementary(frequency))));
        List<SignalPoint> highPass = LinearBins(frequency => Math.Max(
            -120.0, DataHelper.AmplitudeToDecibels(1.0 - Complementary(frequency))));
        List<SignalPoint> together = LinearBins(_ => 0.0);

        double psycho = SpectrumSmoothing.PsychoacousticCode;
        List<SignalPoint> honest = VirtualCrossoverAnalysis.SumLossCurve(
            Resample(together, 0),
            [Resample(lowPass, 0), Resample(highPass, 0)],
            psycho);

        Assert.All(
            honest.Where(point => point.X is >= 50 and <= 15_000),
            point => Assert.Equal(0.0, point.Y, 2));

        // Smoothing the operands before dividing draws a fake dip at the corner.
        List<SignalPoint> fromSmoothedOperands = VirtualCrossoverAnalysis.SumLossCurve(
            Resample(together, psycho),
            [Resample(lowPass, psycho), Resample(highPass, psycho)]);

        double invented = fromSmoothedOperands
            .Where(point => point.X is >= 50 and <= 100)
            .Min(point => point.Y);
        Assert.True(
            invented < -0.5,
            $"the operand-first order should invent a dip at the corner: {invented}");
    }

    private static List<SignalPoint> LinearBins(Func<double, double> decibelsAt)
    {
        var bins = new List<SignalPoint>(10_000);
        for (int i = 1; i <= 10_000; i++)
        {
            double frequency = i * 2.0;
            bins.Add(new SignalPoint(frequency, decibelsAt(frequency)));
        }

        return bins;
    }

    private static double At(List<SignalPoint> bins, double frequency) =>
        bins[Math.Clamp((int)Math.Round(frequency / 2.0) - 1, 0, bins.Count - 1)].Y;

    private static List<SignalPoint> Resample(
        List<SignalPoint> bins,
        double smoothingInverseOctaves) =>
        DataHelper.LogarithmicResample(
            bins,
            20,
            20_000,
            1024,
            null,
            SpectrumSmoothing.SmoothingOctaves(smoothingInverseOctaves),
            psychoacoustic: SpectrumSmoothing.IsPsychoacoustic(smoothingInverseOctaves));

    [Fact]
    public void SumLossCurve_TruncatesToTheShortestGrid()
    {
        var shortChannel = new List<SignalPoint> { new(500, 0.0) };
        var sum = new List<SignalPoint> { new(500, 0.0), new(1_000, 0.0) };

        Assert.Single(VirtualCrossoverAnalysis.SumLossCurve(sum, [shortChannel]));
    }

    [Fact]
    public void SumLossCurve_GatesPointsFarBelowTheirLocalNeighborhoodPeak()
    {
        // Out-of-band loss is noise-floor phase arithmetic: gated to NaN against the loudest level within an octave.
        var channel = new List<SignalPoint>
        {
            new(500, 0.0), new(1_000, 0.0), new(1_500, -40.0), new(6_000, -3.0)
        };
        var sum = new List<SignalPoint>
        {
            new(500, 0.0), new(1_000, 0.0), new(1_500, -40.0), new(6_000, -3.0)
        };

        List<SignalPoint> loss = VirtualCrossoverAnalysis.SumLossCurve(sum, [channel]);

        Assert.True(double.IsNaN(loss[2].Y));
        Assert.Equal(0.0, loss[0].Y, 3);
        Assert.Equal(0.0, loss[1].Y, 3);
        // No louder neighbour within an octave of 6 kHz, so it is kept.
        Assert.Equal(0.0, loss[3].Y, 3);
        double? dip = VirtualCrossoverAnalysis.MinimumSumLossDb(loss, 20, 8_000);
        Assert.NotNull(dip);
        Assert.Equal(0.0, dip.Value, 3);
    }

    [Fact]
    public void GroupDelayMs_OfAPureDelay_EqualsTheDelay()
    {
        PreparedDspResponse prepared =
            PreparedDspResponse.Create(new DspChannelChain(DelayMs: 1.5), SampleRate);

        foreach (double frequency in new[] { 100.0, 1_000.0, 5_000.0 })
        {
            Assert.Equal(1.5, prepared.GroupDelayMs(frequency), 2);
        }
    }

    [Fact]
    public void FindBestAlignment_DetectsAnInvertedChannel()
    {
        // The variable channel is a delayed AND inverted copy: the search must
        // find the delay and report the polarity flip instead of settling on a
        // half-period-off compromise.
        Complex[] fixedIr = UnitImpulse(4_096, 100);
        Complex[] variable = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(4_096, 100),
            new DspChannelChain(InvertPolarity: true),
            SampleRate,
            SampleRate);
        Complex[] fixedDelayed = VirtualCrossoverAnalysis.ApplyChain(
            fixedIr, new DspChannelChain(DelayMs: 0.5), SampleRate, SampleRate);

        AlignmentResult result = VirtualCrossoverAnalysis.FindBestAlignment(
            variable, [fixedDelayed], SampleRate, 200, 10_000, -1, 2);

        Assert.True(result.InvertPolarity);
        Assert.Equal(0.5, result.DelayMs, 3);
    }

    [Fact]
    public void FindBestAlignment_KeepsPolarityForAMatchingChannel()
    {
        Complex[] variable = UnitImpulse(4_096, 100);
        Complex[] fixedIr = UnitImpulse(4_096, 148);

        AlignmentResult result = VirtualCrossoverAnalysis.FindBestAlignment(
            variable, [fixedIr], SampleRate, 200, 10_000, 0, 2);

        Assert.False(result.InvertPolarity);
        Assert.Equal(1.0, result.DelayMs, 3);
    }

    [Fact]
    public void FindBestAlignment_WideWindowKeepsTheTrueSolutionAtACrossover()
    {
        // Flip + half-period impostors sit at ±0.5 ms; the loss score must reject them without a prior.
        Complex[] variable = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(8_192, 200),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.HighPass,
                HighPassEdge: new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24))),
            SampleRate,
            SampleRate);
        Complex[] fixedIr = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(8_192, 200),
            new DspChannelChain(
                DelayMs: 0.4,
                Crossover: new CrossoverSpec(
                    CrossoverKind.LowPass,
                    new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24))),
            SampleRate,
            SampleRate);

        AlignmentResult result = VirtualCrossoverAnalysis.FindBestAlignment(
            variable, [fixedIr], SampleRate, 500, 2_000, -1.1, 1.9);

        Assert.False(result.InvertPolarity);
        Assert.Equal(0.4, result.DelayMs, 2);
    }

    [Fact]
    public void FindAlignmentCandidates_ReportsBothSidesOfTheDegeneracy()
    {
        // Both the flipped echo solution and the direct alignment are local optima; both must be exposed.
        Complex[] variable = UnitImpulse(4_096, 100);
        var fixedIr = new Complex[4_096];
        fixedIr[100] = Complex.One;
        fixedIr[124] = new Complex(-1.1, 0); // +0.5 ms at 48 kHz, inverted.

        IReadOnlyList<AlignmentCandidate> candidates =
            VirtualCrossoverAnalysis.FindAlignmentCandidates(
                variable, [fixedIr], SampleRate, 500, 2_000, -1, 1);

        Assert.True(candidates.Count >= 2);
        Assert.Contains(candidates, item =>
            item.InvertPolarity && Math.Abs(item.DelayMs - 0.5) < 0.1);
        Assert.Contains(candidates, item =>
            !item.InvertPolarity && Math.Abs(item.DelayMs) < 0.1);
        Assert.True(candidates[0].ScoreDb >= candidates[^1].ScoreDb);
        Assert.All(candidates, item =>
        {
            Assert.Equal(
                item.LossDb + VirtualCrossoverAnalysis.DipExcessPenaltyWeight
                    * (item.DipDb - item.LossDb),
                item.ScoreDb,
                9);
            Assert.True(item.DipDb <= item.LossDb + 1e-9);
        });
    }

    [Theory]
    [InlineData(1_000, 1.0, 6.0)]   // an utterly ordinary band
    [InlineData(4_000, 2.0, -8.0)]
    public void RequiredTailSamples_OrdinaryPeqStaysAtTheMinimumPadding(
        double frequencyHz,
        double q,
        double gainDb)
    {
        // Pole radius in BiquadCoefficients' additive convention (z² − A1·z − A2); the textbook 1 + a1 + a2
        // reading misjudged stable sections and pinned padding at the 262144-sample cap.
        var chain = new DspChannelChain(Peq: new EqualizationCurve(
            new[] { new PeqBand(frequencyHz, q, gainDb) }));
        PreparedDspResponse prepared = PreparedDspResponse.Create(chain, 48_000);

        int tail = prepared.RequiredTailSamples(120.0, 8_192, 262_144, 48_000);

        Assert.Equal(8_192, tail);
    }

    [Fact]
    public void RequiredTailSamples_CrossoverStaysAtTheMinimumPadding()
    {
        var chain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24)));
        PreparedDspResponse prepared = PreparedDspResponse.Create(chain, 48_000);

        Assert.Equal(8_192, prepared.RequiredTailSamples(120.0, 8_192, 262_144, 48_000));
    }

    [Fact]
    public void RequiredTailSamples_LowFrequencyHighQPeqIsLongButFinite()
    {
        // 20 Hz / Q 10 rings ~100k samples to −120 dB: past the floor, under the cap.
        var chain = new DspChannelChain(Peq: new EqualizationCurve(
            new[] { new PeqBand(20, 10, 12) }));
        PreparedDspResponse prepared = PreparedDspResponse.Create(chain, 48_000);

        int tail = prepared.RequiredTailSamples(120.0, 8_192, 262_144, 48_000);

        Assert.InRange(tail, 50_000, 262_143);
    }

    [Fact]
    public void ApplyChain_LowFrequencyHighQPeqDoesNotWrapIntoTheEarlyResponse()
    {
        // A 20 Hz / Q 10 ring outlasts a fixed 8192-sample tail and wrapped into the early IR.
        var ir = new Complex[57_000];
        ir[24_000] = Complex.One;
        var chain = new DspChannelChain(Peq: new EqualizationCurve(
            new[] { new PeqBand(20, 10, 12) }));

        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(ir, chain, 48_000, 48_000);

        double peak = 0;
        for (int i = 0; i < processed.Length; i++)
        {
            peak = Math.Max(peak, processed[i].Magnitude);
        }
        double preArrival = 0;
        for (int i = 0; i < 23_000; i++)
        {
            preArrival = Math.Max(preArrival, processed[i].Magnitude);
        }

        Assert.True(
            preArrival < peak * 1e-4,
            $"wrap-around energy before the arrival: {20 * Math.Log10(preArrival / peak):0.0} dB re peak");
    }

    [Fact]
    public void FindAlignmentCandidates_ReportsEachPolaritysOwnOptimumAtAGappedJunction()
    {
        // Gapped junction (LP 1300 / HP 1800): the list must carry each polarity's best lobe, never flipped by refinement.
        Complex[] woofer = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(16_384, 400),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.LowPass,
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_300, 24))),
            SampleRate,
            SampleRate);
        Complex[] tweeter = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(16_384, 400),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.HighPass,
                HighPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.Butterworth, 1_800, 24))),
            SampleRate,
            SampleRate);

        IReadOnlyList<AlignmentCandidate> candidates =
            VirtualCrossoverAnalysis.FindAlignmentCandidates(
                tweeter, [woofer], SampleRate, 650, 2_600, -1.5, 1.5);

        AlignmentCandidate bestNormal = candidates.First(item => !item.InvertPolarity);
        AlignmentCandidate bestInverted = candidates.First(item => item.InvertPolarity);
        Assert.Equal(bestNormal, candidates[0]);
        Assert.InRange(bestNormal.DelayMs, -0.05, 0.25);
        Assert.InRange(bestInverted.DelayMs, -0.4, -0.05);
        Assert.True(bestInverted.ScoreDb > bestNormal.ScoreDb - 0.5);
    }

    [Fact]
    public void FindAlignmentCandidates_ForcedPolarityReturnsThatSignHonestlyScored()
    {
        // Forcing a sign must search that sign's grid, not re-stamp the other polarity's winner.
        Complex[] woofer = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(16_384, 400),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.LowPass,
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_300, 24))),
            SampleRate,
            SampleRate);
        Complex[] tweeter = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(16_384, 400),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.HighPass,
                HighPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.Butterworth, 1_800, 24))),
            SampleRate,
            SampleRate);

        IReadOnlyList<AlignmentCandidate> free =
            VirtualCrossoverAnalysis.FindAlignmentCandidates(
                tweeter, [woofer], SampleRate, 650, 2_600, -1.5, 1.5);
        IReadOnlyList<AlignmentCandidate> forced =
            VirtualCrossoverAnalysis.FindAlignmentCandidates(
                tweeter, [woofer], SampleRate, 650, 2_600, -1.5, 1.5,
                forcedPolarity: true);

        Assert.NotEmpty(forced);
        Assert.All(forced, candidate => Assert.True(candidate.InvertPolarity));

        AlignmentCandidate freeInverted = free.First(item => item.InvertPolarity);
        AlignmentCandidate freeNormal = free.First(item => !item.InvertPolarity);
        Assert.Equal(freeInverted.DelayMs, forced[0].DelayMs, 3);
        Assert.Equal(freeInverted.ScoreDb, forced[0].ScoreDb, 3);
        Assert.True(
            Math.Abs(forced[0].DelayMs - freeNormal.DelayMs) > 0.05,
            "the forced-inverted delay must be the inverted optimum, not the normal one's");
    }

    [Fact]
    public void FindAlignmentCandidates_DipExcessOutranksASlightlyBetterAverage()
    {
        // Inverted narrowband build-up behind the woofer front: the dip-excess penalty must outrank the average.
        // Pins only the score ordering, not which lobe deserves the junction.
        Complex[] woofer = ImpulseAt(8.0);
        Complex[] mode = VirtualCrossoverAnalysis.ApplyChain(
            ImpulseAt(8.0 + 6.25, -6.0),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 150, 36),
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 100, 36))),
            SampleRate,
            SampleRate);
        for (int i = 0; i < woofer.Length; i++)
        {
            woofer[i] += mode[i];
        }
        Complex[] sub = ImpulseAt(8.0);

        IReadOnlyList<AlignmentCandidate> candidates =
            VirtualCrossoverAnalysis.FindAlignmentCandidates(
                sub, [woofer], SampleRate, 40, 160, -9, 9,
                priorDelayMs: null, priorSigmaMs: 0, forcedPolarity: null,
                levelMatch: true, out _);

        AlignmentCandidate winner = candidates[0];
        AlignmentCandidate notched = candidates.Single(item =>
            !item.InvertPolarity && Math.Abs(item.DelayMs + 1.0) < 0.5);
        Assert.True(winner.InvertPolarity);
        Assert.True(notched.LossDb > winner.LossDb);
        Assert.True(notched.DipDb < winner.DipDb);
        Assert.True(notched.ScoreDb < winner.ScoreDb);
    }

    private static Complex[] ImpulseAt(double offsetMs, double amplitude = 1.0)
    {
        var ir = new Complex[16_384];
        ir[480 + (int)Math.Round(offsetMs / 1000.0 * SampleRate)] = amplitude;
        return ir;
    }

    [Fact]
    public void FindBestAlignment_PriorBreaksTheFlippedLobeDegeneracy()
    {
        // Inverted 1.1x echo half a period after the arrival: without the prior the search takes the bait.
        Complex[] variable = UnitImpulse(4_096, 100);
        var fixedIr = new Complex[4_096];
        fixedIr[100] = Complex.One;
        fixedIr[124] = new Complex(-1.1, 0); // +0.5 ms at 48 kHz, inverted.

        AlignmentResult unguided = VirtualCrossoverAnalysis.FindBestAlignment(
            variable, [fixedIr], SampleRate, 500, 2_000, -1, 1);
        AlignmentResult guided = VirtualCrossoverAnalysis.FindBestAlignment(
            variable, [fixedIr], SampleRate, 500, 2_000, -1, 1,
            priorDelayMs: 0, priorSigmaMs: 0.25);

        Assert.True(unguided.InvertPolarity);
        Assert.Equal(0.5, unguided.DelayMs, 1);
        Assert.False(guided.InvertPolarity);
        Assert.Equal(0.0, guided.DelayMs, 1);
    }

    [Fact]
    public void FindBandLimitedArrivalMs_ReadsTheArrivalInsideTheBand()
    {
        Complex[] ir = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(8_192, 480),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.LowPass,
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24))),
            SampleRate,
            SampleRate);

        double arrivalMs = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
            ir, SampleRate, 20, 1_000);

        // LR24 low-pass GD adds a fraction of a ms; the window is tight enough to require tracking it.
        Assert.InRange(arrivalMs, 10.0, 10.6);
    }

    [Fact]
    public void MeasureBandLevelDb_ReadsTheGainDifferenceBetweenResponses()
    {
        // The absolute figure has an arbitrary reference; the contract is the difference over the same band.
        Complex[] reference = UnitImpulse(8_192, 480);
        Complex[] quieter = VirtualCrossoverAnalysis.ApplyChain(
            reference,
            new DspChannelChain(GainDb: -6),
            SampleRate,
            SampleRate);

        double? referenceLevel = VirtualCrossoverAnalysis.MeasureBandLevelDb(
            reference, SampleRate, 300, 3_000);
        double? quieterLevel = VirtualCrossoverAnalysis.MeasureBandLevelDb(
            quieter, SampleRate, 300, 3_000);

        Assert.NotNull(referenceLevel);
        Assert.NotNull(quieterLevel);
        Assert.Equal(6.0, referenceLevel.Value - quieterLevel.Value, 2);
    }

    [Fact]
    public void MeasureBandLevelDb_AveragesEnergy_SoAnInterferenceCombBarelyDropsIt()
    {
        // Comb |H|^2 = 2 - 2cos(200w): power average reads +3 dB, a dB average ~0. Pins POWER averaging.
        Complex[] single = UnitImpulse(8_192, 480);
        Complex[] comb = UnitImpulse(8_192, 480);
        comb[680] = -Complex.One;

        double? singleLevel =
            VirtualCrossoverAnalysis.MeasureBandLevelDb(single, SampleRate, 400, 2_800);
        double? combLevel =
            VirtualCrossoverAnalysis.MeasureBandLevelDb(comb, SampleRate, 400, 2_800);

        Assert.NotNull(singleLevel);
        Assert.NotNull(combLevel);
        Assert.InRange(combLevel.Value - singleLevel.Value, 2.5, 3.1);
    }

    [Fact]
    public void MeasureBandLevelDb_BandWithoutBinsReturnsNull()
    {
        // Last bin at 23 997.1 Hz (2.93 Hz grid): the band must start above it to hold no bins.
        Complex[] ir = UnitImpulse(8_192, 480);

        Assert.Null(VirtualCrossoverAnalysis.MeasureBandLevelDb(
            ir, SampleRate, 23_999, 23_999.9));
    }

    [Fact]
    public void AnalyzeBandLimitedArrival_RefusesABandNarrowerThanAThirdOctave()
    {
        // A band too narrow is refused, not silently widened.
        Complex[] ir = UnitImpulse(8_192, 480);

        TimeAlignmentAnalysisResult narrow =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                ir, SampleRate, 1_000, 1_100);

        Assert.False(narrow.IsValid);
    }

    [Fact]
    public void AnalyzeBandLimitedArrival_FindsAnArrivalParkedBeyondTheSearchWindowByChainLatency()
    {
        // 3RC field case: ~160 ms playback buffering, beyond the 80 ms peak-search window.
        Complex[] ir = UnitImpulse(131_072, 7_680); // 160 ms at 48 kHz

        TimeAlignmentAnalysisResult result =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                ir, SampleRate, 500, 2_000);

        Assert.True(result.IsValid);
        Assert.InRange(result.FirstArrivalDelayMilliseconds, 159.0, 161.0);
        Assert.InRange(result.StrongestDelayMilliseconds, 159.0, 161.0);
    }

    [Fact]
    public void AnalyzeBandLimitedArrival_AcceptsExactlyAThirdOctave()
    {
        Complex[] ir = UnitImpulse(8_192, 480);

        TimeAlignmentAnalysisResult result =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                ir, SampleRate, 1_000,
                1_000 * VirtualCrossoverAnalysis.MinimumArrivalBandRatio);

        Assert.True(result.IsValid);
        Assert.InRange(result.FirstArrivalDelayMilliseconds, 9.5, 10.5);
    }

    [Fact]
    public void FindBandLimitedCorrelationDelay_ReturnsDelayToAddToSecondSignal()
    {
        Complex[] first = UnitImpulse(8_192, 2_000);
        Complex[] second = UnitImpulse(8_192, 1_952);

        CorrelationAlignmentResult result =
            VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                first,
                second,
                SampleRate,
                centerFrequencyHz: 1_000,
                passOctaves: 1,
                searchRangeMs: 3);

        Assert.False(result.BestByMagnitude.InvertPolarity);
        Assert.Equal(1.0, result.BestByMagnitude.DelayMs, 2);
        Assert.True(result.BestByMagnitude.Coefficient > 0.95);
    }

    // The neighbour is adjacency (next lobe), not the strongest opposite extremum.
    [Fact]
    public void FindBandLimitedCorrelationDelay_ReportsTheAdjacentOppositeLobe()
    {
        Complex[] first = UnitImpulse(8_192, 2_000);
        Complex[] second = UnitImpulse(8_192, 1_952);

        CorrelationAlignmentResult result =
            VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                first,
                second,
                SampleRate,
                centerFrequencyHz: 1_000,
                passOctaves: 1,
                searchRangeMs: 3);

        CorrelationDelayCandidate besidePeak =
            Assert.IsType<CorrelationDelayCandidate>(result.PositiveOppositeNeighbor);
        CorrelationDelayCandidate besideTrough =
            Assert.IsType<CorrelationDelayCandidate>(result.NegativeOppositeNeighbor);

        Assert.True(besidePeak.InvertPolarity);
        Assert.True(besidePeak.Coefficient < 0);
        Assert.False(besideTrough.InvertPolarity);
        Assert.True(besideTrough.Coefficient > 0);

        double neighborDistanceMs =
            Math.Abs(besidePeak.DelayMs - result.PositivePeak.DelayMs);
        double strongestDistanceMs =
            Math.Abs(result.NegativeTrough.DelayMs - result.PositivePeak.DelayMs);
        Assert.True(neighborDistanceMs <= strongestDistanceMs + 1e-9,
            $"neighbour {neighborDistanceMs:0.000} ms is farther than the " +
            $"strongest opposite extremum {strongestDistanceMs:0.000} ms");
        Assert.InRange(neighborDistanceMs, 0.2, 0.8);
    }

    // A reflection bump inside the first opposite lobe: the neighbour must be the lobe crest, not the first local extremum.
    [Fact]
    public void FindBandLimitedCorrelationDelay_NeighborIsTheLobeCrestNotARipple()
    {
        Complex[] first = UnitImpulse(8_192, 2_000);
        var second = new Complex[8_192];
        second[1_952] = Complex.One;
        second[1_952 + 10] = 0.2;
        second[1_952 + 20] = 0.4;
        second[1_952 + 70] = 0.7;

        CorrelationAlignmentResult result =
            VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                first,
                second,
                SampleRate,
                centerFrequencyHz: 1_000,
                passOctaves: 2,
                searchRangeMs: 3,
                phaseTransform: true);

        CorrelationDelayCandidate besidePeak =
            Assert.IsType<CorrelationDelayCandidate>(result.PositiveOppositeNeighbor);

        Assert.True(besidePeak.DelayMs > result.PositivePeak.DelayMs,
            $"neighbour at {besidePeak.DelayMs:0.000} ms is on the wrong side " +
            $"of the peak at {result.PositivePeak.DelayMs:0.000} ms");
        Assert.True(besidePeak.Coefficient < -0.6,
            $"neighbour depth {besidePeak.Coefficient:0.000} is a ripple, not " +
            "the lobe's crest");
    }

    [Fact]
    public void FindBandLimitedCorrelationDelay_PhaseTransformFindsTheSameDelay()
    {
        Complex[] first = UnitImpulse(8_192, 2_000);
        Complex[] second = UnitImpulse(8_192, 1_952);

        CorrelationAlignmentResult result =
            VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                first,
                second,
                SampleRate,
                centerFrequencyHz: 1_000,
                passOctaves: 2,
                searchRangeMs: 3,
                phaseTransform: true);

        Assert.False(result.BestByMagnitude.InvertPolarity);
        Assert.Equal(1.0, result.BestByMagnitude.DelayMs, 2);
        Assert.True(result.BestByMagnitude.Coefficient > 0.95);
    }

    [Fact]
    public void FindBandLimitedCorrelationDelay_ReportsNegativeTroughAsInversion()
    {
        Complex[] first = UnitImpulse(8_192, 2_000);
        Complex[] second = UnitImpulse(8_192, 1_952);
        second[1_952] = -Complex.One;

        CorrelationAlignmentResult result =
            VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                first,
                second,
                SampleRate,
                centerFrequencyHz: 1_000,
                passOctaves: 1,
                searchRangeMs: 3);

        Assert.True(result.BestByMagnitude.InvertPolarity);
        Assert.Equal(1.0, result.BestByMagnitude.DelayMs, 2);
        Assert.True(result.BestByMagnitude.Coefficient < -0.95);
    }

    [Fact]
    public void FindBandLimitedCorrelationDelay_CenterLagReachesAnOffsetBeyondTheZeroWindow()
    {
        // 400 samples (8.33 ms) apart, beyond a ±3 ms window around zero: centering on the arrival is required.
        Complex[] first = UnitImpulse(16_384, 4_000);
        Complex[] second = UnitImpulse(16_384, 3_600);
        const double offsetMs = 400.0 / SampleRate * 1_000.0;

        CorrelationAlignmentResult centered =
            VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                first, second, SampleRate,
                centerFrequencyHz: 1_000, passOctaves: 2, searchRangeMs: 3,
                centerLagMs: offsetMs, phaseTransform: true);
        CorrelationAlignmentResult uncentered =
            VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                first, second, SampleRate,
                centerFrequencyHz: 1_000, passOctaves: 2, searchRangeMs: 3,
                centerLagMs: 0, phaseTransform: true);

        Assert.Equal(offsetMs, centered.PositivePeak.DelayMs, 2);
        Assert.True(centered.PositivePeak.Coefficient > 0.95);

        Assert.True(Math.Abs(uncentered.BestByMagnitude.DelayMs) <= 3.05);
        Assert.True(Math.Abs(uncentered.PositivePeak.Coefficient) < 0.5);
    }

    [Fact]
    public void FindBandLimitedCorrelationDelay_ExposesPositivePeakAndInvertedTrough()
    {
        // The seed reads PositivePeak directly, so both flags must hold regardless of which wins.
        Complex[] first = UnitImpulse(8_192, 2_000);
        Complex[] second = UnitImpulse(8_192, 1_952);

        CorrelationAlignmentResult result =
            VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                first, second, SampleRate,
                centerFrequencyHz: 1_000, passOctaves: 2, searchRangeMs: 3,
                phaseTransform: true);

        Assert.False(result.PositivePeak.InvertPolarity);
        Assert.True(result.PositivePeak.Coefficient > 0.95);
        Assert.True(result.NegativeTrough.InvertPolarity);
        Assert.True(result.NegativeTrough.Coefficient < 0);
        Assert.True(result.Confidence >= 0);
        Assert.Equal(result.PositivePeak.DelayMs, result.BestByMagnitude.DelayMs, 6);
    }

    [Fact]
    public void FindBandLimitedCorrelationDelay_FlagsWindowEdgeExtremaAsEdgePinned()
    {
        // Edge-pinned argmax is a cut through the rising lobe; position and magnitude are artifacts.
        Complex[] first = UnitImpulse(8_192, 2_000);
        Complex[] second = UnitImpulse(8_192, 2_000 - 197);
        const double offsetMs = 197.0 / SampleRate * 1_000.0;

        CorrelationAlignmentResult pinned =
            VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                first, second, SampleRate,
                centerFrequencyHz: 100, passOctaves: 1, searchRangeMs: 3,
                phaseTransform: true);
        CorrelationAlignmentResult centered =
            VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                first, second, SampleRate,
                centerFrequencyHz: 100, passOctaves: 1, searchRangeMs: 3,
                centerLagMs: offsetMs, phaseTransform: true);

        Assert.True(pinned.PositivePeak.EdgePinned);
        Assert.False(centered.PositivePeak.EdgePinned);
        Assert.InRange(centered.PositivePeak.DelayMs, offsetMs - 0.05, offsetMs + 0.05);
    }

    [Fact]
    public void FindBandLimitedCorrelationDelay_ReportsTheSamePolarityRival()
    {
        // Two positive copies a period apart: PositiveRival exposes what peak-vs-trough Confidence cannot see.
        Complex[] first = UnitImpulse(8_192, 2_000);
        var second = new Complex[8_192];
        second[1_800] = 0.97;
        second[1_236] = 1.0;
        double centerMs = 200.0 / SampleRate * 1_000.0;

        CorrelationAlignmentResult result =
            VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                first, second, SampleRate,
                centerFrequencyHz: 85, passOctaves: 3.5, searchRangeMs: 15,
                centerLagMs: centerMs, phaseTransform: true);

        Assert.NotNull(result.PositiveRival);
        Assert.False(result.PositiveRival!.InvertPolarity);
        Assert.True(result.PositiveRival.Coefficient > 0);
        Assert.InRange(
            Math.Abs(result.PositivePeak.DelayMs - result.PositiveRival.DelayMs),
            10.0, 13.5);
        Assert.True(
            result.PositivePeak.Coefficient - result.PositiveRival.Coefficient < 0.2);
    }

    [Fact]
    public void AnalyzeBandLimitedArrival_ZeroPaddedTailDoesNotInflateTheSnr()
    {
        // ApplyChain's power-of-two padding must not collapse the quantile noise floor behind the arrival SNR.
        var random = new Random(20_260_718);
        var raw = new Complex[65_536];
        for (int i = 0; i < raw.Length; i++)
        {
            raw[i] = new Complex((random.NextDouble() * 2.0 - 1.0) * 1e-3, 0.0);
        }
        raw[2_000] = Complex.One;
        var padded = new Complex[131_072];
        Array.Copy(raw, padded, raw.Length);

        TimeAlignmentAnalysisResult rawResult = VirtualCrossoverAnalysis
            .AnalyzeBandLimitedArrival(raw, SampleRate, 1_900, 20_000);
        TimeAlignmentAnalysisResult paddedResult = VirtualCrossoverAnalysis
            .AnalyzeBandLimitedArrival(padded, SampleRate, 1_900, 20_000);

        Assert.True(rawResult.IsValid);
        Assert.True(paddedResult.IsValid);
        Assert.InRange(
            paddedResult.SignalToNoiseDecibels,
            rawResult.SignalToNoiseDecibels - 1.0,
            rawResult.SignalToNoiseDecibels + 1.0);
    }

    [Fact]
    public void AnalyzeBandLimitedArrival_ApplyChainMetadataPinsTheAnalysisRange()
    {
        // validSampleCount metadata must read exactly like an explicit crop.
        var random = new Random(20_260_723);
        var raw = new Complex[4_096];
        for (int i = 0; i < raw.Length; i++)
        {
            raw[i] = new Complex((random.NextDouble() * 2.0 - 1.0) * 1e-3, 0.0);
        }
        raw[2_000] += Complex.One;

        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            raw, new DspChannelChain(DelayMs: 25), SampleRate, SampleRate,
            out ValidSampleRange validRange);

        int delaySamples = (int)(25.0 / 1_000.0 * SampleRate);
        Assert.Equal(
            new ValidSampleRange(delaySamples, delaySamples + raw.Length),
            validRange);

        TimeAlignmentAnalysisResult viaMetadata = VirtualCrossoverAnalysis
            .AnalyzeBandLimitedArrival(
                processed, SampleRate, 1_900, 20_000, validRange);
        TimeAlignmentAnalysisResult raw2 = VirtualCrossoverAnalysis
            .AnalyzeBandLimitedArrival(raw, SampleRate, 1_900, 20_000);

        // Delay prefix excluded from the noise floor; position stays in full-record coordinates.
        Assert.True(viaMetadata.IsValid);
        Assert.InRange(
            viaMetadata.SignalToNoiseDecibels,
            raw2.SignalToNoiseDecibels - 1.0,
            raw2.SignalToNoiseDecibels + 1.0);
        Assert.InRange(
            viaMetadata.FirstArrivalDelayMilliseconds -
                raw2.FirstArrivalDelayMilliseconds,
            24.9,
            25.1);
    }

    [Fact]
    public void FindBandLimitedCorrelationDelay_ReportsTheSameSignRivalForTheTrough()
    {
        Complex[] first = UnitImpulse(8_192, 2_000);
        var second = new Complex[8_192];
        second[1_800] = -0.97;
        second[1_236] = -1.0;
        double centerMs = 200.0 / SampleRate * 1_000.0;

        CorrelationAlignmentResult result =
            VirtualCrossoverAnalysis.FindBandLimitedCorrelationDelay(
                first, second, SampleRate,
                centerFrequencyHz: 85, passOctaves: 3.5, searchRangeMs: 15,
                centerLagMs: centerMs, phaseTransform: true);

        Assert.NotNull(result.NegativeRival);
        Assert.True(result.NegativeRival!.InvertPolarity);
        Assert.True(result.NegativeRival.Coefficient < 0);
        Assert.InRange(
            Math.Abs(result.NegativeTrough.DelayMs - result.NegativeRival.DelayMs),
            10.0, 13.5);
        Assert.True(
            Math.Abs(result.NegativeTrough.Coefficient) -
            Math.Abs(result.NegativeRival.Coefficient) < 0.2);
    }

    [Fact]
    public void EstimatePolarity_ReadsTheFirstSignificantExcursion()
    {
        Complex[] positive = UnitImpulse(256, 50);
        Assert.Equal(
            PolarityEstimate.Positive,
            VirtualCrossoverAnalysis.EstimatePolarity(positive));

        Complex[] negative = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(256, 50),
            new DspChannelChain(InvertPolarity: true),
            SampleRate,
            SampleRate);
        Assert.Equal(
            PolarityEstimate.Negative,
            VirtualCrossoverAnalysis.EstimatePolarity(negative));
    }

    [Fact]
    public void EstimatePolarity_IgnoresALargerLaterRingingLobe()
    {
        // Polarity is set by the first significant excursion, not the global extremum.
        var ir = new Complex[256];
        for (int i = 0; i < 8; i++)
        {
            ir[80 + i] = new Complex(0.7 * Math.Sin(i / 8.0 * Math.PI), 0);
            ir[92 + i] = new Complex(-1.0 * Math.Sin(i / 8.0 * Math.PI), 0);
        }

        Assert.Equal(
            PolarityEstimate.Positive,
            VirtualCrossoverAnalysis.EstimatePolarity(ir));

        ir[10] = new Complex(-0.2, 0);
        Assert.Equal(
            PolarityEstimate.Positive,
            VirtualCrossoverAnalysis.EstimatePolarity(ir));
    }

    [Fact]
    public void EstimatePolarity_ReadsASmallLeadingLobeDespitePreRinging()
    {
        // Pre-ringing (~8%) and deep ringing must not override the ~35% leading lobe.
        var ir = new Complex[512];
        for (int i = 0; i < 40; i++)
        {
            ir[60 + i] = new Complex(0.08 * Math.Sin(i * 1.3), 0);
        }
        for (int i = 0; i < 6; i++)
        {
            double lobe = Math.Sin(i / 6.0 * Math.PI);
            ir[120 + i] = new Complex(0.35 * lobe, 0);
            ir[128 + i] = new Complex(-1.0 * lobe, 0);
            ir[136 + i] = new Complex(0.9 * lobe, 0);
        }

        Assert.Equal(
            PolarityEstimate.Positive,
            VirtualCrossoverAnalysis.EstimatePolarity(ir));
    }

    [Fact]
    public void EstimatePolarity_IsUnknownForASilentResponse()
    {
        Assert.Equal(
            PolarityEstimate.Unknown,
            VirtualCrossoverAnalysis.EstimatePolarity(new Complex[64]));
    }

    [Fact]
    public void FindPeakIndex_ReturnsTheStrongestSample()
    {
        var ir = new Complex[100];
        ir[10] = new Complex(0.5, 0);
        ir[42] = new Complex(-0.9, 0);

        Assert.Equal(42, VirtualCrossoverAnalysis.FindPeakIndex(ir));
    }

    [Fact]
    public void GuardClauses_RejectInvalidInput()
    {
        Assert.Throws<ArgumentException>(() => VirtualCrossoverAnalysis.ApplyChain(
            Array.Empty<Complex>(), DspChannelChain.Identity, SampleRate, SampleRate));
        Assert.Throws<ArgumentOutOfRangeException>(() => VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(16, 0), DspChannelChain.Identity, 0, 0));
        Assert.Throws<ArgumentException>(
            () => VirtualCrossoverAnalysis.SumImpulseResponses([]));
        Assert.Throws<ArgumentException>(
            () => VirtualCrossoverAnalysis.FindPeakIndex(Array.Empty<Complex>()));
    }

    // An LR24 pair at 1 kHz with the upper channel late by the given time.
    private static JunctionAlignmentSide LatePair(double upperDelayMs)
    {
        var edge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        Complex[] lower = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(16_384, 480),
            new DspChannelChain(Crossover: new CrossoverSpec(CrossoverKind.LowPass, LowPassEdge: edge)),
            SampleRate, SampleRate);
        Complex[] upper = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(16_384, 480),
            new DspChannelChain(
                Crossover: new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: edge), DelayMs: upperDelayMs),
            SampleRate, SampleRate);
        return new JunctionAlignmentSide(upper, lower, SampleRate);
    }

    [Fact]
    public void AJointReadOfOneSide_IsTheSingleSideRead_FigureForFigure()
    {
        JunctionAlignmentSide side = LatePair(0.3);

        var single = VirtualCrossoverAnalysis.MeasureAlignedJunctionSpectrum(
            side.VariableImpulseResponse, [side.FixedImpulseResponse], SampleRate, 500, 2_000, 1.0)!.Value;
        var joint = VirtualCrossoverAnalysis.MeasureJointlyAlignedJunctionSpectra([side], 500, 2_000, 1.0)!.Value;

        Assert.Equal(single.Alignment, joint.Alignment);
        Assert.Equal(single.Reading, Assert.Single(joint.Readings));
    }

    [Fact]
    public void AJointRead_TimesEverySideByOneShift_ChosenOnTheirMean()
    {
        // Late by 0.3 ms on one side and early by 0.3 on the other: each side alone would be moved its own way, and
        // one shift for both lands between them, reading both sides a little short of their own best.
        JunctionAlignmentSide late = LatePair(0.3);
        JunctionAlignmentSide early = LatePair(-0.3);

        var lateAlone = VirtualCrossoverAnalysis.MeasureAlignedJunctionSpectrum(
            late.VariableImpulseResponse, [late.FixedImpulseResponse], SampleRate, 500, 2_000, 1.0)!.Value;
        var joint = VirtualCrossoverAnalysis.MeasureJointlyAlignedJunctionSpectra(
            [late, early], 500, 2_000, 1.0)!.Value;

        Assert.InRange(lateAlone.Alignment.DelayMs, -0.35, -0.25);
        Assert.InRange(joint.Alignment.DelayMs, -0.1, 0.1);
        Assert.Equal(2, joint.Readings.Count);
        Assert.True(joint.Readings[0]!.LossDb < lateAlone.Reading.LossDb - 0.1);
        Assert.Equal(joint.Readings[0]!.LossDb, joint.Readings[1]!.LossDb, 1);
    }
}
