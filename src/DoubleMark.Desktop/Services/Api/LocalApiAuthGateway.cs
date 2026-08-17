using DoubleMark.Desktop.Services.Account;

namespace DoubleMark.Desktop.Services.Api;

public sealed class LocalApiAuthGateway : IAuthGateway
{
    private readonly DoubleMarkApiClient _api;

    public LocalApiAuthGateway(DoubleMarkApiClient api)
    {
        _api = api;
    }

    public bool IsConfigured => _api.IsConfigured;
    public event EventHandler? AuthStateChanged;
    public string? AccessToken => _api.AccessToken;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task<AccountUser?> SignIn(string email, string password)
    {
        var user = await _api.LoginAsync(email, password);
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
        return user;
    }

    public async Task SignOut()
    {
        await _api.LogoutAsync();
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public AccountUser? GetCurrentUser() => _api.CurrentUser;

    public async Task<AccountUser?> RestoreSession()
    {
        var user = await _api.RestoreAsync();
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
        return user;
    }

    public async Task<AccountUser?> RefreshSession()
    {
        var user = await _api.RefreshAsync();
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
        return user;
    }
}
