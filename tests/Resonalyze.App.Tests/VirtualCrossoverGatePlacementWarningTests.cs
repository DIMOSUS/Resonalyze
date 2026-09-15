using System;
using System.Collections.Generic;
using System.Globalization;

namespace Resonalyze.App.Tests;

/// <summary>Figures are from the Passat session, which inherited an absolute 15.06 ms gate from another car (5/50/20 ms gate).</summary>
public sealed class VirtualCrossoverGatePlacementWarningTests
{
    [Fact]
    public void AGateInheritedFromAnotherSessionReadsAsOpeningLate()
    {
        Assert.Equal(
            VirtualCrossoverPanel.GateCutKind.OpensAfterArrival,
            VirtualCrossoverPanel.JudgeGateCut(
                startMs: 5.967, gateOffsetMs: 15.06, plateauMs: 50.0, rightMs: 20.0,
                placementLossDb: 1.0, ownArrivalLossDb: -42.9));
        Assert.Equal(
            VirtualCrossoverPanel.GateCutKind.OpensAfterArrival,
            VirtualCrossoverPanel.JudgeGateCut(
                startMs: 4.101, gateOffsetMs: 15.06, plateauMs: 50.0, rightMs: 20.0,
                placementLossDb: 9.9, ownArrivalLossDb: -53.7));
        Assert.Equal(
            VirtualCrossoverPanel.GateCutKind.OpensAfterArrival,
            VirtualCrossoverPanel.JudgeGateCut(
                startMs: 4.119, gateOffsetMs: 15.06, plateauMs: 50.0, rightMs: 20.0,
                placementLossDb: 15.2, ownArrivalLossDb: -55.0));
        // The sub's own 65 Hz low-pass delays and smears it: only -21.3 dB lost. Over the ceiling is the test.
        Assert.Null(VirtualCrossoverPanel.JudgeGateCut(
            startMs: 10.388, gateOffsetMs: 15.06, plateauMs: 50.0, rightMs: 20.0,
            placementLossDb: -21.3, ownArrivalLossDb: -34.9));
    }

    [Fact]
    public void AWindowOpeningJustAfterTheEarliestFrontIsNotFlagged()
    {
        // Late, but not late ENOUGH to lose the response: the ceiling is the other half of the test.
        Assert.Null(VirtualCrossoverPanel.JudgeGateCut(
            startMs: 2.907, gateOffsetMs: 2.969, plateauMs: 50.0, rightMs: 20.0,
            placementLossDb: -67.4, ownArrivalLossDb: -68.4));
        Assert.Null(VirtualCrossoverPanel.JudgeGateCut(
            startMs: 2.924, gateOffsetMs: 2.969, plateauMs: 50.0, rightMs: 20.0,
            placementLossDb: -50.8, ownArrivalLossDb: -63.3));
        Assert.Null(VirtualCrossoverPanel.JudgeGateCut(
            startMs: 4.739, gateOffsetMs: 2.969, plateauMs: 50.0, rightMs: 20.0,
            placementLossDb: -65.7, ownArrivalLossDb: -50.7));
    }

    [Fact]
    public void AChannelInsideThePlateauIsNotFlaggedForArrivingLate()
    {
        Assert.Null(VirtualCrossoverPanel.JudgeGateCut(
            startMs: 20.0, gateOffsetMs: 4.125, plateauMs: 50.0, rightMs: 20.0,
            placementLossDb: -60.0, ownArrivalLossDb: -30.0));
    }

    [Fact]
    public void AChannelInsideTheFadeOutIsNotReportedAsMissing()
    {
        // The fade-out starts at unity, so a front just past the plateau is attenuated, not absent.
        Assert.Null(VirtualCrossoverPanel.JudgeGateCut(
            startMs: 9.0, gateOffsetMs: 4.0, plateauMs: 4.0, rightMs: 20.0,
            placementLossDb: -60.0, ownArrivalLossDb: -30.0));
        Assert.Null(VirtualCrossoverPanel.JudgeGateCut(
            startMs: 9.624, gateOffsetMs: 4.125, plateauMs: 4.0, rightMs: 1.5,
            placementLossDb: -60.0, ownArrivalLossDb: -30.0));
        Assert.Equal(
            VirtualCrossoverPanel.GateCutKind.ClosesBeforeArrival,
            VirtualCrossoverPanel.JudgeGateCut(
                startMs: 9.625, gateOffsetMs: 4.125, plateauMs: 4.0, rightMs: 1.5,
                placementLossDb: -60.0, ownArrivalLossDb: -30.0));
    }

    [Fact]
    public void AChannelBeyondTheWindowReadsAsAWindowThatClosedTooEarly()
    {
        // Past the window the leading-edge figure reads -282 dB (the best in the session), so this side is judged on geometry.
        Assert.Equal(
            VirtualCrossoverPanel.GateCutKind.ClosesBeforeArrival,
            VirtualCrossoverPanel.JudgeGateCut(
                startMs: 24.119, gateOffsetMs: 7.115, plateauMs: 4.0, rightMs: 1.5,
                placementLossDb: -282.0, ownArrivalLossDb: -55.0));
        Assert.Equal(
            VirtualCrossoverPanel.GateCutKind.ClosesBeforeArrival,
            VirtualCrossoverPanel.JudgeGateCut(
                startMs: 20.0, gateOffsetMs: 4.125, plateauMs: 4.0, rightMs: 1.5,
                placementLossDb: -60.0, ownArrivalLossDb: -30.0));
    }

