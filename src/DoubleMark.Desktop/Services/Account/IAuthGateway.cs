using Supabase.Gotrue;

namespace DoubleMark.Desktop.Services.Account;

public interface IAuthGateway
{
    bool IsConfigured { get; }
    event EventHandler? AuthStateChanged;
    Task InitializeAsync();
    Task<AccountUser?> SignIn(string email, string password);
    Task SignOut();
    AccountUser? GetCurrentUser();
    Session? GetSession();
    Task<AccountUser?> RestoreSession();
    Task<AccountUser?> RefreshSession();
}
