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
}
