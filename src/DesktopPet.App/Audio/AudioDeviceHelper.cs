using NAudio.CoreAudioApi;

namespace DesktopPet.App.Audio;

public sealed record AudioDeviceInfo(string Id, string DisplayName, bool IsDefault);

public static class AudioDeviceHelper
{
    public static IReadOnlyList<AudioDeviceInfo> GetMicrophoneDevices()
    {
        return GetDevices(DataFlow.Capture);
    }

    public static IReadOnlyList<AudioDeviceInfo> GetSystemAudioDevices()
    {
        return GetDevices(DataFlow.Render);
    }

    private static IReadOnlyList<AudioDeviceInfo> GetDevices(DataFlow dataFlow)
    {
        var devices = new List<AudioDeviceInfo>();
        using var enumerator = new MMDeviceEnumerator();
        var defaultDevice = enumerator.GetDefaultAudioEndpoint(dataFlow, Role.Multimedia);
        var defaultId = defaultDevice.ID;
        defaultDevice.Dispose();

        var collection = enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active);
        for (var i = 0; i < collection.Count; i++)
        {
            using var device = collection[i];
            var isDefault = string.Equals(device.ID, defaultId, StringComparison.Ordinal);
            devices.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, isDefault));
        }

        return devices.OrderBy(d => d.IsDefault ? 0 : 1).ThenBy(d => d.DisplayName).ToArray();
    }
}
