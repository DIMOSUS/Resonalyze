using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

internal static class AuditionFixtures
{
    public const int Rate = 48_000;

    public static readonly MicrophoneCalibrationEntry Good = new("cal-good", "good mic", true, "good.txt");

    public static readonly MicrophoneCalibrationEntry Broken = new("cal-broken", "broken mic", true, "broken.txt");

    public static CalibrationFile Curve(double db) =>
        CalibrationFile.FromPoints([new CalibrationPoint(20, db), new CalibrationPoint(20_000, db)]);

    public static VirtualCrossoverAuditionContext Context(
        bool averages = true,
        VirtualCrossoverAuditionOwnCalibration? own = null,
        string? borrowedSide = null) =>
        new(
            Response(1.0),
            Response(0.5),
            Rate,
            2,
            2,
            borrowedSide,
            id => id switch
            {
                "cal-good" => Curve(3.0),
                "cal-broken" => CalibrationFile.Parse("not a calibration"),
                _ => null
            },
            VirtualCrossoverCalibrationSelection.EntriesWith([Good, Broken], null),
            null,
            own ?? new VirtualCrossoverAuditionOwnCalibration(Curve(-2.0), "own mic", null),
            averages
                ? new VirtualCrossoverAuditionSpatialAverage(
                    Response(0.8), Response(0.4), ["Set offset +1.0 dB, channels disagree by 0.4 dB."])
                : null,
            averages ? null : "no captures.");

    public static VirtualCrossoverAuditionOwnCalibration Conflict() =>
        new(null, null, "the channels were not measured through one calibration (a: A; b: B)");

    // A decaying ring: enough of a tail for the trim, short enough to render in milliseconds.
    private static Complex[] Response(double gain)
    {
        var response = new Complex[4_096];
        for (int i = 0; i < 1_500; i++)
        {
            response[i] = gain * Math.Exp(-i / 150.0) * Math.Cos(0.07 * i);
        }

        return response;
    }

    public static string Track(string directory, string name, int rate, int channels, double seconds)
    {
        string path = Path.Combine(directory, name);
        int frames = (int)(rate * seconds);
        var content = new float[channels][];
        for (int channel = 0; channel < channels; channel++)
        {
            content[channel] = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                content[channel][i] = (float)(0.3 * Math.Sin(2 * Math.PI * (220 + 110 * channel) * i / rate));
            }
        }

        AudioFileCodec.WriteWav(path, new AudioFileContent(content, rate));
        return path;
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "resonalyze-audition-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
