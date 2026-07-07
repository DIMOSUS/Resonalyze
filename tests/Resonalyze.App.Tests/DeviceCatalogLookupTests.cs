namespace Resonalyze.App.Tests;

/// <summary>
/// A saved device/driver/channel that is no longer present must be reported as
/// absent (-1), never silently remapped to the first list entry — the options
/// panel would re-persist the wrong device on the next apply.
/// </summary>
public sealed class DeviceCatalogLookupTests
{
    [Fact]
    public void FindDeviceIndex_FindsDeviceByNumber()
    {
        AudioDeviceInfo[] devices =
        [
            new AudioDeviceInfo(-1, "Default recording device"),
            new AudioDeviceInfo(0, "Line In"),
            new AudioDeviceInfo(1, "USB Interface", Channels: 2)
        ];

        Assert.Equal(0, AudioDeviceCatalog.FindDeviceIndex(devices, -1));
        Assert.Equal(2, AudioDeviceCatalog.FindDeviceIndex(devices, 1));
    }

    [Fact]
    public void FindDeviceIndex_ReportsMissingDeviceInsteadOfFirstEntry()
    {
        AudioDeviceInfo[] devices =
        [
            new AudioDeviceInfo(-1, "Default recording device"),
            new AudioDeviceInfo(0, "Line In")
        ];

        Assert.Equal(-1, AudioDeviceCatalog.FindDeviceIndex(devices, 7));
    }

    [Fact]
    public void CreateMissingDevice_KeepsTheDeviceNumberAndMarksTheName()
    {
        AudioDeviceInfo missing = AudioDeviceCatalog.CreateMissingDevice(7);

        Assert.Equal(7, missing.DeviceNumber);
        Assert.Contains("(missing)", missing.Name);
    }

    [Fact]
    public void FindDriverIndex_FindsDriverCaseInsensitively()
    {
        AsioDeviceInfo[] drivers =
        [
            new AsioDeviceInfo("Focusrite USB ASIO"),
            new AsioDeviceInfo("RME Fireface")
        ];

        Assert.Equal(1, AsioDeviceCatalog.FindDriverIndex(drivers, "rme fireface"));
    }

    [Fact]
    public void FindDriverIndex_DefaultsToFirstDriverOnlyWhenNoNameIsSaved()
    {
        AsioDeviceInfo[] drivers = [new AsioDeviceInfo("Focusrite USB ASIO")];

        Assert.Equal(0, AsioDeviceCatalog.FindDriverIndex(drivers, null));
        Assert.Equal(0, AsioDeviceCatalog.FindDriverIndex(drivers, " "));
        Assert.Equal(-1, AsioDeviceCatalog.FindDriverIndex(
            Array.Empty<AsioDeviceInfo>(),
            null));
    }

    [Fact]
    public void FindDriverIndex_ReportsMissingDriverInsteadOfFirstEntry()
    {
        AsioDeviceInfo[] drivers = [new AsioDeviceInfo("Focusrite USB ASIO")];

        Assert.Equal(-1, AsioDeviceCatalog.FindDriverIndex(drivers, "RME Fireface"));
    }

    [Fact]
    public void MissingAsioDriver_KeepsItsNameAndMarksTheDisplayText()
    {
        var missing = new AsioDeviceInfo("RME Fireface", Missing: true);

        Assert.Equal("RME Fireface", missing.DriverName);
        Assert.Equal("(missing) RME Fireface", missing.ToString());
    }

    [Fact]
    public void FindChannelIndex_FindsChannelByOffset()
    {
        AsioChannelInfo[] channels =
        [
            new AsioChannelInfo(0, "Input 1"),
            new AsioChannelInfo(1, "Input 2"),
            new AsioChannelInfo(2, "Input 3")
        ];

        Assert.Equal(2, AsioDeviceCatalog.FindChannelIndex(channels, 2));
    }

    [Fact]
    public void FindChannelIndex_ReportsMissingOffsetInsteadOfFirstChannel()
    {
        AsioChannelInfo[] channels =
        [
            new AsioChannelInfo(0, "Input 1"),
            new AsioChannelInfo(1, "Input 2")
        ];

        Assert.Equal(-1, AsioDeviceCatalog.FindChannelIndex(channels, 7));
        Assert.Equal(-1, AsioDeviceCatalog.FindChannelIndex(
            Array.Empty<AsioChannelInfo>(),
            0));
    }
}
