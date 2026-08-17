using System.Reflection;
using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Settings;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptSettingsStoreTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly CrptSettingsStore _store;

    public CrptSettingsStoreTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "DoubleMark.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _store = new CrptSettingsStore(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp test directory.
        }
    }

    [Fact]
    public void DefaultPaths_AreUnderAppSettingsDirectory()
    {
        var store = new CrptSettingsStore();

        store.SettingsPath.Should().Be(
            Path.Combine(AppSettings.SettingsDirectory, CrptSettingsStore.SettingsFileName));
        store.SecretsPath.Should().Be(
            Path.Combine(AppSettings.SettingsDirectory, CrptSettingsStore.SecretsFileName));
    }

    [Fact]
    public void CustomDirectory_PathsUseProvidedDirectory()
    {
        _store.SettingsPath.Should().Be(
            Path.Combine(_tempDirectory, CrptSettingsStore.SettingsFileName));
        _store.SecretsPath.Should().Be(
            Path.Combine(_tempDirectory, CrptSettingsStore.SecretsFileName));
    }

    [Fact]
    public void EffectiveProductCatalogPath_DefaultsToCrptCatalogJsonUnderSettingsDirectory()
    {
        var settings = new CrptSettings();

        settings.EffectiveProductCatalogPath.Should().Be(
            Path.Combine(AppSettings.SettingsDirectory, CrptSettings.DefaultProductCatalogFileName));
    }

    [Fact]
    public void LoadMerged_WhenFilesMissing_ReturnsEmptySnapshot()
    {
        var snapshot = _store.LoadMerged();

        snapshot.Settings.Should().BeEquivalentTo(new CrptSettings());
        snapshot.Secrets.Should().BeEquivalentTo(new CrptSecrets());
    }

    [Fact]
    public void SaveLoadMerged_RoundtripViaSnapshotMergeSplit()
    {
        var settings = new CrptSettings
        {
            Inn = "0000000000",
            ProductGroups = ["chemistry"],
            NkUseJwtFromTrueApi = false,
        };
        var secrets = new CrptSecrets
        {
            OmsId = "00000000-0000-4000-8000-000000000001",
            ConnectionId = "00000000-0000-4000-8000-000000000002",
            CertificateThumbprint = "ABCDEF1234567890",
            NkApiKey = "nk-secret-api-key",
        };

        var snapshot = CrptSettingsSnapshot.Merge(settings, secrets);
        _store.Save(snapshot);

        var loaded = _store.LoadMerged();
        var (splitSettings, splitSecrets) = loaded.Split();

        splitSettings.Should().BeEquivalentTo(settings);
        splitSecrets.Should().BeEquivalentTo(secrets);
        loaded.Should().BeEquivalentTo(snapshot);
    }

    [Fact]
    public void LoadSecrets_WhenDpapiFileIsCorrupt_ReturnsEmptySecrets()
    {
        var settings = new CrptSettings { Inn = "0000000000" };
        var secrets = new CrptSecrets
        {
            OmsId = "00000000-0000-4000-8000-000000000001",
            CertificateThumbprint = "ABCDEF1234567890",
        };
        _store.Save(settings, secrets);

        File.WriteAllBytes(_store.SecretsPath, [0x01, 0x02, 0x03, 0xFF, 0xFE]);

        var loadedSecrets = _store.LoadSecrets();
        loadedSecrets.Should().BeEquivalentTo(new CrptSecrets());
    }

    [Fact]
    public void LoadSettings_WhenJsonIsCorrupt_ReturnsEmptySettings()
    {
        File.WriteAllText(_store.SettingsPath, "{ not valid json");

        var loadedSettings = _store.LoadSettings();
        loadedSettings.Should().BeEquivalentTo(new CrptSettings());
    }

    [Fact]
    public void Save_SecretsAreNotStoredInPlainSettingsJson()
    {
        var settings = new CrptSettings
        {
            Inn = "0000000000",
            SuzBaseUrl = CrptSettings.DefaultSuzBaseUrl,
        };
        var secrets = new CrptSecrets
        {
            OmsId = "00000000-0000-4000-8000-000000000001",
            ConnectionId = "00000000-0000-4000-8000-000000000002",
            CertificateThumbprint = "ABCDEF1234567890",
            NkApiKey = "nk-secret-api-key",
        };

        _store.Save(settings, secrets);

        var plainJson = File.ReadAllText(_store.SettingsPath);
        plainJson.Should().Contain("0000000000");
        plainJson.Should().NotContain(secrets.OmsId);
        plainJson.Should().NotContain(secrets.ConnectionId);
        plainJson.Should().NotContain(secrets.CertificateThumbprint);
        plainJson.Should().NotContain(secrets.NkApiKey);
        File.Exists(_store.SecretsPath).Should().BeTrue();
    }

    [Fact]
    public void SaveLoad_RoundtripPreservesSettingsAndSecrets()
    {
        var settings = new CrptSettings
        {
            Environment = CrptEnvironment.Production,
            Roles = [CrptOrganizationRole.Manufacturer, CrptOrganizationRole.Importer],
            ProductGroups = ["chemistry", "milk"],
            Inn = "0000000000",
            Gs1OrganizationNumber = "0000000000000",
            SuzBaseUrl = CrptSettings.DefaultSuzBaseUrl,
            TrueApiBaseUrl = CrptSettings.DefaultTrueApiBaseUrl,
            AutoRefreshToken = false,
            ContactPerson = "Test Contact",
            NkBaseUrl = CrptSettings.DefaultNkBaseUrl,
            NkUseJwtFromTrueApi = false,
            NkSyncOnlyPublished = false,
            NkSyncOnlySigned = false,
            ProductGroupTemplateDefaults = new Dictionary<string, int> { ["chemistry"] = 46 },
            ProductCatalogPath = Path.Combine(_tempDirectory, "custom-catalog.json"),
        };
        var secrets = new CrptSecrets
        {
            OmsId = "00000000-0000-4000-8000-000000000001",
            ConnectionId = "00000000-0000-4000-8000-000000000002",
            CertificateThumbprint = "ABCDEF1234567890",
            NkApiKey = "nk-secret-api-key",
        };

        _store.Save(settings, secrets);

        var loadedSettings = _store.LoadSettings();
        var loadedSecrets = _store.LoadSecrets();

        loadedSettings.Should().BeEquivalentTo(settings);
        loadedSecrets.Should().BeEquivalentTo(secrets);
    }

    [Fact]
    public void SaveLoad_RoundtripPreservesNkCategorySettings()
    {
        var settings = new CrptSettings
        {
            Inn = "0000000000",
            NkKnownCategories = ["Synthetic Alpha", "Synthetic Beta"],
            NkVisibleCategories = ["Synthetic Alpha"],
        };
        var secrets = new CrptSecrets();

        _store.Save(settings, secrets);

        var loadedSettings = _store.LoadSettings();
        loadedSettings.NkKnownCategories.Should().Equal("Synthetic Alpha", "Synthetic Beta");
        loadedSettings.NkVisibleCategories.Should().Equal("Synthetic Alpha");

        var plainJson = File.ReadAllText(_store.SettingsPath);
        plainJson.Should().Contain("nkKnownCategories");
        plainJson.Should().Contain("nkVisibleCategories");
    }

    [Fact]
    public void LoadSettings_MissingNkCategoryLists_DefaultsToEmpty()
    {
        File.WriteAllText(_store.SettingsPath, """{ "inn": "0000000000" }""");

        var settings = _store.LoadSettings();

        settings.NkKnownCategories.Should().BeEmpty();
        settings.NkVisibleCategories.Should().BeEmpty();
    }

    [Fact]
    public void LoadSettings_MissingNkSyncFlags_DefaultsToFalse()
    {
        File.WriteAllText(_store.SettingsPath, """{ "inn": "0000000000" }""");

        var settings = _store.LoadSettings();

        settings.NkSyncOnlyPublished.Should().BeFalse();
        settings.NkSyncOnlySigned.Should().BeFalse();
    }

    [Fact]
    public void DpapiProtector_RoundtripPreservesPayload()
    {
        var protector = new DpapiCrptSecretsProtector();
        var payload = "{\"omsId\":\"00000000-0000-4000-8000-000000000001\"}"u8.ToArray();

        var protectedBytes = protector.Protect(payload);
        var restored = protector.Unprotect(protectedBytes);

        protectedBytes.Should().NotBeEquivalentTo(payload);
        restored.Should().BeEquivalentTo(payload);
    }

    [Fact]
    public void DefaultUrls_MatchSpecPlaceholderExample()
    {
        var settings = new CrptSettings();

        settings.SuzBaseUrl.Should().Be("https://suzgrid.crpt.ru/");
        settings.TrueApiBaseUrl.Should().Be("https://markirovka.crpt.ru/");
        settings.NkBaseUrl.Should().Be(CrptUrl.ProductionNkBaseUrl);
        settings.NkUseJwtFromTrueApi.Should().BeTrue();
    }

    [Fact]
    public void LoadSettings_MigratesLegacyLatinApiNkBaseUrlFromDisk()
    {
        var legacyJson = $$"""
            {
              "environment": "production",
              "nkBaseUrl": "https://{{CrptUrl.LegacyLatinApiNkPunycodeHost}}/"
            }
            """;
        File.WriteAllText(_store.SettingsPath, legacyJson);

        var settings = _store.LoadSettings();

        settings.NkBaseUrl.Should().Be(CrptUrl.ProductionNkBaseUrl);
    }

    [Fact]
    public void LoadSettings_MigratesLegacyNkBaseUrlFromDisk()
    {
        var legacyJson = $$"""
            {
              "environment": "production",
              "nkBaseUrl": "https://{{CrptUrl.LegacyWrongNkPunycodeHost}}/"
            }
            """;
        File.WriteAllText(_store.SettingsPath, legacyJson);

        var settings = _store.LoadSettings();

        settings.NkBaseUrl.Should().Be(CrptUrl.ProductionNkBaseUrl);
    }

    [Fact]
    public void LoadSettings_ReconcilesSandboxEnvironmentWithLegacyNkUrlOnDisk()
    {
        var legacyJson = """
            {
              "environment": "sandbox",
              "suzBaseUrl": "https://suzgrid.crpt.ru/",
              "trueApiBaseUrl": "https://markirovka.crpt.ru/",
              "nkBaseUrl": "https://api.национальный-каталог.рф/"
            }
            """;
        File.WriteAllText(_store.SettingsPath, legacyJson);

        var settings = _store.LoadSettings();

        settings.Environment.Should().Be(CrptEnvironment.Sandbox);
        settings.SuzBaseUrl.Should().Be(CrptEnvironmentDefaults.SandboxSuzBaseUrl);
        settings.TrueApiBaseUrl.Should().Be(CrptEnvironmentDefaults.SandboxTrueApiBaseUrl);
        settings.NkBaseUrl.Should().Be(CrptEnvironmentDefaults.SandboxNkBaseUrl);

        var persisted = File.ReadAllText(_store.SettingsPath);
        persisted.Should().Contain("api.nk.sandbox.crptech.ru");
    }

    [Fact]
    public void CrptSettings_ContainsAllSection2FieldMappings()
    {
        var settingsProperties = typeof(CrptSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        settingsProperties.Should().Contain(nameof(CrptSettings.Roles));
        settingsProperties.Should().Contain(nameof(CrptSettings.ProductGroups));
        settingsProperties.Should().Contain(nameof(CrptSettings.Gs1OrganizationNumber));
        settingsProperties.Should().Contain(nameof(CrptSettings.SuzBaseUrl));
        settingsProperties.Should().Contain(nameof(CrptSettings.AutoRefreshToken));
        settingsProperties.Should().Contain(nameof(CrptSettings.NkBaseUrl));
        settingsProperties.Should().Contain(nameof(CrptSettings.NkUseJwtFromTrueApi));
        settingsProperties.Should().Contain(nameof(CrptSettings.ProductCatalogPath));

        var secretsProperties = typeof(CrptSecrets)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        secretsProperties.Should().Contain(nameof(CrptSecrets.OmsId));
        secretsProperties.Should().Contain(nameof(CrptSecrets.ConnectionId));
        secretsProperties.Should().Contain(nameof(CrptSecrets.CertificateThumbprint));
        secretsProperties.Should().Contain(nameof(CrptSecrets.NkApiKey));

        typeof(CrptAuthRuntimeState)
            .GetProperty(nameof(CrptAuthRuntimeState.TokenExpiresAt))
            .Should().NotBeNull();
    }

    [Fact]
    public void ToConnectionSettings_MapsSettingsAndSecrets()
    {
        var settings = new CrptSettings
        {
            Inn = "0000000000",
            SuzBaseUrl = CrptSettings.DefaultSuzBaseUrl,
            TrueApiBaseUrl = CrptSettings.DefaultTrueApiBaseUrl,
            ContactPerson = "Contact",
            ProductGroups = ["chemistry"],
            ProductGroupTemplateDefaults = new Dictionary<string, int> { ["chemistry"] = 46 },
        };
        var secrets = new CrptSecrets
        {
            OmsId = "00000000-0000-4000-8000-000000000001",
            ConnectionId = "00000000-0000-4000-8000-000000000002",
            CertificateThumbprint = "ABCDEF1234567890",
        };

        var connection = CrptConnectionSettingsBridge.ToConnectionSettings(settings, secrets);

        connection.Inn.Should().Be(settings.Inn);
        connection.SuzBaseUrl.Should().Be(settings.SuzBaseUrl);
        connection.TrueApiBaseUrl.Should().Be(settings.TrueApiBaseUrl);
        connection.OmsId.Should().Be(secrets.OmsId);
        connection.ConnectionId.Should().Be(secrets.ConnectionId);
        connection.CertificateThumbprint.Should().Be(secrets.CertificateThumbprint);
        connection.ContactPerson.Should().Be(settings.ContactPerson);
        connection.ProductGroup.Should().Be("chemistry");
        connection.TemplateId.Should().Be(46);
    }
}
