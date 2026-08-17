using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DoubleMark.Desktop.Services.Account;

namespace DoubleMark.Desktop.Services.Api;

public sealed class DoubleMarkApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _http;
    private readonly ApiSessionStore _store;
    private ApiSession? _session;

    public DoubleMarkApiClient(string baseUrl, ApiSessionStore? store = null)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _store = store ?? new ApiSessionStore();
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl + "/"), Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DoubleMark-Desktop/2.1");
        _session = _store.Load();
    }

    public string BaseUrl { get; }
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
    public ApiSession? Session => _session;
    public string? AccessToken => _session?.AccessToken;

    public AccountUser? CurrentUser =>
        _session == null || string.IsNullOrWhiteSpace(_session.UserId)
            ? null
            : new AccountUser(_session.UserId, _session.Email);

    public async Task<AccountUser> RegisterAsync(string email, string password, CancellationToken ct = default)
    {
        var tokens = await PostAnonymousAsync<AuthTokensResponse>("api/auth/register", new { email, password }, ct);
        SaveSession(tokens);
        return CurrentUser!;
    }

    public async Task<AccountUser> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var tokens = await PostAnonymousAsync<AuthTokensResponse>("api/auth/login", new { email, password }, ct);
        SaveSession(tokens);
        return CurrentUser!;
    }

    public async Task<AccountUser?> RestoreAsync(CancellationToken ct = default)
    {
        _session = _store.Load();
        if (_session == null)
            return null;

        if (_session.ExpiresAtUtc <= DateTime.UtcNow.AddMinutes(1))
        {
            try { await RefreshAsync(ct); }
            catch { ClearSession(); return null; }
        }

        return CurrentUser;
    }

    public async Task<AccountUser?> RefreshAsync(CancellationToken ct = default)
    {
        if (_session == null || string.IsNullOrWhiteSpace(_session.RefreshToken))
            return null;

        var tokens = await PostAnonymousAsync<AuthTokensResponse>(
            "api/auth/refresh",
            new { refreshToken = _session.RefreshToken },
            ct);
        SaveSession(tokens);
        return CurrentUser;
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            if (_session != null)
                await SendAsync(HttpMethod.Post, "api/auth/logout", new { refreshToken = _session.RefreshToken }, ct);
        }
        catch
        {
            // best effort
        }
        finally
        {
            ClearSession();
        }
    }

    public Task<ProfileDto?> GetProfileAsync(CancellationToken ct = default) =>
        GetAsync<ProfileDto>("api/me/profile", ct);

    public Task<ProfileDto?> UpdateProfileAsync(ProfileUpdate update, CancellationToken ct = default) =>
        SendAsync<ProfileDto>(HttpMethod.Put, "api/me/profile", new
        {
            organization = update.Organization,
            inn = update.Inn,
            phone = update.Phone
        }, ct);

    public Task<SubscriptionDto?> GetSubscriptionAsync(CancellationToken ct = default) =>
        GetAsync<SubscriptionDto>("api/me/subscription", ct);

    public Task<List<PaymentDto>?> GetPaymentsAsync(CancellationToken ct = default) =>
        GetAsync<List<PaymentDto>>("api/me/payments", ct);

    public Task<List<DeviceDto>?> GetDevicesAsync(CancellationToken ct = default) =>
        GetAsync<List<DeviceDto>>("api/me/devices", ct);

    public Task<DeviceRegistrationDto?> UpsertDeviceAsync(
        string deviceId,
        string deviceName,
        string platform,
        int devicesLimit,
        CancellationToken ct = default) =>
        SendAsync<DeviceRegistrationDto>(HttpMethod.Post, "api/me/devices", new
        {
            deviceId,
            deviceName,
            platform,
            devicesLimit
        }, ct);

    public Task<List<TemplateDto>?> GetTemplatesAsync(CancellationToken ct = default) =>
        GetAsync<List<TemplateDto>>("api/me/templates", ct);

    public Task<TemplateDto?> UpsertTemplateAsync(object body, CancellationToken ct = default) =>
        SendAsync<TemplateDto>(HttpMethod.Put, "api/me/templates", body, ct);

    public Task DeleteTemplateAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, "api/me/templates/" + id, null, ct);

    public Task SetDefaultTemplateAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/me/templates/" + id + "/default", null, ct);

    public Task<List<ScanHistoryDto>?> GetScanHistoryAsync(CancellationToken ct = default) =>
        GetAsync<List<ScanHistoryDto>>("api/me/scan-history", ct);

    public Task<CountDto?> GetScanHistoryCountAsync(CancellationToken ct = default) =>
        GetAsync<CountDto>("api/me/scan-history/count", ct);

    public Task<ScanHistoryDto?> AddScanHistoryAsync(object body, CancellationToken ct = default) =>
        SendAsync<ScanHistoryDto>(HttpMethod.Post, "api/me/scan-history", body, ct);

    public Task DeleteScanHistoryItemAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, "api/me/scan-history/" + id, null, ct);

    public Task ClearScanHistoryAsync(CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, "api/me/scan-history", null, ct);

    private void SaveSession(AuthTokensResponse tokens)
    {
        _session = new ApiSession
        {
            UserId = tokens.UserId.ToString(),
            Email = tokens.Email,
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            ExpiresAtUtc = tokens.ExpiresAtUtc.ToUniversalTime()
        };
        _store.Save(_session);
    }

    private void ClearSession()
    {
        _session = null;
        _store.Clear();
    }

    private async Task<T> PostAnonymousAsync<T>(string path, object body, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(path, body, JsonOptions, ct);
        await EnsureSuccess(response);
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Пустой ответ API.");
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        ApplyAuth(request);
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await RefreshAsync(ct);
            using var retry = new HttpRequestMessage(HttpMethod.Get, path);
            ApplyAuth(retry);
            using var retryResponse = await _http.SendAsync(retry, ct);
            await EnsureSuccess(retryResponse);
            return await retryResponse.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        }

        await EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await SendCoreAsync(method, path, body, ct);
        await EnsureSuccess(response);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return default;
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
    }

    private async Task SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await SendCoreAsync(method, path, body, ct);
        await EnsureSuccess(response);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        ApplyAuth(request);
        if (body != null)
            request.Content = JsonContent.Create(body, options: JsonOptions);

        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
            return response;

        response.Dispose();
        await RefreshAsync(ct);
        var retry = new HttpRequestMessage(method, path);
        ApplyAuth(retry);
        if (body != null)
            retry.Content = JsonContent.Create(body, options: JsonOptions);
        return await _http.SendAsync(retry, ct);
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_session?.AccessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var text = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException($"API {(int)response.StatusCode}: {text}");
    }

    private sealed class AuthTokensResponse
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public DateTime ExpiresAtUtc { get; set; }
        public Guid UserId { get; set; }
        public string Email { get; set; } = "";
    }

    public sealed class ProfileDto
    {
        public Guid UserId { get; set; }
        public string? Email { get; set; }
        public string? Organization { get; set; }
        public string? Inn { get; set; }
        public string? Phone { get; set; }
        public string? Role { get; set; }
    }

    public sealed class SubscriptionDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string? PlanId { get; set; }
        public string Status { get; set; } = "";
        public DateTime? CurrentPeriodStart { get; set; }
        public DateTime? CurrentPeriodEnd { get; set; }
        public DateTime? TrialEndsAt { get; set; }
        public int DevicesLimit { get; set; }
        public string? ProviderSubscriptionId { get; set; }
    }

    public sealed class PaymentDto
    {
        public Guid Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? PlanId { get; set; }
        public decimal? Amount { get; set; }
        public string? Currency { get; set; }
        public string? Status { get; set; }
    }

    public sealed class DeviceDto
    {
        public Guid UserId { get; set; }
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string Platform { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime LastSeenAt { get; set; }
    }

    public sealed class DeviceRegistrationDto
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public DeviceDto? Device { get; set; }
    }

    public sealed class TemplateDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public decimal WidthMm { get; set; }
        public decimal HeightMm { get; set; }
        public string? PrinterName { get; set; }
        public string TemplateData { get; set; } = "{}";
        public bool IsDefault { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class ScanHistoryDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string RawCode { get; set; } = "";
        public string CodeHash { get; set; } = "";
        public string? Source { get; set; }
        public int? GsCount { get; set; }
        public bool HasAi01 { get; set; }
        public bool HasAi21 { get; set; }
        public bool HasAi91 { get; set; }
        public bool HasAi92 { get; set; }
        public string? Gtin { get; set; }
        public string? Serial { get; set; }
        public DateTime ScannedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class CountDto
    {
        public int Count { get; set; }
        public int Limit { get; set; }
    }
}
