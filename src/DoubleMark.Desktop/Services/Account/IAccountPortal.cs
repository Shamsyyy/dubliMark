namespace DoubleMark.Desktop.Services.Account;

public interface IAccountPortal
{
    bool IsConfigured { get; }
    Task<AccountSnapshot> RestoreAccount();
    Task<AccountSnapshot> SignIn(string email, string password);
    Task SignOut();
    Task<AccountSnapshot> Refresh();
}
