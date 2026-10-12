using Winx.Shell.Services.Audio;

namespace Winx.Tests;

/// <summary>Ícono de cada dispositivo según su forma (EndpointFormFactor) y si es Bluetooth.</summary>
public sealed class AudioDeviceKindTests
{
    [Theory]
    [InlineData(1, "{1}.HDAUDIO\\FUNC_01", false, AudioDeviceKind.Speakers)]
    [InlineData(3, "{1}.USB\\VID_0E6A&PID_0110", false, AudioDeviceKind.Headphones)]
    [InlineData(5, "{1}.USB\\VID_1234", false, AudioDeviceKind.Headset)]
    [InlineData(9, "{1}.HDAUDIO\\FUNC_01&VEN_10DE", false, AudioDeviceKind.Display)]
    [InlineData(8, "{1}.HDAUDIO\\FUNC_01", false, AudioDeviceKind.Digital)]
    [InlineData(4, "{1}.USB\\VID_0E6A", true, AudioDeviceKind.Microphone)]
    [InlineData(10, "{1}.ROOT\\VIRTUAL", true, AudioDeviceKind.Microphone)]
    [InlineData(10, "{1}.ROOT\\VIRTUAL", false, AudioDeviceKind.Other)]
    public void Kind_follows_form_factor(int formFactor, string path, bool input, AudioDeviceKind expected) =>
        Assert.Equal(expected, AudioService.KindOf(formFactor, path, input));

    [Fact]
    public void Endpoint_guid_is_the_last_part_of_the_device_id() =>
        Assert.Equal("{36ad2c8a-624c-468d-9e5f-841bd1548a4f}",
                     AudioDeviceOptions.EndpointGuid("{0.0.0.00000000}.{36ad2c8a-624c-468d-9e5f-841bd1548a4f}"));

    [Fact]
    public void Elevated_command_rejects_other_properties() =>
        Assert.Equal(2, AudioDeviceOptions.RunElevated(
            ["--audio-property", "Render", "{36ad2c8a-624c-468d-9e5f-841bd1548a4f}", "Properties", "{a45c254e-df1c-4efd-8020-67d146a850e0},2", "1"]));

    [Theory]
    [InlineData("{1}.BTHENUM\\{0000110B-0000-1000-8000-00805F9B34FB}")]
    [InlineData("{1}.BTHHFENUM\\BTHHFPAUDIO")]
    [InlineData("{1}.BTHLEDEVICE\\{0000184E}")]
    public void Bluetooth_wins_over_form_factor(string path) =>
        Assert.Equal(AudioDeviceKind.Bluetooth, AudioService.KindOf(3, path, input: false));
}
