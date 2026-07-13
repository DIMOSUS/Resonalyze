using Resonalyze;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverSheetPdfTests
{
    [Fact]
    public void Export_WritesAValidPdfFile()
    {
        var project = new VirtualCrossoverProjectFile();
        project.Pairs[0].Mono = true;
        project.Pairs[0].Left.SourceFilePath = "sub.json";
        project.Pairs[0].Left.DisplayName = "Sub";
        project.Pairs[0].Left.GainDb = -3.0;
        project.Pairs[0].Left.DelayMs = 1.5;
        project.Pairs[0].Left.CrossoverKind = CrossoverKind.LowPass;
        project.Pairs[1].Left.SourceFilePath = "top.json";
        project.Pairs[1].Left.DisplayName = "Top";
        project.Pairs[1].Left.CrossoverKind = CrossoverKind.HighPass;
        project.Pairs[1].Left.PeqBands.Add(new PeqBand(1000, 2.0, -3.0));
        project.Pairs[1].Right.SourceFilePath = "top r.json";
        project.Pairs[1].Right.DisplayName = "Top R";
        project.Pairs[1].Right.CrossoverKind = CrossoverKind.HighPass;
        // A stereo pair with ONE loaded side falls back to the single-channel
        // layout instead of an L/R table with an empty column.
        project.Pairs[2].Right.SourceFilePath = "half.json";
        project.Pairs[2].Right.DisplayName = "Half";

        string path = Path.Combine(Path.GetTempPath(), $"vdsp_{Guid.NewGuid():N}.pdf");
        try
        {
            VirtualCrossoverSheetPdf.Export(path, project, "metric: 0.42", 48_000);

            byte[] bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 0);
            // Every PDF starts with the "%PDF" signature.
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Export_HandlesProjectWithoutSources()
    {
        var project = new VirtualCrossoverProjectFile();
        string path = Path.Combine(Path.GetTempPath(), $"vdsp_{Guid.NewGuid():N}.pdf");
        try
        {
            VirtualCrossoverSheetPdf.Export(path, project, metricLine: null, 44_100);
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
