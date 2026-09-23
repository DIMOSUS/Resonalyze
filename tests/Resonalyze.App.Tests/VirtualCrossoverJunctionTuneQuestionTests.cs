using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverJunctionTuneQuestionTests
{
    private static readonly CrossoverFamilyChoice Butterworth =
        CrossoverFamilyChoice.Offered.First(choice => choice.Value == CrossoverFilterFamily.Butterworth);

    private static readonly CrossoverFamilyChoice LinkwitzRiley =
        CrossoverFamilyChoice.Offered.First(choice => choice.Value == CrossoverFilterFamily.LinkwitzRiley);

    private static readonly JunctionTuneOutcome Found =
        new([JunctionTuneLine.Of("Junction tune A/B: applied.")], CanApply: true, "A better crossover was found.", false);

    private static JunctionTuneDefaults FirstAcoustic(int index) => index == 0
        ? new JunctionTuneDefaults(80, 200, [CrossoverFilterFamily.LinkwitzRiley],
            new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24))
        : new JunctionTuneDefaults(500, 2_000, [CrossoverFilterFamily.Butterworth], null);

    private static VirtualCrossoverJunctionTuneQuestion Opened(
        Func<int, JunctionTuneDefaults> defaults,
        VirtualCrossoverJunctionTuneSettings? remembered = null,
        params string[] junctions)
    {
        var question = new VirtualCrossoverJunctionTuneQuestion();
        question.Open(junctions.Length > 0 ? junctions : ["A-B", "B-C"], defaults, remembered);
        return question;
    }

    private static JunctionTuneRequest Search(VirtualCrossoverJunctionTuneQuestion question, JunctionTuneOutcome outcome)
    {
        JunctionTuneRequest request = question.Ask()!;
        int asked = question.BeginSearch();
        Assert.True(question.Searching);
        Assert.Null(question.Ask());
        question.Land(asked, request, outcome);
        question.EndSearch();
        return request;
    }

    [Fact]
    public void TheFirstJunctionOpensOnItsOwnFamiliesAndGoal_TheSecondOnlyMovesTheWindow()
    {
        VirtualCrossoverJunctionTuneQuestion question = Opened(FirstAcoustic);

        Assert.Equal(0, question.JunctionIndex);
        Assert.Equal((80m, 200m), (question.MinHz, question.MaxHz));
        Assert.Equal([CrossoverFilterFamily.LinkwitzRiley], question.Families);
        Assert.True(question.Acoustic);
        Assert.Equal(LinkwitzRiley, question.GoalFamily);
        Assert.Equal(24, question.GoalSlope);
        Assert.Equal(VirtualCrossoverJunctionTuneQuestion.NothingYet, question.Status);

        question.ShowJunction(1);

        Assert.Equal((500m, 2_000m), (question.MinHz, question.MaxHz));
        Assert.Equal([CrossoverFilterFamily.LinkwitzRiley], question.Families);
        Assert.True(question.Acoustic);
    }

    [Fact]
    public void ApplyStandsForASearchThatRan_AndAChangedQuestionRetiresIt()
    {
        VirtualCrossoverJunctionTuneQuestion question = Opened(FirstAcoustic);
        question.ShowJunction(1);
        question.SetFamily(CrossoverFilterFamily.LinkwitzRiley, false);

        Assert.Null(question.Ask());
        Assert.Equal(VirtualCrossoverJunctionTuneQuestion.NoFamily, question.Status);
        Assert.Equal(JunctionTuneStatusTone.Warning, question.StatusTone);

        question.SetFamily(CrossoverFilterFamily.LinkwitzRiley, true);
        JunctionTuneRequest asked = Search(question, Found);

        Assert.Same(asked, question.Result);
        Assert.Equal(1, asked.JunctionIndex);
        Assert.Equal(new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24), asked.AcousticGoal);
        Assert.Equal(Found.Report, question.Report);
        Assert.Equal(JunctionTuneStatusTone.Warning, question.StatusTone);

        question.SetWindow(question.MinHz, 1_500m);
        Assert.Null(question.Result);
        Assert.Equal(VirtualCrossoverJunctionTuneQuestion.Again, question.Status);
    }

    [Theory]
    [InlineData(true, false, "Error")]
    [InlineData(false, true, "Success")]
    [InlineData(false, false, "Warning")]
    public void TheStatusSaysWhetherTheAnswerIsARefusalAnAdviceOrAMiss(bool refused, bool recommended, string tone)
    {
        VirtualCrossoverJunctionTuneQuestion question = Opened(FirstAcoustic);

        Search(question, new JunctionTuneOutcome([], CanApply: !refused, "answer", refused, recommended));

        Assert.Equal("answer", question.Status);
        Assert.Equal(tone, question.StatusTone.ToString());
        Assert.Equal(!refused, question.Result != null);
    }

    [Fact]
    public void AQuestionChangedWhileTheSearchRan_TakesNoAnswerAtAll()
    {
        VirtualCrossoverJunctionTuneQuestion question = Opened(FirstAcoustic);
        JunctionTuneRequest request = question.Ask()!;
        int asked = question.BeginSearch();
        question.SetWindow(question.MinHz, 150m);

        Assert.False(question.Land(asked, request, Found));
        question.EndSearch();

        Assert.Null(question.Result);
        Assert.Empty(question.Report);
        Assert.Equal(VirtualCrossoverJunctionTuneQuestion.Again, question.Status);
    }

    [Fact]
    public void SwitchingJunction_KeepsEachWindow_AndDropsTheReport()
    {
        VirtualCrossoverJunctionTuneQuestion question = Opened(index => index == 0
            ? new JunctionTuneDefaults(80, 200, [CrossoverFilterFamily.Butterworth], null)
            : new JunctionTuneDefaults(500, 2_000, [CrossoverFilterFamily.LinkwitzRiley],
                new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24)));
        question.SetWindow(90m, 190m);
        Search(question, Found);

        question.ShowJunction(1);

        Assert.Equal((500m, 2_000m), (question.MinHz, question.MaxHz));
        Assert.Empty(question.Report);
        Assert.Null(question.Result);
        Assert.False(question.Acoustic);

        question.ShowJunction(0);

        Assert.Equal((90m, 190m), (question.MinHz, question.MaxHz));
    }

    [Fact]
    public void ARememberedQuestionComesBack_AndAWindowNoLongerHoldingTheCornerGivesWay()
    {
        Func<int, JunctionTuneDefaults> defaults = index => index == 0
            ? new JunctionTuneDefaults(80, 200, [CrossoverFilterFamily.Butterworth], null, CornerHz: 125)
            : new JunctionTuneDefaults(500, 2_000, [CrossoverFilterFamily.LinkwitzRiley], null, CornerHz: 1_000);
        VirtualCrossoverJunctionTuneQuestion first = Opened(defaults);
        first.ShowJunction(1);
        first.SetFamily(CrossoverFilterFamily.Bessel, true);
        first.SetIndependentSlopes(false);
        first.SetSplitCorners(false);
        first.SetSlopeWindow(18, 36);
        first.SetGoalFamily(Butterworth);
        first.SetGoalSlope(30);
        first.SetMode(acoustic: true);
        first.SetSumBudget(0.6m);
        first.SetWindow(700m, 1_400m);
        VirtualCrossoverJunctionTuneSettings left = first.Remembered();

        VirtualCrossoverJunctionTuneQuestion second = Opened(defaults, left);

        Assert.Equal(1, second.JunctionIndex);
        Assert.Equal((700m, 1_400m), (second.MinHz, second.MaxHz));
        Assert.True(second.Bessel);
        Assert.False(second.IndependentSlopes);
        Assert.False(second.SplitCorners);
        Assert.Equal((18, 36), (second.MinSlope, second.MaxSlope));
        Assert.Equal(Butterworth, second.GoalFamily);
        Assert.Equal(30, second.GoalSlope);
        Assert.True(second.Acoustic);
        Assert.Equal(0.6m, second.SumBudget);

        VirtualCrossoverJunctionTuneQuestion moved = Opened(
            index => index == 0
                ? defaults(0)
                : new JunctionTuneDefaults(2_000, 4_000, [CrossoverFilterFamily.LinkwitzRiley], null, CornerHz: 2_800),
            left);
        Assert.Equal(2_000m, moved.MinHz);
        Assert.True(moved.Bessel);
    }

    [Fact]
    public void TheSlopeWindowIsWhatTheSummationSearchMayUse_EitherWayRound()
    {
        VirtualCrossoverJunctionTuneQuestion question = Opened(
            _ => new JunctionTuneDefaults(80, 200, [CrossoverFilterFamily.LinkwitzRiley], null), null, "A-B");

        Assert.Equal([12, 18, 24, 30, 36, 42, 48], question.Ask()!.Slopes);
        Assert.Null(question.Ask()!.AcousticGoal);

        question.SetSlopeWindow(36, 24);
        Assert.Equal([24, 30, 36], question.Ask()!.Slopes);

        question.SetMode(acoustic: true);
        JunctionTuneRequest acoustic = question.Ask()!;
        Assert.Empty(acoustic.Slopes);
        Assert.Equal(new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24), acoustic.AcousticGoal);
        Assert.Equal(VirtualCrossoverJunctionTuneQuestion.DefaultSumBudgetDb, acoustic.SumSlackDb);
    }

    [Fact]
    public void AGoalFamilyKeepsTheSlopeWhereItOffersIt_ElseItsSecond_AndNoFamilyStatesNoGoal()
    {
        VirtualCrossoverJunctionTuneQuestion question = Opened(FirstAcoustic);
        question.SetGoalSlope(36);

        question.SetGoalFamily(Butterworth);
        Assert.Equal([6, 12, 18, 24, 30, 36, 42, 48], question.GoalSlopes);
        Assert.Equal(36, question.GoalSlope);

        question.SetGoalSlope(30);
        question.SetGoalFamily(LinkwitzRiley);
        Assert.Equal(24, question.GoalSlope);

        question.SetGoalFamily(null);
        Assert.Empty(question.GoalSlopes);
        Assert.Null(question.GoalSlope);
        Assert.Null(question.Ask()!.AcousticGoal);
    }

    [Fact]
    public void WithNoJunctionInView_ItSaysSoAndAsksNothing()
    {
        var question = new VirtualCrossoverJunctionTuneQuestion();
        question.Open([], _ => throw new InvalidOperationException("no junction to open"), null);

        Assert.Null(question.Ask());
        Assert.Equal(VirtualCrossoverJunctionTuneQuestion.NoJunction, question.Status);
        Assert.Equal(JunctionTuneStatusTone.Warning, question.StatusTone);
    }

    [Fact]
    public void AWindowNoDecimalCanHold_OpensClampedRatherThanThrowing()
    {
        VirtualCrossoverJunctionTuneQuestion question = Opened(
            _ => new JunctionTuneDefaults(80, 200, [CrossoverFilterFamily.LinkwitzRiley], null),
            new VirtualCrossoverJunctionTuneSettings { Junction = "A-B", Windows = { ["A-B"] = [1e100, 2e100] } },
            "A-B");

        Assert.Equal(VirtualCrossoverJunctionTuneQuestion.CornerRange.Maximum, question.MaxHz);
    }
}
