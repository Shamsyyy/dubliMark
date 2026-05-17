namespace DoubleMark.Desktop.Services.Account;

public sealed class AuthService
{
    private readonly IAuthGateway _gateway;

    public AuthService(SupabaseClientFactory clientFactory)
        : this(new SupabaseAuthGateway(clientFactory))
    {
    }

    public AuthService(IAuthGateway gateway)
    {
        _gateway = gateway;
        _gateway.AuthStateChanged += (_, _) => AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsConfigured => _gateway.IsConfigured;
    public event EventHandler? AuthStateChanged;

    public Task InitializeAsync() => _gateway.InitializeAsync();

    public Task<AccountUser?> SignIn(string email, string password) =>
        _gateway.SignIn(email, password);

    public Task SignOut() => _gateway.SignOut();

    public AccountUser? GetCurrentUser() => _gateway.GetCurrentUser();

    public Supabase.Gotrue.Session? GetSession() => _gateway.GetSession();

    public bool HasAccessToken =>
        !string.IsNullOrWhiteSpace(GetSession()?.AccessToken);

    public Task<AccountUser?> RestoreSession() => _gateway.RestoreSession();

    public Task<AccountUser?> RefreshSession() => _gateway.RefreshSession();

    public void OnAuthStateChanged(EventHandler handler) =>
        AuthStateChanged += handler;
}
