using System.Security.Cryptography.X509Certificates;
using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Settings;

namespace DoubleMark.Desktop.Services.Crpt;

/// <summary>
/// SUZ order orchestration for Desktop (spec §9).
/// </summary>
public sealed class CrptSuzService : ICrptSuzService, IDisposable
{
    private static readonly TimeSpan DefaultPollTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan InitialPollDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxPollDelay = TimeSpan.FromSeconds(30);

    private readonly ICrptSettingsStore _settingsStore;
    private readonly ICrptCertificateProvider _certificateProvider;
    private readonly CrptOrderRepository _orderRepository;
    private readonly Func<CrptConnectionSettings, ICrptSuzClient> _suzClientFactory;
    private readonly Func<CrptConnectionSettings, X509Certificate2, CancellationToken, Task<string>> _suzTokenProvider;

    public CrptSuzService(
        ICrptSettingsStore settingsStore,
        ICrptCertificateProvider certificateProvider,
        CrptOrderRepository orderRepository,
        Func<CrptConnectionSettings, ICrptSuzClient>? suzClientFactory = null,
        Func<CrptConnectionSettings, X509Certificate2, CancellationToken, Task<string>>? suzTokenProvider = null)
    {
        _settingsStore = settingsStore;
        _certificateProvider = certificateProvider;
        _orderRepository = orderRepository;
        _suzClientFactory = suzClientFactory ?? (connection => new CrptSuzClient(connection));
        _suzTokenProvider = suzTokenProvider ?? GetSuzClientTokenFromAuthAsync;
    }

    public async Task<string> CreateOrderAsync(
        string gtin,
        int quantity,
        string productGroup,
        int? templateId = null,
        CancellationToken cancellationToken = default)
    {
        var connection = BuildConnectionSettings(productGroup, templateId);
        var certificate = _certificateProvider.FindCertificate(connection);
        var suzToken = await GetSuzClientTokenAsync(connection, certificate, cancellationToken);

        var request = new CreateSuzOrderRequest
        {
            ProductGroup = productGroup,
            ContactPerson = connection.ContactPerson,
            Products =
            [
                new CreateSuzOrderProduct
                {
                    Gtin = gtin,
                    Quantity = quantity,
                    TemplateId = templateId ?? connection.TemplateId,
                },
            ],
        };

        using var client = _suzClientFactory(connection);
        return await client.CreateOrderAsync(suzToken, certificate, request, cancellationToken);
    }

