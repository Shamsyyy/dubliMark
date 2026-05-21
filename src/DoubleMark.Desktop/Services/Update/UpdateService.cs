using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using DoubleMark.Desktop.Services;

namespace DoubleMark.Desktop.Services.Update;

public sealed class UpdateService
{
    public const string UpdateJsonUrl =
        "https://shamsyyy.github.io/doublemarksite/updates/update.json";

    public const string DownloadsPageUrl =
        "https://shamsyyy.github.io/doublemarksite/download";

    private static readonly string[] AllowedHosts =
    {
        "shamsyyy.github.io",
        "github.com",
        "www.github.com",
        "raw.githubusercontent.com",
        "objects.githubusercontent.com",
        "githubusercontent.com"
    };

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DoubleMark-Updater/1.0");
        return client;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static UpdateService Instance { get; } = new();

    public UpdateCheckResult? LastCheck { get; private set; }

    public string GetCurrentVersion() => AppReleaseInfoProvider.Current.Version;

    public bool IsNewerVersion(string remoteVersion, string currentVersion) =>
        VersionComparer.IsNewer(remoteVersion, currentVersion);

    public bool RequiresMandatoryUpdate(UpdateManifest? manifest)
    {
        if (manifest == null)
            return false;

        var current = GetCurrentVersion();
        if (!string.IsNullOrWhiteSpace(manifest.MinSupportedVersion) &&
            VersionComparer.IsBelowMinimum(current, manifest.MinSupportedVersion))
            return true;

        return manifest.Mandatory &&
               IsNewerVersion(manifest.Version, current);
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var current = GetCurrentVersion();
        Log("Current version: " + current);
        Log("Checking: " + UpdateJsonUrl);

        try
        {
            using var response = await Http.GetAsync(UpdateJsonUrl, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var failed = new UpdateCheckResult(
                    UpdateCheckStatus.Failed,
                    null,
                    "Обновления сейчас недоступны.",
                    "update.json not found (404)");
                LastCheck = failed;
                Log("Update check failed: " + failed.LogMessage);
                return failed;
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version))
            {
                var failed = new UpdateCheckResult(
                    UpdateCheckStatus.Failed,
                    null,
                    "Не удалось прочитать информацию об обновлении.",
                    "update.json is empty or invalid");
                LastCheck = failed;
                Log("Update check failed: " + failed.LogMessage);
                return failed;
            }

            Log("Latest version: " + manifest.Version);
            var available = IsNewerVersion(manifest.Version, current);
            Log("Update available: " + available);

            if (!available)
            {
                var upToDate = new UpdateCheckResult(
                    UpdateCheckStatus.UpToDate,
                    manifest,
                    "У вас последняя версия.",
                    null);
                LastCheck = upToDate;
                return upToDate;
            }

            var result = new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                manifest,
                "Доступна новая версия DoubleMark " + manifest.Version + ".",
                null);
            LastCheck = result;
            return result;
        }
        catch (HttpRequestException ex)
        {
            var failed = new UpdateCheckResult(
                UpdateCheckStatus.Failed,
                null,
                "Не удалось проверить обновления. Проверьте подключение к интернету.",
                ex.Message);
            LastCheck = failed;
            Log("Update check failed: " + ex.Message);
            return failed;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            var failed = new UpdateCheckResult(
                UpdateCheckStatus.Failed,
                null,
                "Не удалось проверить обновления. Проверьте подключение к интернету.",
                ex.Message);
            LastCheck = failed;
            Log("Update check failed: timeout");
            return failed;
        }
        catch (Exception ex)
        {
            var failed = new UpdateCheckResult(
                UpdateCheckStatus.Failed,
                null,
                "Обновления сейчас недоступны.",
                ex.GetType().Name + ": " + ex.Message);
            LastCheck = failed;
            Log("Update check failed: " + failed.LogMessage);
            return failed;
        }
    }

    public async Task<UpdateDownloadResult> DownloadUpdateAsync(
        UpdateManifest manifest,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidateInstallerUrl(manifest.InstallerUrl, out var uri, out var urlError))
        {
            Log("Update failed: " + urlError);
            return new UpdateDownloadResult(UpdateDownloadStatus.Failed, null, urlError, urlError);
        }

        var updatesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DoubleMark",
            "Updates");
        Directory.CreateDirectory(updatesDir);

        var fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            fileName = "DoubleMarkSetup-" + manifest.Version + ".exe";

        var targetPath = Path.Combine(updatesDir, fileName);
        if (File.Exists(targetPath))
        {
            try { File.Delete(targetPath); }
            catch { /* best effort */ }
        }

        Log("Download started: " + manifest.InstallerUrl);

        var tempPath = targetPath + ".download";
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            if (attempt > 1)
                Log("Download retry attempt " + attempt);

            try
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); }
                    catch { /* best effort */ }
                }

                using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var statusMsg = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                    Log("Update failed: " + statusMsg);
                    lastError = new HttpRequestException(statusMsg);
                    if (attempt < 2 && IsTransientStatus(response.StatusCode))
                        continue;

                    return new UpdateDownloadResult(
                        UpdateDownloadStatus.Failed,
                        null,
                        BuildDownloadErrorMessage(response.StatusCode),
                        statusMsg);
                }

                var total = response.Content.Headers.ContentLength ?? -1;
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using (var output = File.Create(tempPath))
                {
                    var buffer = new byte[81920];
                    long readTotal = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        readTotal += read;
                        if (total > 0)
                        {
                            var percent = readTotal * 100.0 / total;
                            progress?.Report(percent);
                            if (readTotal % (1024 * 512) < read)
                                Log("Download progress: " + percent.ToString("0") + "%");
                        }
                    }
                }

                if (File.Exists(targetPath))
                {
                    try { File.Delete(targetPath); }
                    catch { /* best effort */ }
                }

                File.Move(tempPath, targetPath);

                progress?.Report(100);
                Log("Download completed: " + targetPath);

                if (!VerifySha256(targetPath, manifest.Sha256))
                {
                    var actualHash = ComputeSha256(targetPath);
                    Log("SHA256 expected: " + manifest.Sha256);
                    Log("SHA256 actual: " + actualHash);
                    Log("Update failed: hash mismatch");
                    try { File.Delete(targetPath); }
                    catch { /* best effort */ }

                    return new UpdateDownloadResult(
                        UpdateDownloadStatus.HashMismatch,
                        null,
                        "Файл обновления повреждён или не прошёл проверку безопасности. Скачайте установщик вручную со страницы DoubleMark.",
                        "SHA256 mismatch");
                }

                Log("SHA256 verified");
                return new UpdateDownloadResult(UpdateDownloadStatus.Success, targetPath, null, null);
            }
            catch (Exception ex) when (attempt < 2 && IsTransientException(ex))
            {
                lastError = ex;
                Log("Download transient error: " + ex.Message);
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); }
                    catch { /* best effort */ }
                }
            }
        }

        if (File.Exists(targetPath))
        {
            try { File.Delete(targetPath); }
            catch { /* best effort */ }
        }

        Log("Update failed: " + (lastError?.Message ?? "unknown"));
        return new UpdateDownloadResult(
            UpdateDownloadStatus.Failed,
            null,
            BuildDownloadErrorMessage(lastError),
            lastError?.Message);
    }

    private static bool IsTransientStatus(System.Net.HttpStatusCode status) =>
        status is System.Net.HttpStatusCode.RequestTimeout
            or System.Net.HttpStatusCode.TooManyRequests
            or System.Net.HttpStatusCode.InternalServerError
            or System.Net.HttpStatusCode.BadGateway
            or System.Net.HttpStatusCode.ServiceUnavailable
            or System.Net.HttpStatusCode.GatewayTimeout;

    private static bool IsTransientException(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or IOException;

    private static string BuildDownloadErrorMessage(System.Net.HttpStatusCode status) =>
        status switch
        {
            System.Net.HttpStatusCode.NotFound =>
                "Установщик не найден на сервере (404). Загрузите файл на сайт или скачайте вручную через «Страница загрузки».",
            System.Net.HttpStatusCode.Forbidden =>
                "Доступ к файлу обновления запрещён (403). Скачайте установщик вручную с сайта DoubleMark.",
            _ => "Не удалось скачать обновление. Проверьте интернет и антивирус (~80 МБ), либо скачайте вручную через «Страница загрузки»."
        };

    private static string BuildDownloadErrorMessage(Exception? ex)
    {
        if (ex is HttpRequestException { StatusCode: { } code })
            return BuildDownloadErrorMessage(code);

        if (ex is TaskCanceledException)
            return "Превышено время ожидания при скачивании (~80 МБ). Повторите позже или скачайте вручную с сайта.";

        if (ex is IOException)
            return "Не удалось сохранить файл обновления. Проверьте свободное место на диске и антивирус.";

        return "Не удалось скачать обновление. Скачайте установщик вручную через «Страница загрузки».";
    }

    public bool VerifySha256(string filePath, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHash) || !File.Exists(filePath))
            return false;

        var actual = ComputeSha256(filePath);
        return string.Equals(
            actual,
            expectedHash.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    public void StartInstallerAndExit(string installerPath)
    {
        if (!File.Exists(installerPath))
            throw new FileNotFoundException("Installer not found.", installerPath);

        Log("Starting installer: " + installerPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true
        });

        System.Windows.Application.Current.Shutdown();
    }

    public static bool TryValidateInstallerUrl(string url, out Uri uri, out string error)
    {
        uri = null!;
        error = "";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            error = "Некорректная ссылка на установщик.";
            return false;
        }

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "Ссылка на установщик должна использовать HTTPS.";
            return false;
        }

        if (!AllowedHosts.Contains(parsed.Host, StringComparer.OrdinalIgnoreCase))
        {
            error = "Обновление доступно только с доверенного домена DoubleMark.";
            return false;
        }

        uri = parsed;
        return true;
    }

    private static void Log(string message) =>
        LoggingService.Info("Update", message);
}
