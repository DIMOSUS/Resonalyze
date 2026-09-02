using System.Reflection;
using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze.App.Tests;

/// <summary>
/// The validator is what stands between a chat assistant's reply and a user's
/// tune: it decides admissibility against the live session, with the same limits
/// the project file and the channel block enforce, and it refuses to apply a
/// value that was reasoned about a state the session has since left.
/// </summary>
public sealed class AgentProposalValidatorTests
{
    private const string Package = "b6bd73c2-997b-4fe0-814a-d123cc403b8a";

    [Fact]
    public void Review_AcceptsEveryOperationKindAgainstTheStateItWasWrittenFor()
    {
        AgentSessionSnapshot session = Session();
        AgentChannelSnapshot bLeft = session.Find("B:left")!;
        AgentProposal proposal = Proposal(
            new SetPolarityOperation("op-1", "B:left", "phase", false, true),
            new SetGainOperation("op-2", "A:right", "level", -2.0, -2.6),
            new SetDelayOperation("op-3", "A:right", "arrival", 1.42, 1.37),
            new SetCrossoverOperation("op-4", "B:left", "top",
                Crossover("BandPass", Edge("LinkwitzRiley", 250, 24), Edge("LinkwitzRiley", 2800, 24)),
                Crossover("BandPass", Edge("LinkwitzRiley", 250, 24), Edge("LinkwitzRiley", 2600, 24))),
            new ReplacePeqBankOperation("op-5", "B:left", "door",
                AgentPeqHash.Compute(bLeft.Settings.PeqPreampDb, bLeft.Settings.PeqBands),
                new AgentPeqBank(-1.0, [new AgentPeqBand("Peaking", 820, 2.1, -2.4)])));

        AgentProposalReview review = AgentProposalValidator.Review(proposal, session);

        Assert.Empty(review.Warnings);
        Assert.All(review.Verdicts, verdict => Assert.True(verdict.Applicable, verdict.Message));
        Assert.Collection(review.Verdicts,
            v => { Assert.Equal(AgentVerdictStatus.Valid, v.Status); Assert.Equal("Normal", v.Current); Assert.Equal("Inverted", v.Proposed); },
            v => { Assert.Equal(AgentVerdictStatus.Valid, v.Status); Assert.Equal("-2.0 dB", v.Current); Assert.Equal("-2.6 dB", v.Proposed); },
            v => { Assert.Equal(AgentVerdictStatus.Valid, v.Status); Assert.Equal("1.42 ms", v.Current); Assert.Equal("1.37 ms", v.Proposed); },
            v => { Assert.Equal(AgentVerdictStatus.Warning, v.Status); Assert.Contains("2600 Hz", v.Proposed); Assert.Equal(AgentProposalValidator.DeviceLimitsUnknown, v.Message); },
            v => { Assert.Equal(AgentVerdictStatus.Warning, v.Status); Assert.Equal("2 bands, preamp 0.0 dB", v.Current); Assert.Equal("1 band, preamp -1.0 dB", v.Proposed); });
        Assert.Equal("B left", review.Verdicts[0].ChannelLabel);

        // Reviewing mutates nothing: the live settings are what they were.
        Assert.False(bLeft.Settings.InvertPolarity);
        Assert.Equal(2, bLeft.Settings.PeqBands.Count);
        Assert.Equal(-2.0, session.Find("A:right")!.Settings.GainDb);
    }

    [Fact]
    public void Review_RejectsAnOperationWhoseExpectedValueIsNotTheCurrentOne()
    {
        AgentSessionSnapshot session = Session();
        AgentProposal proposal = Proposal(
            new SetGainOperation("op-1", "A:right", "", -2.5, -3.0),
            new SetDelayOperation("op-2", "A:right", "", 1.4, 1.37),
            new SetPolarityOperation("op-3", "B:left", "", true, false),
            new SetCrossoverOperation("op-4", "B:left", "",
                Crossover("BandPass", Edge("LinkwitzRiley", 250, 24), Edge("Butterworth", 2800, 24)),
                Crossover("BandPass", Edge("LinkwitzRiley", 250, 24), Edge("LinkwitzRiley", 2600, 24))),
            new ReplacePeqBankOperation("op-5", "B:left", "", "000000000000",
                new AgentPeqBank(0, [])));

        AgentProposalReview review = AgentProposalValidator.Review(proposal, session);

        Assert.All(review.Verdicts, verdict =>
        {
            Assert.Equal(AgentVerdictStatus.Rejected, verdict.Status);
            Assert.Contains("changed since the package was copied", verdict.Message);
        });
        Assert.Contains("gain is -2.0 dB", review.Verdicts[0].Message);
        Assert.Contains("low-pass edge", review.Verdicts[3].Message);
        Assert.False(review.HasApplicable);
    }

    [Fact]
    public void Review_RejectsUnknownChannels_IncludingTheRightSideOfAMonoBlock()
    {
        AgentProposal proposal = Proposal(
            new SetGainOperation("op-1", "C:right", "", 0, -1),
            new SetGainOperation("op-2", "D:left", "", 0, -1),
            new SetGainOperation("op-3", "a:left", "", 0, -1));

        AgentProposalReview review = AgentProposalValidator.Review(proposal, Session());

        Assert.All(review.Verdicts, verdict =>
        {
            Assert.Equal(AgentVerdictStatus.Rejected, verdict.Status);
            Assert.Contains("Unknown channel", verdict.Message);
        });
    }

    [Fact]
    public void Review_RejectsANoOpAndConflictingOperations()
    {
        AgentProposal proposal = Proposal(
            new SetGainOperation("op-1", "A:right", "", -2.0, -2.0),
            new SetDelayOperation("op-2", "A:right", "", 1.42, 1.5),
            new SetDelayOperation("op-3", "A:right", "", 1.42, 1.6),
            new SetPolarityOperation("op-4", "A:right", "", false, true));

        AgentProposalReview review = AgentProposalValidator.Review(proposal, Session());

        Assert.Equal("No change.", review.Verdicts[0].Message);
        Assert.Equal(AgentVerdictStatus.Rejected, review.Verdicts[1].Status);
        Assert.Contains("Conflicts with op-3", review.Verdicts[1].Message);
        Assert.Contains("Conflicts with op-2", review.Verdicts[2].Message);
        Assert.Equal(AgentVerdictStatus.Valid, review.Verdicts[3].Status);
    }

