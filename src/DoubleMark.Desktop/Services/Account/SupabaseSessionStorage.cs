using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace DoubleMark.Desktop.Services.Account;

public sealed class SupabaseSessionStorage : IGotrueSessionPersistence<Session>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _sessionPath;

    public SupabaseSessionStorage()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DoubleMark");
        Directory.CreateDirectory(directory);
        _sessionPath = Path.Combine(directory, "doublemark-session.bin");
    }

    public void SaveSession(Session session)
    {
        var json = JsonSerializer.Serialize(session, JsonOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_sessionPath, protectedBytes);
    }

    public void DestroySession()
    {
        if (File.Exists(_sessionPath))
            File.Delete(_sessionPath);
    }

    public Session? LoadSession()
    {
        try
        {
            if (!File.Exists(_sessionPath))
                return null;

            var protectedBytes = File.ReadAllBytes(_sessionPath);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<Session>(json, JsonOptions);
        }
        catch
        {
            DestroySession();
            return null;
        }
    }
}
