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
        // A variable channel that is only a -60 dB residue of the fixed one:
        // the capped (+30 dB) search-side level match lifts it to exactly
        // the -30 dB reliability gate, where a matched-magnitude evidence
        // read would pass it as measurable overlap. Observability must be
        // judged on the RAW balance — the level match rescales the scoring
        // frame, it cannot manufacture a measurable delay.
        Complex[] fixedIr = UnitImpulse(4_096, 100);
        var tail = new Complex[4_096];
        tail[120] = 0.001; // -60 dB, same broadband spectral shape.

        (double LossDb, double DipDb)? loss = VirtualCrossoverAnalysis.MeasureSumLoss(
            tail, [fixedIr], SampleRate, 500, 2_000,
            levelMatch: true, requireDelayEvidence: true);

        Assert.Null(loss);
    }

    [Fact]
    public void FindAlignmentCandidates_LevelMatch_CandidatesAreGainInvariant()
    {
        // The same physical pair at 0, -10 and -20 dB of variable-channel
        // gain must produce the same lobe choices: the level match rescales
        // the scoring frame, so within its documented ±30 dB cap the search
        // cannot follow the playback gains.
        Complex[] fixedIr = UnitImpulse(4_096, 100);
        Complex[] reference = UnitImpulse(4_096, 120);
        double? baselineDelayMs = null;
        foreach (double gain in new[] { 1.0, 0.316, 0.1 })
        {
            Complex[] variable = reference.Select(value => value * gain).ToArray();
            IReadOnlyList<AlignmentCandidate> candidates =
                VirtualCrossoverAnalysis.FindAlignmentCandidates(
                    variable, [fixedIr], SampleRate, 500, 2_000, -2, 2,
                    levelMatch: true);
            Assert.NotEmpty(candidates);
            baselineDelayMs ??= candidates[0].DelayMs;
            Assert.InRange(
                candidates[0].DelayMs,
                baselineDelayMs.Value - 0.005,
                baselineDelayMs.Value + 0.005);
            Assert.False(candidates[0].InvertPolarity);
        }
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
        // 0.5 ms is half a period at 1 kHz: around the band center the two
        // impulses cancel, and the averaged loss over the octave-wide window
        // must show it clearly.
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
        // With one channel scaled to near-zero the "sum" degenerates to the
        // other channel alone: no cancellation is possible, so the misaligned
        // pair's deep loss must mostly disappear.
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

        // Two flat, equal spectra overlap perfectly (O=1) across the entire
        // band, whose nominal width is log2(2000/500) = 2 octaves.
        Assert.InRange(octaves, 1.7, 2.05);
    }

    [Fact]
    public void EffectiveOverlapOctaves_GatesBandWhereBothChannelsAreFarBelowSignal()
    {
        // Both channels are the SAME steep band-pass, so O(f)=1 across the whole
        // 500-2000 Hz band — yet outside the 500-700 Hz pass-band both fall far
        // below the in-band peak. Equal levels there are not usable shared band,
        // so the reliability gate drops them: the overlap reads the pass-band
        // alone (~1 oct), not the full ~2 that ungated equal levels would count.
        Complex[] bandPass = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(8_192, 100),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                LowPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.LinkwitzRiley, 700, 48),
                HighPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.LinkwitzRiley, 500, 48))),
            SampleRate);

        double octaves = VirtualCrossoverAnalysis.EffectiveOverlapOctaves(
            bandPass, [bandPass], SampleRate, 500, 2_000);

        Assert.InRange(octaves, 0.7, 1.4);
    }

    [Fact]
    public void EffectiveOverlapOctaves_DisjointDriversBarelyOverlap()
    {
        // The fixed driver rolls off almost two octaves below the band; the
        // variable is flat. Across 500-2000 Hz only one driver radiates, so
        // the genuinely shared band collapses toward zero — the degenerate
        // hand-over the engine's trust floor is there to catch.
        Complex[] fixedIr = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(4_096, 100),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.LowPass,
                LowPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.Butterworth, 125, 36))),
            SampleRate);
        Complex[] variableIr = UnitImpulse(4_096, 100);

        double octaves = VirtualCrossoverAnalysis.EffectiveOverlapOctaves(
            variableIr, [fixedIr], SampleRate, 500, 2_000);

        Assert.True(octaves < 0.1, $"expected near-zero overlap, got {octaves:0.000}");
    }

    [Fact]
    public void EffectiveOverlapOctaves_CrossoverPairKeepsAFractionOfTheBand()
    {
        // A real hand-over: a low-pass and a high-pass sharing a 1 kHz corner.
        // The two genuinely overlap around the corner but each rolls off alone
        // toward the band edges, so the effective overlap is a healthy fraction
        // of the nominal 2 octaves — comfortably above the trust floor and well
        // clear of the disjoint case.
        Complex[] lower = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(4_096, 100),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.LowPass,
                LowPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.LinkwitzRiley, 1_000, 24))),
            SampleRate);
        Complex[] upper = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(4_096, 100),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.HighPass,
                HighPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.LinkwitzRiley, 1_000, 24))),
            SampleRate);

        double octaves = VirtualCrossoverAnalysis.EffectiveOverlapOctaves(
            upper, [lower], SampleRate, 500, 2_000);

        Assert.InRange(octaves, 0.3, 1.4);
    }

    // A low-frequency junction scene at an arbitrary sample rate: a direct
    // arrival plus an early reflection ~30 ms later (inside the ~85 ms gate at
    // every rate, but outside the 21 ms window a fixed 4096-sample gate would
    // give at 192 kHz), band-limited to the 80 Hz overlap so window duration —
    // hence frequency resolution — actually bears on the recovered delay.
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
            sampleRate);
    }

    private static double RecoveredJunctionDelayMs(int sampleRate, double knownDelayMs)
    {
        // The variable arrives knownDelayMs LATER, so the delay that realigns
        // it is the negative of that (delay to ADD to the variable).
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
        // The gate is sized in time, so the same physical scene must yield the
        // same delay whatever the sample rate. 192 kHz is the discriminating
        // case: a fixed 4096-sample gate is only 21 ms there, so it CUTS the
        // 30 ms reflection this scene carries and drifts the estimate — the old
        // implementation fails this assertion while 48 and 96 kHz (85 / 43 ms
        // windows, both past the ~40 ms plateau) would pass either way.
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
        // With one side silent the loss |F+V|/(|F|+|V|) is flat 0 dB for every
        // delay and polarity — no delay evidence exists, and returning
        // prior-shaped "candidates" would let a caller fabricate an alignment.
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
            ir, DspChannelChain.Identity, SampleRate);

        Assert.Equal(100, VirtualCrossoverAnalysis.FindPeakIndex(processed));
        Assert.Equal(1.0, processed[100].Real, 9);
        Assert.Equal(0.0, processed[99].Magnitude, 9);
    }

    [Fact]
    public void ApplyChain_DelayMovesThePeakByWholeSamples()
    {
        // 1 ms at 48 kHz is exactly 48 samples, so the impulse lands on a sample
        // with no interpolation spread.
        Complex[] ir = UnitImpulse(2_048, 100);

        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            ir, new DspChannelChain(DelayMs: 1.0), SampleRate);

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
            SampleRate);

        Assert.Equal(-0.5, processed[10].Real, 6);
    }

    [Fact]
    public void ApplyChain_KeepsARealImpulseReal()
    {
        // The chain response is conjugate-mirrored across Nyquist, so filtering a
        // real impulse must not leak into the imaginary part.
        Complex[] ir = UnitImpulse(1_024, 50);

        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            ir,
            new DspChannelChain(
                DelayMs: 0.13,
                Crossover: new CrossoverSpec(
                    CrossoverKind.LowPass,
                    new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_000, 24))),
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
            ir, chain, SampleRate);
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
        // The impulse sits near the end of the IR; the padding must absorb the
        // delay shift instead of wrapping it back to sample 0.
        Complex[] ir = UnitImpulse(1_024, 1_000);

        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            ir, new DspChannelChain(DelayMs: 5.0), SampleRate);

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
            SampleRate);

        Complex[] sum = VirtualCrossoverAnalysis.SumImpulseResponses([a, b]);

        Assert.Equal(0.0, sum.Max(sample => sample.Magnitude), 9);
    }

    [Fact]
    public void LinkwitzRileySplit_SumsBackToTheOriginal()
    {
        // Splitting one impulse into LR24 low-pass and high-pass branches and
        // summing them reconstructs an allpass copy of the original: same
        // magnitude everywhere, in particular unit total energy.
        Complex[] ir = UnitImpulse(4_096, 200);

        Complex[] lowBranch = VirtualCrossoverAnalysis.ApplyChain(
            ir,
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.LowPass,
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24))),
            SampleRate);
        Complex[] highBranch = VirtualCrossoverAnalysis.ApplyChain(
            ir,
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.HighPass,
                HighPassEdge: new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24))),
            SampleRate);

        Complex[] sum = VirtualCrossoverAnalysis.SumImpulseResponses([lowBranch, highBranch]);

        double energy = sum.Sum(sample => sample.Magnitude * sample.Magnitude);
        Assert.Equal(1.0, energy, 6);
    }

    [Fact]
    public void FindBestDelayMs_RecoversAWholeSampleOffset()
    {
        // The fixed channel arrives 48 samples (1 ms) later, so the variable
        // channel needs exactly 1 ms of delay to line up.
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
            SampleRate);

        double delay = VirtualCrossoverAnalysis.FindBestDelayMs(
            variable, [fixedIr], SampleRate, 200, 10_000);

        Assert.Equal(0.7708, delay, 3);
    }

    [Fact]
    public void FindBestDelayMs_ReturnsNegative_WhenTheVariableChannelLags()
    {
        // The variable channel already arrives 0.5 ms late; the best "delay"
        // is negative, telling the caller to move it to the other channel.
        Complex[] variable = UnitImpulse(4_096, 124);
        Complex[] fixedIr = UnitImpulse(4_096, 100);

        double delay = VirtualCrossoverAnalysis.FindBestDelayMs(
            variable, [fixedIr], SampleRate, 200, 10_000);

        Assert.Equal(-0.5, delay, 3);
    }

    [Fact]
    public void FindBestDelayMs_AlignsCrossoverBranches()
    {
        // A realistic use: LR24 low/high branches of one impulse with the high
        // branch offset by 0.4 ms. Aligning inside the crossover window must
        // recover that offset even though the branches only overlap around 1 kHz.
        Complex[] low = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(8_192, 300),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.LowPass,
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24))),
            SampleRate);
        Complex[] high = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(8_192, 300),
            new DspChannelChain(
                DelayMs: 0.4,
                Crossover: new CrossoverSpec(
                    CrossoverKind.HighPass,
                    HighPassEdge: new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24))),
            SampleRate);

        double delay = VirtualCrossoverAnalysis.FindBestDelayMs(
            low, [high], SampleRate, 500, 2_000);

        Assert.Equal(0.4, delay, 2);
    }

    [Fact]
    public void AverageSumLossDb_IsZeroForCoherentCurves_AndNegativeUnderCancellation()
    {
        // Two identical +0 dB channels sum to +6.02 dB: zero loss. A sum sitting
        // at the channel level instead loses 6.02 dB.
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
            coherentSum, [channel, channel], 100, 10_000);
        double? loss = VirtualCrossoverAnalysis.AverageSumLossDb(
            degradedSum, [channel, channel], 100, 10_000);

        Assert.NotNull(zeroLoss);
        Assert.Equal(0.0, zeroLoss.Value, 3);
        Assert.NotNull(loss);
        Assert.Equal(-6.0206, loss.Value, 3);
    }

    [Fact]
    public void MinimumSumLossDb_ReadsTheDeepestNotch()
    {
        // A narrow -12 dB notch barely moves the average but must read at full
        // depth as the dip.
        var channel = new List<SignalPoint>
        {
            new(500, 0.0), new(1_000, 0.0), new(2_000, 0.0)
        };
        var sum = new List<SignalPoint>
        {
            new(500, 6.0206), new(1_000, -5.9794), new(2_000, 6.0206)
        };

        double? dip = VirtualCrossoverAnalysis.MinimumSumLossDb(
            sum, [channel, channel], 100, 10_000);

        Assert.NotNull(dip);
        Assert.Equal(-12.0, dip.Value, 3);
    }

    [Fact]
    public void AverageSumLossDb_IgnoresPointsOutsideTheWindow()
    {
        var channel = new List<SignalPoint> { new(100, 0.0), new(1_000, 0.0) };
        var sum = new List<SignalPoint> { new(100, -20.0), new(1_000, 6.0206) };

        double? loss = VirtualCrossoverAnalysis.AverageSumLossDb(
            sum, [channel, channel], 500, 2_000);

        Assert.NotNull(loss);
        Assert.Equal(0.0, loss.Value, 3);
    }

    [Fact]
    public void SumLossCurve_IsThePerPointComplexVsMagnitudeSumGap()
    {
        // Two identical +0 dB channels: their phase-blind magnitude sum is +6.02 dB,
        // so the loss at each point is the sum curve minus 6.02.
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
    public void SumLossCurve_TruncatesToTheShortestGrid()
    {
        var shortChannel = new List<SignalPoint> { new(500, 0.0) };
        var sum = new List<SignalPoint> { new(500, 0.0), new(1_000, 0.0) };

        Assert.Single(VirtualCrossoverAnalysis.SumLossCurve(sum, [shortChannel]));
    }

    [Fact]
    public void SumLossCurve_GatesPointsFarBelowTheirLocalNeighborhoodPeak()
    {
        // Outside every channel's band the "loss" is the phase arithmetic of
        // noise floors — it swings to deep fake dips no listener can hear. A
        // point whose combined channel magnitude sits more than the level gate
        // below the loudest level within an octave of it reads NaN, so the drawn
        // curve breaks there and the avg/dip read-outs skip it. The reference is
        // local, so a quiet-but-in-band region (a tilted treble) is judged against
        // its own level rather than a distant louder band.
        var channel = new List<SignalPoint>
        {
            new(500, 0.0), new(1_000, 0.0), new(1_500, -40.0), new(6_000, -3.0)
        };
        var sum = new List<SignalPoint>
        {
            new(500, 0.0), new(1_000, 0.0), new(1_500, -40.0), new(6_000, -3.0)
        };

        List<SignalPoint> loss = VirtualCrossoverAnalysis.SumLossCurve(sum, [channel]);

        // 1500 Hz sits 40 dB below its neighbor at 1000 Hz (within an octave) — gated.
        Assert.True(double.IsNaN(loss[2].Y));
        // The in-band points read their true 0 dB single-channel loss.
        Assert.Equal(0.0, loss[0].Y, 3);
        Assert.Equal(0.0, loss[1].Y, 3);
        // 6 kHz is quiet in absolute terms, but no louder neighbor sits within an
        // octave (nearest is 1.5 kHz, further down and quieter), so it is kept.
        Assert.Equal(0.0, loss[3].Y, 3);
        double? dip = VirtualCrossoverAnalysis.MinimumSumLossDb(
            sum, [channel], 20, 8_000);
        Assert.NotNull(dip);
        Assert.Equal(0.0, dip.Value, 3);
    }

    [Fact]
    public void GroupDelayMs_OfAPureDelay_EqualsTheDelay()
    {
        // A pure delay has a constant group delay equal to the delay itself.
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
            SampleRate);
        Complex[] fixedDelayed = VirtualCrossoverAnalysis.ApplyChain(
            fixedIr, new DspChannelChain(DelayMs: 0.5), SampleRate);

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
        // A realistic crossover pair: LR24 low-pass vs high-pass at 1 kHz sum
        // in phase, so the truth is the applied 0.4 ms delay with no flip. The
        // ±1.5 ms window spans the (flip + half-period) impostors at ±0.5 ms
        // around it; the loss-based score must reject them by the off-corner
        // cancellations they create, without any prior.
        Complex[] variable = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(8_192, 200),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.HighPass,
                HighPassEdge: new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24))),
            SampleRate);
        Complex[] fixedIr = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(8_192, 200),
            new DspChannelChain(
                DelayMs: 0.4,
                Crossover: new CrossoverSpec(
                    CrossoverKind.LowPass,
                    new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24))),
            SampleRate);

        AlignmentResult result = VirtualCrossoverAnalysis.FindBestAlignment(
            variable, [fixedIr], SampleRate, 500, 2_000, -1.1, 1.9);

        Assert.False(result.InvertPolarity);
        Assert.Equal(0.4, result.DelayMs, 2);
    }

    [Fact]
    public void FindAlignmentCandidates_ReportsBothSidesOfTheDegeneracy()
    {
        // The same echo-bait construction: both the flipped solution against
        // the stronger echo and the direct alignment are local optima, and the
        // candidate list must expose both so a caller can disambiguate with
        // outside evidence (the channel's other junction).
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
        // Best first.
        Assert.True(candidates[0].ScoreDb >= candidates[^1].ScoreDb);
        // Without a prior the score is the raw in-band average plus the
        // dip-excess penalty; the dip (a minimum) can never sit above the
        // average.
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
        // The pole radius must be read in the ADDITIVE feedback convention
        // BiquadCoefficients uses (z² − A1·z − A2): the textbook 1 + a1 + a2
        // formulas mis-read every ordinary stable section as unstable and
        // pinned the padding at the 262144-sample maximum — ballooning every
        // Virtual DSP / Auto delay FFT for any crossover or PEQ.
        var chain = new DspChannelChain(Peq: new EqualizationCurve(
            new[] { new PeqBand(frequencyHz, q, gainDb) }));
        PreparedDspResponse prepared = PreparedDspResponse.Create(chain, 48_000);

        int tail = prepared.RequiredTailSamples(120.0, 8_192, 262_144);

        Assert.Equal(8_192, tail);
    }

    [Fact]
    public void RequiredTailSamples_CrossoverStaysAtTheMinimumPadding()
    {
        var chain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24)));
        PreparedDspResponse prepared = PreparedDspResponse.Create(chain, 48_000);

        Assert.Equal(8_192, prepared.RequiredTailSamples(120.0, 8_192, 262_144));
    }

    [Fact]
    public void RequiredTailSamples_LowFrequencyHighQPeqIsLongButFinite()
    {
        // 20 Hz / Q 10 rings for ~100k samples to −120 dB at 48 kHz — well
        // past the floor but comfortably under the cap; hitting the cap would
        // mean the section was misread as unstable.
        var chain = new DspChannelChain(Peq: new EqualizationCurve(
            new[] { new PeqBand(20, 10, 12) }));
        PreparedDspResponse prepared = PreparedDspResponse.Create(chain, 48_000);

        int tail = prepared.RequiredTailSamples(120.0, 8_192, 262_144);

        Assert.InRange(tail, 50_000, 262_143);
    }

    [Fact]
    public void ApplyChain_LowFrequencyHighQPeqDoesNotWrapIntoTheEarlyResponse()
    {
        // A 20 Hz / Q 10 / +12 dB peaking filter rings for hundreds of
        // milliseconds — far past the old fixed 8192-sample tail. With the IR
        // length near the FFT boundary the ring wrapped circularly into the
        // early response, corrupting the IR, the phase and every alignment
        // sum built on it. The padding now follows the chain's slowest pole.
        var ir = new Complex[57_000];
        ir[24_000] = Complex.One;
        var chain = new DspChannelChain(Peq: new EqualizationCurve(
            new[] { new PeqBand(20, 10, 12) }));

        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(ir, chain, 48_000);

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
        // The field regime where the polarity curves run shallow and nearly
        // tied: a gapped junction (LP 1300 / HP 1800 leaves a 0.66-octave
        // spectral hole). Candidates are seeded per polarity, so the list must
        // carry the best lobe of EACH polarity — AlignmentSelection's
        // normal-polarity preference needs the runner-up polarity present to
        // have anything to prefer. Also pins that a candidate's polarity is
        // its own optimum: refinement never flips it.
        Complex[] woofer = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(16_384, 400),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.LowPass,
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_300, 24))),
            SampleRate);
        Complex[] tweeter = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(16_384, 400),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.HighPass,
                HighPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.Butterworth, 1_800, 24))),
            SampleRate);

        IReadOnlyList<AlignmentCandidate> candidates =
            VirtualCrossoverAnalysis.FindAlignmentCandidates(
                tweeter, [woofer], SampleRate, 650, 2_600, -1.5, 1.5);

        AlignmentCandidate bestNormal = candidates.First(item => !item.InvertPolarity);
        AlignmentCandidate bestInverted = candidates.First(item => item.InvertPolarity);
        // The true handover (no delay, no flip) wins; the flipped lobe half a
        // period off stays in the list as a genuine near-tie the downstream
        // selection rules must see.
        Assert.Equal(bestNormal, candidates[0]);
        Assert.InRange(bestNormal.DelayMs, -0.05, 0.25);
        Assert.InRange(bestInverted.DelayMs, -0.4, -0.05);
        Assert.True(bestInverted.ScoreDb > bestNormal.ScoreDb - 0.5);
    }

    [Fact]
    public void FindAlignmentCandidates_ForcedPolarityReturnsThatSignHonestlyScored()
    {
        // The stereo polarity-inheritance path forces a driver's sign to match its
        // counterpart's. Forcing must generate ONLY that polarity's grid, so every
        // candidate is a genuine optimum of that sign — its delay and score belong
        // to it. Regression against the earlier fake: taking the opposite polarity's
        // winner and merely re-stamping its InvertPolarity bit, which kept a delay
        // (~half a period off) and a score that were never evaluated for the sign.
        Complex[] woofer = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(16_384, 400),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.LowPass,
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_300, 24))),
            SampleRate);
        Complex[] tweeter = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(16_384, 400),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.HighPass,
                HighPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.Butterworth, 1_800, 24))),
            SampleRate);

        IReadOnlyList<AlignmentCandidate> free =
            VirtualCrossoverAnalysis.FindAlignmentCandidates(
                tweeter, [woofer], SampleRate, 650, 2_600, -1.5, 1.5);
        IReadOnlyList<AlignmentCandidate> forced =
            VirtualCrossoverAnalysis.FindAlignmentCandidates(
                tweeter, [woofer], SampleRate, 650, 2_600, -1.5, 1.5,
                forcedPolarity: true);

        // Only the forced sign is returned.
        Assert.NotEmpty(forced);
        Assert.All(forced, candidate => Assert.True(candidate.InvertPolarity));

        // The forced winner IS the free search's inverted optimum — same delay and
        // score, honestly evaluated — not the (winning) normal candidate re-stamped.
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
        // An asymmetric junction (LR 12 dB high-pass vs Butterworth 48 dB
        // low-pass) with an in-band reflection on the woofer. The raw-average
        // optimum is an inverted lobe whose good average hides a deep smoothed
        // notch; a neighbouring non-inverted lobe averages slightly worse but
        // stays much flatter. Ranked by average alone the notched impostor
        // would reach the selection tie-breaks as the winner; the dip-excess
        // penalty must put the flat lobe first.
        var tweeterIr = new Complex[8_192];
        tweeterIr[480] = Complex.One;
        Complex[] tweeter = VirtualCrossoverAnalysis.ApplyChain(
            tweeterIr,
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.HighPass,
                HighPassEdge: new CrossoverEdge(
                    CrossoverFilterFamily.LinkwitzRiley, 1_000, 12))),
            SampleRate);

        var wooferIr = new Complex[8_192];
        wooferIr[480] = Complex.One;
        wooferIr[480 + 96] = new Complex(0.7, 0); // reflection 2 ms after the arrival
        Complex[] woofer = VirtualCrossoverAnalysis.ApplyChain(
            wooferIr,
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.LowPass,
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_000, 48))),
            SampleRate);

        IReadOnlyList<AlignmentCandidate> candidates =
            VirtualCrossoverAnalysis.FindAlignmentCandidates(
                tweeter, [woofer], SampleRate, 500, 2_000, -3, 3);

        AlignmentCandidate winner = candidates[0];
        AlignmentCandidate notched = candidates.Single(item =>
            item.InvertPolarity && Math.Abs(item.DelayMs - 0.73) < 0.15);
        Assert.False(winner.InvertPolarity);
        Assert.InRange(winner.DelayMs, 1.1, 1.4);
        // The impostor averages better yet notches deeper — and loses.
        Assert.True(notched.LossDb > winner.LossDb);
        Assert.True(notched.DipDb < winner.DipDb);
        Assert.True(notched.ScoreDb < winner.ScoreDb);
    }

    [Fact]
    public void FindBestAlignment_PriorBreaksTheFlippedLobeDegeneracy()
    {
        // The fixed channel carries an inverted echo (1.1x) half a period after
        // the arrival, so summing the variable channel flipped against the echo
        // genuinely scores a little better in-band than the direct alignment.
        // Without the prior the search takes that bait; a prior at the
        // arrival-based delay keeps the non-inverted solution.
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
        // A crossover-filtered impulse: the band-passed arrival detector must
        // land near the true excitation time despite the filter ringing.
        Complex[] ir = VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(8_192, 480),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.LowPass,
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24))),
            SampleRate);

        double arrivalMs = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
            ir, SampleRate, 20, 1_000);

        // 480 samples at 48 kHz = 10 ms; the LR24 low-pass group delay adds a small
        // fraction of a millisecond. The previous ±1.5 ms window was wide enough to
        // pass even if the estimate ignored the offset entirely; this tighter window
        // (commensurate with the actual group delay) requires it to track the arrival.
        Assert.InRange(arrivalMs, 10.0, 10.6);
    }

    [Fact]
    public void MeasureBandLevelDb_ReadsTheGainDifferenceBetweenResponses()
    {
        // The absolute figure carries an arbitrary reference; the contract is
        // the DIFFERENCE between two responses over the same band — here an
        // exact virtual -6 dB gain stage.
        Complex[] reference = UnitImpulse(8_192, 480);
        Complex[] quieter = VirtualCrossoverAnalysis.ApplyChain(
            reference,
            new DspChannelChain(GainDb: -6),
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
        // A comb (impulse minus an equal copy 200 samples later) is the archetype
        // of cabin interference: |H|^2 = 2 - 2cos(200w), a train of deep nulls whose
        // period (48 kHz / 200 = 240 Hz) packs ~10 nulls into 400-2800 Hz. Its mean
        // energy is 2 (the peaks reach +6 dB), so an energy average reads +3.01 dB
        // over the single impulse, while a dB (geometric-mean) average reads ~0 dB
        // because the nulls' depth exactly cancels the peaks in the log domain.
        // Pins that the metric averages POWER, not dB.
        Complex[] single = UnitImpulse(8_192, 480);
        Complex[] comb = UnitImpulse(8_192, 480);
        comb[680] = -Complex.One;

        double? singleLevel =
            VirtualCrossoverAnalysis.MeasureBandLevelDb(single, SampleRate, 400, 2_800);
        double? combLevel =
            VirtualCrossoverAnalysis.MeasureBandLevelDb(comb, SampleRate, 400, 2_800);

        Assert.NotNull(singleLevel);
        Assert.NotNull(combLevel);
        // Power domain: ~+3 dB (measured 2.85, shy of the ideal 3.01 by finite-band
        // and discrete-bin effects). A dB-domain mean would land near 0 dB.
        Assert.InRange(combLevel.Value - singleLevel.Value, 2.5, 3.1);
    }

    [Fact]
    public void MeasureBandLevelDb_BandWithoutBinsReturnsNull()
    {
        // 23 990 - 23 999 Hz at 48 kHz falls between the last usable FFT bin
        // and Nyquist: no bins, no level.
        Complex[] ir = UnitImpulse(8_192, 480);

        Assert.Null(VirtualCrossoverAnalysis.MeasureBandLevelDb(
            ir, SampleRate, 23_990, 23_999));
    }

    [Fact]
    public void AnalyzeBandLimitedArrival_RefusesABandNarrowerThanAThirdOctave()
    {
        // The band used to be widened to at least half an octave behind the
        // caller's back, so a deliberately exact band (an L/R shared band, a
        // localization sub-band) was silently replaced by a different
        // question. Now the band passes through as given and a band too
        // narrow to place an arrival in is refused as invalid — not answered
        // with a plausible-looking number from a wider band.
        Complex[] ir = UnitImpulse(8_192, 480);

        TimeAlignmentAnalysisResult narrow =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                ir, SampleRate, 1_000, 1_100);

        Assert.False(narrow.IsValid);
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
        // The channels are 400 samples (8.33 ms) apart — far outside a ±3 ms
        // window around zero. Centering the window on that arrival estimate is
        // what lets the search reach the true peak; without it the search is
        // trapped inside ±3 ms and cannot find the offset.
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

        // Trapped in ±3 ms around zero, the uncentered search cannot reach the
        // 8.33 ms peak and reports a weak in-window extremum instead.
        Assert.True(Math.Abs(uncentered.BestByMagnitude.DelayMs) <= 3.05);
        Assert.True(Math.Abs(uncentered.PositivePeak.Coefficient) < 0.5);
    }

    [Fact]
    public void FindBandLimitedCorrelationDelay_ExposesPositivePeakAndInvertedTrough()
    {
        // The positive peak and the negative trough are reported independently of
        // which one wins by magnitude: the seed path reads PositivePeak directly,
        // so its non-inverted flag and the trough's inverted flag must hold.
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
        // The true peak sits ~4.1 ms out while the window reaches 3 ms: the
        // argmax lands on (or one grid sample inside) the boundary — a cut
        // through the rising lobe, not a measured extremum, so its position
        // AND magnitude are artifacts of where the window ended. The flag is
        // the honest signal callers gate on; re-centering the window on the
        // true lag clears it.
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
        // Sub-sample refinement may move the interior peak a hair off the
        // sample grid; the point here is the flag, not the position.
        Assert.InRange(centered.PositivePeak.DelayMs, offsetMs - 0.05, offsetMs + 0.05);
    }

    [Fact]
    public void FindBandLimitedCorrelationDelay_ReportsTheSamePolarityRival()
    {
        // Two positive copies ~a period apart in the second channel produce
        // two same-polarity alignment lobes. The strongest OTHER lobe must
        // come back as PositiveRival — the ambiguity peak-vs-trough
        // Confidence cannot see — while a clean single-copy pair reports no
        // comparable rival (kernel side structure only).
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
        // The two lobes sit the copies' spacing apart and carry comparable
        // whitened correlation — the near-tie the seed gate must refuse.
        Assert.InRange(
            Math.Abs(result.PositivePeak.DelayMs - result.PositiveRival.DelayMs),
            10.0, 13.5);
        Assert.True(
            result.PositivePeak.Coefficient - result.PositiveRival.Coefficient < 0.2);
    }

    [Fact]
    public void AnalyzeBandLimitedArrival_ZeroPaddedTailDoesNotInflateTheSnr()
    {
        // The band-limited twin of the broadband-onset padding contract: the
        // synthetic power-of-two tail ApplyChain appends must not collapse the
        // quantile noise floor behind the arrival SNR that the stereo bridge
        // and the cross-side ladder gate on.
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
        // The band-limited twin of the onset metadata contract: a short
        // record through a REAL scale-only ApplyChain (content far before the
        // padded record's midpoint) analyzed with the validSampleCount
        // metadata must read exactly like an explicit crop to it.
        var random = new Random(20_260_723);
        var raw = new Complex[4_096];
        for (int i = 0; i < raw.Length; i++)
        {
            raw[i] = new Complex((random.NextDouble() * 2.0 - 1.0) * 1e-3, 0.0);
        }
        raw[2_000] += Complex.One;

        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            raw, new DspChannelChain(DelayMs: 25), SampleRate,
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

        // The delay prefix is excluded from the noise floor while the arrival
        // position stays in full-record coordinates: the raw read plus the
        // 25 ms delay, at the raw record's SNR.
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
        // The mirror of the positive-rival case: two INVERTED copies ~a
        // period apart produce two same-polarity trough lobes, and a caller
        // seeding from the dominant trough owes it the same rival scrutiny —
        // the strongest OTHER negative lobe must come back as NegativeRival.
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
            SampleRate);
        Assert.Equal(
            PolarityEstimate.Negative,
            VirtualCrossoverAnalysis.EstimatePolarity(negative));
    }

    [Fact]
    public void EstimatePolarity_IgnoresALargerLaterRingingLobe()
    {
        // A band-limited driver: the arrival swings positive, then the ringing
        // grows into a bigger negative lobe. The polarity is set by the first
        // significant excursion, not the global extremum.
        var ir = new Complex[256];
        for (int i = 0; i < 8; i++)
        {
            ir[80 + i] = new Complex(0.7 * Math.Sin(i / 8.0 * Math.PI), 0);
            ir[92 + i] = new Complex(-1.0 * Math.Sin(i / 8.0 * Math.PI), 0);
        }

        Assert.Equal(
            PolarityEstimate.Positive,
            VirtualCrossoverAnalysis.EstimatePolarity(ir));

        // Low-level noise ahead of the arrival stays below the threshold and
        // must not steal the polarity call.
        ir[10] = new Complex(-0.2, 0);
        Assert.Equal(
            PolarityEstimate.Positive,
            VirtualCrossoverAnalysis.EstimatePolarity(ir));
    }

    [Fact]
    public void EstimatePolarity_ReadsASmallLeadingLobeDespitePreRinging()
    {
        // A wide-band driver: symmetric anti-aliasing pre-ringing (~8% of the
        // peak, arbitrary signs), then a modest positive leading lobe (~35%)
        // followed by much larger ringing in both directions. The polarity is
        // set by the leading lobe, not by the pre-ringing or the deep ringing.
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
            Array.Empty<Complex>(), DspChannelChain.Identity, SampleRate));
        Assert.Throws<ArgumentOutOfRangeException>(() => VirtualCrossoverAnalysis.ApplyChain(
            UnitImpulse(16, 0), DspChannelChain.Identity, 0));
        Assert.Throws<ArgumentException>(
            () => VirtualCrossoverAnalysis.SumImpulseResponses([]));
        Assert.Throws<ArgumentException>(
            () => VirtualCrossoverAnalysis.FindPeakIndex(Array.Empty<Complex>()));
    }
}
