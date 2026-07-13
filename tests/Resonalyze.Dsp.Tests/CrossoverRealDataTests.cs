using System.Text.Json;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The crossover wizard on the real measurements in <c>assets/test_data</c>:
/// the user's left 4-way (sub / midbass / midrange / tweeter). Pins the
/// localization contract the synthetic tests cannot — on a real broad-band
/// midbass the flatness search alone drags the midbass/midrange handover up to
/// the top of its class range (~500 Hz), where a poorly-imaging midbass carries
/// the localizable low-mids. The localization bias must keep that handover below
/// the ~300 Hz threshold.
/// </summary>
public sealed class CrossoverRealDataTests
{
    [Fact]
    public void ProposeRanked_RealCabin_KeepsTheMidbassHandoverBelowLocalization()
    {
        AutoSetupSource sub = LoadSource("sub woof closed window.json", DriverType.Subwoofer);
        AutoSetupSource woof = LoadSource("l woof.json", DriverType.Midbass);
        AutoSetupSource mid = LoadSource("l mid.json", DriverType.Midrange);
        AutoSetupSource twr = LoadSource("l twr.json", DriverType.Tweeter);
        var sources = new List<AutoSetupSource> { sub, woof, mid, twr };

        var options = CrossoverAutoSetupOptions.Default(44_100);
        IReadOnlyList<RankedCrossoverProposal> ranked =
            CrossoverAutoSetup.ProposeRanked(sources, options, null, 8);
        IReadOnlyList<CrossoverProposal> best = ranked[0].Proposals;

        // The midbass/midrange handover is the midbass channel's low-pass (and,
        // by construction, the midrange's high-pass). Without the localization
        // bias the flatness search pins it near 500 Hz (the top of the
        // Midbass->Midrange class band); it must instead sit in the
        // localizable-safe region a hair below the ~300 Hz threshold.
        double? midbassLowPass = best[1].LowPassEdge?.FrequencyHz;
        double? midHighPass = best[2].HighPassEdge?.FrequencyHz;
        Assert.NotNull(midbassLowPass);
        Assert.NotNull(midHighPass);
        Assert.Equal(midbassLowPass!.Value, midHighPass!.Value, 3);
        Assert.InRange(midbassLowPass.Value, 150, 300);

        // The other handovers stay sane: sub below its localizability limit, the
        // tweeter above its resonance floor.
        Assert.InRange(best[0].LowPassEdge!.Value.FrequencyHz, 60, 100);
        Assert.True(best[3].HighPassEdge!.Value.FrequencyHz >= 1_200);
    }

    // The app's own saved magnitude curve (previewFrequencyResponse) with the
    // per-bin transfer coherence resampled onto that grid — the same inputs the
    // wizard reads, minus the harmonic-distortion curve (which only tightens the
    // bounds further, never loosens them).
    private static AutoSetupSource LoadSource(string fileName, DriverType type)
    {
        string path = Path.Combine(FindTestDataDirectory(), fileName);
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
        JsonElement preview = root.GetProperty("previewFrequencyResponse");
        JsonElement frequencies = preview.GetProperty("frequencies");
        JsonElement magnitudes = preview.GetProperty("magnitudesDb");
        int count = frequencies.GetArrayLength();

        var magnitude = new List<SignalPoint>(count);
        var gridHz = new double[count];
        for (int i = 0; i < count; i++)
        {
            double hz = frequencies[i].GetDouble();
            gridHz[i] = hz;
            magnitude.Add(new SignalPoint(hz, magnitudes[i].GetDouble()));
        }

        JsonElement coherenceBins = root.GetProperty("transferCoherence");
        int bins = coherenceBins.GetArrayLength();
        double binHz = (double)sampleRate / ((bins - 1) * 2);
        var coherence = new List<double>(count);
        for (int i = 0; i < count; i++)
        {
            int index = Math.Clamp((int)Math.Round(gridHz[i] / binHz), 0, bins - 1);
            coherence.Add(coherenceBins[index].GetDouble());
        }

        return new AutoSetupSource(magnitude, type, coherence);
    }

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
