using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Resonalyze.App.Tests;

/// <summary>
/// A colour the application paints comes from <c>UiThemePalette</c> and nowhere else, so a second theme answers
/// every one of them. This scans the source for colours built out of numbers or framework names.
/// </summary>
public sealed partial class UiPaletteCoverageTests
{
    /// <summary>File, and why its literals are not theme colours.</summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        ["source/Ui/UiThemePalette.cs"] = "the palette itself",
        ["source/Ui/Dialogs/ColorPickerDialog.cs"] = "swatches the user picks a CURVE colour from",
        ["source/Overlays/Overlay.cs"] = "black or white chosen by the luminance of the user's own slot colour",
        ["source/Shell/Form1.Designer.cs"] = "the overlay slot row is painted in the user's slot colour",
        ["source/Plotting/PlotModelStyle.cs"] =
            "OxyPlot's own defaults, compared against to tell a styled axis from an untouched one, " +
            "plus the waterfall magnitude ramp",
        ["source/Tools/Eq/TuningSheetPdf.cs"] = "printed on paper, which has no theme",
        ["source/Tools/Sheets/PdfSheet.cs"] = "printed on paper, which has no theme",
        ["source/Tools/Sheets/VirtualCrossoverSheetPdf.cs"] = "printed on paper, which has no theme",
    };

    [Fact]
    public void NoColourIsBuiltOutsideThePalette()
    {
        string root = RepositoryRoot();
        List<string> offenders = [];
        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(root, "source"), "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) ||
                relative.Contains("/bin/", StringComparison.Ordinal) ||
                Allowed.ContainsKey(relative))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match match in LiteralPattern().Matches(lines[i]))
                {
                    offenders.Add($"{relative}:{i + 1}: {match.Value}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Colours built outside UiThemePalette:\r\n" + string.Join("\r\n", offenders));
    }

    [Fact]
    public void EveryAllowedFile_StillExists()
    {
        string root = RepositoryRoot();
        foreach (string relative in Allowed.Keys)
        {
            Assert.True(
                File.Exists(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))),
                $"{relative} is on the colour-literal allow list but is gone; drop the entry.");
        }
    }

    private static string RepositoryRoot([CallerFilePath] string sourcePath = "")
    {
        DirectoryInfo? directory = new FileInfo(sourcePath).Directory;
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException($"No AGENTS.md above {sourcePath}.");
    }

    // A colour spelled out in numbers or in a framework name. An alpha wrapped around an expression
    // (FromArgb(140, color), FromAColor(90, ...)) is a shade of a colour that already came from the palette.
    [GeneratedRegex(
        @"(?<![.\w])Color\.FromArgb\(\s*\d+\s*,\s*\d+\s*,\s*\d+" +
        @"|OxyColor\.From(?:Rgb|Argb)\(\s*\d+\s*,\s*[\dx]" +
        @"|(?<![.\w])Color\.(?!FromArgb|FromRgb|FromName|Empty|Transparent)[A-Z]\w*" +
        @"|OxyColors\.(?!Transparent|Undefined|Automatic)[A-Z]\w*" +
        @"|SystemColors\.\w+")]
    private static partial Regex LiteralPattern();
}
