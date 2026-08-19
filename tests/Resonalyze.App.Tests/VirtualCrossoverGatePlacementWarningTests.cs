using System;
using System.Collections.Generic;
using System.Globalization;

namespace Resonalyze.App.Tests;

/// <summary>
/// Pins when Virtual DSP reports the window it gates the shown side at as
/// misplaced, and what it says about it. A gate offset is an ABSOLUTE time, so
/// it outlives the measurements it was fitted on: the Passat session inherited
/// 15.06 ms from another car's project and drew every driver's reverberant tail
/// as its response — at full plausibility, because a tail has a magnitude too,
/// which is why this has to be reported rather than looked at.
/// <para>
/// Every figure below is what the panel itself computes on that session: the
/// estimated start of each PROCESSED channel, and
/// <see cref="Resonalyze.Dsp.DataHelper.GateLeadingEdgeLossDb"/> at the
/// placement in use against the same gate on that channel's own arrival
/// (5/50/20 ms, the gate the session was saved with).
/// </para>
/// </summary>
public sealed class VirtualCrossoverGatePlacementWarningTests
{
    [Fact]
    public void AGateInheritedFromAnotherSessionReadsAsCutting()
    {
        // The right side, pinned at 15.06 ms: midbass, midrange and tweeter all
        // arrive between 4.1 and 6.0 ms, and the window is 43 to 70 dB worse
        // there than on their own arrivals.
        Assert.True(VirtualCrossoverPanel.GateCutsChannel(
            startMs: 5.967, gateOffsetMs: 15.06, placementLossDb: 1.0, ownArrivalLossDb: -42.9));
        Assert.True(VirtualCrossoverPanel.GateCutsChannel(
            startMs: 4.101, gateOffsetMs: 15.06, placementLossDb: 9.9, ownArrivalLossDb: -53.7));
        Assert.True(VirtualCrossoverPanel.GateCutsChannel(
            startMs: 4.119, gateOffsetMs: 15.06, placementLossDb: 15.2, ownArrivalLossDb: -55.0));
        // Not the subwoofer of that same side: its own 65 Hz low-pass delays it
        // to 10.4 ms and smears its front, so the same window discards only
        // -21.3 dB of it. Over the ceiling is the test, not "arrives earlier".
        Assert.False(VirtualCrossoverPanel.GateCutsChannel(
            startMs: 10.388, gateOffsetMs: 15.06, placementLossDb: -21.3, ownArrivalLossDb: -34.9));
    }

    [Fact]
    public void TheSameSessionOnTheAutoAnchorIsNotFlagged()
    {
        // The left side of that session, which was never pinned: the shared
        // window sits on the earliest processed peak (2.97 ms). Two channels
        // start before it — the anchor is a PEAK and their fronts precede it —
        // and one of them is measurably worse there than on its own arrival,
        // which is exactly why the ceiling is the other half of the test.
        Assert.False(VirtualCrossoverPanel.GateCutsChannel(
            startMs: 2.907, gateOffsetMs: 2.969, placementLossDb: -67.4, ownArrivalLossDb: -68.4));
        Assert.False(VirtualCrossoverPanel.GateCutsChannel(
            startMs: 2.924, gateOffsetMs: 2.969, placementLossDb: -50.8, ownArrivalLossDb: -63.3));
        Assert.False(VirtualCrossoverPanel.GateCutsChannel(
            startMs: 4.739, gateOffsetMs: 2.969, placementLossDb: -65.7, ownArrivalLossDb: -50.7));
    }

    [Fact]
    public void AChannelDelayedPastTheGateIsNotFlagged()
    {
        // Alignment delays move a driver AFTER the window opens. Nothing of it
        // lies ahead of the plateau then, which is the placement working, not
        // failing — flagging it would fire on every aligned setup.
        Assert.False(VirtualCrossoverPanel.GateCutsChannel(
            startMs: 20.0, gateOffsetMs: 4.125, placementLossDb: -60.0, ownArrivalLossDb: -30.0));
    }

    [Fact]
    public void AGateTooShortForTheChannelIsNotReportedAsAMisplacement()
    {
        // The v5 session's default gate (0.5/4/1.5 ms) cannot hold one period
        // of a 55 Hz subwoofer wherever it sits: -19.4 dB at the shared window
        // and -19.4 dB at the channel's own arrival. Over the ceiling, but no
        // placement fixes it — reporting it would stop the automatic commands
        // on something moving the gate cannot cure.
        Assert.False(VirtualCrossoverPanel.GateCutsChannel(
            startMs: 3.0, gateOffsetMs: 4.0, placementLossDb: -19.4, ownArrivalLossDb: -19.4));
        // Measurably worse than its own arrival is a placement problem again.
        Assert.True(VirtualCrossoverPanel.GateCutsChannel(
            startMs: 3.0, gateOffsetMs: 4.0, placementLossDb: -12.7, ownArrivalLossDb: -19.4));
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
        });
    }

    [Fact]
    public void APinnedGateIsToldToGoBackToAuto()
    {
        RunWithInvariantCulture(() =>
        {
            string detail = VirtualCrossoverPanel.FormatGateCutDetail(PassatVerdict(pinned: true));

            Assert.Contains("plateau starts at 15.06 ms", detail);
            Assert.Contains("press Auto", detail);
            // Where Auto would put it: the figure the user is being asked to accept.
            Assert.Contains("4.10 ms", detail);
            // Every channel it cuts, with what the window costs that channel.
            Assert.Contains("C — arrives 4.10 ms, leading-edge loss +9.9 dB", detail);
            Assert.Contains("D — arrives 4.12 ms, leading-edge loss +15.2 dB", detail);
            // The other side keeps its own placement, and only the shown side
            // was judged, so the reader is sent to check it.
            Assert.Contains("says nothing about L", detail);
        });
    }

    [Fact]
    public void AnAutoGateThatStillCutsIsToldToWidenTheShoulderInstead()
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
                Pinned: true,
                RightSide: true,
                AutoOffsetMs: 4.101,
                Cut: new List<VirtualCrossoverPanel.GateCutChannel>
                {
                    new("D", 4.119, double.PositiveInfinity)
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
            Pinned: pinned,
            RightSide: true,
            AutoOffsetMs: 4.101,
            Cut: new List<VirtualCrossoverPanel.GateCutChannel>
            {
                new("B", 5.967, 1.0),
                new("C", 4.101, 9.9),
                new("D", 4.119, 15.2)
            });
}
