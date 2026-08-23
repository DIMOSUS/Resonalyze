using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The calibration cache, the once-per-session problem reporting and the path
/// fallbacks moved off Form1 into <see cref="MicrophoneCalibrationService"/>;
/// these pin the behavior the shell relies on: legacy calibration.txt lookup,
/// resolution of the additional entries (files and angular estimates), and
/// warnings that never repeat within a session even across a cache
/// invalidation.
/// </summary>
public sealed class MicrophoneCalibrationServiceTests : IDisposable
{
    private const string ValidCalibration = "20 2.5\n1000 2.5\n20000 2.5\n";

    private readonly string tempDirectory;
    private readonly List<(string Path, string? Reason)> reportedProblems = new();
    private readonly List<MicrophoneCalibrationDefinition> definitions = new();
    private string? zeroDegreePath;

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

        Assert.Null(service.Get(MicrophoneCalibrationIds.ZeroDegrees));
        Assert.Null(service.Get(null));
        Assert.False(service.GetEntries()[0].Available);
        Assert.Empty(reportedProblems);
    }

    [Fact]
    public void Get_CachesTheLoadedFile()
    {
        zeroDegreePath = WriteFile("zero.txt", ValidCalibration);
        MicrophoneCalibrationService service = CreateService();

        CalibrationFile? first = service.Get(MicrophoneCalibrationIds.ZeroDegrees);

        Assert.NotNull(first);
        Assert.True(first!.HasData);
        Assert.Same(first, service.Get(MicrophoneCalibrationIds.ZeroDegrees));
        Assert.Empty(reportedProblems);
    }

    [Fact]
    public void Get_ReportsAnUnparsableFileOncePerSession()
    {
        zeroDegreePath = WriteFile("broken.txt", "not a calibration\n");
        MicrophoneCalibrationService service = CreateService();

        CalibrationFile? calibration = service.Get(MicrophoneCalibrationIds.ZeroDegrees);
        service.Get(MicrophoneCalibrationIds.ZeroDegrees);

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

        Assert.Null(service.Get(MicrophoneCalibrationIds.ZeroDegrees));
        Assert.Null(service.Get(MicrophoneCalibrationIds.ZeroDegrees));

        (string path, string? reason) = Assert.Single(reportedProblems);
        Assert.Equal(zeroDegreePath, path);
        Assert.Contains("not found", reason);
    }

    [Fact]
    public void Get_ReportsAnUnknownCalibrationIdOncePerSession()
    {
        MicrophoneCalibrationService service = CreateService();

        Assert.Null(service.Get("deleted-entry"));
        Assert.Null(service.Get("deleted-entry"));

        (_, string? reason) = Assert.Single(reportedProblems);
        Assert.Contains("no longer configured", reason);
    }

    [Fact]
    public void InvalidateCache_ReloadsFilesButKeepsSessionProblemReports()
    {
        zeroDegreePath = WriteFile("broken.txt", "not a calibration\n");
        MicrophoneCalibrationService service = CreateService();
        CalibrationFile? first = service.Get(MicrophoneCalibrationIds.ZeroDegrees);

        service.InvalidateCache();
        CalibrationFile? second = service.Get(MicrophoneCalibrationIds.ZeroDegrees);

        Assert.NotSame(first, second);
        Assert.Single(reportedProblems);
    }

    [Fact]
    public void InvalidateCache_PicksUpAnEditedDefinition()
    {
        zeroDegreePath = WriteFile("zero.txt", ValidCalibration);
        definitions.Add(NewAngle("angle", 90));
        MicrophoneCalibrationService service = CreateService();
        double before = service.Get("angle")!.GetDecibelCorrection(10_000);

        definitions[0].AngleDegrees = 30;
        service.InvalidateCache();
        double after = service.Get("angle")!.GetDecibelCorrection(10_000);

        Assert.True(after > before);
    }

    [Fact]
    public void Get_FallsBackToTheLegacyZeroDegreeFile()
    {
        WriteFile("calibration.txt", ValidCalibration);
        MicrophoneCalibrationService service = CreateService();

        CalibrationFile? calibration = service.Get(MicrophoneCalibrationIds.ZeroDegrees);

        Assert.NotNull(calibration);
        Assert.True(calibration!.HasData);
        Assert.True(service.GetEntries()[0].Available);
    }

    [Fact]
    public void Get_AngleEntry_AddsTheEstimateToItsBaseCalibration()
    {
        zeroDegreePath = WriteFile("zero.txt", ValidCalibration);
        definitions.Add(NewAngle("angle", 90));
        MicrophoneCalibrationService service = CreateService();

        CalibrationFile? angled = service.Get("angle");

        Assert.NotNull(angled);
        double expected = 2.5 + MicrophoneAngleModel
            .Estimate(new MicrophoneAngleRequest(90, 12.7))
            .DeltaDb(10_000);
        Assert.Equal(expected, angled!.GetDecibelCorrection(10_000), precision: 9);
        Assert.Same(angled, service.Get("angle"));
    }

    [Fact]
    public void Get_AngleEntry_OnAxisReturnsTheBaseCalibrationItself()
    {
        zeroDegreePath = WriteFile("zero.txt", ValidCalibration);
        definitions.Add(NewAngle("on-axis", 0));
        MicrophoneCalibrationService service = CreateService();

        Assert.Same(
            service.Get(MicrophoneCalibrationIds.ZeroDegrees),
            service.Get("on-axis"));
    }

    [Fact]
    public void Get_AngleEntry_DerivesFromTheNamedFileRatherThanTheZeroDegreeSlot()
    {
        zeroDegreePath = WriteFile("zero.txt", ValidCalibration);
        definitions.Add(new MicrophoneCalibrationDefinition
        {
            Id = "second",
            Name = "Second microphone",
            Kind = MicrophoneCalibrationKind.File,
            Path = WriteFile("second.txt", "20 1.0\n1000 1.0\n20000 1.0\n")
        });
        MicrophoneCalibrationDefinition angle = NewAngle("angle", 90);
        angle.BaseId = "second";
        definitions.Add(angle);
        MicrophoneCalibrationService service = CreateService();

        double expected = 1.0 + MicrophoneAngleModel
            .Estimate(new MicrophoneAngleRequest(90, 12.7))
            .DeltaDb(10_000);
        Assert.Equal(
            expected,
            service.Get("angle")!.GetDecibelCorrection(10_000),
            precision: 9);
    }

    [Fact]
    public void GetEntries_MarksAnUnparsableFileUnavailable()
    {
        // Availability is about yielding a correction, not about the file being
        // on disk: an unparsable one corrects by 0 dB everywhere, which a
        // selector marked "ready" would hide.
        zeroDegreePath = WriteFile("broken.txt", "not a calibration\n");
        definitions.Add(new MicrophoneCalibrationDefinition
        {
            Id = "second",
            Name = "Second microphone",
            Kind = MicrophoneCalibrationKind.File,
            Path = WriteFile("empty.txt", string.Empty)
        });
        definitions.Add(NewAngle("angle", 90));
        MicrophoneCalibrationService service = CreateService();

        Assert.All(service.GetEntries(), entry => Assert.False(entry.Available));
        // Listing entries reads the files, but the warning belongs to correcting
        // a measurement with one, so it must not have been raised yet.
        Assert.Empty(reportedProblems);
        Assert.False(service.Get(MicrophoneCalibrationIds.ZeroDegrees)!.HasData);
        Assert.Single(reportedProblems);
    }

    [Fact]
    public void GetEntries_MarksAnAngleWithoutABaseUnavailable()
    {
        definitions.Add(NewAngle("angle", 45));
        MicrophoneCalibrationService service = CreateService();

        MicrophoneCalibrationEntry entry = service.GetEntries()[^1];

        Assert.Equal("angle", entry.Id);
        Assert.False(entry.Available);
        Assert.Null(service.Get("angle"));
    }

    [Fact]
    public void GetEntries_ListsTheZeroDegreeSlotFirstThenTheConfiguredOrder()
    {
        zeroDegreePath = WriteFile("zero.txt", ValidCalibration);
        definitions.Add(NewAngle("first", 30));
        definitions.Add(NewAngle("second", 60));
        MicrophoneCalibrationService service = CreateService();

        Assert.Equal(
            [MicrophoneCalibrationIds.ZeroDegrees, "first", "second"],
            service.GetEntries().Select(entry => entry.Id));
    }

    [Fact]
    public void GetEntries_NamesTheFileOfEachFileBackedEntry()
    {
        // A Virtual DSP session that carries a curve also says which file it was, so
        // the entries report theirs — by name only, the folder means nothing elsewhere.
        zeroDegreePath = WriteFile("ECM8000_0deg.txt", ValidCalibration);
        definitions.Add(new MicrophoneCalibrationDefinition
        {
            Id = "ninety",
            Name = "90°",
            Kind = MicrophoneCalibrationKind.File,
            Path = WriteFile("ECM8000_90deg.txt", ValidCalibration)
        });
        definitions.Add(NewAngle("angle", 45));
        MicrophoneCalibrationService service = CreateService();

        Assert.Equal(
            ["ECM8000_0deg.txt", "ECM8000_90deg.txt", null],
            service.GetEntries().Select(entry => entry.FileName));
    }

    private static MicrophoneCalibrationDefinition NewAngle(string id, double angleDegrees) =>
        new()
        {
            Id = id,
            Name = id,
            Kind = MicrophoneCalibrationKind.Angle,
            AngleDegrees = angleDegrees,
            FrontDiameterMm = 12.7
        };

    private MicrophoneCalibrationService CreateService() =>
        new(
            () => zeroDegreePath,
            () => definitions,
            (path, reason) => reportedProblems.Add((path, reason)),
            legacyZeroDegreeDirectory: tempDirectory);

    private string WriteFile(string name, string content)
    {
        string path = Path.Combine(tempDirectory, name);
        File.WriteAllText(path, content);
        return path;
    }
}
