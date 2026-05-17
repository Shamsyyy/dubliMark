using DoubleMark.Desktop.Settings;

namespace DoubleMark.Desktop.Services.Account;

public sealed class SupabaseClientFactory
{
    private readonly SupabaseConfig _config;
    private readonly SupabaseSessionStorage _sessionStorage;
    private Supabase.Client? _client;

    public SupabaseClientFactory(SupabaseConfig? config = null, SupabaseSessionStorage? sessionStorage = null)
    {
        _config = config ?? SupabaseConfigLoader.Load();
        _sessionStorage = sessionStorage ?? new SupabaseSessionStorage();
    }

    public bool IsConfigured => _config.IsConfigured;
    public string SupabaseUrl => _config.Url;
    public SupabaseSessionStorage SessionStorage => _sessionStorage;

    public Supabase.Client GetClient()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Не настроено подключение к серверу DoubleMark. Проверьте SUPABASE_URL и SUPABASE_ANON_KEY.");

        if (_client != null)
            return _client;

        var options = new Supabase.SupabaseOptions
        {
            AutoConnectRealtime = false,
            AutoRefreshToken = true,
            SessionHandler = _sessionStorage
        };
        _client = new Supabase.Client(_config.Url, _config.AnonKey, options);
        return _client;
    }
}
