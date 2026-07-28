using Xunit;

namespace Resonalyze.Testing;

/// <summary>
/// A <see cref="FactAttribute"/> that reports the test as SKIPPED, with a
/// reason, when the WASAPI endpoint environment variables are not set.
///
/// The tests used to open with <c>if (endpoints is null) return;</c>, which xUnit
/// records as a pass — so a machine with no audio hardware showed nine green
/// hardware tests that had not executed a single assert. xUnit v2 has no runtime
/// skip (that arrived in v3), but <see cref="FactAttribute.Skip"/> is evaluated
/// at discovery, which is early enough: the variables have to be set before the
/// test host starts anyway.
///
/// Linked into both test projects rather than duplicated; see the Compile item
/// in each .csproj.
/// </summary>
public sealed class HardwareFactAttribute : FactAttribute
{
    public const string CaptureEndpointVariable = "RESONALYZE_WASAPI_CAPTURE_ENDPOINT_ID";
    public const string RenderEndpointVariable = "RESONALYZE_WASAPI_RENDER_ENDPOINT_ID";

    public HardwareFactAttribute()
    {
        if (!HasEndpoints())
        {
            Skip = SkipReason;
        }
    }

    internal static string SkipReason =>
        $"Set {CaptureEndpointVariable} and {RenderEndpointVariable} " +
        "to run the hardware smoke tests.";

    public static bool HasEndpoints() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CaptureEndpointVariable)) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RenderEndpointVariable));

    /// <summary>
    /// The endpoint pair. Only called from a <see cref="HardwareFactAttribute"/>
    /// test, so the variables are set; the throw is a guard against a plain
    /// <c>[Fact]</c> creeping in, not an expected path.
    /// </summary>
    public static (string Capture, string Render) Endpoints()
    {
        string? capture = Environment.GetEnvironmentVariable(CaptureEndpointVariable);
        string? render = Environment.GetEnvironmentVariable(RenderEndpointVariable);
        if (string.IsNullOrWhiteSpace(capture) || string.IsNullOrWhiteSpace(render))
        {
            throw new InvalidOperationException(
                $"{CaptureEndpointVariable}/{RenderEndpointVariable} are unset; " +
                $"mark the test [{nameof(HardwareFactAttribute)}] so it is skipped instead.");
        }

        return (capture, render);
    }
}

/// <summary>The <see cref="TheoryAttribute"/> counterpart of <see cref="HardwareFactAttribute"/>.</summary>
public sealed class HardwareTheoryAttribute : TheoryAttribute
{
    public HardwareTheoryAttribute()
    {
        if (!HardwareFactAttribute.HasEndpoints())
        {
            Skip = HardwareFactAttribute.SkipReason;
        }
    }
}
