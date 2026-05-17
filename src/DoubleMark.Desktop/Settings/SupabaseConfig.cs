using System.IO;
using System.Text.Json;

namespace DoubleMark.Desktop.Settings;

public sealed record SupabaseConfig(string Url, string AnonKey)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Url) &&
        !string.IsNullOrWhiteSpace(AnonKey);
}

public static class SupabaseConfigLoader
{
    private const string LocalConfigFileName = "appsettings.local.json";
    private static readonly string[] EnvFileNames = { ".env.local", ".env" };

    public static SupabaseConfig Load()
    {
        var url = FirstValue("SUPABASE_URL", "VITE_SUPABASE_URL");
        var anonKey = FirstValue("SUPABASE_ANON_KEY", "VITE_SUPABASE_ANON_KEY");

        if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(anonKey))
            return new SupabaseConfig(url, anonKey);

        var localConfig = LoadLocalConfig();
        var envConfig = LoadEnvConfig();
        var config = new SupabaseConfig(
            url
            ?? FirstJsonValue(localConfig, "SUPABASE_URL", "VITE_SUPABASE_URL")
            ?? FirstJsonValue(localConfig, "Supabase:Url", "Supabase:URL")
            ?? FirstJsonValue(envConfig, "SUPABASE_URL", "VITE_SUPABASE_URL")
            ?? "",
            anonKey
            ?? FirstJsonValue(localConfig, "SUPABASE_ANON_KEY", "VITE_SUPABASE_ANON_KEY")
            ?? FirstJsonValue(localConfig, "Supabase:AnonKey", "Supabase:ANON_KEY")
            ?? FirstJsonValue(envConfig, "SUPABASE_ANON_KEY", "VITE_SUPABASE_ANON_KEY")
            ?? "");

        if (config.AnonKey.Contains("service_role", StringComparison.OrdinalIgnoreCase))
            return new SupabaseConfig(config.Url, "");

        return config;
    }

    private static string? FirstValue(params string[] names) =>
        names.Select(Environment.GetEnvironmentVariable)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static Dictionary<string, string>? LoadLocalConfig()
    {
        var paths = CandidateDirectories()
            .Select(directory => Path.Combine(directory, LocalConfigFileName));

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
                continue;

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                FlattenJson(document.RootElement, "", values);
                return values;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static Dictionary<string, string>? LoadEnvConfig()
    {
        foreach (var path in CandidateDirectories()
                     .SelectMany(directory => EnvFileNames.Select(fileName => Path.Combine(directory, fileName)))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
                continue;

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                var separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                values[line[..separator].Trim()] = line[(separator + 1)..].Trim().Trim('"');
            }

            return values;
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

    private static void FlattenJson(JsonElement element, string prefix, Dictionary<string, string> values)
    {
        foreach (var property in element.EnumerateObject())
        {
            var key = string.IsNullOrWhiteSpace(prefix) ? property.Name : prefix + ":" + property.Name;
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                FlattenJson(property.Value, key, values);
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.String)
                values[key] = property.Value.GetString() ?? "";
        }
    }

    private static string? FirstJsonValue(Dictionary<string, string>? values, params string[] names)
    {
        if (values == null)
            return null;

        foreach (var name in names)
        {
            if (values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}
