using DoubleMark.Desktop.Services.Account;
using FluentAssertions;

public sealed class AccountServicesTests
{
    [Fact]
    public async Task SignIn_Success_ReturnsUser()
    {
        var auth = new AuthService(new FakeAuthGateway(new AccountUser("user-1", "user@example.com")));

        var user = await auth.SignIn("user@example.com", "password");

        user.Should().BeEquivalentTo(new AccountUser("user-1", "user@example.com"));
    }

    [Fact]
    public async Task SignIn_Error_Propagates()
    {
        var auth = new AuthService(new FakeAuthGateway(null, new InvalidOperationException("invalid login")));

        var act = async () => await auth.SignIn("bad@example.com", "wrong");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("invalid login");
    }

    [Fact]
    public async Task RestoreSession_ReturnsPersistedUser()
    {
        var auth = new AuthService(new FakeAuthGateway(new AccountUser("user-2", "restore@example.com")));

        var user = await auth.RestoreSession();

        user.Should().BeEquivalentTo(new AccountUser("user-2", "restore@example.com"));
    }

    [Fact]
    public void HasActiveSubscription_ActiveBeforePeriodEnd_IsActive()
    {
        var now = DateTimeOffset.Parse("2026-01-10T00:00:00Z");
        var subscription = Subscription("active", periodEnd: now.AddDays(1));

        var status = SubscriptionRules.GetStatus(subscription, now);

        status.IsActive.Should().BeTrue();
        status.DisplayStatus.Should().Be("Активна");
    }

    [Fact]
    public void HasActiveSubscription_Expired_IsInactive()
    {
        var now = DateTimeOffset.Parse("2026-01-10T00:00:00Z");
        var subscription = Subscription("active", periodEnd: now.AddDays(-1));

        var status = SubscriptionRules.GetStatus(subscription, now);

        status.IsActive.Should().BeFalse();
        status.DisplayStatus.Should().Be("Подписка истекла");
    }

    [Fact]
    public void HasActiveSubscription_TrialingBeforeTrialEnd_IsActive()
    {
        var now = DateTimeOffset.Parse("2026-01-10T00:00:00Z");
        var subscription = Subscription("trialing", trialEnd: now.AddDays(3));

        var status = SubscriptionRules.GetStatus(subscription, now);

        status.IsActive.Should().BeTrue();
        status.DisplayStatus.Should().Be("Trial");
    }

    [Fact]
    public void DeviceLimit_NewDeviceAtLimit_IsBlocked()
    {
        var devices = new[] { Device("device-1") };

        DeviceRules.IsWithinDeviceLimit(devices, "device-2", devicesLimit: 1)
            .Should().BeFalse();
    }

    [Fact]
    public void DeviceLimit_ExistingDeviceAtLimit_IsAllowed()
    {
        var devices = new[] { Device("device-1") };

        DeviceRules.IsWithinDeviceLimit(devices, "device-1", devicesLimit: 1)
            .Should().BeTrue();
    }

    [Fact]
    public void InactiveSubscription_BlocksPremiumActions()
    {
        FeatureAccessRules.CanUsePremiumFeature(SubscriptionStatus.Missing)
            .Should().BeFalse();
    }

    private static AccountSubscription Subscription(
        string status,
        DateTimeOffset? periodEnd = null,
        DateTimeOffset? trialEnd = null) =>
        new("sub-1", "user-1", "pro", status, null, periodEnd, trialEnd, 1);

    private static AccountDevice Device(string id) =>
        new("user-1", id, "PC", "Windows", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class FakeAuthGateway : IAuthGateway
    {
        private readonly AccountUser? _user;
        private readonly Exception? _signInError;

        public FakeAuthGateway(AccountUser? user, Exception? signInError = null)
        {
            _user = user;
            _signInError = signInError;
        }

        public bool IsConfigured => true;
        public event EventHandler? AuthStateChanged;
        public Task InitializeAsync() => Task.CompletedTask;

        public Task<AccountUser?> SignIn(string email, string password)
        {
            if (_signInError != null)
                throw _signInError;

            AuthStateChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(_user);
        }

        public Task SignOut() => Task.CompletedTask;
        public AccountUser? GetCurrentUser() => _user;
        public Supabase.Gotrue.Session? GetSession() => null;
        public Task<AccountUser?> RestoreSession() => Task.FromResult(_user);
        public Task<AccountUser?> RefreshSession() => Task.FromResult(_user);
    }
}
