using System.Numerics;
using System.Text.Json;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// Regression tests on real measurements from <c>assets/test_data</c> (a 4-way
/// left channel: sub / woofer / mid / tweeter transfer IRs plus the virtual
/// crossover project that pairs them). These pin the field failure where the
/// first-arrival detector read the woofer's direct sound as pre-ringing of the
/// reverberant reflection cluster and shifted the B/C pair arrival ~10 ms late,
/// wrecking the Auto delay chain downstream.
/// </summary>
public sealed class RealMeasurementArrivalTests
{
    [Fact]
    public void WooferMidPair_BandLimitedArrivals_MatchTheFieldReference()
    {
        // The B/C junction of the user's system: woofer (BP 80-175) against mid
        // (BP 175-1300), arrivals read in the shared pair band 87.5-350 Hz on
        // the processed IRs — exactly what AutoAlignmentEngine stage 1 does.
        // Reference values come from the known-good Auto delay log; the broken
        // detector reported the woofer at ~21.2 ms instead.
        (int wooferRate, Complex[] wooferIr) = LoadTransferIr("l woof.json");
        (int midRate, Complex[] midIr) = LoadTransferIr("l mid.json");

        Complex[] woofer = VirtualCrossoverAnalysis.ApplyChain(
            wooferIr,
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 175, 24),
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 80, 24))),
            wooferRate);
        Complex[] mid = VirtualCrossoverAnalysis.ApplyChain(
            midIr,
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1300, 24),
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 175, 24))),
            midRate);

        double wooferArrivalMs = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
            woofer, wooferRate, 87.5, 350);
        double midArrivalMs = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
            mid, midRate, 87.5, 350);

        Assert.InRange(wooferArrivalMs, 11.2, 11.8);
        Assert.InRange(midArrivalMs, 7.7, 8.3);
    }

    [Fact]
    public void WooferBroadbandAnalysis_SeparatesSignalGradeFromArrivalProminence()
    {
        // The Time Alignment panel scenario that motivated the split: this
        // recording's SNR is excellent (~50 dB), while the first arrival sits
        // ~25 dB down the woofer's slow leading edge, 2 ms before the in-room
        // peak. One folded "quality" figure read this as Fair (24.9 dB); the
        // two numbers must tell the two stories separately.
        (int sampleRate, Complex[] ir) = LoadTransferIr("l woof.json");
        var samples = new double[ir.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = ir[i].Real;
        }

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            samples, sampleRate, new TimeAlignmentAnalysisOptions
            {
                WrapPeakPositions = true
            });

        Assert.InRange(result.SignalToNoiseDecibels, 48.0, 52.0);
        Assert.InRange(result.FirstArrivalProminenceDecibels, -27.0, -22.0);
        Assert.True(result.StrongestPeakIsSeparateArrival);
    }

    private static (int SampleRate, Complex[] Ir) LoadTransferIr(string fileName)
    {
        string path = Path.Combine(FindTestDataDirectory(), fileName);
        // assets/test_data is a submodule; a clone without it leaves the
        // directory empty, and the raw FileNotFoundException would send
        // whoever hits it in the wrong direction.
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"{fileName} is missing - initialize the measurement-data " +
                "submodule with 'git submodule update --init assets/test_data'.",
                path);
        }

        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        int sampleRate = root.GetProperty("sampleRate").GetInt32();
        JsonElement samples = root.GetProperty("transferRealSamples");
        var impulseResponse = new Complex[samples.GetArrayLength()];
        int index = 0;
        foreach (JsonElement sample in samples.EnumerateArray())
        {
            impulseResponse[index++] = new Complex(sample.GetDouble(), 0.0);
        }

        return (sampleRate, impulseResponse);
    }

    // The measurement files live in the repository, not in the test output, so
    // walk up from the test binary to the repo root. Failing loudly on a
    // missing directory is deliberate: a silently skipped regression test is
    // worse than a broken build.
    private static string FindTestDataDirectory()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, "assets", "test_data");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "assets/test_data was not found above the test binary.");
    }
}
