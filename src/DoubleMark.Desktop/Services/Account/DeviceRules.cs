namespace DoubleMark.Desktop.Services.Account;

public static class DeviceRules
{
    public static bool IsWithinDeviceLimit(
        IReadOnlyList<AccountDevice> devices,
        string currentDeviceId,
        int devicesLimit)
    {
        var limit = Math.Max(1, devicesLimit);
        return devices.Any(device => device.DeviceId == currentDeviceId) || devices.Count < limit;
    }
}
