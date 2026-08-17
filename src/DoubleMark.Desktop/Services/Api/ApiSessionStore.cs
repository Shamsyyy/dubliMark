using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DoubleMark.Desktop.Services.Api;

public sealed class ApiSessionStore
{
    private readonly string _path;

    public ApiSessionStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DoubleMark");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "api-session.bin");
    }

    public void Save(ApiSession session)
    {
        var json = JsonSerializer.Serialize(session);
        var bytes = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, protectedBytes);
    }

    public ApiSession? Load()
    {
        if (!File.Exists(_path))
            return null;

        try
        {
            var protectedBytes = File.ReadAllBytes(_path);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<ApiSession>(Encoding.UTF8.GetString(bytes));
        }
        catch
        {
            return null;
        }
    }

    public void Clear()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }
}

public sealed class ApiSession
{
    public string UserId { get; set; } = "";
    public string? Email { get; set; }
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
}
