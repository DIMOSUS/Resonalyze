using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class MicrophoneCalibrationsSessionTests
{
    private static MicrophoneCalibrationDefinition File(string id, string name, string? path) =>
        new() { Id = id, Name = name, Kind = MicrophoneCalibrationKind.File, Path = path };

    private static MicrophoneCalibrationDefinition Angle(string id, string name, string? baseId = null) =>
        new() { Id = id, Name = name, Kind = MicrophoneCalibrationKind.Angle, AngleDegrees = 45, BaseId = baseId };

    private static MicrophoneCalibrationsSession Session(string? zero = "zero.cal") => new(
        [File("f1", "left", "left.cal"), File("f2", "right", "right.cal"), Angle("a1", "45 on left", "f1"), Angle("a2", "45")],
        zero);

    private static CalibrationFileProbe Probe(params (string Path, CalibrationFileState State)[] files) =>
        new(path => files.FirstOrDefault(file => file.Path == path) is { Path: not null } found
            ? found.State
            : CalibrationFileState.Missing);

    [Fact]
    public void TheSessionEditsACopy()
    {
        MicrophoneCalibrationDefinition original = File("f1", "left", "left.cal");
        var session = new MicrophoneCalibrationsSession([original], null);

        Assert.True(session.Rename("f1", "renamed"));

        Assert.Equal("left", original.Name);
        Assert.Equal("renamed", session.Definitions[0].Name);
    }

    [Fact]
    public void AnAddedFileIsNamedAfterItAndGetsAFreshId()
    {
        MicrophoneCalibrationsSession session = Session();

        MicrophoneCalibrationDefinition? added = session.AddFile(@"C:\cal\ECM8000 90.txt");

        Assert.NotNull(added);
        Assert.Same(added, session.Definitions[^1]);
        Assert.Equal("ECM8000 90", added.Name);
        Assert.Equal(MicrophoneCalibrationKind.File, added.Kind);
        Assert.Equal(@"C:\cal\ECM8000 90.txt", added.Path);
        Assert.True(MicrophoneCalibrationDefinition.IsGeneratedId(added.Id));
        Assert.DoesNotContain(session.Definitions.Take(4), definition => definition.Id == added.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void NoChosenFileAddsNothing(string? path)
    {
        MicrophoneCalibrationsSession session = Session();

        Assert.Null(session.AddFile(path));
        Assert.Equal(4, session.Definitions.Count);
    }

    [Fact]
    public void ANewEstimateIsNinetyDegreesAndJoinsOnlyWhenAdded()
    {
        MicrophoneCalibrationsSession session = Session();

        MicrophoneCalibrationDefinition estimate = session.NewAngle();

        Assert.Equal(MicrophoneCalibrationKind.Angle, estimate.Kind);
        Assert.Equal(90.0, estimate.AngleDegrees);
        Assert.Equal("90°", estimate.Name);
        Assert.Equal(4, session.Definitions.Count);
        session.Add(estimate);
        Assert.Same(estimate, session.Definitions[^1]);
    }

    [Fact]
    public void APathChangesOnlyToAnotherChosenFile()
    {
        MicrophoneCalibrationsSession session = Session();
        MicrophoneCalibrationDefinition left = session.Definitions[0];

        Assert.False(session.SetPath(left, null));
        Assert.False(session.SetPath(left, "left.cal"));
        Assert.True(session.SetPath(left, "  other.cal "));

        Assert.Equal("other.cal", left.Path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ANamelessRenameIsRefused(string? label)
    {
        MicrophoneCalibrationsSession session = Session();

        Assert.False(session.Rename("f1", label));
        Assert.Equal("left", session.Definitions[0].Name);
    }

    [Fact]
    public void ARenameIsTrimmedAndFindsItsEntryWhateverTheCase()
    {
        MicrophoneCalibrationsSession session = Session();

        Assert.False(session.Rename(null, "x"));
        Assert.False(session.Rename("gone", "x"));
        Assert.True(session.Rename("F2", "  Passenger  "));

        Assert.Equal("Passenger", session.Definitions[1].Name);
    }

    [Fact]
    public void RemovingABaseSendsItsEstimatesToTheZeroDegreeCalibration()
    {
        MicrophoneCalibrationsSession session = Session();

        session.Remove(session.Definitions[0]);

        Assert.Equal(["f2", "a1", "a2"], session.Definitions.Select(definition => definition.Id));
        Assert.Null(session.Find("a1")!.BaseId);
    }

    [Fact]
    public void OnlyOtherFilesCanBeABase()
    {
        MicrophoneCalibrationsSession session = Session();

        Assert.Equal(["f1", "f2"], session.BaseCandidates(session.Find("a1")!).Select(definition => definition.Id));
        Assert.Equal(["f2"], session.BaseCandidates(session.Find("F1")!).Select(definition => definition.Id));
    }

    [Fact]
    public void RowsDescribeEachEntry()
    {
        using var culture = new InvariantCultureScope();
        var session = new MicrophoneCalibrationsSession(
        [
            File("f1", "left", @"C:\cal\left.cal"),
            File("f2", "empty", null),
            Angle("a1", "on left", "f1"),
            Angle("a2", "on zero"),
            new MicrophoneCalibrationDefinition
            {
                Id = "a3", Name = "xref", Kind = MicrophoneCalibrationKind.Angle, AngleDegrees = 30,
                Reference = MicrophoneAngleReference.SonarworksXref20
            },
            new MicrophoneCalibrationDefinition
            {
                Id = "a4", Name = "removed", Kind = MicrophoneCalibrationKind.Angle, AngleDegrees = 22.5,
                FrontDiameterMm = 6.35, Grid = MicrophoneProtectionGrid.Removed, BaseId = "gone"
            },
        ], "zero.cal");

        IReadOnlyList<MicrophoneCalibrationRow> rows = MicrophoneCalibrationRows.Read(session, Probe());

        Assert.Equal(["f1", "f2", "a1", "a2", "a3", "a4"], rows.Select(row => row.Id));
        Assert.Equal(["File", "File", "Angle", "Angle", "Angle", "Angle"], rows.Select(row => row.Kind));
        Assert.Equal("left", rows[0].Name);
        Assert.Equal(
            [
                "left.cal", "no file", "45° from left · 12.7 mm, grid unknown", "45° from 0° · 12.7 mm, grid unknown",
                "30° from 0° · Sonarworks XREF 20", "22.5° from 0° · 6.35 mm, grid removed"
            ],
            rows.Select(row => row.Details));
    }

    [Fact]
    public void AFileEntryIsReadyOnlyWithAUsableFile()
    {
        var session = new MicrophoneCalibrationsSession(
            [File("f1", "a", "ok.cal"), File("f2", "b", "bad.cal"), File("f3", "c", "gone.cal"), File("f4", "d", " ")],
            null);

        IReadOnlyList<MicrophoneCalibrationRow> rows = MicrophoneCalibrationRows.Read(
            session,
            Probe(("ok.cal", CalibrationFileState.Usable), ("bad.cal", CalibrationFileState.Unusable)));

        Assert.Equal(["ready", "unusable file", "unusable file", "no file selected"], rows.Select(row => row.Status));
    }

    [Fact]
    public void AnEstimateIsJudgedByItsBase()
    {
        var session = new MicrophoneCalibrationsSession(
        [
            File("ok", "ok", "ok.cal"),
            File("bad", "bad", "bad.cal"),
            File("gone", "gone", "gone.cal"),
            Angle("on-ok", "1", "ok"),
            Angle("on-bad", "2", "bad"),
            Angle("on-gone", "3", "gone"),
            Angle("on-estimate", "4", "on-ok"),
            Angle("on-zero", "5"),
        ], "zero.cal");
        CalibrationFileProbe probe = Probe(
            ("ok.cal", CalibrationFileState.Usable),
            ("bad.cal", CalibrationFileState.Unusable),
            ("zero.cal", CalibrationFileState.Usable));

        IReadOnlyList<MicrophoneCalibrationRow> rows = MicrophoneCalibrationRows.Read(session, probe);

        Assert.Equal(
            ["estimated", "unusable base file", "base calibration missing", "base calibration missing", "estimated"],
            rows.Skip(3).Select(row => row.Status));
    }

    [Theory]
    [InlineData(null, "base calibration missing")]
    [InlineData("zero.cal", "unusable base file")]
    public void AnEstimateOnZeroDegreesNeedsThatFile(string? zero, string status)
    {
        var session = new MicrophoneCalibrationsSession([Angle("a", "a")], zero);

        MicrophoneCalibrationRow row = Assert.Single(
            MicrophoneCalibrationRows.Read(session, Probe(("zero.cal", CalibrationFileState.Unusable))));

        Assert.Equal(status, row.Status);
    }

    [Fact]
    public void TheProbeReadsEachFileOnce()
    {
        var reads = new List<string>();
        var probe = new CalibrationFileProbe(path =>
        {
            reads.Add(path);
            return CalibrationFileState.Usable;
        });
        MicrophoneCalibrationsSession session = Session();

        MicrophoneCalibrationRows.Read(session, probe);
        MicrophoneCalibrationRows.Read(session, probe);

        Assert.Equal(["left.cal", "right.cal", "zero.cal"], reads.Order());
    }

    [Fact]
    public void TheProbeReadsTheDisk()
    {
        using var folder = new TemporaryDirectory();
        System.IO.File.WriteAllText(folder.File("good.cal"), "20 0\n1000 0.5\n20000 1\n");
        System.IO.File.WriteAllText(folder.File("bad.cal"), "not a calibration\n");
        var probe = new CalibrationFileProbe();

        Assert.Equal(CalibrationFileState.Usable, probe.State(folder.File("good.cal")));
        Assert.Equal(CalibrationFileState.Unusable, probe.State(folder.File("bad.cal")));
        Assert.Equal(CalibrationFileState.Missing, probe.State(folder.File("gone.cal")));
    }
}
