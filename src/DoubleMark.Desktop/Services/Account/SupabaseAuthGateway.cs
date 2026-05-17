using Supabase.Gotrue;

namespace DoubleMark.Desktop.Services.Account;

public sealed class SupabaseAuthGateway : IAuthGateway
{
    private readonly SupabaseClientFactory _clientFactory;
    private bool _initialized;

    public SupabaseAuthGateway(SupabaseClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public bool IsConfigured => _clientFactory.IsConfigured;
    public event EventHandler? AuthStateChanged;

    public async Task InitializeAsync()
    {
        if (!IsConfigured || _initialized)
            return;

        var client = _clientFactory.GetClient();
        client.Auth.AddStateChangedListener((_, _) => AuthStateChanged?.Invoke(this, EventArgs.Empty));
        await client.InitializeAsync();
        _initialized = true;
    }

    public async Task<AccountUser?> SignIn(string email, string password)
    {
        await InitializeAsync();
        var session = await _clientFactory.GetClient().Auth.SignIn(email, password);
        return ToAccountUser(_clientFactory.GetClient().Auth.CurrentUser, session);
    }

    public async Task SignOut()
    {
        if (!IsConfigured)
            return;

        await _clientFactory.GetClient().Auth.SignOut();
        _clientFactory.SessionStorage.DestroySession();
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public AccountUser? GetCurrentUser()
    {
        if (!IsConfigured)
            return null;

        return ToAccountUser(_clientFactory.GetClient().Auth.CurrentUser, _clientFactory.GetClient().Auth.CurrentSession);
    }

    public Session? GetSession() =>
        IsConfigured ? _clientFactory.GetClient().Auth.CurrentSession : null;

    public async Task<AccountUser?> RestoreSession()
    {
        await InitializeAsync();
        if (!IsConfigured)
            return null;

        var auth = _clientFactory.GetClient().Auth;
        auth.LoadSession();
        if (auth.CurrentSession == null)
            return null;

        try
        {
            await auth.RetrieveSessionAsync();
        }
        catch
        {
            _clientFactory.SessionStorage.DestroySession();
            return null;
        }

        return ToAccountUser(auth.CurrentUser, auth.CurrentSession);
    }

    public async Task<AccountUser?> RefreshSession()
    {
        if (!IsConfigured)
            return null;

        await _clientFactory.GetClient().Auth.RefreshSession();
        return GetCurrentUser();
    }

    private static AccountUser? ToAccountUser(User? user, Session? session)
    {
        var id = user?.Id;
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return new AccountUser(id, user?.Email);
    }
}
