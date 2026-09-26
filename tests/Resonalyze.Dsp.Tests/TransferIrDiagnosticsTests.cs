using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp.Tests;

public sealed class TransferIrDiagnosticsTests
{
    private const int SampleRate = 48_000;

    private static void AddToneBurst(
        double[] impulseResponse,
        double startMs,
        double frequencyHz,
        int periods,
        double amplitude)
    {
        int start = (int)Math.Round(startMs * SampleRate / 1000.0);
        int length = (int)Math.Round(periods * SampleRate / frequencyHz);
        for (int i = 0; i < length && start + i < impulseResponse.Length; i++)
        {
            double hann = 0.5 - 0.5 * Math.Cos(Math.Tau * i / length);
            impulseResponse[start + i] +=
                amplitude * hann * Math.Sin(Math.Tau * frequencyHz * i / SampleRate);
        }
    }

    // v3 midbass shape: band-limited arrival, out-of-band content travelling with it, a broadband interface click at a fixed early sample.
    private static double[] CrosstalkRecord(
        double clickAmplitude,
        double outOfBandAmplitude = 0.05)
    {
        var impulseResponse = new double[32_768];
        AddToneBurst(impulseResponse, startMs: 40.0, frequencyHz: 300, periods: 30, amplitude: 1.0);
        if (outOfBandAmplitude > 0)
        {
            AddToneBurst(impulseResponse, startMs: 40.0, frequencyHz: 2000, periods: 40, amplitude: outOfBandAmplitude);
        }
        if (clickAmplitude > 0)
        {
            impulseResponse[30] = clickAmplitude;
        }
        return impulseResponse;
    }

    [Fact]
    public void DetectDominantBand_BracketsANarrowbandRecord()
    {
        var impulseResponse = new double[32_768];
        AddToneBurst(impulseResponse, startMs: 20.0, frequencyHz: 500, periods: 40, amplitude: 1.0);

        DominantBand band = TransferIrDiagnostics.DetectDominantBand(impulseResponse, SampleRate);

        Assert.InRange(band.PeakHz, 400, 620);
        Assert.True(band.LowHz < 500 && band.HighHz > 500);
        Assert.True(
            Math.Log2(band.HighHz / band.LowHz) < 2.0,
            $"band {band.LowHz:0}-{band.HighHz:0} Hz is too wide for a narrowband burst");
    }

    [Fact]
    public void DetectDominantBand_BridgesANarrowCancellationNotch()
    {
        // A deep narrow in-cabin notch must be stepped across, not split the band into islands.
        var impulseResponse = new double[65_536];
        for (int k = 0; k <= 15; k++)
        {
            if (k is 5 or 6)
            {
                continue; // the notch: ~356-400 Hz carved out
            }
            AddToneBurst(
                impulseResponse,
                startMs: 20.0,
                frequencyHz: 200.0 * Math.Pow(2.0, k / 6.0),
                periods: 60,
                amplitude: 1.0);
        }

        DominantBand band = TransferIrDiagnostics.DetectDominantBand(impulseResponse, SampleRate);

        Assert.True(
            band.LowHz < 220,
            $"low edge {band.LowHz:0} Hz should reach the 200 Hz component");
        Assert.True(
            band.HighHz > 1000,
            $"high edge {band.HighHz:0} Hz should cross the notch to the upper island");
    }

    [Fact]
    public void DetectDominantBand_CoversABroadbandImpulse()
    {
        var impulseResponse = new double[32_768];
        impulseResponse[300] = 1.0;

        DominantBand band = TransferIrDiagnostics.DetectDominantBand(impulseResponse, SampleRate);

        Assert.True(band.LowHz <= 25);
        Assert.True(band.HighHz >= 15_000);
    }

    [Fact]
    public void DetectDominantBand_IgnoresAFalsePeakBelowTheCoherenceFloor()
    {
        var impulseResponse = new double[65_536];
        AddToneBurst(
            impulseResponse,
            startMs: 20.0,
            frequencyHz: 160,
            periods: 40,
            amplitude: 10.0);
        AddToneBurst(
            impulseResponse,
            startMs: 20.0,
            frequencyHz: 3_000,
            periods: 40,
            amplitude: 1.0);
        var coherence = new double[impulseResponse.Length / 2 + 1];
        int firstTrustedBin = (int)Math.Ceiling(
            500.0 * impulseResponse.Length / SampleRate);
        Array.Fill(coherence, 1.0, firstTrustedBin, coherence.Length - firstTrustedBin);

        DominantBand rawBand = TransferIrDiagnostics.DetectDominantBand(
            impulseResponse, SampleRate);
        DominantBand trustedBand = TransferIrDiagnostics.DetectDominantBand(
            impulseResponse,
            SampleRate,
            coherence: coherence);

        Assert.InRange(rawBand.PeakHz, 120, 220);
        Assert.InRange(trustedBand.PeakHz, 2_500, 3_500);
        Assert.True(trustedBand.LowHz >= 500);
    }

