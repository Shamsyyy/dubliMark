using System.Security.Cryptography.X509Certificates;

namespace DoubleMark.Crpt;

public interface ICrptSuzClient : IDisposable
{
    Task<string> CreateOrderAsync(
        string suzClientToken,
        X509Certificate2 certificate,
        CreateSuzOrderRequest request,
        CancellationToken ct = default);

    Task<CrptSuzOrderStatus> GetOrderStatusAsync(
        string suzClientToken,
        string remoteOrderId,
        string gtin,
        CancellationToken ct = default);

    Task<CrptSuzCodesBlock> GetCodesBlockAsync(
        string suzClientToken,
        string remoteOrderId,
        string gtin,
        int quantity,
        string? lastBlockId,
        CancellationToken ct = default);

    Task CloseOrderAsync(
        string suzClientToken,
        string remoteOrderId,
        CancellationToken ct = default);
}
