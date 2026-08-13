using System.Numerics;

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

    // The field shape (v3 midbass records): a band-limited main arrival, the
    // driver's weak out-of-band content travelling WITH it, and a small
    // broadband click parked at a fixed early sample by the interface.
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
        // A 40-period Hann burst is spectrally tight: the -20 dB band must
        // not swallow octaves of silence around it.
        Assert.True(
            Math.Log2(band.HighHz / band.LowHz) < 2.0,
            $"band {band.LowHz:0}-{band.HighHz:0} Hz is too wide for a narrowband burst");
    }

    [Fact]
    public void DetectDominantBand_BridgesANarrowCancellationNotch()
    {
        // An in-cabin interference notch splits the driver's working band
        // into two islands. The expansion must step across a deep-but-narrow
        // dip instead of keeping only the island around the loudest room
        // gain — the AutoBand default would otherwise throw away half the
        // driver's real band.
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
    public void DetectCrosstalkHead_FindsTheEarlyBroadbandClick()
    {
        // -21 dB re the record's max — the field click's level, inside the
        // full-band first-peak threshold, i.e. the click IS the full-band
        // First Arrival until removed.
        double[] impulseResponse = CrosstalkRecord(clickAmplitude: 0.09);

        CrosstalkHeadGate? gate = TransferIrDiagnostics.DetectCrosstalkHead(
            impulseResponse, SampleRate);

        Assert.NotNull(gate);
        // The gate covers the click (sample 30) and ends far before the
        // 40 ms front.
        Assert.True(gate.Value.GateEndSample > 30);
        Assert.True(gate.Value.GateEndSample < SampleRate * 30 / 1000);
        Assert.InRange(gate.Value.BurstTimeMs, 0.3, 1.1);
    }

    [Fact]
    public void DetectCrosstalkHead_AClickHotterThanTheDriversTailIsStillFound()
    {
        // The MORE dangerous artifact: the click outguns the driver's
        // out-of-band content, so it is the complement band's strongest
        // event. A detector keyed on "the strongest peak comes later" would
        // go blind exactly here.
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
        // No driver out-of-band tail at all: the click is the complement
        // band's only event, first and strongest at once.
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
        // A weak IN-BAND early arrival (a real direct front 35 ms before the
        // strong reflection cluster) has no complement island — the
        // complement carries sound only where the record's genuine
        // out-of-band content is, which travels with the arrivals.
        double[] impulseResponse = CrosstalkRecord(clickAmplitude: 0.0);
        AddToneBurst(impulseResponse, startMs: 5.0, frequencyHz: 300, periods: 10, amplitude: 0.1);

        Assert.Null(TransferIrDiagnostics.DetectCrosstalkHead(impulseResponse, SampleRate));
    }

    [Fact]
    public void DetectCrosstalkHead_AFullRangeRecordIsLeftAlone()
    {
        // A broadband record has no complement band to test — and the field
        // data showed its head click is inert for the engine anyway.
        var impulseResponse = new double[32_768];
        impulseResponse[30] = 0.02;
        impulseResponse[2_000] = 1.0;

        Assert.Null(TransferIrDiagnostics.DetectCrosstalkHead(impulseResponse, SampleRate));
    }

    [Theory]
    [InlineData(0.09)]
    [InlineData(0.15)]
    public void EstimateIrStart_LandsOnTheFrontDespiteTheHeadClick(
        double clickAmplitude)
    {
        // The field failure the estimator exists for: the broadband click at
        // sample 30 IS the full-band first arrival, but carries no energy
        // inside the driver's 300 Hz band — the in-band read must walk the
        // genuine front at 40 ms instead, at both field click levels.
        double[] impulseResponse = CrosstalkRecord(clickAmplitude);

        IrStartEstimate? start = TransferIrDiagnostics.EstimateIrStart(
            impulseResponse, SampleRate);

        Assert.NotNull(start);
        Assert.True(start.Value.DominantBandLimited);
        // On the front's rise (40 ms + a fraction of the 100 ms Hann rise),
        // far past the click at 0.6 ms.
        Assert.InRange(start.Value.StartMs, 40.0, 65.0);
        Assert.True(start.Value.EarlyMs <= start.Value.StartMs);
        Assert.True(start.Value.StartMs <= start.Value.LateMs);
    }

    [Fact]
    public void EstimateIrStart_ACabinModeDoesNotDragTheReadOffTheArrival()
    {
        // The field failure behind the arrival band's wider floor: a door
        // woofer at the listening position, where one cabin mode towers ~20 dB
        // over the driver's own working band. Read at the 15 dB CONTENT floor
        // the band collapses around that mode — and a band that narrow cannot
        // resolve anything shorter than its own ~8 ms, so the crossing walked
        // back from its smeared envelope read 15.9 ms for an arrival at 20 ms,
        // milliseconds before the record leaves its noise floor. The arrival
        // band has to reach past the mode into the content the driver has.
        var impulseResponse = new double[65_536];
        int arrival = SampleRate * 20 / 1000;
        double decay = 30.0 * SampleRate / 1000.0;
        for (int i = 0; arrival + i < impulseResponse.Length && i < 12 * decay; i++)
        {
            impulseResponse[arrival + i] += Math.Exp(-i / decay) *
                Math.Sin(Math.Tau * 123.0 * i / SampleRate);
        }
        // The driver's own front: wideband and brief, so it stands 20 dB below
        // the mode across the spectrum however tall its samples are.
        int front = (int)Math.Round(0.4 * SampleRate / 1000.0);
        for (int i = 0; i < front; i++)
        {
            impulseResponse[arrival + i] +=
                10.0 * (0.5 - 0.5 * Math.Cos(Math.Tau * i / front));
        }

        // The premise: at the content floor this record's band IS the mode.
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
        // A delta's envelope rises within the Hilbert skirt: the crossing
        // sits within a fraction of a millisecond of the front, and the
        // 10-vs-50 % spread stays tight.
        Assert.InRange(start.Value.StartMs, 19.5, 20.05);
        Assert.InRange(
            start.Value.LateMs - start.Value.EarlyMs, 0.0, 0.5);
    }

    [Fact]
    public void EstimateIrStart_AFrontRunningOffTheRecordHeadStaysAtZero()
    {
        // A narrowband arrival that begins before the record does: the
        // envelope is already well above the low crossings at sample 0, so
        // the backward walk runs out of record. The crossings must stop at
        // the record start rather than extrapolate into negative time.
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
        // Too short for any spectral analysis — refused BEFORE the FFT, even
        // at a valid sample rate.
        Assert.Null(TransferIrDiagnostics.EstimateIrStart(
            new double[] { 1.0 }, SampleRate));
        Assert.Null(TransferIrDiagnostics.EstimateIrStart(
            new double[] { 1.0 }, sampleRate: 0));
    }

    [Fact]
    public void EstimateIrStart_RefusesANoiseOnlyRecord()
    {
        // A noise envelope still has a strongest peak the first-arrival
        // search falls back to; only the SNR floor exposes that there is no
        // front to measure. Deterministic LCG noise (approx. Gaussian via a
        // sum of uniforms) so the test never flakes.
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

    // Deterministic LCG noise, the same generator the noise-only estimate
    // test uses — the compactness tests must never flake either.
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
        // A CAUSAL arrival — fast attack, exponential decay — over a
        // realistic noise floor, in a buffer many times the compactness
        // window: the good-measurement shape (field records read
        // 28.8-48.6 dB). Deliberately not the symmetric Hann burst of the
        // crosstalk tests: a real acoustic IR is front-loaded, and the
        // symmetric case (a zero-phase gate kernel) is covered by the
        // gated-transfer band tests below.
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
        // The envelope peaks at the first sine crest right after the onset.
        Assert.InRange(compactness.Value.PeakDelayMs, 5, 30);
    }

    // The field shape of a transfer built from an unusable reference (a
    // loopback that was playback bleed): stationary division noise across
    // the whole buffer with giant spikes at the circular wrap point — peak
    // at sample ~2 or wrapped into negative time, energy everywhere. The
    // real set read 11.2-15.7 dB.
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

    // No peak-position rule, by design: an electrical chain measurement
    // (mic input wired straight to a processor output) legitimately peaks
    // at zero delay and must pass on its clean shape alone.
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
        // Too short to carve a meaningful window, or nothing to measure.
        Assert.Null(TransferIrDiagnostics.MeasureCompactness(
            new double[100], SampleRate));
        Assert.Null(TransferIrDiagnostics.MeasureCompactness(
            new double[65_536], SampleRate));
        Assert.Null(TransferIrDiagnostics.MeasureCompactness(
            StationaryNoise(65_536, seed: 3), sampleRate: 0));

        // Non-finite content refuses too — the caller treats null as a
        // failed measurement, so a NaN capture can never pass by silently
        // poisoning every comparison.
        double[] poisonedByNaN = StationaryNoise(65_536, seed: 4);
        poisonedByNaN[123] = double.NaN;
        Assert.Null(TransferIrDiagnostics.MeasureCompactness(
            poisonedByNaN, SampleRate));
        double[] poisonedByInfinity = StationaryNoise(65_536, seed: 5);
        poisonedByInfinity[321] = double.PositiveInfinity;
        Assert.Null(TransferIrDiagnostics.MeasureCompactness(
            poisonedByInfinity, SampleRate));
    }

    // Band sweeps are a supported workflow, and the zero-phase excitation
    // gate turns even an ideal H(f)=1 into a symmetric band-limited kernel
    // whose pre-ringing lives in negative (wrapped) time — the compactness
    // window must accommodate it at every allowed band. Each case runs the
    // PRODUCTION estimator with the production gate shape (full band plus
    // fade guard bands): the ideal transfer must clear the floor with
    // margin, gated uncorrelated noise must stay far below it. A 10 ms
    // pre-window fails the 20-50 Hz case at 19.9 dB, which is what this
    // test pins.
    [Theory]
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
        // The main arrival region is untouched.
        int front = SampleRate * 40 / 1000;
        for (int i = front; i < front + 1000; i++)
        {
            Assert.Equal(complexIr[i], clean[i]);
        }
        // And the original was not mutated.
        Assert.Equal(0.09, impulseResponse[30]);
    }

    // The measure that tells a matched excitation from a mismatched one: how far
    // the arrival stands above its own two milliseconds.
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

    // A decay that runs for a quarter of a second still counts as an arrival:
    // the floor has to pass a reverberant room, not just an anechoic one. This is
    // the case that ruled out measuring the SHARE of energy near the arrival —
    // this record keeps only 12 % of it there.
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

    // What a sweep deconvolved against the WRONG sweep looks like: the energy is
    // spread over tens of milliseconds instead of landing.
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
}
