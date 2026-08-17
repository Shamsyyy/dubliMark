using System.IO;
using System.Text.Json;

namespace DoubleMark.Desktop.Settings;

public enum BackendMode
{
    LocalApi,
    Supabase
}

public sealed record BackendConfig(
    BackendMode Mode,
    string ApiBaseUrl,
    string? SupabaseUrl,
    string? SupabaseAnonKey)
{
    public bool IsConfigured =>
        Mode == BackendMode.LocalApi
            ? !string.IsNullOrWhiteSpace(ApiBaseUrl)
            : !string.IsNullOrWhiteSpace(SupabaseUrl) && !string.IsNullOrWhiteSpace(SupabaseAnonKey);
}

public static class BackendConfigLoader
{
    public static BackendConfig Load()
    {
        var modeRaw = Environment.GetEnvironmentVariable("DOUBLEMARK_BACKEND")
                      ?? ReadJson("Backend:Mode")
                      ?? ReadJson("BackendMode")
                      ?? "LocalApi";

        var mode = modeRaw.Equals("Supabase", StringComparison.OrdinalIgnoreCase)
            ? BackendMode.Supabase
            : BackendMode.LocalApi;

        var apiUrl = Environment.GetEnvironmentVariable("DOUBLEMARK_API_URL")
                     ?? ReadJson("Backend:ApiBaseUrl")
                     ?? ReadJson("ApiBaseUrl")
                     ?? "http://localhost:5080";

        var supabase = SupabaseConfigLoader.Load();
        return new BackendConfig(mode, apiUrl.TrimEnd('/'), supabase.Url, supabase.AnonKey);
    }

    private static string? ReadJson(params string[] keys)
    {
        foreach (var dir in CandidateDirectories())
        {
            foreach (var file in new[] { "appsettings.local.json", "appsettings.json" })
            {
                var path = Path.Combine(dir, file);
                if (!File.Exists(path))
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    var flat = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    Flatten(doc.RootElement, "", flat);
                    foreach (var key in keys)
                    {
                        if (flat.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                            return value;
                    }
                }
                catch
                {
                    // ignore malformed local config
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        yield return AppContext.BaseDirectory;
        yield return Environment.CurrentDirectory;
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory.Parent != null)
        {
            directory = directory.Parent;
            yield return directory.FullName;
        }
    }

    private static void Flatten(JsonElement element, string prefix, Dictionary<string, string> values)
    {
        foreach (var property in element.EnumerateObject())
        {
            var key = string.IsNullOrWhiteSpace(prefix) ? property.Name : prefix + ":" + property.Name;
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                Flatten(property.Value, key, values);
                continue;
            }

            if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                values[key] = property.Value.ToString();
        }
    }
}
