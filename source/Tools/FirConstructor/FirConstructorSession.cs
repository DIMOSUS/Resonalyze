using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>What a handoff sets aside: the controls' design and the bare kernel shown or still rebuilding.</summary>
internal sealed record FirConstructorStandaloneWork(
    FirCrossoverDesign Controls,
    FirFilter? BareKernel,
    string? BareName);

/// <summary>One rebuild: a design to build (null for a bare kernel) or a bare kernel to show, at a rate. An edit
/// settles a moment first, so a burst of them builds once; only the session's latest rebuild lands.</summary>
internal sealed record FirConstructorRebuild(
    int Generation, FirCrossoverDesign? Design, FirFilter? BareKernel, string? Name, int RateHz, bool Settle) : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();

    public CancellationToken Token => cancellation.Token;

    public void Cancel() => cancellation.Cancel();

    public void Dispose() => cancellation.Dispose();
}

/// <summary>The FIR Constructor's state: the kernel it shows (a design's, or a bare one) with its rendering, the Virtual
/// DSP side it edits with the standalone work set aside meanwhile, and the rebuild in flight. See
/// docs/tech/dsp-fir-crossover-design.md#constructor-code-map.</summary>
internal sealed class FirConstructorSession
{
    private FirConstructorRebuild? activeRebuild;
    private int rebuildGeneration;
    private FirConstructorStandaloneWork? standaloneWork;

    // Kept so a handoff can set it aside while its rebuild is still running.
    private FirFilter? requestedBareKernel;
    private string? requestedBareName;

    /// <summary>What the plots show; null before a kernel lands and while the design has a problem.</summary>
    public FirConstructorRendering? Rendering { get; private set; }

    public FirFilter? Kernel => Rendering?.Kernel;

    /// <summary>Null for a bare kernel.</summary>
    public FirCrossoverDesign? Design { get; private set; }

    public string? KernelName { get; private set; }

    /// <summary>The rate the shown kernel was built or is shown at.</summary>
    public int RateHz { get; private set; } = 48_000;

    public string Problem { get; private set; } = string.Empty;

    public FirConstructorHandoffRequest? Handoff { get; private set; }

    public bool InHandoff => Handoff != null;

    public bool RebuildPending { get; private set; }

    /// <summary>The controls' design: its rebuild, or null with the problem in its place.</summary>
    public FirConstructorRebuild? Edit(FirCrossoverDesign candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        requestedBareKernel = null;
        requestedBareName = null;
        if (candidate.Problem() is { } problem)
        {
            CancelRebuild();
            Design = null;
            Rendering = null;
            KernelName = null;
            Problem = problem;
            return null;
        }

        Problem = string.Empty;
        return StartRebuild(candidate, null, null, candidate.SampleRateHz, settle: true);
    }

    /// <summary>A kernel shown as it is at <paramref name="rateHz"/>; any edit replaces it with a design.</summary>
    public FirConstructorRebuild ShowBare(FirFilter kernel, string? name, int rateHz)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        requestedBareKernel = kernel;
        requestedBareName = name;
        Problem = string.Empty;
        return StartRebuild(null, kernel, name, rateHz, settle: false);
    }

    public bool IsCurrent(FirConstructorRebuild rebuild) => rebuild.Generation == rebuildGeneration;

    /// <summary>False, with nothing changed, when a later edit has taken its place.</summary>
    public bool Land(FirConstructorRebuild rebuild, FirConstructorRendering rendering)
    {
        if (!IsCurrent(rebuild))
        {
            return false;
        }

        Design = rebuild.Design;
        Rendering = rendering;
        KernelName = rebuild.Name;
        RateHz = rebuild.RateHz;
        RebuildPending = false;
        return true;
    }

    /// <summary>The rebuild threw: nothing is shown, and the problem says why. False, with nothing changed, when a later
    /// edit has taken its place.</summary>
    public bool Fail(FirConstructorRebuild rebuild, string message)
    {
        if (!IsCurrent(rebuild))
        {
            return false;
        }

        Design = null;
        Rendering = null;
        KernelName = null;
        RebuildPending = false;
        Problem = "The kernel could not be built: " + message;
        return true;
    }

    public void Finish(FirConstructorRebuild rebuild)
    {
        if (ReferenceEquals(activeRebuild, rebuild))
        {
            activeRebuild = null;
        }
    }

    /// <summary>Opens a side. The first handoff sets the standalone work aside; a later one keeps it.</summary>
    public void BeginHandoff(FirConstructorHandoffRequest request, FirCrossoverDesign controls)
    {
        ArgumentNullException.ThrowIfNull(request);

        standaloneWork ??= Handoff == null
            ? new FirConstructorStandaloneWork(controls, requestedBareKernel, requestedBareName)
            : null;
        Handoff = request;
    }

    /// <summary>Closes the side and hands back the standalone work to put back, if a handoff set it aside.</summary>
    public FirConstructorStandaloneWork? EndHandoff()
    {
        Handoff = null;
        FirConstructorStandaloneWork? work = standaloneWork;
        standaloneWork = null;
        return work;
    }

    private FirConstructorRebuild StartRebuild(
        FirCrossoverDesign? design, FirFilter? bare, string? name, int rateHz, bool settle)
    {
        activeRebuild?.Cancel();
        var rebuild = new FirConstructorRebuild(++rebuildGeneration, design, bare, name, rateHz, settle);
        activeRebuild = rebuild;
        RebuildPending = true;
        return rebuild;
    }

    private void CancelRebuild()
    {
        activeRebuild?.Cancel();
        activeRebuild = null;
        rebuildGeneration++;
        RebuildPending = false;
    }
}
