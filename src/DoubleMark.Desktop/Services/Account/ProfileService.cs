namespace DoubleMark.Desktop.Services.Account;

public sealed class ProfileService
{
    private readonly SupabaseClientFactory _clientFactory;

    public ProfileService(SupabaseClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public async Task<AccountProfile?> GetProfile(string userId)
    {
        var result = await _clientFactory.GetClient()
            .From<ProfileRow>()
            .Where(row => row.Id == userId)
            .Get();

        var row = result.Models.FirstOrDefault();
        AccountDiagnostics.Log("profile query result: " + (row == null
            ? "empty"
            : $"id={row.Id}, email={row.Email}, company_name={row.CompanyName}, role={row.Role}"));
        return row is not null
            ? AccountRowMapping.ToProfile(row)
            : null;
    }

    public async Task<AccountProfile?> GetOrCreateProfile(AccountUser user)
    {
        var profile = await GetProfile(user.Id);
        if (profile != null)
            return profile;

        var row = new ProfileRow
        {
            Id = user.Id,
            Email = user.Email,
            Role = "user"
        };
        await _clientFactory.GetClient().From<ProfileRow>().Upsert(row);
        AccountDiagnostics.Log("profile upsert created: id=" + user.Id + ", email=" + user.Email);
        return await GetProfile(user.Id);
    }

    public async Task<AccountProfile?> UpdateProfile(string userId, ProfileUpdate update)
    {
        await _clientFactory.GetClient()
            .From<ProfileRow>()
            .Where(row => row.Id == userId)
            .Set(row => row.CompanyName!, update.Organization)
            .Set(row => row.Inn!, update.Inn)
            .Set(row => row.Phone!, update.Phone)
            .Update();

        return await GetProfile(userId);
    }
}