    [Theory]
    [InlineData(-60.1, "between")]
    [InlineData(20.1, "between")]
    [InlineData(-2.55, "multiple of 0.1")]
    public void Review_HoldsGainToTheChannelBlocksOwnRangeAndStep(double proposed, string words)
    {
        AgentProposal proposal = Proposal(new SetGainOperation("op-1", "A:right", "", -2.0, proposed));

        AgentOperationVerdict verdict = AgentProposalValidator.Review(proposal, Session()).Verdicts[0];

        Assert.Equal(AgentVerdictStatus.Rejected, verdict.Status);
        Assert.Contains(words, verdict.Message);
    }

    [Theory]
    [InlineData(-0.01, "between")]
    [InlineData(100.01, "between")]
    [InlineData(1.375, "multiple of 0.01")]
    public void Review_HoldsDelayToTheChannelBlocksOwnRangeAndStep(double proposed, string words)
    {
        AgentProposal proposal = Proposal(new SetDelayOperation("op-1", "A:right", "", 1.42, proposed));

        AgentOperationVerdict verdict = AgentProposalValidator.Review(proposal, Session()).Verdicts[0];

        Assert.Equal(AgentVerdictStatus.Rejected, verdict.Status);
        Assert.Contains(words, verdict.Message);
    }

    [Fact]
    public void Review_WarnsAboveTheProcessorsDelayCeiling_ButDoesNotRefuse()
    {
        AgentProposal proposal = Proposal(new SetDelayOperation("op-1", "A:right", "", 1.42, 12.5));

        AgentOperationVerdict verdict = AgentProposalValidator.Review(proposal, Session(maxDelayMs: 10)).Verdicts[0];

        Assert.Equal(AgentVerdictStatus.Warning, verdict.Status);
        Assert.Contains("ceiling of 10.00 ms", verdict.Message);
        Assert.True(verdict.Applicable);
    }

    [Fact]
    public void GainAndDelayLimits_MatchTheChannelBlocksFields()
    {
        // The validator restates the block's numeric fields rather than reading them
        // (it runs with no control in sight); this is what keeps the two together.
        StaTest.Run(() =>
        {
            using var control = new VirtualCrossoverChannelControl();
            Assert.Equal((decimal)AgentProposalValidator.MinimumGainDb, control.GainInput.Minimum);
            Assert.Equal((decimal)AgentProposalValidator.MaximumGainDb, control.GainInput.Maximum);
            Assert.Equal(1, control.GainInput.DecimalPlaces);
            Assert.Equal((decimal)AgentProposalValidator.MinimumDelayMs, control.DelayInput.Minimum);
            Assert.Equal((decimal)AgentProposalValidator.MaximumDelayMs, control.DelayInput.Maximum);
            Assert.Equal(2, control.DelayInput.DecimalPlaces);
        });
    }

    [Theory]
    [InlineData("Bandpass", "LinkwitzRiley", 24, 2600, "Unknown crossover kind")]
    [InlineData("BandPass", "linkwitzriley", 24, 2600, "Unknown crossover family")]
    [InlineData("BandPass", "1", 24, 2600, "Unknown crossover family")]
    [InlineData("BandPass", "LinkwitzRiley", 18, 2600, "offers slopes")]
    [InlineData("BandPass", "LinkwitzRiley", 24, 9, "corner frequency is invalid")]
    [InlineData("BandPass", "LinkwitzRiley", 24, 23_000, "Nyquist")]
    public void Review_HoldsACrossoverToTheFamiliesSlopesCornersAndNyquist(
        string kind, string family, int slope, double lowPassHz, string words)
    {
        AgentProposal proposal = Proposal(new SetCrossoverOperation("op-1", "B:left", "",
            Crossover("BandPass", Edge("LinkwitzRiley", 250, 24), Edge("LinkwitzRiley", 2800, 24)),
            Crossover(kind, Edge("LinkwitzRiley", 250, 24), Edge(family, lowPassHz, slope))));

        AgentOperationVerdict verdict = AgentProposalValidator.Review(proposal, Session(processorRateHz: 44_100)).Verdicts[0];

        Assert.Equal(AgentVerdictStatus.Rejected, verdict.Status);
        Assert.Contains(words, verdict.Message);
    }