    [Fact]
    public void DetectDominantBand_DoesNotPromoteOneTrustedBinToAWholeBand()
    {
        var impulseResponse = new double[65_536];
        AddToneBurst(
            impulseResponse,
            startMs: 20.0,
            frequencyHz: 2_000,
            periods: 80,
            amplitude: 1.0);
        AddToneBurst(
            impulseResponse,
            startMs: 20.0,
            frequencyHz: 5_000,
            periods: 80,
            amplitude: 20.0);
        var coherence = new double[impulseResponse.Length / 2 + 1];
        int trustedLowBin = (int)Math.Floor(
            1_700.0 * impulseResponse.Length / SampleRate);
        int trustedHighBin = (int)Math.Ceiling(
            2_300.0 * impulseResponse.Length / SampleRate);
        Array.Fill(
            coherence,
            1.0,
            trustedLowBin,
            trustedHighBin - trustedLowBin + 1);
        int isolatedBin = (int)Math.Round(
            5_000.0 * impulseResponse.Length / SampleRate);
        coherence[isolatedBin] = 1.0;

        DominantBand band = TransferIrDiagnostics.DetectDominantBand(
            impulseResponse,
            SampleRate,
            coherence: coherence);

        Assert.InRange(band.PeakHz, 1_700, 2_300);
        Assert.True(band.HighHz < 3_000);
    }

    [Fact]
    public void DetectCrosstalkHead_FindsTheEarlyBroadbandClick()
    {
        // -21 dB re max: the click is the full-band First Arrival until removed.
        double[] impulseResponse = CrosstalkRecord(clickAmplitude: 0.09);

        CrosstalkHeadGate? gate = TransferIrDiagnostics.DetectCrosstalkHead(
            impulseResponse, SampleRate);

        Assert.NotNull(gate);
        Assert.True(gate.Value.GateEndSample > 30);
        Assert.True(gate.Value.GateEndSample < SampleRate * 30 / 1000);
        Assert.InRange(gate.Value.BurstTimeMs, 0.3, 1.1);
    }

    [Fact]
    public void DetectCrosstalkHead_AClickHotterThanTheDriversTailIsStillFound()
    {
        // The click outguns the out-of-band content: a 'strongest peak comes later' detector would go blind.
        double[] impulseResponse = CrosstalkRecord(clickAmplitude: 0.15);

        CrosstalkHeadGate? gate = TransferIrDiagnostics.DetectCrosstalkHead(
            impulseResponse, SampleRate);

        Assert.NotNull(gate);
        Assert.True(gate.Value.GateEndSample > 30);
        Assert.True(gate.Value.GateEndSample < SampleRate * 30 / 1000);
    }

    [Fact]
    public void DetectCrosstalkHead_AClickThatIsTheOnlyComplementEventIsFound()
    {
        double[] impulseResponse = CrosstalkRecord(
            clickAmplitude: 0.09, outOfBandAmplitude: 0.0);

        CrosstalkHeadGate? gate = TransferIrDiagnostics.DetectCrosstalkHead(
            impulseResponse, SampleRate);

        Assert.NotNull(gate);
        Assert.True(gate.Value.GateEndSample > 30);
        Assert.True(gate.Value.GateEndSample < SampleRate * 30 / 1000);
    }

    [Fact]
    public void DetectCrosstalkHead_CleanRecordYieldsNoGate()
    {
        double[] impulseResponse = CrosstalkRecord(clickAmplitude: 0.0);

        Assert.Null(TransferIrDiagnostics.DetectCrosstalkHead(impulseResponse, SampleRate));
    }

    [Fact]
    public void DetectCrosstalkHead_AGenuineWeakEarlyArrivalIsNotGated()
    {
        // A weak in-band early front has no complement island.
        double[] impulseResponse = CrosstalkRecord(clickAmplitude: 0.0);
        AddToneBurst(impulseResponse, startMs: 5.0, frequencyHz: 300, periods: 10, amplitude: 0.1);

        Assert.Null(TransferIrDiagnostics.DetectCrosstalkHead(impulseResponse, SampleRate));
    }

    [Fact]
    public void DetectCrosstalkHead_AFullRangeRecordIsLeftAlone()
    {
        var impulseResponse = new double[32_768];
        impulseResponse[30] = 0.02;
        impulseResponse[2_000] = 1.0;

        Assert.Null(TransferIrDiagnostics.DetectCrosstalkHead(impulseResponse, SampleRate));
    }

    [Fact]
    public void EstimateIrStart_DelayPrefixDoesNotInflateTheSnr()
    {
        // A chain delay's silent prefix sinks the noise floor: a refused noise record read 'credible' at 22.5 ms without the valid range.
        var random = new Random(20_260_724);
        var raw = new Complex[4_096];
        for (int i = 0; i < raw.Length; i++)
        {
            raw[i] = new Complex(random.NextDouble() * 2.0 - 1.0, 0.0);
        }
        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            raw, new DspChannelChain(DelayMs: 25), 48_000, 48_000,
            out ValidSampleRange validRange);

        Assert.Null(TransferIrDiagnostics.EstimateIrStart(raw, 48_000));
        Assert.NotNull(TransferIrDiagnostics.EstimateIrStart(processed, 48_000));
        Assert.Null(TransferIrDiagnostics.EstimateIrStart(
            processed, 48_000, validRange));

