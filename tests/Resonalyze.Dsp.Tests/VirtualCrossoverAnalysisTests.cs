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

        // 480 samples at 48 kHz = 10 ms; the LR24 group delay adds a fraction.
        Assert.InRange(arrivalMs, 9.5, 11.5);
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