    [Fact]
    public void Review_RequiresTheEdgesTheKindUses_AndKeepsTheStoredOneItDoesNot()
    {
        AgentSessionSnapshot session = Session();
        AgentProposal proposal = Proposal(
            new SetCrossoverOperation("op-1", "B:left", "",
                Crossover("BandPass", Edge("LinkwitzRiley", 250, 24), Edge("LinkwitzRiley", 2800, 24)),
                Crossover("HighPass", Edge("Bessel", 300, 18), null)),
            new SetCrossoverOperation("op-2", "A:right", "",
                Crossover("Off", null, null),
                Crossover("LowPass", null, null)));

        AgentProposalReview review = AgentProposalValidator.Review(proposal, session);

        Assert.True(review.Verdicts[0].Applicable, review.Verdicts[0].Message);
        Assert.Equal(AgentVerdictStatus.Rejected, review.Verdicts[1].Status);
        Assert.Contains("needs its lowPass edge", review.Verdicts[1].Message);

        var copy = AgentOperations.CloneEditable(session.Find("B:left")!.Settings);
        AgentOperations.Apply((AgentSettingsOperation)review.Verdicts[0].Operation!, copy);
        Assert.Equal(CrossoverKind.HighPass, copy.CrossoverKind);
        Assert.Equal(new CrossoverEdge(CrossoverFilterFamily.Bessel, 300, 18, 1.0), copy.HighPassEdge);
        Assert.Equal(new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2800, 24), copy.LowPassEdge);
    }

    [Fact]
    public void Review_ChecksChebyshevRipple_AndOnlyThere()
    {
        AgentProposal proposal = Proposal(
            new SetCrossoverOperation("op-1", "B:left", "",
                Crossover("BandPass", Edge("LinkwitzRiley", 250, 24), Edge("LinkwitzRiley", 2800, 24)),
                Crossover("BandPass", Edge("Chebyshev", 250, 24, 4.0), Edge("LinkwitzRiley", 2800, 24))),
            new SetCrossoverOperation("op-2", "B:left", "",
                Crossover("BandPass", Edge("LinkwitzRiley", 250, 24), Edge("LinkwitzRiley", 2800, 24)),
                Crossover("BandPass", Edge("Chebyshev", 250, 24, 0.5), Edge("Butterworth", 2800, 24, 4.0))));

        AgentProposalReview review = AgentProposalValidator.Review(proposal, Session());

        Assert.Contains("ripple is invalid", review.Verdicts[0].Message);
        // op-2 is the only applicable crossover on B left, so no conflict; Butterworth
        // ignores its ripple, so 4.0 there is stored, not refused.
        Assert.True(review.Verdicts[1].Applicable, review.Verdicts[1].Message);
    }

    [Fact]
    public void Review_HoldsAPeqBankToItsCountTypesAndNyquist_AndKeepsQAsRbj()
    {
        AgentSessionSnapshot session = Session();
        AgentChannelSnapshot bLeft = session.Find("B:left")!;
        string hash = AgentPeqHash.Compute(bLeft.Settings.PeqPreampDb, bLeft.Settings.PeqBands);
        var tooMany = Enumerable.Range(0, EqualizationCurve.MaxBandCount + 1)
            .Select(index => new AgentPeqBand("Peaking", 100 + index, 1, -1)).ToList();
        AgentProposal proposal = Proposal(
            new ReplacePeqBankOperation("op-1", "B:left", "", hash, new AgentPeqBank(0, tooMany)),
            new ReplacePeqBankOperation("op-2", "B:left", "", hash,
                new AgentPeqBank(0, [new AgentPeqBand("Bell", 100, 1, -1)])),
            new ReplacePeqBankOperation("op-3", "B:left", "", hash,
                new AgentPeqBank(0, [new AgentPeqBand("Peaking", 23_000, 1, -1)])),
            new ReplacePeqBankOperation("op-4", "B:left", "", hash,
                new AgentPeqBank(0, [new AgentPeqBand("Peaking", 100, 0, -1)])),
            new ReplacePeqBankOperation("op-5", "B:left", "", hash,
                new AgentPeqBank(0, [new AgentPeqBand("Peaking", 100, 1, -1), new AgentPeqBand("AllPassSecondOrder", 2400, 0.8, 0)])),
            new ReplacePeqBankOperation("op-6", "B:left", "", hash,
                new AgentPeqBank(61, [])));

        AgentProposalReview review = AgentProposalValidator.Review(proposal, Session(processorRateHz: 44_100));

        Assert.Contains("at most 32 bands", review.Verdicts[0].Message);
        Assert.Contains("Unknown PEQ band type 'Bell'", review.Verdicts[1].Message);
        Assert.Contains("Nyquist", review.Verdicts[2].Message);
        Assert.Contains("PEQ band is invalid", review.Verdicts[3].Message);
        Assert.Equal(AgentVerdictStatus.Warning, review.Verdicts[4].Status);
        Assert.Contains("PEQ preamp is invalid", review.Verdicts[5].Message);

        var copy = AgentOperations.CloneEditable(bLeft.Settings);
        AgentOperations.Apply((AgentSettingsOperation)review.Verdicts[4].Operation!, copy);
        Assert.Equal(0.8, copy.PeqBands[1].Q);
        Assert.Equal(PeqBandType.AllPassSecondOrder, copy.PeqBands[1].Type);
    }

    [Fact]
    public void Review_WarnsWhenABanksNetResponseRisesAboveUnity_JudgedOnTheWholeBankNotOnABandsSign()
    {
        AgentSessionSnapshot session = Session();
        AgentChannelSnapshot bLeft = session.Find("B:left")!;
        string hash = AgentPeqHash.Compute(bLeft.Settings.PeqPreampDb, bLeft.Settings.PeqBands);
        string Judge(AgentPeqBank bank) => AgentProposalValidator.Review(
            Proposal(new ReplacePeqBankOperation("op-1", "B:left", "", hash, bank)), session)
            .Verdicts[0].Message;

        // A +3 dB bell with nothing against it.
        string bare = Judge(new AgentPeqBank(0, [new AgentPeqBand("Peaking", 1_000, 1.5, 3)]));
        // The peak sits on the headroom grid's nearest point to the bell's centre.
        Assert.Matches(@"rises to \+3\.0 dB at (9[89]\d|10[01]\d)(\.\d+)? Hz", bare);
        Assert.Contains("lower the preamp by 3.0 dB", bare);
        // The same bell under a −3 dB preamp: net never above unity.
        Assert.DoesNotContain("rises",
            Judge(new AgentPeqBank(-3, [new AgentPeqBand("Peaking", 1_000, 1.5, 3)])));
        // A +3 dB bell inside a −6 dB shelf that covers it: net stays below zero.
        Assert.DoesNotContain("rises",
            Judge(new AgentPeqBank(0, [new AgentPeqBand("LowShelf", 4_000, 0.7, -6), new AgentPeqBand("Peaking", 1_000, 1.5, 3)])));
        // A cut only: no rise, no warning.
        Assert.DoesNotContain("rises",
            Judge(new AgentPeqBank(0, [new AgentPeqBand("Peaking", 1_000, 1.5, -3)])));
    }

    [Fact]
    public void Review_WarnsOnANarrowBellInsideTheChannelsOwnJunctionZone()
    {
        // B left is band-passed 250 Hz .. 2800 Hz, so its junction zones run
        // 125..500 Hz and 1400..5600 Hz.
        AgentSessionSnapshot session = Session();
        AgentChannelSnapshot bLeft = session.Find("B:left")!;
        string hash = AgentPeqHash.Compute(bLeft.Settings.PeqPreampDb, bLeft.Settings.PeqBands);
        string Judge(params AgentPeqBand[] bands) => AgentProposalValidator.Review(
            Proposal(new ReplacePeqBankOperation("op-1", "B:left", "", hash, new AgentPeqBank(0, bands))), session)
            .Verdicts[0].Message;

        string narrow = Judge(new AgentPeqBand("Peaking", 300, 4, -3));
        Assert.Contains("Band at 300 Hz (Q 4)", narrow);
        Assert.Contains("around the 250 Hz crossover", narrow);
        Assert.Contains("keep Q at or below 2", narrow);
        Assert.Contains("around the 2800 Hz crossover", Judge(new AgentPeqBand("Peaking", 2_000, 2.5, -3)));

        // Wide enough, outside both zones, a shelf, or an all-pass: no comment.
        Assert.DoesNotContain("junction zone", Judge(new AgentPeqBand("Peaking", 300, 1.5, -3)));
        Assert.DoesNotContain("junction zone", Judge(new AgentPeqBand("Peaking", 1_000, 4, -3)));
        Assert.DoesNotContain("junction zone", Judge(new AgentPeqBand("LowShelf", 300, 4, -3)));
        Assert.DoesNotContain("junction zone", Judge(new AgentPeqBand("AllPassSecondOrder", 2_400, 4, 0)));

        // A channel with no crossover has no junction zone of its own to warn about.
        string aRightHash = AgentPeqHash.Compute(0, []);
        string open = AgentProposalValidator.Review(
            Proposal(new ReplacePeqBankOperation("op-1", "A:right", "", aRightHash,
                new AgentPeqBank(0, [new AgentPeqBand("Peaking", 300, 6, -3)]))), session)
            .Verdicts[0].Message;
        Assert.DoesNotContain("junction zone", open);
    }

    [Fact]
    public void Review_JudgesTheJunctionZoneOnTheChannelAsItWouldEndUp()
    {
        // B left holds a Q 4 bell at 1 kHz, outside both of its zones today (the
        // corners are 250 Hz and 2.8 kHz). A crossover move alone puts the low-pass
        // at 1.2 kHz and the existing bell in its zone: the crossover row says so.
        AgentSessionSnapshot session = Session();
        AgentChannelSnapshot bLeft = session.Find("B:left")!;
        bLeft.Settings.PeqBands = [new PeqBand(1_000, 4, -3)];
        string hash = AgentPeqHash.Compute(bLeft.Settings.PeqPreampDb, bLeft.Settings.PeqBands);
        AgentCrossover current = Crossover("BandPass", Edge("LinkwitzRiley", 250, 24), Edge("LinkwitzRiley", 2800, 24));
        AgentCrossover moved = Crossover("BandPass", Edge("LinkwitzRiley", 250, 24), Edge("LinkwitzRiley", 1200, 24));

        AgentProposalReview alone = AgentProposalValidator.Review(
            Proposal(new SetCrossoverOperation("op-1", "B:left", "", current, moved)), session);
        Assert.Equal(AgentVerdictStatus.Warning, alone.Verdicts[0].Status);
        Assert.Contains("Band at 1000 Hz (Q 4)", alone.Verdicts[0].Message);
        Assert.Contains("around the 1200 Hz crossover", alone.Verdicts[0].Message);

        // The crossover move plus a new bank with a narrow bell at 1.5 kHz: the bank
        // is judged against the crossover the OTHER row proposes, not today's.
        AgentProposalReview both = AgentProposalValidator.Review(
            Proposal(
                new SetCrossoverOperation("op-1", "B:left", "", current, moved),
                new ReplacePeqBankOperation("op-2", "B:left", "", hash,
                    new AgentPeqBank(0, [new AgentPeqBand("Peaking", 1_500, 5, -3)]))),
            session);
        Assert.All(both.Verdicts, verdict => Assert.Equal(AgentVerdictStatus.Warning, verdict.Status));
        Assert.Contains("Band at 1500 Hz (Q 5)", both.Verdicts[1].Message);
        Assert.Contains("around the 1200 Hz crossover", both.Verdicts[1].Message);
        Assert.DoesNotContain("1000 Hz", both.Verdicts[1].Message);

        // A narrow bell at 1 kHz alone, against today's crossover: in no zone.
        AgentProposalReview bankAlone = AgentProposalValidator.Review(
            Proposal(new ReplacePeqBankOperation("op-2", "B:left", "", hash,
                new AgentPeqBank(0, [new AgentPeqBand("Peaking", 1_000, 5, -3)]))),
            session);
        Assert.DoesNotContain("junction zone", bankAlone.Verdicts[0].Message);
    }

    [Fact]
    public void PeqHeadroom_ReadsTheNetPeakAtTheProcessorsRate()
    {
        (double peakDb, double peakHz) = AgentPeqHeadroom.Peak(-1, [new PeqBand(820, 2.1, 4)], 96_000);
        Assert.Equal(3.0, peakDb, 1);
        Assert.InRange(peakHz, 780, 860);

        (peakDb, _) = AgentPeqHeadroom.Peak(-2.5, [], 96_000);
        Assert.Equal(-2.5, peakDb);

        // A narrow bell at the bottom of the band, and one so narrow that a
        // 512-point log grid steps over it: both are found within a tenth of a dB
        // of their top. Outside 20 Hz .. 20 kHz nothing is judged: a band there is
        // legal, and what it does there is not a tuning question.
        (peakDb, peakHz) = AgentPeqHeadroom.Peak(0, [new PeqBand(25, 30, 12)], 96_000);
        Assert.Equal(12.0, peakDb, 1);
        Assert.InRange(peakHz, 24.5, 25.5);
        (peakDb, _) = AgentPeqHeadroom.Peak(0, [new PeqBand(30_000, 30, 12)], 96_000);
        Assert.True(peakDb < 1, peakDb.ToString());
        (peakDb, peakHz) = AgentPeqHeadroom.Peak(0, [new PeqBand(1_003, 60, 12)], 96_000);
        Assert.Equal(12.0, peakDb, 1);
        Assert.InRange(peakHz, 1_000, 1_006);
        // Two bells that together top out between their centres.
        (peakDb, _) = AgentPeqHeadroom.Peak(0, [new PeqBand(900, 3, 4), new PeqBand(1_100, 3, 4)], 96_000);
        Assert.True(peakDb > 4.5, peakDb.ToString());
    }

    [Fact]
    public void PeqHash_ChangesWithAnyBandFieldOrderOrPreamp()
    {
        PeqBand a = new(100, 1.5, -2);
        PeqBand b = new(1000, 2.0, 3, PeqBandType.LowShelf);
        string baseline = AgentPeqHash.Compute(0, [a, b]);

        Assert.Equal(12, baseline.Length);
        Assert.Equal(baseline, AgentPeqHash.Compute(0, [a, b]));
        Assert.NotEqual(baseline, AgentPeqHash.Compute(0, [b, a]));
        Assert.NotEqual(baseline, AgentPeqHash.Compute(-1, [a, b]));
        Assert.NotEqual(baseline, AgentPeqHash.Compute(0, [a with { Q = 1.6 }, b]));
        Assert.NotEqual(baseline, AgentPeqHash.Compute(0, [a, b with { Type = PeqBandType.HighShelf }]));
        Assert.NotEqual(baseline, AgentPeqHash.Compute(0, [a, b with { GainDb = 3.1 }]));
        Assert.NotEqual(baseline, AgentPeqHash.Compute(0, [a with { FrequencyHz = 100.1 }, b]));
    }

    [Fact]
    public void Review_WarnsWhenTheReplyAnswersAnotherPackage_AndNotWhenItNamesNone()
    {
        AgentProposal other = Proposal(new SetGainOperation("op-1", "A:right", "", -2.0, -3.0)) with
        {
            PackageId = "11111111-2222-3333-4444-555555555555"
        };
        AgentProposal none = other with { PackageId = null };

        Assert.Single(AgentProposalValidator.Review(other, Session()).Warnings);
        Assert.Empty(AgentProposalValidator.Review(none, Session()).Warnings);
        Assert.Empty(AgentProposalValidator.Review(other, Session(lastPackageId: null)).Warnings);
        // A warning is only a warning: the row itself still applies.
        Assert.True(AgentProposalValidator.Review(other, Session()).Verdicts[0].Applicable);
    }

    [Fact]
    public void Review_ListsParserRejectionsAsRejectedRows()
    {
        AgentProposal proposal = Proposal(new SetGainOperation("op-2", "A:right", "", -2.0, -3.0)) with
        {
            Rejected = [new AgentRejectedOperation("op-1", "setTarget", "Unsupported operation 'setTarget'.")]
        };

        AgentProposalReview review = AgentProposalValidator.Review(proposal, Session());

        Assert.Equal(AgentVerdictStatus.Rejected, review.Verdicts[0].Status);
        Assert.Equal("op-1", review.Verdicts[0].Id);
        Assert.Equal("setTarget", review.Verdicts[0].Parameter);
        Assert.False(review.Verdicts[0].Applicable);
        Assert.True(review.Verdicts[1].Applicable);
    }

    [Fact]
    public void CheckSelection_RefusesACombinationThatIsInvalidAsAWhole()
    {
        // Each operation alone passes; together they ask a low-pass corner below the
        // high-pass one, which the project's validator has no rule against — so this
        // pins that the whole-set check runs the SAME validator and nothing stricter,
        // and that a combination it does refuse names the channel.
        AgentSessionSnapshot session = Session();
        AgentProposal proposal = Proposal(
            new SetGainOperation("op-1", "A:right", "", -2.0, -3.0),
            new SetDelayOperation("op-2", "A:right", "", 1.42, 2.0));
        AgentProposalReview review = AgentProposalValidator.Review(proposal, session);

        Assert.Null(AgentProposalValidator.CheckSelection(review.Verdicts));

        // A verdict whose operation is invalid slips in only if the review was
        // bypassed; the whole-set check still catches it.
        AgentOperationVerdict forged = review.Verdicts[0] with
        {
            Operation = new SetGainOperation("op-1", "A:right", "", -2.0, 500)
        };
        string? problem = AgentProposalValidator.CheckSelection([forged, review.Verdicts[1]]);
        Assert.NotNull(problem);
        Assert.StartsWith("A right:", problem);
        Assert.Equal(-2.0, session.Find("A:right")!.Settings.GainDb);
    }


    [Fact]
    public void Review_TakesTheEngineRequestsThisBuildRuns_AndRefusesTheOnesItDoesNot()
    {
        AgentProposal proposal = Proposal(
            new UseSpatialAverageOperation("op-1", "the averages are attached", "MicArray", true),
            new RunAutoCrossoverOperation("op-2", "the corners are guesses"),
            new RunAutoDelayOperation("op-3", "realign after the flip", 0.35, null, null, null, null),
            new AutoTunePeqOperation("op-4", "B:left", "fit the door", -6, 100, 8000, true, false, "point"));

        AgentProposalReview review = AgentProposalValidator.Review(
            proposal, Session(captures: ["MicArray"]));

        Assert.Collection(review.Verdicts,
            v =>
            {
                Assert.Equal(AgentVerdictStatus.Valid, v.Status);
                Assert.Equal(AgentProposalValidator.AllChannels, v.ChannelLabel);
                Assert.Equal("Spatial average", v.Parameter);
                Assert.Equal("Off, hybrid off", v.Current);
                Assert.Equal("MicArray, hybrid on", v.Proposed);
            },
            v =>
            {
                Assert.Equal(AgentVerdictStatus.Warning, v.Status);
                Assert.Equal(AgentProposalValidator.AllChannels, v.ChannelLabel);
                Assert.Contains("rewrites the crossover and the gain", v.Message);
                Assert.True(v.Applicable);
            },
            v =>
            {
                Assert.Equal(AgentVerdictStatus.Warning, v.Status);
                Assert.True(v.Applicable);
                Assert.Contains("rewrites the delay and polarity", v.Message);
                Assert.Contains("without its dialog", v.Message);
                Assert.Equal("scene 0.25 ms LHD, gains off, rear fill 15.00 ms", v.Current);
                Assert.Equal("scene 0.35 ms LHD, gains off, rear fill 15.00 ms", v.Proposed);
            },
            v =>
            {
                Assert.Equal(AgentVerdictStatus.Warning, v.Status);
                Assert.True(v.Applicable);
                Assert.Contains("replaces this channel's whole PEQ bank", v.Message);
                Assert.Contains("without the EQ Wizard", v.Message);
                Assert.Equal("B left", v.ChannelLabel);
                Assert.Equal("2 bands, preamp 0.0 dB", v.Current);
                Assert.Equal(
                    "auto-tune 100 Hz to 8000 Hz, target -6.0 dB, shelves allowed, " +
                    "cuts and boosts, from the point measurement", v.Proposed);
            });

        // The list the package publishes is the list the review holds a reply to.
        Assert.Equal(
            ["setGainDb", "setDelayMs", "setPolarity", "setCrossover", "replacePeqBank",
                "useSpatialAverage", "runAutoCrossover", "runAutoDelay", "autoTunePeq"],
            AgentProtocol.Operations);
    }

    [Fact]
    public void Review_DescribesAnAutoDelayRunAgainstTheProjectsOwnFigures()
    {
        AgentProposal proposal = Proposal(
            new RunAutoDelayOperation("op-1", "", null, true, true, 2.0, 12.5));

        AgentOperationVerdict verdict = AgentProposalValidator.Review(
            proposal, Session(adjustGains: false)).Verdicts[0];

        // What it leaves out is what the dialog would open with; the near-side
        // cut appears only on the side of the line where the balance is on.
        Assert.Equal("scene 0.25 ms LHD, gains off, rear fill 15.00 ms", verdict.Current);
        Assert.Equal(
            "scene 0.25 ms RHD, gains on, near-side cut 2.0 dB, rear fill 12.50 ms",
            verdict.Proposed);
    }

    [Theory]
    [InlineData(6.0, null, null, "The scene offset must be between 0.00 and 5.00 ms")]
    [InlineData(0.255, null, null, "The scene offset must be a multiple of 0.01 ms")]
    [InlineData(null, 7.0, null, "The near-side cut must be between 0.0 and 6.0 dB")]
    [InlineData(null, 1.25, null, "The near-side cut must be a multiple of 0.1 dB")]
    [InlineData(null, null, 45.0, "The rear fill offset must be between 0.0 and 30.0 ms")]
    public void Review_HoldsAnAutoDelayRequestToTheDialogsOwnFields(
        double? sceneOffsetMs, double? nearSideCutDb, double? rearFillOffsetMs, string words)
    {
        AgentProposal proposal = Proposal(new RunAutoDelayOperation(
            "op-1", "", sceneOffsetMs, null, null, nearSideCutDb, rearFillOffsetMs));

        AgentOperationVerdict verdict = AgentProposalValidator.Review(proposal, Session()).Verdicts[0];

        Assert.Equal(AgentVerdictStatus.Rejected, verdict.Status);
        // The value is wrong on its own terms, so that is what the row says —
        // not that the engine happens to be unavailable in this build.
        Assert.Contains(words, verdict.Message);
    }

    [Theory]
    [InlineData("D:mono", null, null, null, null, "has no measurement")]
    [InlineData("B:left", 90.0, null, null, null, "The target level must be between -120 and 60 dB")]
    [InlineData("B:left", null, 8000.0, 100.0, null, "lower edge must sit below its upper edge")]
    [InlineData("B:left", null, 100.0, 60_000.0, null, "upper edge must sit above 0 Hz and below")]
    [InlineData("B:left", null, null, null, "hybrid", "Unknown auto-tune source 'hybrid'")]
    [InlineData("B:left", null, null, null, "spatialAverage", "carries no spatial average")]
    public void Review_HoldsAnAutoTuneRequestToTheChannelAndTheWizardsFields(
        string channelId, double? targetLevelDb, double? minHz, double? maxHz, string? source, string words)
    {
        AgentProposal proposal = Proposal(new AutoTunePeqOperation(
            "op-1", channelId, "", targetLevelDb, minHz, maxHz, null, null, source));

        AgentOperationVerdict verdict = AgentProposalValidator.Review(proposal, Session()).Verdicts[0];

        Assert.Equal(AgentVerdictStatus.Rejected, verdict.Status);
        Assert.Contains(words, verdict.Message);
    }

    [Theory]
    [InlineData("MicArray", true, VirtualCrossoverSpatialAverageMode.Off, false, "MovingMic", "No channel in this session carries a MicArray capture")]
    [InlineData("MovingMic", false, VirtualCrossoverSpatialAverageMode.Off, false, "MovingMic", "nothing to do")]
    [InlineData("Off", true, VirtualCrossoverSpatialAverageMode.Off, false, "MovingMic", "Unknown spatial average mode 'Off'")]
    [InlineData("movingmic", true, VirtualCrossoverSpatialAverageMode.Off, false, "MovingMic", "Unknown spatial average mode 'movingmic'")]
    [InlineData("MovingMic", true, VirtualCrossoverSpatialAverageMode.MovingMic, true, "MovingMic", "No change.")]
    public void Review_HoldsASpatialAverageRequestToWhatTheSessionCarries(
        string mode,
        bool hybrid,
        VirtualCrossoverSpatialAverageMode current,
        bool hybridTicked,
        string capture,
        string words)
    {
        AgentProposal proposal = Proposal(new UseSpatialAverageOperation("op-1", "", mode, hybrid));

        AgentOperationVerdict verdict = AgentProposalValidator.Review(
            proposal,
            Session(spatialAverageMode: current, hybridTicked: hybridTicked, captures: [capture]))
            .Verdicts[0];

        Assert.Equal(AgentVerdictStatus.Rejected, verdict.Status);
        Assert.Contains(words, verdict.Message);
    }

    [Fact]
    public void Review_AcceptsTheHybridTickAloneWhenTheModeIsAlreadyTheOneAsked()
    {
        AgentProposal proposal = Proposal(
            new UseSpatialAverageOperation("op-1", "", "MovingMic", true));

        AgentOperationVerdict verdict = AgentProposalValidator.Review(
            proposal,
            Session(
                spatialAverageMode: VirtualCrossoverSpatialAverageMode.MovingMic,
                hybridTicked: false,
                captures: ["MovingMic"]))
            .Verdicts[0];

        Assert.True(verdict.Applicable);
        Assert.Equal("MovingMic, hybrid off", verdict.Current);
        Assert.Equal("MovingMic, hybrid on", verdict.Proposed);
    }

    [Fact]
    public void Review_RefusesAHandWrittenValueTheRequestedEngineWouldWriteOver()
    {
        AgentProposal proposal = Proposal(
            new RunAutoCrossoverOperation("op-1", "let the wizard split them"),
            new SetCrossoverOperation("op-2", "B:left", "",
                Crossover("BandPass", Edge("LinkwitzRiley", 250, 24), Edge("LinkwitzRiley", 2800, 24)),
                Crossover("BandPass", Edge("LinkwitzRiley", 250, 24), Edge("LinkwitzRiley", 2600, 24))),
            new SetGainOperation("op-3", "A:right", "", -2.0, -2.6),
            new SetDelayOperation("op-4", "A:right", "", 1.42, 1.37));

        AgentProposalReview review = AgentProposalValidator.Review(proposal, Session());

        Assert.True(review.Verdicts[0].Applicable);
        Assert.Equal(AgentVerdictStatus.Rejected, review.Verdicts[1].Status);
        Assert.Equal("Would be overwritten by Auto crossover (op-1).", review.Verdicts[1].Message);
        Assert.Equal(AgentVerdictStatus.Rejected, review.Verdicts[2].Status);
        // The wizard leaves delay and polarity alone; that is Auto delay's work.
        Assert.True(review.Verdicts[3].Applicable);
    }

    [Fact]
    public void Review_RejectsTheBankAnAutoTuneOnTheSameChannelWouldReplace()
    {
        AgentSessionSnapshot session = Session();
        AgentChannelSnapshot bLeft = session.Find("B:left")!;
        string hash = AgentPeqHash.Compute(bLeft.Settings.PeqPreampDb, bLeft.Settings.PeqBands);
        AgentProposal proposal = Proposal(
            new AutoTunePeqOperation("op-1", "B:left", "", null, null, null, null, null, null),
            new ReplacePeqBankOperation("op-2", "B:left", "", hash,
                new AgentPeqBank(0, [new AgentPeqBand("Peaking", 820, 2.1, -2.4)])));

        AgentProposalReview review = AgentProposalValidator.Review(proposal, session);

        Assert.True(review.Verdicts[0].Applicable);
        Assert.Equal(AgentVerdictStatus.Rejected, review.Verdicts[1].Status);
        Assert.Equal("Would be overwritten by Auto-tune (op-1).", review.Verdicts[1].Message);
    }

    [Fact]
    public void Review_RefusesAnAutoTuneForTheSideNotOnScreen()
    {
        // The fit is built on the handoff the PEQ menu would build, and that is
        // the side on screen's: its gate pin, its anchor, its hybrid datum.
        AgentProposal proposal = Proposal(
            new AutoTunePeqOperation("op-1", "B:left", "", null, null, null, null, null, null),
            new AutoTunePeqOperation("op-2", "A:right", "", null, null, null, null, null, null));

        AgentProposalReview left = AgentProposalValidator.Review(proposal, Session());
        Assert.True(left.Verdicts[0].Applicable);
        Assert.Equal(AgentVerdictStatus.Rejected, left.Verdicts[1].Status);
        Assert.Contains("side not on screen", left.Verdicts[1].Message);

        AgentProposalReview right = AgentProposalValidator.Review(
            proposal, Session() with { ActiveSideRight = true });
        Assert.Equal(AgentVerdictStatus.Rejected, right.Verdicts[0].Status);
        Assert.True(right.Verdicts[1].Applicable);
    }

    [Fact]
    public void Review_TakesAnEmptyBank_AsTheWayToClearAChannelsPeq()
    {
        AgentSessionSnapshot session = Session();
        AgentChannelSnapshot bLeft = session.Find("B:left")!;
        string hash = AgentPeqHash.Compute(bLeft.Settings.PeqPreampDb, bLeft.Settings.PeqBands);
        AgentProposal proposal = Proposal(
            new ReplacePeqBankOperation("op-1", "B:left", "", hash, new AgentPeqBank(0, [])));

        AgentOperationVerdict verdict = Assert.Single(
            AgentProposalValidator.Review(proposal, session).Verdicts);

        Assert.True(verdict.Applicable);
        Assert.Equal("0 bands, preamp 0.0 dB", verdict.Proposed);
    }

    [Fact]
    public void Review_RefusesAutoTunesThatDisagreeAboutTheProjectsTargetLevel()
    {
        // The target level is one datum for the project: two fits at two levels
        // would leave the first bank tuned against a level the project no
        // longer holds. The first stated level stands; a request that states
        // none, or the same, is fine.
        AgentProposal proposal = Proposal(
            new AutoTunePeqOperation("op-1", "B:left", "", -6, null, null, null, null, null),
            new AutoTunePeqOperation("op-2", "A:left", "", -8, null, null, null, null, null),
            new AutoTunePeqOperation("op-3", "C:mono", "", -6, null, null, null, null, null),
            new AutoTunePeqOperation("op-4", "A:left", "", null, null, null, null, null, null));

        AgentProposalReview review = AgentProposalValidator.Review(proposal, Session());

        Assert.True(review.Verdicts[0].Applicable);
        Assert.Equal(AgentVerdictStatus.Rejected, review.Verdicts[1].Status);
        Assert.Contains("op-1 already states -6.0 dB", review.Verdicts[1].Message);
        Assert.True(review.Verdicts[2].Applicable);
        // op-4 repeats op-2's channel: refused as a repeat, not for its level.
        Assert.Equal(AgentVerdictStatus.Rejected, review.Verdicts[3].Status);
        Assert.Contains("Already requested by op-2", review.Verdicts[3].Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Overwrites_ReadsWhatEachEngineActuallyWrites(bool projectAdjustsGains)
    {
        AgentSessionSnapshot session = Session(adjustGains: projectAdjustsGains);
        var autoDelay = new RunAutoDelayOperation("op-1", "", null, null, null, null, null);
        var autoCrossover = new RunAutoCrossoverOperation("op-2", "");
        var autoTune = new AutoTunePeqOperation("op-3", "B:left", "", null, null, null, null, null, null);
        var gain = new SetGainOperation("op-4", "A:right", "", -2.0, -2.6);
        var delay = new SetDelayOperation("op-5", "A:right", "", 1.42, 1.37);
        var polarity = new SetPolarityOperation("op-6", "B:left", "", false, true);
        var crossover = new SetCrossoverOperation("op-7", "B:left", "",
            Crossover("BandPass", Edge("LinkwitzRiley", 250, 24), Edge("LinkwitzRiley", 2800, 24)),
            Crossover("BandPass", Edge("LinkwitzRiley", 250, 24), Edge("LinkwitzRiley", 2600, 24)));
        var bank = new ReplacePeqBankOperation("op-8", "B:left", "", "hash",
            new AgentPeqBank(0, [new AgentPeqBand("Peaking", 820, 2.1, -2.4)]));
        var otherBank = new ReplacePeqBankOperation("op-9", "A:right", "", "hash",
            new AgentPeqBank(0, [new AgentPeqBand("Peaking", 820, 2.1, -2.4)]));

        Assert.True(AgentProposalValidator.Overwrites(autoDelay, delay, session));
        Assert.True(AgentProposalValidator.Overwrites(autoDelay, polarity, session));
        Assert.False(AgentProposalValidator.Overwrites(autoDelay, crossover, session));
        // The gain balance is an opt-in, and a run that does not ask for it
        // leaves a hand-written trim standing.
        Assert.Equal(projectAdjustsGains, AgentProposalValidator.Overwrites(autoDelay, gain, session));
        Assert.True(AgentProposalValidator.Overwrites(
            autoDelay with { AdjustGains = true }, gain, session));
        Assert.False(AgentProposalValidator.Overwrites(
            autoDelay with { AdjustGains = false }, gain, session));

        Assert.True(AgentProposalValidator.Overwrites(autoCrossover, crossover, session));
        Assert.True(AgentProposalValidator.Overwrites(autoCrossover, gain, session));
        Assert.False(AgentProposalValidator.Overwrites(autoCrossover, delay, session));

        // Auto-tune reaches one channel's bank, and only that one.
        Assert.True(AgentProposalValidator.Overwrites(autoTune, bank, session));
        Assert.False(AgentProposalValidator.Overwrites(autoTune, otherBank, session));
        Assert.False(AgentProposalValidator.Overwrites(autoTune, crossover, session));
    }

    [Fact]
    public void Review_ReadsTheFinalStateOffTheRowsThatSurvive_NotTheOnesItRefused()
    {
        AgentSessionSnapshot session = Session();
        AgentChannelSnapshot bLeft = session.Find("B:left")!;
        // B left runs LR24 250-2800. The bell at 1350 Hz sits in the junction zone
        // of the PROPOSED 2600 Hz corner (an octave below it) and outside the zone
        // of the 2800 Hz corner the channel actually keeps.
        AgentProposal proposal = Proposal(
            new RunAutoCrossoverOperation("op-1", "let the wizard split them"),
            new SetCrossoverOperation("op-2", "B:left", "",
                Crossover("BandPass", Edge("LinkwitzRiley", 250, 24), Edge("LinkwitzRiley", 2800, 24)),
                Crossover("BandPass", Edge("LinkwitzRiley", 250, 24), Edge("LinkwitzRiley", 2600, 24))),
            new ReplacePeqBankOperation("op-3", "B:left", "door",
                AgentPeqHash.Compute(bLeft.Settings.PeqPreampDb, bLeft.Settings.PeqBands),
                new AgentPeqBank(0, [new AgentPeqBand("Peaking", 1350, 3, -3)])));

        AgentProposalReview review = AgentProposalValidator.Review(proposal, session);

        // The crossover row is refused, so it moves no corner — and the bank must
        // not be judged against a corner that is not going to be there.
        Assert.Equal(AgentVerdictStatus.Rejected, review.Verdicts[1].Status);
        Assert.True(review.Verdicts[2].Applicable);
        Assert.DoesNotContain("junction zone", review.Verdicts[2].Message);
        Assert.Equal(AgentProposalValidator.DeviceLimitsUnknown, review.Verdicts[2].Message);
    }

    [Fact]
    public void Review_RefusesASecondRequestForTheSameEngine_KeepingTheFirst()
    {
        AgentProposal proposal = Proposal(
            new RunAutoCrossoverOperation("op-1", "split them"),
            new RunAutoCrossoverOperation("op-2", "split them again"));

        AgentProposalReview review = AgentProposalValidator.Review(proposal, Session());

        Assert.True(review.Verdicts[0].Applicable);
        Assert.Equal(AgentVerdictStatus.Rejected, review.Verdicts[1].Status);
        Assert.Contains("Already requested by op-1", review.Verdicts[1].Message);
    }

    [Fact]
    public void EngineInputLimits_MatchTheFieldsTheyWouldBeTypedInto()
    {
        // Same reason as the gain and delay pin above: the validator restates
        // these ranges rather than reading them off a control it cannot see.
        StaTest.Run(() =>
        {
            using var dialog = new VirtualCrossoverAutoDelayDialog();
            AssertField(dialog, "numericSceneOffset",
                AgentProposalValidator.MinimumSceneOffsetMs,
                AgentProposalValidator.MaximumSceneOffsetMs,
                AgentProposalValidator.SceneOffsetStepMs);
            AssertField(dialog, "numericNearSideCut",
                AgentProposalValidator.MinimumNearSideCutDb,
                AgentProposalValidator.MaximumNearSideCutDb,
                AgentProposalValidator.NearSideCutStepDb);
            AssertField(dialog, "numericRearFill",
                AgentProposalValidator.MinimumRearFillOffsetMs,
                AgentProposalValidator.MaximumRearFillOffsetMs,
                AgentProposalValidator.RearFillOffsetStepMs);

            using var panel = new VirtualCrossoverPanel();
            AssertField(panel, "numericTargetLevel",
                AgentProposalValidator.MinimumTargetLevelDb,
                AgentProposalValidator.MaximumTargetLevelDb,
                AgentProposalValidator.TargetLevelStepDb);
        });
    }

    // A field's step is its decimal places: the arrows' Increment is a
    // convenience, the places are what a typed value is rounded to.
    private static void AssertField(
        Control owner, string name, double minimum, double maximum, double step)
    {
        var field = (DarkNumericUpDown)owner.GetType()
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(owner)!;
        Assert.Equal((decimal)minimum, field.Minimum);
        Assert.Equal((decimal)maximum, field.Maximum);
        Assert.Equal(step, Math.Pow(10, -field.DecimalPlaces), 12);
    }

    private static AgentSessionSnapshot Session(
        int processorRateHz = 96_000,
        double maxDelayMs = 50,
        string? lastPackageId = Package,
        VirtualCrossoverSpatialAverageMode spatialAverageMode = VirtualCrossoverSpatialAverageMode.Off,
        bool hybridTicked = false,
        bool adjustGains = false,
        IReadOnlyList<string>? captures = null)
    {
        var aLeft = new VirtualCrossoverChannelSettings { GainDb = 0, DelayMs = 0 };
        var aRight = new VirtualCrossoverChannelSettings { GainDb = -2.0, DelayMs = 1.42 };
        var bLeft = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.BandPass,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 250, 24),
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2800, 24),
            PeqBands = [new PeqBand(820, 2.1, -2.4), new PeqBand(3000, 1.0, 1.5)]
        };
        var bRight = new VirtualCrossoverChannelSettings();
        var cMono = new VirtualCrossoverChannelSettings { CrossoverKind = CrossoverKind.LowPass };
        var dMono = new VirtualCrossoverChannelSettings();
        return new AgentSessionSnapshot(
            [
                new AgentChannelSnapshot("A", AgentChannelSide.Left, aLeft, true, captures ?? []),
                new AgentChannelSnapshot("A", AgentChannelSide.Right, aRight, true, captures ?? []),
                new AgentChannelSnapshot("B", AgentChannelSide.Left, bLeft, true, captures ?? []),
                new AgentChannelSnapshot("B", AgentChannelSide.Right, bRight, true, captures ?? []),
                new AgentChannelSnapshot("C", AgentChannelSide.Mono, cMono, true, captures ?? []),
                // The block a source was never resolved for: an engine asked to
                // fit it has nothing to read.
                new AgentChannelSnapshot("D", AgentChannelSide.Mono, dMono, false, [])
            ],
            processorRateHz,
            maxDelayMs,
            lastPackageId,
            new AgentAutoDelaySettings(0.25, RightHandDrive: false, adjustGains, 1.0, 15.0),
            spatialAverageMode,
            hybridTicked);
    }

    private static AgentProposal Proposal(params AgentOperation[] operations) =>
        new(Package, "summary", [], [], operations, []);

    private static AgentCrossover Crossover(string kind, AgentCrossoverEdge? highPass, AgentCrossoverEdge? lowPass) =>
        new(kind, highPass, lowPass);

    private static AgentCrossoverEdge Edge(string family, double frequencyHz, int slope, double? ripple = null) =>
        new(family, frequencyHz, slope, ripple);
}
