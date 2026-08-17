using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using DoubleMark.Core.Crpt;
using DoubleMark.Core.Parsing;

namespace DoubleMark.Crpt;

public sealed class CrptSuzClient : ICrptSuzClient
{
    private readonly HttpClient _http;
    private readonly CrptConnectionSettings _settings;
    private readonly bool _disposeHttpClient;

    public CrptSuzClient(CrptConnectionSettings settings, HttpClient? httpClient = null, bool disposeHttpClient = true)
    {
        _settings = settings;
        _disposeHttpClient = httpClient is null || disposeHttpClient;
        _http = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri(CrptHttp.NormalizeBaseUrl(settings.SuzBaseUrl)),
        };
    }

    public async Task<string> PingAsync(string suzClientToken, CancellationToken ct = default)
    {
        var url = $"api/v3/ping?omsId={Uri.EscapeDataString(_settings.OmsId)}";
        using var request = await CreateSuzRequestAsync(HttpMethod.Get, url, content: null, suzClientToken, ct);
        using var response = await _http.SendAsync(request, ct);
        return await CrptHttp.ReadSuccessBodyAsync(response, ct);
    }

    public async Task<string> CreateOrderAsync(
        string suzClientToken,
        X509Certificate2 certificate,
        CreateSuzOrderRequest request,
        CancellationToken ct = default)
    {
        var body = BuildOrderBody(request);
        var compactJson = CrptJson.ToCompact(body);
        var signature = CryptoProCadesSigner.SignDetachedOrderBodyBase64(compactJson, certificate);

        var url = $"api/v3/order?omsId={Uri.EscapeDataString(_settings.OmsId)}";
        using var httpRequest = await CreateSuzRequestAsync(
            HttpMethod.Post,
            url,
            new StringContent(compactJson, Encoding.UTF8, "application/json"),
            suzClientToken,
            ct);
        httpRequest.Headers.Add("X-Signature", signature);

        using var response = await _http.SendAsync(httpRequest, ct);
        var responseText = await CrptHttp.ReadSuccessBodyAsync(response, ct);
        return CrptSuzResponseParser.ParseCreateOrderId(responseText);
    }

    public async Task<CrptSuzOrderStatus> GetOrderStatusAsync(
        string suzClientToken,
        string remoteOrderId,
        string gtin,
        CancellationToken ct = default)
    {
        var url =
            $"api/v3/order/status?omsId={Uri.EscapeDataString(_settings.OmsId)}" +
            $"&orderId={Uri.EscapeDataString(remoteOrderId)}" +
            $"&gtin={Uri.EscapeDataString(gtin)}";

        using var request = await CreateSuzRequestAsync(HttpMethod.Get, url, content: null, suzClientToken, ct);
        using var response = await _http.SendAsync(request, ct);
        var responseText = await CrptHttp.ReadSuccessBodyAsync(response, ct);
        return CrptSuzResponseParser.ParseOrderStatus(responseText);
    }

    public async Task<CrptSuzCodesBlock> GetCodesBlockAsync(
        string suzClientToken,
        string remoteOrderId,
        string gtin,
        int quantity,
        string? lastBlockId,
        CancellationToken ct = default)
    {
        var url = new StringBuilder(
            $"api/v3/codes?omsId={Uri.EscapeDataString(_settings.OmsId)}" +
            $"&orderId={Uri.EscapeDataString(remoteOrderId)}" +
            $"&gtin={Uri.EscapeDataString(gtin)}" +
            $"&quantity={quantity}");

        if (!string.IsNullOrWhiteSpace(lastBlockId))
            url.Append("&blockId=").Append(Uri.EscapeDataString(lastBlockId));

        using var request = await CreateSuzRequestAsync(HttpMethod.Get, url.ToString(), content: null, suzClientToken, ct);
        using var response = await _http.SendAsync(request, ct);
        var responseText = await CrptHttp.ReadSuccessBodyAsync(response, ct);
        return CrptSuzResponseParser.ParseCodesBlock(responseText);
    }

    public async Task CloseOrderAsync(
        string suzClientToken,
        string remoteOrderId,
        CancellationToken ct = default)
    {
        var url = $"api/v3/order/close?omsId={Uri.EscapeDataString(_settings.OmsId)}";
        var body = new Dictionary<string, object> { ["orderId"] = remoteOrderId };
        var compactJson = CrptJson.ToCompact(body);

        using var request = await CreateSuzRequestAsync(
            HttpMethod.Post,
            url,
            new StringContent(compactJson, Encoding.UTF8, "application/json"),
            suzClientToken,
            ct);

        using var response = await _http.SendAsync(request, ct);
        _ = await CrptHttp.ReadSuccessBodyAsync(response, ct);
    }

    public async Task<CrptCreateOrderResult> CreateOrderAsync(
        string suzClientToken,
        X509Certificate2 certificate,
        string gtin,
        int quantity,
        CancellationToken ct)
    {
        var request = BuildLegacyCreateRequest(gtin, quantity);
        var orderId = await CreateOrderAsync(suzClientToken, certificate, request, ct);
        return new CrptCreateOrderResult(orderId, ExpectedCompleteTimestampMs: null, RawResponse: orderId);
    }

    public async Task<CrptOrderFlowResult> CreateAndDownloadAsync(
        string suzClientToken,
        X509Certificate2 certificate,
        string gtin,
        int quantity,
        CancellationToken ct)
    {
        var created = await CreateOrderAsync(suzClientToken, certificate, gtin, quantity, ct);

        for (var attempt = 0; attempt < 60; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            var status = await GetOrderStatusAsync(suzClientToken, created.OrderId, gtin, ct);

            if (status.IsReadyForDownload)
                break;

            if (status.IsTerminalFailure)
                throw new InvalidOperationException($"Order rejected: {status.RawJson}");
        }

        var codesResponse = await DownloadAllCodesJsonAsync(
            suzClientToken,
            created.OrderId,
            gtin,
            quantity,
            ct);

        await CloseOrderAsync(suzClientToken, created.OrderId, ct);
        return new CrptOrderFlowResult(created.OrderId, codesResponse);
    }

    public async Task<string> GetOrderStatusRawAsync(string suzClientToken, string orderId, string gtin, CancellationToken ct)
    {
        var status = await GetOrderStatusAsync(suzClientToken, orderId, gtin, ct);
        return status.RawJson;
    }

    public async Task<string> GetCodesAsync(
        string suzClientToken,
        string orderId,
        string gtin,
        int quantity,
        string? blockId,
        CancellationToken ct)
    {
        var block = await GetCodesBlockAsync(suzClientToken, orderId, gtin, quantity, blockId, ct);
        return CrptJson.ToCompact(new Dictionary<string, object?>
        {
            ["blockId"] = block.BlockId,
            ["codes"] = block.Codes,
            ["isLast"] = block.IsLast,
        });
    }

    public async Task<CrptDocumentSubmitResult> SendUtilisationAsync(
        string suzClientToken,
        X509Certificate2 certificate,
        IReadOnlyList<string> markingCodes,
        CancellationToken ct)
    {
        var productionDate = _settings.UtilisationProductionDate
            ?? DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        var expirationDate = _settings.UtilisationExpirationDate
            ?? DateOnly.FromDateTime(DateTime.Now.AddYears(3)).ToString("yyyy-MM-dd");

        var body = new Dictionary<string, object>
        {
            ["productGroup"] = _settings.ProductGroup,
            ["sntins"] = markingCodes,
            ["attributes"] = new Dictionary<string, object>
            {
                ["productionDate"] = productionDate,
                ["expirationDate"] = expirationDate,
            },
        };

        var compactJson = CrptJson.ToCompact(body);
        var signature = CryptoProCadesSigner.SignDetachedOrderBodyBase64(compactJson, certificate);

        var url = $"api/v3/utilisation?omsId={Uri.EscapeDataString(_settings.OmsId)}";
        using var request = await CreateSuzRequestAsync(
            HttpMethod.Post,
            url,
            new StringContent(compactJson, Encoding.UTF8, "application/json"),
            suzClientToken,
            ct);
        request.Headers.Add("X-Signature", signature);

        using var response = await _http.SendAsync(request, ct);
        var text = await CrptHttp.ReadSuccessBodyAsync(response, ct);
        var docId = TryReadDocumentId(text) ?? Guid.NewGuid().ToString();
        return new CrptDocumentSubmitResult(docId, text);
    }

    public static IReadOnlyList<string> ParseCodesFromOrderFile(string json) =>
        CrptSuzResponseParser.ParseCodesBlock(json).Codes;

    public static IReadOnlyList<string> ValidateAndParseCodes(CrptSuzCodesBlock block)
    {
        var items = new List<string>(block.Codes.Count);
        foreach (var raw in block.Codes)
        {
            if (!Gs1BarcodeEncoding.LooksLikeGs1Cz(raw))
                throw new CrptSuzException("SUZ returned non-GS1 payload");

            items.Add(raw);
        }

        return items;
    }

    public static Dictionary<string, object> BuildOrderBody(CreateSuzOrderRequest request)
    {
        if (request.Products.Count == 0)
            throw new ArgumentException("At least one product is required.", nameof(request));

        var attributes = request.Attributes is not null
            ? new Dictionary<string, object>(request.Attributes)
            : new Dictionary<string, object>();

        if (!string.IsNullOrWhiteSpace(request.ContactPerson) && !attributes.ContainsKey("contactPerson"))
            attributes["contactPerson"] = request.ContactPerson!;

        if (!attributes.ContainsKey("releaseMethodType"))
            attributes["releaseMethodType"] = "PRODUCTION";
        if (!attributes.ContainsKey("createMethodType"))
            attributes["createMethodType"] = "SELF_MADE";

        var products = new List<Dictionary<string, object>>(request.Products.Count);
        foreach (var product in request.Products)
        {
            var entry = new Dictionary<string, object>
            {
                ["gtin"] = product.Gtin,
                ["quantity"] = product.Quantity,
                ["serialNumberType"] = product.SerialNumberType ?? "OPERATOR",
                ["cisType"] = product.CisType ?? "UNIT",
            };

            if (product.TemplateId.HasValue)
                entry["templateId"] = product.TemplateId.Value;

            products.Add(entry);
        }

        return new Dictionary<string, object>
        {
            ["productGroup"] = request.ProductGroup,
            ["attributes"] = attributes,
            ["products"] = products.ToArray(),
        };
    }

    private CreateSuzOrderRequest BuildLegacyCreateRequest(string gtin, int quantity) =>
        new()
        {
            ProductGroup = _settings.ProductGroup,
            ContactPerson = _settings.ContactPerson,
            Products =
            [
                new CreateSuzOrderProduct
                {
                    Gtin = gtin,
                    Quantity = quantity,
                    TemplateId = _settings.TemplateId,
                },
            ],
        };

    private async Task<string> DownloadAllCodesJsonAsync(
        string suzClientToken,
        string remoteOrderId,
        string gtin,
        int quantity,
        CancellationToken ct)
    {
        string? lastBlockId = null;
        string? lastBlockJson = null;

        while (true)
        {
            var block = await GetCodesBlockAsync(suzClientToken, remoteOrderId, gtin, quantity, lastBlockId, ct);
            ValidateAndParseCodes(block);
            lastBlockJson = CrptJson.ToCompact(new Dictionary<string, object?>
            {
                ["blockId"] = block.BlockId,
                ["codes"] = block.Codes,
                ["isLast"] = block.IsLast,
            });

            if (block.IsLast)
                break;

            lastBlockId = block.BlockId;
        }

        return lastBlockJson ?? """{"codes":[]}""";
    }

    private Task<HttpRequestMessage> CreateSuzRequestAsync(
        HttpMethod method,
        string relativeUrl,
        HttpContent? content,
        string suzClientToken,
        CancellationToken ct)
    {
        _ = ct;
        var request = new HttpRequestMessage(method, relativeUrl) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("clientToken", suzClientToken);

        if (!string.IsNullOrWhiteSpace(_settings.OmsId))
            request.Headers.Add("X-OMS-Id", _settings.OmsId);

        return Task.FromResult(request);
    }

    private static string? TryReadDocumentId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("reportId", out var reportId))
                return reportId.GetString();
            if (doc.RootElement.TryGetProperty("id", out var id))
                return id.GetString();
        }
        catch
        {
            // ignore parse errors
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
            _http.Dispose();
    }
}
