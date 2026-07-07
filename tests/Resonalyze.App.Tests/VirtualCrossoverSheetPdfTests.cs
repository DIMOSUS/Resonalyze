using Resonalyze;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverSheetPdfTests
{
    [Fact]
    public void Export_WritesAValidPdfFile()
    {
        var project = new VirtualCrossoverProjectFile();
        project.Channels[0].SourceFilePath = "sub.json";
        project.Channels[0].DisplayName = "Sub";
        project.Channels[0].GainDb = -3.0;
        project.Channels[0].DelayMs = 1.5;
        project.Channels[0].CrossoverKind = CrossoverKind.LowPass;
        project.Channels[1].SourceFilePath = "top.json";
        project.Channels[1].DisplayName = "Top";
        project.Channels[1].CrossoverKind = CrossoverKind.HighPass;
        project.Channels[1].PeqBands.Add(new PeqBand(1000, 2.0, -3.0));

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