    public async Task<SuzOrderRemoteStatus> PollUntilReadyAsync(
        string remoteOrderId,
        string gtin,
        TimeSpan? timeout = null,
        IProgress<SuzOrderProgress>? progress = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var connection = BuildConnectionSettings(productGroup: null, templateId: null);
        var suzToken = await GetSuzClientTokenAsync(
            connection,
            _certificateProvider.FindCertificate(connection),
            cancellationToken);

        using var client = _suzClientFactory(connection);
        var deadline = DateTimeOffset.UtcNow + (timeout ?? DefaultPollTimeout);
        var delay = pollInterval ?? InitialPollDelay;
        var pollCount = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await client.GetOrderStatusAsync(suzToken, remoteOrderId, gtin, cancellationToken);

            if (status.IsReadyForDownload)
            {
                progress?.Report(new SuzOrderProgress("Готово к скачиванию", 100));
                return status.Status;
            }

            if (status.IsTerminalFailure)
                throw new CrptSuzException(status.ErrorMessage ?? "SUZ order failed");

            pollCount++;
            var percent = Math.Min(95, pollCount * 5);
            progress?.Report(new SuzOrderProgress("Ожидание СУЗ", percent));

            await Task.Delay(delay, cancellationToken);
            if (pollInterval is null)
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, MaxPollDelay.TotalMilliseconds));
        }

        throw new CrptSuzException("SUZ order polling timed out.");
    }

    public async Task<IReadOnlyList<CrptMarkingCodeItem>> DownloadCodesAsync(
        string localOrderId,
        string remoteOrderId,
        string gtin,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        var connection = BuildConnectionSettings(productGroup: null, templateId: null);
        var suzToken = await GetSuzClientTokenAsync(
            connection,
            _certificateProvider.FindCertificate(connection),
            cancellationToken);

        using var client = _suzClientFactory(connection);
        var payloads = new List<string>();
        string? lastBlockId = null;

        while (true)
        {
            var block = await client.GetCodesBlockAsync(
                suzToken,
                remoteOrderId,
                gtin,
                quantity,
                lastBlockId,
                cancellationToken);

            payloads.AddRange(CrptSuzClient.ValidateAndParseCodes(block));

            if (block.IsLast)
                break;

            lastBlockId = block.BlockId;
        }

        await _orderRepository.SaveCodesAsync(localOrderId, payloads, cancellationToken);
        return await _orderRepository.ListCodesByOrderAsync(localOrderId, cancellationToken);
    }

    public async Task CloseOrderAsync(string remoteOrderId, CancellationToken cancellationToken = default)
    {
        var connection = BuildConnectionSettings(productGroup: null, templateId: null);
        var suzToken = await GetSuzClientTokenAsync(
            connection,
            _certificateProvider.FindCertificate(connection),
            cancellationToken);

        using var client = _suzClientFactory(connection);
        await client.CloseOrderAsync(suzToken, remoteOrderId, cancellationToken);
    }

    public async Task<CrptSuzOrder> CreateAndDownloadOrderAsync(
        string gtin,
        int quantity,
        string productGroup,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var settings = _settingsStore.LoadSettings();
        var templateId = settings.ResolveTemplateId(productGroup);
        var localId = Guid.NewGuid().ToString("N");
        var createdAt = DateTimeOffset.UtcNow;

        var pendingOrder = new CrptSuzOrder(
            LocalId: localId,
            RemoteOrderId: null,
            Gtin: gtin,
            RequestedQuantity: quantity,
            ReceivedQuantity: 0,
            ProductGroup: productGroup,
            RemoteStatus: SuzOrderRemoteStatus.Pending,
            CreatedAt: createdAt,
            CompletedAt: null,
            ErrorMessage: null);

        await _orderRepository.SaveAsync(pendingOrder, cancellationToken);

        try
        {
            progress?.Report("Создание заказа СУЗ…");
            var remoteOrderId = await CreateOrderAsync(
                gtin,
                quantity,
                productGroup,
                templateId,
                cancellationToken);

            await _orderRepository.SaveAsync(
                pendingOrder with { RemoteOrderId = remoteOrderId },
                cancellationToken);

            progress?.Report("Ожидание готовности СУЗ…");
            var readyProgress = progress is null
                ? null
                : new Progress<SuzOrderProgress>(p => progress.Report(p.Stage));

            await PollUntilReadyAsync(
                remoteOrderId,
                gtin,
                progress: readyProgress,
                cancellationToken: cancellationToken);

            progress?.Report("Скачивание кодов…");
            var codes = await DownloadCodesAsync(
                localId,
                remoteOrderId,
                gtin,
                quantity,
                cancellationToken);

            progress?.Report("Закрытие заказа…");
            await CloseOrderAsync(remoteOrderId, cancellationToken);

            var completed = pendingOrder with
            {
                RemoteOrderId = remoteOrderId,
                ReceivedQuantity = codes.Count,
                RemoteStatus = SuzOrderRemoteStatus.Closed,
                CompletedAt = DateTimeOffset.UtcNow,
            };

            await _orderRepository.SaveAsync(completed, cancellationToken);
            progress?.Report("Заказ завершён.");
            return completed;
        }
        catch (Exception ex)
        {
            var failed = pendingOrder with
            {
                RemoteStatus = SuzOrderRemoteStatus.Error,
                ErrorMessage = ex.Message,
            };
            await _orderRepository.SaveAsync(failed, cancellationToken);
            throw;
        }
    }

    private Task<string> GetSuzClientTokenAsync(
        CrptConnectionSettings connection,
        X509Certificate2 certificate,
        CancellationToken cancellationToken) =>
        _suzTokenProvider(connection, certificate, cancellationToken);

    private async Task<string> GetSuzClientTokenFromAuthAsync(
        CrptConnectionSettings connection,
        X509Certificate2 certificate,
        CancellationToken cancellationToken)
    {
        using var authClient = new CrptAuthClient(connection);
        var token = await authClient.AuthenticateForSuzAsync(certificate, cancellationToken);
        return token.Value;
    }

    private CrptConnectionSettings BuildConnectionSettings(string? productGroup, int? templateId)
    {
        var settings = _settingsStore.LoadSettings();
        var secrets = _settingsStore.LoadSecrets();
        return CrptConnectionSettingsBridge.ToConnectionSettings(settings, secrets, productGroup, templateId);
    }

    public void Dispose()
    {
    }
}
