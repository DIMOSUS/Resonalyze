using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The calibration cache, the once-per-session problem reporting and the path
/// fallbacks moved off Form1 into <see cref="MicrophoneCalibrationService"/>;
/// these pin the behavior the shell relied on: legacy calibration.txt lookup,
/// the 90°-from-0° approximation, and warnings that never repeat within a
/// session even across a cache invalidation.
/// </summary>
public sealed class MicrophoneCalibrationServiceTests : IDisposable
{
    private const string ValidCalibration = "20 2.5\n1000 2.5\n20000 2.5\n";

    private readonly string tempDirectory;
    private readonly List<(string Path, string? Reason)> reportedProblems = new();
    private string? zeroDegreePath;
    private string? ninetyDegreePath;

    public MicrophoneCalibrationServiceTests()
    {
        tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "resonalyze-calibration-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Get_ReturnsNullAndStaysSilentWhenNothingIsConfigured()
    {
        MicrophoneCalibrationService service = CreateService();

        Assert.Null(service.Get(MicrophoneCalibrationMode.Degrees0));
        Assert.Null(service.Get(MicrophoneCalibrationMode.Degrees90));
        Assert.False(service.Has(MicrophoneCalibrationMode.Degrees0));
        Assert.False(service.Has(MicrophoneCalibrationMode.Degrees90));
        Assert.Empty(reportedProblems);
    }

    [Fact]
    public void Get_CachesTheLoadedFile()
    {
        zeroDegreePath = WriteFile("zero.txt", ValidCalibration);
        MicrophoneCalibrationService service = CreateService();

        CalibrationFile? first = service.Get(MicrophoneCalibrationMode.Degrees0);

        Assert.NotNull(first);
        Assert.True(first!.HasData);
        Assert.Same(first, service.Get(MicrophoneCalibrationMode.Degrees0));
        Assert.Empty(reportedProblems);
    }

    [Fact]
    public void Get_ReportsAnUnparsableFileOncePerSession()
    {
        zeroDegreePath = WriteFile("broken.txt", "not a calibration\n");
        MicrophoneCalibrationService service = CreateService();

        CalibrationFile? calibration = service.Get(MicrophoneCalibrationMode.Degrees0);
        service.Get(MicrophoneCalibrationMode.Degrees0);

        Assert.NotNull(calibration);
        Assert.False(calibration!.HasData);
        (string path, string? reason) = Assert.Single(reportedProblems);
        Assert.Equal(zeroDegreePath, path);
        Assert.Equal(calibration.LoadError, reason);
    }

    [Fact]
    public void Get_ReportsAConfiguredButMissingFileOncePerSession()
    {
        zeroDegreePath = Path.Combine(tempDirectory, "deleted.txt");
        MicrophoneCalibrationService service = CreateService();

        Assert.Null(service.Get(MicrophoneCalibrationMode.Degrees0));
        Assert.Null(service.Get(MicrophoneCalibrationMode.Degrees0));

        (string path, string? reason) = Assert.Single(reportedProblems);
        Assert.Equal(zeroDegreePath, path);
        Assert.Contains("not found", reason);
    }

    [Fact]
    public void InvalidateCache_ReloadsFilesButKeepsSessionProblemReports()
    {
        zeroDegreePath = WriteFile("broken.txt", "not a calibration\n");
        MicrophoneCalibrationService service = CreateService();
        CalibrationFile? first = service.Get(MicrophoneCalibrationMode.Degrees0);

        service.InvalidateCache();
        CalibrationFile? second = service.Get(MicrophoneCalibrationMode.Degrees0);

        Assert.NotSame(first, second);
        Assert.Single(reportedProblems);
    }

    [Fact]
    public void Get_FallsBackToTheLegacyZeroDegreeFile()
    {
        WriteFile("calibration.txt", ValidCalibration);
        MicrophoneCalibrationService service = CreateService();

        CalibrationFile? calibration = service.Get(MicrophoneCalibrationMode.Degrees0);

        Assert.NotNull(calibration);
        Assert.True(calibration!.HasData);
        Assert.True(service.Has(MicrophoneCalibrationMode.Degrees0));
        Assert.True(service.Has(MicrophoneCalibrationMode.Degrees90));
    }

    [Fact]
    public void Get_NinetyDegrees_DerivesTheApproximationFromTheZeroDegreeFile()
    {
        zeroDegreePath = WriteFile("zero.txt", ValidCalibration);
        MicrophoneCalibrationService service = CreateService();

        CalibrationFile? ninety = service.Get(MicrophoneCalibrationMode.Degrees90);

        Assert.NotNull(ninety);
        Assert.True(ninety!.HasData);
        Assert.True(service.Has(MicrophoneCalibrationMode.Degrees90));
        Assert.NotSame(service.Get(MicrophoneCalibrationMode.Degrees0), ninety);
        Assert.Same(ninety, service.Get(MicrophoneCalibrationMode.Degrees90));
        Assert.Equal(
            2.5 + CalibrationFile.Delta90Minus0(10_000),
            ninety.GetDecibelCorrection(10_000),
            precision: 6);
    }

    [Fact]
    public void Get_NinetyDegrees_PrefersTheConfiguredNinetyDegreeFile()
    {
        zeroDegreePath = WriteFile("zero.txt", ValidCalibration);
        ninetyDegreePath = WriteFile("ninety.txt", "20 1.0\n1000 1.0\n20000 1.0\n");
        MicrophoneCalibrationService service = CreateService();

        CalibrationFile? ninety = service.Get(MicrophoneCalibrationMode.Degrees90);

        Assert.NotNull(ninety);
        Assert.Equal(1.0, ninety!.GetDecibelCorrection(1000), precision: 6);
    }

    private MicrophoneCalibrationService CreateService() =>
        new(
            mode => mode switch
            {
                MicrophoneCalibrationMode.Degrees0 => zeroDegreePath,
                MicrophoneCalibrationMode.Degrees90 => ninetyDegreePath,
                _ => null
            },
            (path, reason) => reportedProblems.Add((path, reason)),
            legacyZeroDegreeDirectory: tempDirectory);

    private string WriteFile(string name, string content)
    {
        string path = Path.Combine(tempDirectory, name);
        File.WriteAllText(path, content);
        return path;
    }
}