        var front = new Complex[8_192];
        front[480] = Complex.One;
        Complex[] frontDelayed = VirtualCrossoverAnalysis.ApplyChain(
            front, new DspChannelChain(DelayMs: 25), 48_000, 48_000,
            out ValidSampleRange frontRange);
        IrStartEstimate? plain =
            TransferIrDiagnostics.EstimateIrStart(front, 48_000);
        IrStartEstimate? guarded = TransferIrDiagnostics.EstimateIrStart(
            frontDelayed, 48_000, frontRange);
        Assert.NotNull(plain);
        Assert.NotNull(guarded);
        Assert.Equal(plain.Value.StartMs + 25.0, guarded.Value.StartMs, 2);
    }

    [Theory]
    [InlineData(0.09)]
    [InlineData(0.15)]
    public void EstimateIrStart_LandsOnTheFrontDespiteTheHeadClick(
        double clickAmplitude)
    {
        // The sample-30 click carries no in-band energy: the in-band read must find the 40 ms front.
        double[] impulseResponse = CrosstalkRecord(clickAmplitude);

        IrStartEstimate? start = TransferIrDiagnostics.EstimateIrStart(
            impulseResponse, SampleRate);

        Assert.NotNull(start);
        Assert.True(start.Value.DominantBandLimited);
        Assert.InRange(start.Value.StartMs, 40.0, 65.0);
        Assert.True(start.Value.EarlyMs <= start.Value.StartMs);
        Assert.True(start.Value.StartMs <= start.Value.LateMs);
    }

    [Fact]
    public void EstimateIrStart_ACabinModeDoesNotDragTheReadOffTheArrival()
    {
        // A cabin mode ~20 dB over the working band collapses the 15 dB content band; too narrow to resolve the front (15.9 ms for 20 ms).
        var impulseResponse = new double[65_536];
        int arrival = SampleRate * 20 / 1000;
        double decay = 30.0 * SampleRate / 1000.0;
        for (int i = 0; arrival + i < impulseResponse.Length && i < 12 * decay; i++)
        {
            impulseResponse[arrival + i] += Math.Exp(-i / decay) *
                Math.Sin(Math.Tau * 123.0 * i / SampleRate);
        }
        int front = (int)Math.Round(0.4 * SampleRate / 1000.0);
        for (int i = 0; i < front; i++)
        {
            impulseResponse[arrival + i] +=
                10.0 * (0.5 - 0.5 * Math.Cos(Math.Tau * i / front));
        }

        DominantBand contentBand = TransferIrDiagnostics.DetectDominantBand(
            impulseResponse, SampleRate);
        Assert.True(
            contentBand.HighHz < 300,
            $"content band {contentBand.LowHz:0}-{contentBand.HighHz:0} Hz no longer collapses");

        IrStartEstimate? start = TransferIrDiagnostics.EstimateIrStart(
            impulseResponse, SampleRate);

        Assert.NotNull(start);
        Assert.True(start.Value.DominantBandLimited);
        Assert.True(
            start.Value.BandHighHz > 1_000,
            $"the arrival band stopped at {start.Value.BandHighHz:0} Hz, inside the mode");
        Assert.InRange(start.Value.StartMs, 19.0, 20.05);
        Assert.True(
            start.Value.EarlyMs >= 18.5,
            $"even the 10 % crossing must hug the arrival; it read {start.Value.EarlyMs:0.00} ms");
    }

    [Fact]
    public void EstimateIrStart_ReadsASharpBroadbandFrontTightly()
    {
        var impulseResponse = new double[32_768];
        int front = SampleRate * 20 / 1000;
        impulseResponse[front] = 1.0;

        IrStartEstimate? start = TransferIrDiagnostics.EstimateIrStart(
            impulseResponse, SampleRate);

        Assert.NotNull(start);
        Assert.InRange(start.Value.StartMs, 19.5, 20.05);
        Assert.InRange(
            start.Value.LateMs - start.Value.EarlyMs, 0.0, 0.5);
    }

    [Fact]
    public void EstimateIrStart_AFrontRunningOffTheRecordHeadStaysAtZero()
    {
        // Envelope already high at sample 0: crossings stop at the record start, no negative time.
        var impulseResponse = new double[65_536];
        double decay = 40.0 * SampleRate / 1000.0;
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            impulseResponse[i] =
                Math.Exp(-i / decay) * Math.Sin(Math.Tau * 45.0 * i / SampleRate);
        }

        IrStartEstimate? start = TransferIrDiagnostics.EstimateIrStart(
            impulseResponse, SampleRate);

        Assert.NotNull(start);
        Assert.True(
            start.Value.EarlyMs >= 0.0,
            $"the 10 % crossing read {start.Value.EarlyMs:0.00} ms");
        Assert.True(start.Value.StartMs >= 0.0);
        Assert.True(start.Value.LateMs >= 0.0);
    }

    [Fact]
    public void EstimateIrStart_ComplexOverloadMatchesTheRealOne()
    {
        double[] impulseResponse = CrosstalkRecord(clickAmplitude: 0.09);
        var complexIr = Array.ConvertAll(
            impulseResponse, v => new System.Numerics.Complex(v, 0.0));

        IrStartEstimate? fromReal = TransferIrDiagnostics.EstimateIrStart(
            impulseResponse, SampleRate);
        IrStartEstimate? fromComplex = TransferIrDiagnostics.EstimateIrStart(
            complexIr, SampleRate);

        Assert.Equal(fromReal, fromComplex);
    }

    [Fact]
    public void EstimateIrStart_RefusesSilenceAndDegenerateInput()
    {
        Assert.Null(TransferIrDiagnostics.EstimateIrStart(
            new double[16_384], SampleRate));
        Assert.Null(TransferIrDiagnostics.EstimateIrStart(
            Array.Empty<double>(), SampleRate));
        Assert.Null(TransferIrDiagnostics.EstimateIrStart(
            new double[] { 1.0 }, SampleRate));
        Assert.Null(TransferIrDiagnostics.EstimateIrStart(
            new double[] { 1.0 }, sampleRate: 0));
    }

    [Fact]
    public void EstimateIrStart_RefusesANoiseOnlyRecord()
    {
        // Noise still has a strongest peak; only the SNR floor exposes no front.
        var impulseResponse = new double[65_536];
        uint state = 12_345;
        double NextUniform()
        {
            state = state * 1_664_525u + 1_013_904_223u;
            return state / 4_294_967_296.0;
        }
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            double sum = 0;
            for (int k = 0; k < 12; k++)
            {
                sum += NextUniform();
            }
            impulseResponse[i] = sum - 6.0;
        }

        Assert.Null(TransferIrDiagnostics.EstimateIrStart(
            impulseResponse, SampleRate));
    }

    private static double[] StationaryNoise(int length, uint seed)
    {
        var samples = new double[length];
        uint state = seed;
        for (int i = 0; i < samples.Length; i++)
        {
            state = state * 1_664_525u + 1_013_904_223u;
            samples[i] = state / 4_294_967_296.0 - 0.5;
        }
        return samples;
    }

    [Fact]
    public void MeasureCompactness_GenuineDecayReadsHigh()
    {
        // Causal, front-loaded arrival (field records read 28.8-48.6 dB); the symmetric kernel case is covered below.
        double[] impulseResponse = StationaryNoise(131_072, seed: 7);
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            impulseResponse[i] *= 0.001;
        }
        int start = (int)(0.010 * SampleRate);
        double decaySamples = 0.050 * SampleRate;
        for (int i = 0; i < (int)(0.500 * SampleRate); i++)
        {
            impulseResponse[start + i] +=
                Math.Exp(-i / decaySamples) *
                Math.Sin(Math.Tau * 300 * i / SampleRate);
        }

        TransferIrCompactness? compactness =
            TransferIrDiagnostics.MeasureCompactness(impulseResponse, SampleRate);

        Assert.NotNull(compactness);
        Assert.True(
            compactness.Value.InsideOutsideDb >=
                TransferIrDiagnostics.MinimumCompactnessDb + 10,
            $"genuine shape read {compactness.Value.InsideOutsideDb:0.0} dB");
        Assert.InRange(compactness.Value.PeakDelayMs, 5, 30);
    }

    // Transfer from an unusable loopback: stationary division noise with wrap spikes (field 11.2-15.7 dB).
    [Fact]
    public void MeasureCompactness_StationaryNoiseWithWrapSpikesReadsLow()
    {
        double[] impulseResponse = StationaryNoise(262_144, seed: 42);
        impulseResponse[1] = 300;
        impulseResponse[^2] = -240;

        TransferIrCompactness? compactness =
            TransferIrDiagnostics.MeasureCompactness(impulseResponse, SampleRate);

        Assert.NotNull(compactness);
        Assert.True(
            compactness.Value.InsideOutsideDb <
                TransferIrDiagnostics.MinimumCompactnessDb,
            $"garbage shape read {compactness.Value.InsideOutsideDb:0.0} dB");
    }

    [Fact]
    public void MeasureCompactness_PureNoiseReadsNearZero()
    {
        TransferIrCompactness? compactness = TransferIrDiagnostics.MeasureCompactness(
            StationaryNoise(262_144, seed: 9), SampleRate);

        Assert.NotNull(compactness);
        Assert.InRange(compactness.Value.InsideOutsideDb, -3, 3);
    }

    // No peak-position rule: an electrical chain legitimately peaks at zero delay.
    [Fact]
    public void MeasureCompactness_ElectricalDeltaAtZeroPasses()
    {
        var impulseResponse = new double[65_536];
        impulseResponse[0] = 1.0;

        TransferIrCompactness? compactness =
            TransferIrDiagnostics.MeasureCompactness(impulseResponse, SampleRate);

        Assert.NotNull(compactness);
        Assert.True(
            compactness.Value.InsideOutsideDb >=
                TransferIrDiagnostics.MinimumCompactnessDb);
        Assert.Equal(0, compactness.Value.PeakDelayMs);
    }

    [Fact]
    public void MeasureCompactness_RefusesDegenerateInput()
    {
        Assert.Null(TransferIrDiagnostics.MeasureCompactness(
            new double[100], SampleRate));
        Assert.Null(TransferIrDiagnostics.MeasureCompactness(
            new double[65_536], SampleRate));
        Assert.Null(TransferIrDiagnostics.MeasureCompactness(
            StationaryNoise(65_536, seed: 3), sampleRate: 0));

        // Callers treat null as failure, so NaN can never pass silently.
        double[] poisonedByNaN = StationaryNoise(65_536, seed: 4);
        poisonedByNaN[123] = double.NaN;
        Assert.Null(TransferIrDiagnostics.MeasureCompactness(
            poisonedByNaN, SampleRate));
        double[] poisonedByInfinity = StationaryNoise(65_536, seed: 5);
        poisonedByInfinity[321] = double.PositiveInfinity;
        Assert.Null(TransferIrDiagnostics.MeasureCompactness(
            poisonedByInfinity, SampleRate));
    }

    // The zero-phase gate makes H(f)=1 a symmetric kernel with wrapped pre-ringing; a 10 ms pre-window fails 20-50 Hz at 19.9 dB.
    [Theory]
    [Trait("Category", "Slow")]
    [InlineData(20.0, 50.0)]
    [InlineData(20.0, 80.0)]
    [InlineData(50.0, 100.0)]
    [InlineData(100.0, 200.0)]
    [InlineData(0.0, 0.0)] // full range
    public void MeasureCompactness_JudgesGatedTransfersAtEveryBand(
        double lowFullHz, double highFullHz)
    {
        const int FrameLength = 262_144;
        double nyquist = SampleRate / 2.0;
        ExcitationBandGate gate = lowFullHz > 0
            ? new ExcitationBandGate(
                lowFullHz / 1.44 / nyquist,
                lowFullHz / nyquist,
                highFullHz / nyquist,
                Math.Min(1.0, highFullHz * 1.386 / nyquist))
            : ExcitationBandGate.FullBand;
        double[] reference = StationaryNoise(FrameLength, seed: 7);
        double[] uncorrelated = StationaryNoise(FrameLength, seed: 1234);

        double[] ideal = TransferFunction.ComputeAveragedRelativeIr(
            [new TransferFunctionFrame(reference, reference)],
            gate).ImpulseResponse;
        double[] garbage = TransferFunction.ComputeAveragedRelativeIr(
            [new TransferFunctionFrame(reference, uncorrelated)],
            gate).ImpulseResponse;

        TransferIrCompactness? idealCompactness =
            TransferIrDiagnostics.MeasureCompactness(ideal, SampleRate);
        TransferIrCompactness? garbageCompactness =
            TransferIrDiagnostics.MeasureCompactness(garbage, SampleRate);

        Assert.NotNull(idealCompactness);
        Assert.True(
            idealCompactness.Value.InsideOutsideDb >=
                TransferIrDiagnostics.MinimumCompactnessDb + 10,
            $"ideal {lowFullHz}-{highFullHz} Hz read " +
            $"{idealCompactness.Value.InsideOutsideDb:0.0} dB");
        Assert.NotNull(garbageCompactness);
        Assert.True(
            garbageCompactness.Value.InsideOutsideDb <
                TransferIrDiagnostics.MinimumCompactnessDb - 10,
            $"garbage {lowFullHz}-{highFullHz} Hz read " +
            $"{garbageCompactness.Value.InsideOutsideDb:0.0} dB");
    }

    [Fact]
    public void MeasureCompactness_ComplexTwinMatchesTheRealPath()
    {
        double[] impulseResponse = StationaryNoise(65_536, seed: 11);
        AddToneBurst(impulseResponse, startMs: 15.0, frequencyHz: 500, periods: 20, amplitude: 3.0);
        var complexIr = new System.Numerics.Complex[impulseResponse.Length];
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            complexIr[i] = impulseResponse[i];
        }

        Assert.Equal(
            TransferIrDiagnostics.MeasureCompactness(impulseResponse, SampleRate),
            TransferIrDiagnostics.MeasureCompactness(complexIr, SampleRate));
    }

    [Fact]
    public void CleanCrosstalkHead_ZerosTheHeadAndKeepsTheRest()
    {
        double[] impulseResponse = CrosstalkRecord(clickAmplitude: 0.09);
        CrosstalkHeadGate? gate = TransferIrDiagnostics.DetectCrosstalkHead(
            impulseResponse, SampleRate);
        Assert.NotNull(gate);

        var complexIr = Array.ConvertAll(
            impulseResponse, v => new System.Numerics.Complex(v, 0.0));
        System.Numerics.Complex[] clean = TransferIrDiagnostics.CleanCrosstalkHead(
            complexIr, SampleRate, gate.Value);

        Assert.Equal(complexIr.Length, clean.Length);
        Assert.Equal(0.0, clean[30].Magnitude);
        for (int i = 0; i < gate.Value.GateEndSample; i++)
        {
            Assert.Equal(0.0, clean[i].Magnitude);
        }
        int front = SampleRate * 40 / 1000;
        for (int i = front; i < front + 1000; i++)
        {
            Assert.Equal(complexIr[i], clean[i]);
        }
        Assert.Equal(0.09, impulseResponse[30]);
    }

    [Fact]
    public void ArrivalSharpness_IsHighForACleanArrival()
    {
        var impulseResponse = new Complex[1 << 14];
        impulseResponse[4_000] = new Complex(1.0, 0);
        impulseResponse[4_010] = new Complex(0.4, 0);
        impulseResponse[4_060] = new Complex(0.2, 0);

        double? sharpness = TransferIrDiagnostics.MeasureArrivalSharpnessDb(
            impulseResponse, SampleRate);

        Assert.NotNull(sharpness);
        Assert.True(
            sharpness >= TransferIrDiagnostics.MinimumArrivalSharpnessDb,
            $"sharpness was {sharpness}");
    }

    // A reverberant decay keeps only 12 % of energy near the arrival, which ruled out an energy-share measure.
    [Fact]
    public void ArrivalSharpness_SurvivesALongDecay()
    {
        var impulseResponse = new Complex[1 << 15];
        impulseResponse[8_000] = new Complex(1.0, 0);
        var random = new Random(11);
        for (int i = 1; i < SampleRate / 4; i++)
        {
            double decay = Math.Exp(-6.0 * i / (SampleRate / 4.0));
            impulseResponse[8_000 + i] =
                new Complex((random.NextDouble() - 0.5) * 0.5 * decay, 0);
        }

        double? sharpness = TransferIrDiagnostics.MeasureArrivalSharpnessDb(
            impulseResponse, SampleRate);

        Assert.True(
            sharpness >= TransferIrDiagnostics.MinimumArrivalSharpnessDb,
            $"a reverberant record must pass; sharpness was {sharpness}");
    }

    [Fact]
    public void ArrivalSharpness_IsLowForASmearedArrival()
    {
        var impulseResponse = new Complex[1 << 15];
        var random = new Random(7);
        for (int i = 0; i < SampleRate / 10; i++)
        {
            impulseResponse[8_000 + i] = new Complex(random.NextDouble() - 0.5, 0);
        }

        double? sharpness = TransferIrDiagnostics.MeasureArrivalSharpnessDb(
            impulseResponse, SampleRate);

        Assert.True(
            sharpness < TransferIrDiagnostics.MinimumArrivalSharpnessDb,
            $"a smeared arrival must fail; sharpness was {sharpness}");
    }

    [Fact]
    public void ArrivalSharpness_IsNullWhenThereIsNothingToMeasure()
    {
        Assert.Null(TransferIrDiagnostics.MeasureArrivalSharpnessDb(
            new Complex[1 << 12], SampleRate));
        Assert.Null(TransferIrDiagnostics.MeasureArrivalSharpnessDb(
            [new Complex(double.NaN, 0), new Complex(1, 0)], SampleRate));
        Assert.Null(TransferIrDiagnostics.MeasureArrivalSharpnessDb(
            [new Complex(1, 0)], sampleRate: 0));
    }

    // The symmetric gate kernel is the one legitimate acausal-looking shape; it must stay under the report line.
    [Theory]
    [Trait("Category", "Slow")]
    [InlineData(20.0, 25.0)]
    [InlineData(20.0, 50.0)]
    [InlineData(20.0, 1000.0)]
    [InlineData(50.0, 100.0)]
    [InlineData(0.0, 0.0)] // full range
    public void MeasurePreArrivalDb_IdealGatedTransfersStayUnderTheCeiling(
        double lowFullHz, double highFullHz)
    {
        const int FrameLength = 262_144;
        ExcitationBandGate gate = ProductionGate(lowFullHz, highFullHz);
        double[] reference = StationaryNoise(FrameLength, seed: 7);

        double[] ideal = TransferFunction.ComputeAveragedRelativeIr(
            [new TransferFunctionFrame(reference, reference)],
            gate).ImpulseResponse;

        Assert.True(
            TransferIrDiagnostics.CanJudgePreArrival(gate),
            $"a half-octave guard at {lowFullHz}-{highFullHz} Hz must be judgeable");
        double? preArrival = TransferIrDiagnostics.MeasurePreArrivalDb(ideal, SampleRate);
        Assert.NotNull(preArrival);
        Assert.True(
            preArrival.Value < TransferIrDiagnostics.SuspectPreArrivalDb - 4,
            $"ideal {lowFullHz}-{highFullHz} Hz read {preArrival.Value:0.0} dB");
    }

    // Magnitude-only resonance rings both ways, minimum-phase rings forward: compactness reads both the same, this measure does not.
    [Fact]
    public void MeasurePreArrivalDb_SeparatesADenominatorDipFromACabinResonance()
    {
        const int FrameLength = 262_144;
        // A 20-200 Hz take: the fault scales with what the record carries at the cancelled frequency.
        const double DepthDb = 18.0;
        double[] clean = SyntheticCabinTransfer(
            FrameLength, delayMs: 12.0, decaySeconds: 0.25, highFullHz: 200.0);
        var band = new PeqBand(34.5, 40.0, DepthDb);
        BiquadCoefficients biquad = PeakingBiquad.Compute(band, SampleRate);

        double[] denominatorDip = ApplySpectrum(
            clean, z => Complex.Abs(BiquadResponse(biquad, z)));
        double[] cabinResonance = ApplySpectrum(
            clean, z => BiquadResponse(biquad, z));

        double cleanPreArrival = TransferIrDiagnostics
            .MeasurePreArrivalDb(clean, SampleRate)!.Value;
        double dipPreArrival = TransferIrDiagnostics
            .MeasurePreArrivalDb(denominatorDip, SampleRate)!.Value;
        double cabinPreArrival = TransferIrDiagnostics
            .MeasurePreArrivalDb(cabinResonance, SampleRate)!.Value;

        // A synthetic proves the contrast only; the absolute line is calibrated on field records.
        Assert.True(
            dipPreArrival > cabinPreArrival + 10,
            $"a zero-phase denominator dip read {dipPreArrival:0.0} dB against " +
            $"{cabinPreArrival:0.0} dB for the same depth minimum-phase");
        Assert.True(
            dipPreArrival > TransferIrDiagnostics.SuspectPreArrivalDb,
            $"a zero-phase denominator dip must be reported; read {dipPreArrival:0.0} dB");
        Assert.True(
            cabinPreArrival < cleanPreArrival + 1.0,
            $"a minimum-phase cabin resonance must leave the reading where it was " +
            $"({cleanPreArrival:0.0} dB); it read {cabinPreArrival:0.0} dB");
        Assert.True(
            cabinPreArrival < TransferIrDiagnostics.SuspectPreArrivalDb,
            "a minimum-phase cabin resonance must not even be reported; " +
            $"read {cabinPreArrival:0.0} dB");

        double dipCompactness = TransferIrDiagnostics
            .MeasureCompactness(denominatorDip, SampleRate)!.Value.InsideOutsideDb;
        double cabinCompactness = TransferIrDiagnostics
            .MeasureCompactness(cabinResonance, SampleRate)!.Value.InsideOutsideDb;
        Assert.True(
            Math.Abs(dipCompactness - cabinCompactness) < 2.0,
            "compactness is supposed to be blind to the difference, but read " +
            $"{dipCompactness:0.0} dB against {cabinCompactness:0.0} dB");
    }

    // Without a guard band the gate kernel rings as long as the fault: verdict withheld.
    [Theory]
    [InlineData(0.50, true)]
    [InlineData(0.30, true)]
    [InlineData(0.10, false)]
    [InlineData(0.02, false)]
    public void CanJudgePreArrival_FollowsTheGuardBandWidth(
        double guardOctaves, bool judgeable)
    {
        double nyquist = SampleRate / 2.0;
        double lowFull = 20.0 / nyquist;
        var gate = new ExcitationBandGate(
            lowFull / Math.Pow(2.0, guardOctaves),
            lowFull,
            50.0 / nyquist,
            50.0 * 1.414 / nyquist);

        Assert.Equal(judgeable, TransferIrDiagnostics.CanJudgePreArrival(gate));
    }

    [Fact]
    public void CanJudgePreArrival_AcceptsAGateWithNoLowEdge()
    {
        Assert.True(TransferIrDiagnostics.CanJudgePreArrival(ExcitationBandGate.FullBand));
    }

    // Known blind spot: an obstructed direct path puts direct sound inside the window. Hence a report, not a refusal.
    [Theory]
    [Trait("Category", "Slow")]
    [InlineData(20.0, 25.0, 0.20)]
    [InlineData(20.0, 50.0, 0.20)]
    [InlineData(20.0, 200.0, 0.20)]
    [InlineData(20.0, 1000.0, 0.20)]
    [InlineData(20.0, 1000.0, 0.45)]
    [InlineData(20.0, 20000.0, 0.20)]
    public void MeasurePreArrivalDb_IsFooledByAnObstructedArrival(
        double lowFullHz, double highFullHz, double directAmplitude)
    {
        const int FrameLength = 262_144;
        var record = new double[FrameLength];
        AddDecayingArrival(record, atMs: 300.0, amplitude: directAmplitude, decaySeconds: 0.25);
        AddDecayingArrival(record, atMs: 500.0, amplitude: 1.0, decaySeconds: 0.25);
        double[] banded = BandLimit(record, ProductionGate(lowFullHz, highFullHz));

        double reading = TransferIrDiagnostics
            .MeasurePreArrivalDb(banded, SampleRate)!.Value;

        Assert.True(
            reading > TransferIrDiagnostics.SuspectPreArrivalDb,
            $"{lowFullHz}-{highFullHz} Hz at " +
            $"{100 * directAmplitude * directAmplitude:0} % read {reading:0.0} dB, " +
            "so this fixture no longer demonstrates the blind spot");
    }

    [Fact]
    public void MeasurePreArrivalDb_IsNullWhenThereIsNothingToMeasure()
    {
        Assert.Null(TransferIrDiagnostics.MeasurePreArrivalDb(
            new Complex[SampleRate / 2], SampleRate));
        Assert.Null(TransferIrDiagnostics.MeasurePreArrivalDb(
            new Complex[1 << 12], sampleRate: 0));
        var poisoned = new Complex[1 << 18];
        poisoned[100] = double.NaN;
        Assert.Null(TransferIrDiagnostics.MeasurePreArrivalDb(poisoned, SampleRate));
        Assert.Null(TransferIrDiagnostics.MeasurePreArrivalDb(
            new Complex[1 << 18], SampleRate));
    }

    [Fact]
    public void MeasurePreArrivalDb_ComplexTwinMatchesTheRealPath()
    {
        double[] impulseResponse = SyntheticCabinTransfer(262_144, delayMs: 12.0, decaySeconds: 0.25);
        var complexIr = Array.ConvertAll(impulseResponse, v => new Complex(v, 0.0));

        Assert.Equal(
            TransferIrDiagnostics.MeasurePreArrivalDb(impulseResponse, SampleRate),
            TransferIrDiagnostics.MeasurePreArrivalDb(complexIr, SampleRate));
    }

    // Half-octave guards on each side, as ExponentialSineSweep builds.
    private static ExcitationBandGate ProductionGate(double lowFullHz, double highFullHz)
    {
        if (lowFullHz <= 0)
        {
            return ExcitationBandGate.FullBand;
        }

        double nyquist = SampleRate / 2.0;
        double guard = Math.Sqrt(2.0);
        return new ExcitationBandGate(
            lowFullHz / guard / nyquist,
            lowFullHz / nyquist,
            highFullHz / nyquist,
            Math.Min(1.0, highFullHz * guard / nyquist));
    }

    // A bare kernel concentrates energy in a few samples and flatters every arrival ratio.
    private static double[] SyntheticCabinTransfer(
        int length,
        double delayMs,
        double decaySeconds,
        double highFullHz = 1000.0)
    {
        double[] reference = StationaryNoise(length, seed: 7);
        double[] tail = StationaryNoise(length, seed: 99);
        int arrival = (int)Math.Round(delayMs * SampleRate / 1000.0);
        int decay = (int)Math.Round(decaySeconds * SampleRate);

        var room = new double[length];
        room[arrival] = 1.0;
        for (int i = 1; i < decay; i++)
        {
            // -60 dB over the decay length: a car cabin at low frequency.
            room[(arrival + i) % length] +=
                0.5 * tail[i] * Math.Exp(-6.908 * i / decay);
        }

        return TransferFunction.ComputeAveragedRelativeIr(
            [new TransferFunctionFrame(reference, CircularConvolve(reference, room))],
            ProductionGate(20.0, highFullHz)).ImpulseResponse;
    }

    private static void AddDecayingArrival(
        double[] record,
        double atMs,
        double amplitude,
        double decaySeconds)
    {
        int at = (int)Math.Round(atMs * SampleRate / 1000.0);
        int decay = (int)Math.Round(decaySeconds * SampleRate);
        double[] tail = StationaryNoise(decay, seed: (uint)(at + 17));
        record[at] += amplitude;
        for (int i = 1; i < decay && at + i < record.Length; i++)
        {
            record[at + i] += amplitude * 1.2 * tail[i] * Math.Exp(-6.908 * i / decay);
        }
    }

    private static double[] BandLimit(double[] record, ExcitationBandGate gate)
    {
        double[] reference = StationaryNoise(record.Length, seed: 7);
        return TransferFunction.ComputeAveragedRelativeIr(
            [new TransferFunctionFrame(reference, CircularConvolve(reference, record))],
            gate).ImpulseResponse;
    }

    private static double[] CircularConvolve(double[] left, double[] right)
    {
        int length = left.Length;
        var a = new Complex[length];
        var b = new Complex[length];
        for (int i = 0; i < length; i++)
        {
            a[i] = left[i];
            b[i] = right[i];
        }

        Fourier.Forward(a, FourierOptions.Matlab);
        Fourier.Forward(b, FourierOptions.Matlab);
        for (int bin = 0; bin < length; bin++)
        {
            a[bin] *= b[bin] * length;
        }

        Fourier.Inverse(a, FourierOptions.Matlab);
        return Array.ConvertAll(a, value => value.Real);
    }

    private static Complex BiquadResponse(BiquadCoefficients biquad, Complex z)
    {
        // The stored a1/a2 are negated for miniDSP's additive feedback form.
        Complex inverse = 1.0 / z;
        Complex inverseSquared = inverse * inverse;
        return (biquad.B0 + biquad.B1 * inverse + biquad.B2 * inverseSquared) /
            (1.0 - biquad.A1 * inverse - biquad.A2 * inverseSquared);
    }

    private static double[] ApplySpectrum(
        double[] impulseResponse,
        Func<Complex, Complex> response)
    {
        int length = impulseResponse.Length;
        var spectrum = new Complex[length];
        for (int i = 0; i < length; i++)
        {
            spectrum[i] = impulseResponse[i];
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);
        for (int bin = 0; bin < length; bin++)
        {
            spectrum[bin] *= response(
                Complex.FromPolarCoordinates(1.0, Math.Tau * bin / length));
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);
        return Array.ConvertAll(spectrum, value => value.Real);
    }
}
