using Resonalyze.Dsp;
using static Resonalyze.App.Tests.AuditionFixtures;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverAuditionCalibrationTests
{
    [Fact]
    public void OnlyOwn_HasANote()
    {
        Assert.Null(VirtualCrossoverAuditionCalibration.Note(Session(Context(), Good.Id, Good.Name)));
        Assert.Null(VirtualCrossoverAuditionCalibration.Note(Session(Context(), null, "Off")));

        AuditionCalibrationNote? note = VirtualCrossoverAuditionCalibration.Note(Session(Context(), Own, "Own"));

        Assert.NotNull(note);
        Assert.False(note.Value.Refused);
        Assert.Contains("'own mic'", note.Value.Text);
    }

    [Fact]
    public void ChannelsReadThroughDifferentCurves_RefuseOwn()
    {
        AuditionCalibrationNote? note = VirtualCrossoverAuditionCalibration.Note(
            Session(Context(own: Conflict()), Own, "Own"));

        Assert.True(note!.Value.Refused);
        Assert.StartsWith("REFUSED: the channels were not measured", note.Value.Text);
    }

    [Fact]
    public void MeasurementsThatRecordedNone_SaySo()
    {
        VirtualCrossoverAuditionSession session =
            Session(Context(own: new VirtualCrossoverAuditionOwnCalibration(null, null, null)), Own, "Own");

        Assert.Contains("recorded no calibration", VirtualCrossoverAuditionCalibration.Note(session)!.Value.Text);
        (CalibrationFile? curve, string label) = VirtualCrossoverAuditionCalibration.ForRender(session);
        Assert.Null(curve);
        Assert.Equal("own (as measured): the measurements recorded none", label);
    }

    [Fact]
    public void TheRender_CarriesTheChosenCurve_UnderTheNameItsListGives()
    {
        (CalibrationFile? own, string ownLabel) =
            VirtualCrossoverAuditionCalibration.ForRender(Session(Context(), Own, "Own"));
        Assert.True(CalibrationFile.SameCurve(Curve(-2.0), own));
        Assert.Equal("own (as measured): own mic", ownLabel);

        (CalibrationFile? good, string goodLabel) =
            VirtualCrossoverAuditionCalibration.ForRender(Session(Context(), Good.Id, "good mic (shown)"));
        Assert.True(CalibrationFile.SameCurve(Curve(3.0), good));
        Assert.Equal("good mic (shown)", goodLabel);

        (CalibrationFile? off, string offLabel) =
            VirtualCrossoverAuditionCalibration.ForRender(Session(Context(), null, "Off"));
        Assert.Null(off);
        Assert.Equal("off", offLabel);
    }

    [Theory]
    [InlineData("cal-broken")]
    [InlineData("cal-missing")]
    public void AFileThatCannotBeRead_RendersWithout_AndSaysSo(string id)
    {
        (CalibrationFile? curve, string label) =
            VirtualCrossoverAuditionCalibration.ForRender(Session(Context(), id, "whatever"));

        Assert.Null(curve);
        Assert.Equal("off (the calibration file could not be read)", label);
    }

    private const string Own = VirtualCrossoverCalibrationSelection.OwnId;

    private static VirtualCrossoverAuditionSession Session(VirtualCrossoverAuditionContext context, string? id, string name)
    {
        VirtualCrossoverAuditionSession session = VirtualCrossoverAuditionSession.Restore(context, new());
        session.SelectCalibration(id, name);
        return session;
    }
}
