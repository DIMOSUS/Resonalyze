using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;
using Resonalyze.Options;

// Device resolution for the persisted audio settings. This is not mapping: it
// enumerates real hardware (WindowsAudioEndpointService, AsioDeviceCatalog,
// AudioDeviceCatalog) to decide whether a stored device/endpoint/driver still
// exists and what to fall back to when it does not. Kept apart from the schema
// because it touches the machine, not the file.

namespace Resonalyze;

internal sealed partial class MeasurementSettingsFile
{
    private static int NormalizeDeviceNumber(
        IReadOnlyList<AudioDeviceInfo> devices,
        int deviceNumber) =>
        devices.Any(device => device.DeviceNumber == deviceNumber)
            ? deviceNumber
            : -1;

    private static AudioBackend NormalizeAudioBackend(
        AudioBackend backend,
        string? asioDriverName)
    {
        if (!Enum.IsDefined(backend))
        {
            return AudioBackend.Wave;
        }
        if (backend == AudioBackend.Asio &&
            string.IsNullOrWhiteSpace(NormalizeAsioDriverName(asioDriverName)))
        {
            return AudioBackend.Wave;
        }

        return backend;
    }

    private static string? NormalizeWasapiEndpointId(string? endpointId, bool capture)
    {
        try
        {
            using var service = new WindowsAudioEndpointService();
            IReadOnlyList<AudioEndpointDescriptor> endpoints = capture
                ? service.GetCaptureEndpoints()
                : service.GetRenderEndpoints();
            AudioEndpointDescriptor? exact = endpoints.FirstOrDefault(endpoint =>
                string.Equals(endpoint.Id, endpointId, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(endpointId))
            {
                return exact?.Id ?? endpointId;
            }
            return endpoints.FirstOrDefault(endpoint => endpoint.IsDefault)?.Id;
        }
        catch
        {
            return endpointId;
        }
    }

    private static int NormalizeWasapiSampleRate(
        AudioBackend backend,
        string? captureEndpointId,
        string? renderEndpointId,
        int fallback)
    {
        if (backend != AudioBackend.WasapiShared ||
            captureEndpointId == null ||
            renderEndpointId == null)
        {
            return fallback;
        }
        try
        {
            using var service = new WindowsAudioEndpointService();
            AudioEndpointDescriptor? capture = service.GetCaptureEndpoints()
                .FirstOrDefault(endpoint => endpoint.Id == captureEndpointId);
            AudioEndpointDescriptor? render = service.GetRenderEndpoints()
                .FirstOrDefault(endpoint => endpoint.Id == renderEndpointId);
            return capture != null && render != null &&
                capture.PreferredFormat.SampleRate == render.PreferredFormat.SampleRate
                    ? capture.PreferredFormat.SampleRate
                    : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static string? NormalizeAsioDriverName(string? asioDriverName)
    {
        IReadOnlyList<AsioDeviceInfo> drivers = AsioDeviceCatalog.GetDrivers();
        if (drivers.Count == 0)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(asioDriverName))
        {
            return drivers[0].DriverName;
        }

        return drivers.Any(driver =>
            string.Equals(
                driver.DriverName,
                asioDriverName,
                StringComparison.OrdinalIgnoreCase))
            ? asioDriverName
            : drivers[0].DriverName;
    }

    private static int NormalizeAsioChannelOffset(
        string? asioDriverName,
        int sampleRate,
        int offset,
        bool input)
    {
        AsioDriverInfo driverInfo = AsioDeviceCatalog.GetDriverInfo(
            NormalizeAsioDriverName(asioDriverName),
            sampleRate);
        IReadOnlyList<AsioChannelInfo> channels = input
            ? driverInfo.InputChannels
            : driverInfo.OutputChannels;

        return channels.Any(channel => channel.Offset == offset)
            ? offset
            : 0;
    }

    private static int NormalizeWaveChannelOffset(int offset) =>
        Math.Clamp(offset, 0, 1);

    private static int? NormalizeOptionalWaveChannelOffset(int? offset) =>
        offset.HasValue ? NormalizeWaveChannelOffset(offset.Value) : null;

    private static int? NormalizeOptionalWasapiChannelOffset(int? offset) =>
        offset.HasValue ? Math.Max(0, offset.Value) : null;

    private static int? NormalizeOptionalAsioChannelOffset(
        string? asioDriverName,
        int sampleRate,
        int? offset) =>
        offset.HasValue
            ? NormalizeAsioChannelOffset(asioDriverName, sampleRate, offset.Value, input: true)
            : null;

    private static MicrophoneCalibrationMode NormalizeCalibrationMode(
        MicrophoneCalibrationMode? mode,
        bool legacyUseCalibration)
    {
        if (mode.HasValue && Enum.IsDefined(mode.Value))
        {
            return mode.Value;
        }

        return legacyUseCalibration
            ? MicrophoneCalibrationMode.Degrees0
            : MicrophoneCalibrationMode.Off;
    }

    // Mirrors the UI invariant (see TukeyWindowControlHelper): each fade is in
    // [0, window] and their sum must not exceed the window length. Clamping each
    // to window/2 instead would corrupt valid asymmetric windows (e.g. 256 + 16).
    private static (int Left, int Right) ClampTukeyWindows(
        int left,
        int right,
        int window)
    {
        int clampedLeft = Clamp(left, 0, window);
        int clampedRight = Clamp(right, 0, window);
        if (clampedLeft + clampedRight > window)
        {
            clampedRight = Math.Max(0, window - clampedLeft);
        }

        return (clampedLeft, clampedRight);
    }

    private static int Clamp(int value, int minimum, int maximum) =>
        Math.Clamp(value, minimum, maximum);
}
