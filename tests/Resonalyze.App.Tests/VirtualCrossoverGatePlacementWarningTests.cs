using System;
using System.Collections.Generic;
using System.Globalization;

namespace Resonalyze.App.Tests;

/// <summary>
/// Pins when Virtual DSP reports the window it gates the shown side at as
/// failing its channels, and what it says about it. A gate offset is an
/// ABSOLUTE time, so it outlives the measurements it was fitted on: the Passat
/// session inherited 15.06 ms from another car's project and drew every
/// driver's reverberant tail as its response — at full plausibility, because a
/// tail has a magnitude too, which is why this has to be reported rather than
/// looked at.
/// <para>
/// Every figure below is what the panel itself computes on that session: the
/// estimated start of each PROCESSED channel and
/// <see cref="Resonalyze.Dsp.DataHelper.GateLeadingEdgeLossDb"/> at the
/// placement in use against the same gate on that channel's own arrival
/// (5/50/20 ms, the gate the session was saved with).
/// </para>
/// </summary>
public sealed class VirtualCrossoverGatePlacementWarningTests
{
    [Fact]
    public void AGateInheritedFromAnotherSessionReadsAsOpeningLate()
    {
        // The right side, pinned at 15.06 ms: the midbass, midrange and tweeter
        // all arrive between 4.1 and 6.0 ms, and the window is 43 to 70 dB worse
        // there than on their own arrivals.
        Assert.Equal(
            VirtualCrossoverPanel.GateCutKind.OpensAfterArrival,
            VirtualCrossoverPanel.JudgeGateCut(
                startMs: 5.967, gateOffsetMs: 15.06, plateauMs: 50.0,
                placementLossDb: 1.0, ownArrivalLossDb: -42.9));
        Assert.Equal(
            VirtualCrossoverPanel.GateCutKind.OpensAfterArrival,
            VirtualCrossoverPanel.JudgeGateCut(
                startMs: 4.101, gateOffsetMs: 15.06, plateauMs: 50.0,
                placementLossDb: 9.9, ownArrivalLossDb: -53.7));
        Assert.Equal(
            VirtualCrossoverPanel.GateCutKind.OpensAfterArrival,
            VirtualCrossoverPanel.JudgeGateCut(
                startMs: 4.119, gateOffsetMs: 15.06, plateauMs: 50.0,
                placementLossDb: 15.2, ownArrivalLossDb: -55.0));
        // Not the subwoofer of that same side: its own 65 Hz low-pass delays it
        // to 10.4 ms and smears its front, so the same window discards only
        // -21.3 dB of it. Over the ceiling is the test, not "arrives earlier".
        Assert.Null(VirtualCrossoverPanel.JudgeGateCut(
            startMs: 10.388, gateOffsetMs: 15.06, plateauMs: 50.0,
            placementLossDb: -21.3, ownArrivalLossDb: -34.9));
    }

    [Fact]
    public void TheSameSessionOnTheAutoAnchorIsNotFlagged()
    {
        // The left side of that session, which was never pinned: the shared
        // window sits on the earliest processed peak (2.97 ms). Two channels
        // start before it — the anchor is a PEAK and their fronts precede it —
        // and one of them is measurably worse there than on its own arrival,
        // which is exactly why the ceiling is the other half of the test.
        Assert.Null(VirtualCrossoverPanel.JudgeGateCut(
            startMs: 2.907, gateOffsetMs: 2.969, plateauMs: 50.0,
            placementLossDb: -67.4, ownArrivalLossDb: -68.4));
        Assert.Null(VirtualCrossoverPanel.JudgeGateCut(
            startMs: 2.924, gateOffsetMs: 2.969, plateauMs: 50.0,
            placementLossDb: -50.8, ownArrivalLossDb: -63.3));
        Assert.Null(VirtualCrossoverPanel.JudgeGateCut(
            startMs: 4.739, gateOffsetMs: 2.969, plateauMs: 50.0,
            placementLossDb: -65.7, ownArrivalLossDb: -50.7));
    }

    [Fact]
    public void AChannelInsideThePlateauIsNotFlaggedForArrivingLate()
    {
        // Alignment delays move a driver AFTER the window opens. As long as the
        // plateau still covers it, nothing of it lies ahead of the window and
        // nothing is missing behind it — that is the placement working.
        Assert.Null(VirtualCrossoverPanel.JudgeGateCut(
            startMs: 20.0, gateOffsetMs: 4.125, plateauMs: 50.0,
            placementLossDb: -60.0, ownArrivalLossDb: -30.0));
    }

