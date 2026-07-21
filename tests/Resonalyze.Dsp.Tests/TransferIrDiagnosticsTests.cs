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
    private static double[] CrosstalkRecord(bool withClick)
    {
        var impulseResponse = new double[32_768];
        AddToneBurst(impulseResponse, startMs: 40.0, frequencyHz: 300, periods: 30, amplitude: 1.0);
        AddToneBurst(impulseResponse, startMs: 40.0, frequencyHz: 2000, periods: 40, amplitude: 0.05);
        if (withClick)
        {
            impulseResponse[30] = 0.02;
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
        double[] impulseResponse = CrosstalkRecord(withClick: true);

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
    public void DetectCrosstalkHead_CleanRecordYieldsNoGate()
    {
        double[] impulseResponse = CrosstalkRecord(withClick: false);

        Assert.Null(TransferIrDiagnostics.DetectCrosstalkHead(impulseResponse, SampleRate));
    }

    [Fact]
    public void DetectCrosstalkHead_AGenuineWeakEarlyArrivalIsNotGated()
    {
        // A weak IN-BAND early arrival (a real direct front 35 ms before the
        // strong reflection cluster) has no complement island — the
        // complement carries sound only where the record's genuine
        // out-of-band content is, which travels with the arrivals.
        double[] impulseResponse = CrosstalkRecord(withClick: false);
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

    [Fact]
    public void CleanCrosstalkHead_ZerosTheHeadAndKeepsTheRest()
    {
        double[] impulseResponse = CrosstalkRecord(withClick: true);
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
        Assert.Equal(0.02, impulseResponse[30]);
    }
}
