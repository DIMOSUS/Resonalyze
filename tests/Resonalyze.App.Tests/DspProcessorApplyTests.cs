using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class DspProcessorApplyTests
{
    private readonly VirtualCrossoverSession session = new();

    public DspProcessorApplyTests()
    {
        session.Channels.Add(new VirtualCrossoverChannel("A") { Pair = session.Project.Pairs[0] });
        session.Project.DspProcessorPhaseControl = false;
        session.Project.DspProcessorFirFilters = false;
    }

    private DspProcessorSession Choice() => new(
        session.ProcessorProfile,
        session.Project.DspProcessorRateFollowsMeasurements,
        session.MeasuredSampleRateHz ?? 0,
        session.Project.DspProcessorPhaseControl,
        session.Project.DspProcessorFirFilters)
    {
        Notes = session.Project.AiNotes
    };

    [Fact]
    public void NotesAlone_AreASave_NotAProcessorChange()
    {
        DspProcessorSession choice = Choice();
        choice.Notes = "Doors, 6.5 inch mids.";

        Assert.True(DspProcessorApply.WriteNotes(session.Project, choice));
        Assert.False(DspProcessorApply.WriteProcessor(session, choice).Changed);
        Assert.Equal("Doors, 6.5 inch mids.", session.Project.AiNotes);
        Assert.False(DspProcessorApply.WriteNotes(session.Project, choice));
    }

    [Fact]
    public void ThePhaseAnswerAlone_IsAProcessorChange()
    {
        DspProcessorSession choice = Choice();
        choice.SetPhaseControl(true);

        Assert.True(DspProcessorApply.WriteProcessor(session, choice).Changed);
        Assert.True(session.Project.DspProcessorPhaseControl);
    }

    [Fact]
    public void TheIntentIsCompared_NotTheNumber()
    {
        DspProcessorSession choice = Choice();
        Assert.True(choice.FollowsMeasurements);

        choice.SelectSampleRate(48_000);
        DspProcessorWrite write = DspProcessorApply.WriteProcessor(session, choice);

        Assert.True(write.Changed);
        Assert.False(session.Project.DspProcessorRateFollowsMeasurements);
        Assert.Equal(48_000, session.ProcessorSampleRateHz);
        Assert.False(DspProcessorApply.WriteProcessor(session, Choice()).Changed);
    }

    [Fact]
    public void ConfirmingStoresTheAnswersShown_AndADeviceWithoutEitherDropsWhatItCannotRun()
    {
        VirtualCrossoverChannelSettings left = session.Channels[0].SideSettings(rightSide: false);
        DspProcessorSession helix = Choice();
        helix.SelectModel(DspProcessorCatalog.Preset("helix-dsp-ultra-s"));
        helix.SetFirFilters(true);
        Assert.Equal(new DspProcessorWrite(true, 0, 0), DspProcessorApply.WriteProcessor(session, helix));
        Assert.True(session.Project.DspProcessorPhaseControl);
        Assert.True(session.Project.DspProcessorFirFilters);
        left.PhaseRotationDegrees = 45;
        left.Fir = new FirFilter([1.0, 0.5], 48_000);

        DspProcessorSession panacea = Choice();
        panacea.SelectModel(DspProcessorCatalog.Preset("amp-panacea-v1-v2"));
        panacea.SetFirFilters(false);
        DspProcessorWrite write = DspProcessorApply.WriteProcessor(session, panacea);

        Assert.Equal(new DspProcessorWrite(true, 1, 1), write);
        Assert.Equal(0, left.PhaseRotationDegrees);
        Assert.Null(left.Fir);
        string notice = Assert.IsType<string>(DspProcessorApply.Notice(write));
        Assert.StartsWith("1 channel side had a phase rotation dialled in", notice);
        Assert.Contains("1 channel side had a FIR filter loaded", notice);
        Assert.Null(DspProcessorApply.Notice(new DspProcessorWrite(true, 0, 0)));
        Assert.StartsWith("2 channel sides had", DspProcessorApply.Notice(new DspProcessorWrite(true, 2, 0)));
    }
}