    [Fact]
    public void AChannelBeyondThePlateauReadsAsAWindowThatClosedTooEarly()
    {
        // The same session with the DEFAULT gate (0.5/4/1.5 ms) and its tweeter
        // delayed 20 ms: the Auto window's plateau is over at 11.11 ms and the
        // driver lands at 24.12 ms, so its curve holds none of it. The
        // leading-edge figure not only fails to say so — it reads -282.0 dB
        // there, the BEST placement figure in that session, because a channel
        // past the window has nothing ahead of the plateau to lose either. That
        // is why this side is decided on the window's geometry.
        Assert.Equal(
            VirtualCrossoverPanel.GateCutKind.ClosesBeforeArrival,
            VirtualCrossoverPanel.JudgeGateCut(
                startMs: 24.119, gateOffsetMs: 7.115, plateauMs: 4.0,
                placementLossDb: -282.0, ownArrivalLossDb: -55.0));
        Assert.Equal(
            VirtualCrossoverPanel.GateCutKind.ClosesBeforeArrival,
            VirtualCrossoverPanel.JudgeGateCut(
                startMs: 20.0, gateOffsetMs: 4.125, plateauMs: 4.0,
                placementLossDb: -60.0, ownArrivalLossDb: -30.0));
        // The boundary belongs to the plateau: a front on its last sample is
        // still inside the window.
        Assert.Null(VirtualCrossoverPanel.JudgeGateCut(
            startMs: 8.125, gateOffsetMs: 4.125, plateauMs: 4.0,
            placementLossDb: -60.0, ownArrivalLossDb: -30.0));
    }

    [Fact]
    public void AGateTooShortForTheChannelIsNotReportedAsAMisplacement()
    {
        // The v5 session's default gate cannot hold one period of a 55 Hz
        // subwoofer wherever it sits: -19.4 dB at the shared window and
        // -19.4 dB at the channel's own arrival. Over the ceiling, but no
        // placement fixes it — reporting it would stop the automatic commands
        // on something moving the gate cannot cure.
        Assert.Null(VirtualCrossoverPanel.JudgeGateCut(
            startMs: 3.0, gateOffsetMs: 4.0, plateauMs: 4.0,
            placementLossDb: -19.4, ownArrivalLossDb: -19.4));
        // And two nearby offsets never read bit-identically on a real response,
        // so the margin — not a bare comparison — is what keeps that case out:
        // a hair's difference, and a difference no user can act on, both stay
        // below it.
        Assert.Null(VirtualCrossoverPanel.JudgeGateCut(
            startMs: 3.0, gateOffsetMs: 4.0, plateauMs: 4.0,
            placementLossDb: -19.399, ownArrivalLossDb: -19.4));
        Assert.Null(VirtualCrossoverPanel.JudgeGateCut(
            startMs: 3.0, gateOffsetMs: 4.0, plateauMs: 4.0,
            placementLossDb: -17.0, ownArrivalLossDb: -19.4));
        // Worse by the margin is a placement problem again.
        Assert.Equal(
            VirtualCrossoverPanel.GateCutKind.OpensAfterArrival,
            VirtualCrossoverPanel.JudgeGateCut(
                startMs: 3.0, gateOffsetMs: 4.0, plateauMs: 4.0,
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
            // The arrivals as a range, so the line stays one line however many
            // channels the side holds.
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
                Pinned: false,
                RightSide: false,
                Cut: new List<VirtualCrossoverPanel.GateCutChannel>
                {
                    new("D", 19.108, VirtualCrossoverPanel.GateCutKind.ClosesBeforeArrival, -2.2)
                });

            string warning = VirtualCrossoverPanel.FormatGateCutWarning(verdict);
            // Singular, and the right failure: the curve holds none of the
            // channel rather than holding the wrong part of it.
            Assert.Contains("is over before D arrives (19.11 ms)", warning);
            Assert.Contains("that curve holds none of it", warning);

            string detail = VirtualCrossoverPanel.FormatGateCutDetail(verdict);
            Assert.Contains("plateau runs from 4.13 to 8.13 ms", detail);
            Assert.Contains("the window is over before it starts", detail);
            // A longer window is the fix here; the leading-edge figure has
            // nothing to say about this one, so it is not quoted.
            Assert.Contains("until it covers 19.11 ms", detail);
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
            // Every channel it cuts, with what the window costs that channel.
            Assert.Contains("C — arrives 4.10 ms, ahead of the plateau", detail);
            Assert.Contains("leading-edge loss +9.9 dB", detail);
            Assert.Contains("leading-edge loss +15.2 dB", detail);
            // The other side keeps its own placement, and only the shown side
            // was judged, so the reader is sent to check it.
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

    // The verdict the panel produced on the Passat session's right side.
    private static VirtualCrossoverPanel.GatePlacementVerdict PassatVerdict(bool pinned) =>
        new(
            OffsetMs: 15.06,
            PlateauMs: 50.0,
            Pinned: pinned,
            RightSide: true,
            Cut: new List<VirtualCrossoverPanel.GateCutChannel>
            {
                new("B", 5.967, VirtualCrossoverPanel.GateCutKind.OpensAfterArrival, 1.0),
                new("C", 4.101, VirtualCrossoverPanel.GateCutKind.OpensAfterArrival, 9.9),
                new("D", 4.119, VirtualCrossoverPanel.GateCutKind.OpensAfterArrival, 15.2)
            });
}
