namespace DoubleMark.Desktop.Services.Account;

public sealed class PaymentService
{
    private readonly SubscriptionService _subscriptionService;

    public PaymentService(SubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public Task<IReadOnlyList<AccountPayment>> GetUserPayments(string userId) =>
        _subscriptionService.GetUserPayments(userId);
}
