namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverSourceRulesTests
{
    [Fact]
    public void NoTransferIr_IsRejected()
    {
        Assert.Equal(
            VirtualCrossoverSourceRules.Decision.Reject,
            VirtualCrossoverSourceRules.Evaluate(
                hasTransferIr: false,
                candidateSampleRate: 48_000,
                otherResolvedSampleRates: [48_000]));
    }

    [Fact]
    public void FirstSource_WithNoOtherChannels_IsAccepted()
    {
        Assert.Equal(
            VirtualCrossoverSourceRules.Decision.Accept,
            VirtualCrossoverSourceRules.Evaluate(
                hasTransferIr: true,
                candidateSampleRate: 48_000,
                otherResolvedSampleRates: []));
    }

    [Fact]
    public void MatchingSampleRates_AreAccepted()
    {
        Assert.Equal(
            VirtualCrossoverSourceRules.Decision.Accept,
            VirtualCrossoverSourceRules.Evaluate(
                hasTransferIr: true,
                candidateSampleRate: 48_000,
                otherResolvedSampleRates: [48_000, 48_000]));
    }

    [Fact]
    public void AMismatchedSampleRate_IsRejected()
    {
        Assert.Equal(
            VirtualCrossoverSourceRules.Decision.RejectSampleRateMismatch,
            VirtualCrossoverSourceRules.Evaluate(
                hasTransferIr: true,
                candidateSampleRate: 48_000,
                otherResolvedSampleRates: [48_000, 44_100]));
    }

    [Fact]
    public void NoTransferIr_TakesPriorityOverAMismatch()
    {
        // The missing-IR reject wins even when a rate mismatch also exists.
        Assert.Equal(
            VirtualCrossoverSourceRules.Decision.Reject,
            VirtualCrossoverSourceRules.Evaluate(
                hasTransferIr: false,
                candidateSampleRate: 48_000,
                otherResolvedSampleRates: [44_100]));
    }
}
