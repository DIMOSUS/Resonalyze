using Resonalyze.Integration.AgentBridge;

namespace Resonalyze;

/// <summary>The rules an import's engine requests run by: their fixed order, the progress line each shows, and the inputs
/// the reply leaves to the dialogs' defaults. See docs/tech/agent-bridge.md#engine-order.</summary>
internal static class AgentEngineRequests
{
    public static int Order(AgentOperation operation) => operation switch
    {
        UseSpatialAverageOperation => 0,
        RunAutoCrossoverOperation => 1,
        // After the wizard, before Auto delay, which realigns whatever the crossover became.
        TuneJunctionOperation => 2,
        RunAutoDelayOperation => 3,
        _ => 4
    };

    public static string StepText(AgentOperation operation, AgentOperationVerdict verdict) =>
        operation switch
        {
            UseSpatialAverageOperation spatial => $"Spatial average: {spatial.Mode}…",
            RunAutoCrossoverOperation => "Auto crossover: the wizard is opening…",
            TuneJunctionOperation => $"Junction tune {verdict.ChannelLabel}: searching the crossover…",
            RunAutoDelayOperation => "Auto delay: searching delays and polarities…",
            AutoTunePeqOperation => $"Auto-tune {verdict.ChannelLabel}: fitting the bank…",
            _ => $"{operation.Parameter}…"
        };

    /// <summary>Auto delay's inputs: stated values, dialog defaults for the rest.</summary>
    public static AutoDelayRunRequest AutoDelayRequest(
        RunAutoDelayOperation operation, AgentAutoDelaySettings defaults) =>
        new(
            operation.SceneOffsetMs ?? defaults.SceneOffsetMs,
            operation.RightHandDrive ?? defaults.RightHandDrive,
            operation.AdjustGains ?? defaults.AdjustGains,
            operation.NearSideCutDb ?? defaults.NearSideCutDb,
            operation.RearFillOffsetMs ?? defaults.RearFillOffsetMs);

    /// <summary>The target level every Auto-tune of one import fits to: the first stated level, else the project's.</summary>
    public static double TargetLevelDb(
        IReadOnlyList<AgentOperationVerdict> toApply, double currentTargetLevelDb) =>
        toApply
            .Where(verdict => verdict.Status != AgentVerdictStatus.Rejected)
            .Select(verdict => verdict.Operation)
            .OfType<AutoTunePeqOperation>()
            .Select(tune => tune.TargetLevelDb)
            .FirstOrDefault(level => level != null)
            ?? currentTargetLevelDb;
}
