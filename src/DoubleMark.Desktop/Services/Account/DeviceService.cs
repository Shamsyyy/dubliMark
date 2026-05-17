using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DoubleMark.Desktop.Services.Account;

public sealed class DeviceService
{
    private readonly SupabaseClientFactory _clientFactory;

    public DeviceService(SupabaseClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public string GetDeviceId()
    {
        var path = GetDeviceIdPath();
        if (File.Exists(path))
            return File.ReadAllText(path).Trim();

        var raw = string.Join("|", Environment.MachineName, Environment.UserName, Environment.OSVersion.VersionString);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, hash);
        return hash;
    }

    public string GetDeviceName() => Environment.MachineName;

    public string GetPlatform() => "Windows";

    public async Task<DeviceRegistrationResult> RegisterCurrentDevice(string userId, int devicesLimit)
    {
        var deviceId = GetDeviceId();
        var devices = await GetUserDevices(userId);
        var existing = devices.FirstOrDefault(device => device.DeviceId == deviceId);
        if (!DeviceRules.IsWithinDeviceLimit(devices, deviceId, devicesLimit))
        {
            return new DeviceRegistrationResult(
                false,
                "Превышен лимит устройств по текущему тарифу.",
                null);
        }

        var now = DateTimeOffset.UtcNow;
        var row = new DeviceRow
        {
            UserId = userId,
            DeviceId = deviceId,
            DeviceName = GetDeviceName(),
            Platform = GetPlatform(),
            CreatedAt = AccountRowMapping.ToDateTime(existing?.CreatedAt ?? now),
            LastSeenAt = AccountRowMapping.ToDateTime(now)
        };

        await _clientFactory.GetClient().From<DeviceRow>().Upsert(row);
        return new DeviceRegistrationResult(true, null, AccountRowMapping.ToDevice(row));
    }

    public async Task<IReadOnlyList<AccountDevice>> GetUserDevices(string userId)
    {
        var result = await _clientFactory.GetClient()
            .From<DeviceRow>()
            .Where(row => row.UserId == userId)
            .Get();

        AccountDiagnostics.Log("devices query count: " + result.Models.Count);
        return result.Models
            .Select(AccountRowMapping.ToDevice)
            .OrderByDescending(device => device.LastSeenAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    public async Task UpdateLastSeen(string userId, string deviceId)
    {
        await _clientFactory.GetClient()
            .From<DeviceRow>()
            .Where(row => row.UserId == userId && row.DeviceId == deviceId)
            .Set(row => row.LastSeenAt!, AccountRowMapping.ToDateTime(DateTimeOffset.UtcNow))
            .Update();
    }

    public async Task<bool> CheckDeviceLimit(string userId, int devicesLimit)
    {
        var deviceId = GetDeviceId();
        var devices = await GetUserDevices(userId);
        return DeviceRules.IsWithinDeviceLimit(devices, deviceId, devicesLimit);
    }

    private static string GetDeviceIdPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DoubleMark",
            "device.id");
}