    [Fact]
    public void AGateTooShortForTheChannelIsNotReportedAsAMisplacement()
    {
        // A 55 Hz period does not fit the default gate anywhere (-19.4 dB both ways): no placement fixes it.
        Assert.Null(VirtualCrossoverPanel.JudgeGateCut(
            startMs: 3.0, gateOffsetMs: 4.0, plateauMs: 4.0, rightMs: 1.5,
            placementLossDb: -19.4, ownArrivalLossDb: -19.4));
        // Nearby offsets never read bit-identically, so a margin, not a bare comparison, keeps this out.
        Assert.Null(VirtualCrossoverPanel.JudgeGateCut(
            startMs: 3.0, gateOffsetMs: 4.0, plateauMs: 4.0, rightMs: 1.5,
            placementLossDb: -19.399, ownArrivalLossDb: -19.4));
        Assert.Null(VirtualCrossoverPanel.JudgeGateCut(
            startMs: 3.0, gateOffsetMs: 4.0, plateauMs: 4.0, rightMs: 1.5,
            placementLossDb: -17.0, ownArrivalLossDb: -19.4));
        Assert.Equal(
            VirtualCrossoverPanel.GateCutKind.OpensAfterArrival,
            VirtualCrossoverPanel.JudgeGateCut(
                startMs: 3.0, gateOffsetMs: 4.0, plateauMs: 4.0, rightMs: 1.5,
                placementLossDb: -12.7, ownArrivalLossDb: -19.4));
    }

    [Fact]
    public void TheWarningNamesTheSideTheOffsetAndTheChannelsItCuts()
    {
        RunWithInvariantCulture(() =>
        {
            string warning = VirtualCrossoverPanel.FormatGateCutWarning(PassatVerdict(pinned: true));

            Assert.Contains("R gate at 15.06 ms", warning);
            Assert.Contains("B, C, D", warning);
            Assert.Contains("4.10–5.97 ms", warning);
            Assert.Contains("reverberant tail", warning);
        });
    }

    [Fact]
    public void AWindowThatClosedTooEarlySaysThatInsteadOfBlamingTheTail()
    {
        RunWithInvariantCulture(() =>
        {
            var verdict = new VirtualCrossoverPanel.GatePlacementVerdict(
                OffsetMs: 4.125,
                PlateauMs: 4.0,
                RightMs: 1.5,
                Pinned: false,
                RightSide: false,
                Cut: new List<VirtualCrossoverPanel.GateCutChannel>
                {
                    new("D", 19.108, VirtualCrossoverPanel.GateCutKind.ClosesBeforeArrival, -2.2)
                });

            string warning = VirtualCrossoverPanel.FormatGateCutWarning(verdict);
            Assert.Contains("is over before D arrives (19.11 ms)", warning);
            Assert.Contains("that curve holds none of it", warning);

            string detail = VirtualCrossoverPanel.FormatGateCutDetail(verdict);
            Assert.Contains("plateau runs from 4.13 to 8.13 ms", detail);
            Assert.Contains("fade-out over at 9.63 ms", detail);
            Assert.Contains("after the window closes at 9.63 ms", detail);
            Assert.Contains("raise the plateau in Gate… past 19.11 ms", detail);
            Assert.DoesNotContain("leading-edge loss", detail);
        });
    }

    [Fact]
    public void APinnedGateIsToldToGoBackToAuto()
    {
        RunWithInvariantCulture(() =>
        {
            string detail = VirtualCrossoverPanel.FormatGateCutDetail(PassatVerdict(pinned: true));

            Assert.Contains("plateau runs from 15.06 to 65.06 ms", detail);
            Assert.Contains("press Auto", detail);
            Assert.Contains("C — arrives 4.10 ms, ahead of the plateau", detail);
            Assert.Contains("leading-edge loss +9.9 dB", detail);
            Assert.Contains("leading-edge loss +15.2 dB", detail);
            Assert.Contains("says nothing about L", detail);
        });
    }

    [Fact]
    public void AnAutoGateThatStillOpensLateIsToldToWidenTheShoulderInstead()
    {
        RunWithInvariantCulture(() =>
        {
            string detail = VirtualCrossoverPanel.FormatGateCutDetail(PassatVerdict(pinned: false));

            Assert.DoesNotContain("press Auto", detail);
            Assert.Contains("widen the left fade", detail);
        });
    }

    [Fact]
    public void AWindowHoldingNoneOfAChannelSaysSoInsteadOfPrintingInfinity()
    {
        RunWithInvariantCulture(() =>
        {
            var verdict = new VirtualCrossoverPanel.GatePlacementVerdict(
                OffsetMs: 15.06,
                PlateauMs: 50.0,
                RightMs: 20.0,
                Pinned: true,
                RightSide: true,
                Cut: new List<VirtualCrossoverPanel.GateCutChannel>
                {
                    new("D", 4.119, VirtualCrossoverPanel.GateCutKind.OpensAfterArrival,
                        double.PositiveInfinity)
                });

            Assert.Contains(
                "the window holds none of it",
                VirtualCrossoverPanel.FormatGateCutDetail(verdict));
        });
    }

    private static void RunWithInvariantCulture(Action assertions)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            assertions();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static VirtualCrossoverPanel.GatePlacementVerdict PassatVerdict(bool pinned) =>
        new(
            OffsetMs: 15.06,
            PlateauMs: 50.0,
            RightMs: 20.0,
            Pinned: pinned,
            RightSide: true,
            Cut: new List<VirtualCrossoverPanel.GateCutChannel>
            {
                new("B", 5.967, VirtualCrossoverPanel.GateCutKind.OpensAfterArrival, 1.0),
                new("C", 4.101, VirtualCrossoverPanel.GateCutKind.OpensAfterArrival, 9.9),
                new("D", 4.119, VirtualCrossoverPanel.GateCutKind.OpensAfterArrival, 15.2)
            });
}
