using System.Reflection;
using System.Text.Json;
using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Services.Crpt;
using DoubleMark.Desktop.Settings;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

/// <summary>
/// Spec §6 data model completeness and JSON roundtrip tests.
/// </summary>
public class CrptDataModelsTests
{
    private static readonly string[] Section61SettingsProperties =
    [
        nameof(CrptSettings.Environment),
        nameof(CrptSettings.Roles),
        nameof(CrptSettings.ProductGroups),
        nameof(CrptSettings.Inn),
        nameof(CrptSettings.Gs1OrganizationNumber),
        nameof(CrptSettings.SuzBaseUrl),
        nameof(CrptSettings.TrueApiBaseUrl),
        nameof(CrptSettings.AutoRefreshToken),
        nameof(CrptSettings.ContactPerson),
        nameof(CrptSettings.NkBaseUrl),
        nameof(CrptSettings.NkUseJwtFromTrueApi),
        nameof(CrptSettings.NkSyncOnlyPublished),
        nameof(CrptSettings.NkSyncOnlySigned),
        nameof(CrptSettings.ProductGroupTemplateDefaults),
    ];

    private static readonly string[] Section61SecretProperties =
    [
        nameof(CrptSecrets.OmsId),
        nameof(CrptSecrets.ConnectionId),
        nameof(CrptSecrets.CertificateThumbprint),
    ];

    private static readonly string[] Section62CatalogItemProperties =
    [
        nameof(CrptProductCatalogItem.Gtin),
        nameof(CrptProductCatalogItem.GoodId),
        nameof(CrptProductCatalogItem.Name),
        nameof(CrptProductCatalogItem.TnvedCode),
        nameof(CrptProductCatalogItem.TnvedGroup),
        nameof(CrptProductCatalogItem.ProductGroup),
        nameof(CrptProductCatalogItem.TemplateId),
        nameof(CrptProductCatalogItem.NkStatus),
        nameof(CrptProductCatalogItem.NkProductState),
        nameof(CrptProductCatalogItem.NkCardType),
        nameof(CrptProductCatalogItem.NkCardStatusPrimary),
        nameof(CrptProductCatalogItem.NkDetailedStatuses),
        nameof(CrptProductCatalogItem.CategoryName),
        nameof(CrptProductCatalogItem.NkUpdatedAt),
        nameof(CrptProductCatalogItem.NkStatusRaw),
        nameof(CrptProductCatalogItem.IsSigned),
        nameof(CrptProductCatalogItem.CanOrderCodes),
        nameof(CrptProductCatalogItem.CertificateDocType),
        nameof(CrptProductCatalogItem.CertificateDocNumber),
        nameof(CrptProductCatalogItem.CertificateDocDate),
        nameof(CrptProductCatalogItem.SyncedAt),
        nameof(CrptProductCatalogItem.SyncError),
    ];

    private static readonly string[] Section63OrderProperties =
    [
        "LocalId",
        "RemoteOrderId",
        "Gtin",
        "RequestedQuantity",
        "ReceivedQuantity",
        "ProductGroup",
        "RemoteStatus",
        "CreatedAt",
        "CompletedAt",
        "ErrorMessage",
    ];

    private static readonly string[] Section63CodeProperties =
    [
        "Id",
        "OrderLocalId",
        "RawPayload",
        "Status",
        "PrintedAt",
        "LastError",
    ];

    [Fact]
    public void Section61_CrptSettings_HasAllSpecProperties()
    {
        var properties = GetPublicPropertyNames(typeof(CrptSettings));
        properties.Should().Contain(Section61SettingsProperties);
    }

    [Fact]
    public void Section61_CrptSecrets_HasSecretFieldsFromSpec()
    {
        var properties = GetPublicPropertyNames(typeof(CrptSecrets));
        properties.Should().Contain(Section61SecretProperties);
    }

    [Fact]
    public void Section61_CrptSettingsSnapshot_MergesSettingsAndSecrets()
    {
        var settings = CreateSampleSettings();
        var secrets = CreateSampleSecrets();

        var snapshot = CrptSettingsSnapshot.Merge(settings, secrets);
        var (splitSettings, splitSecrets) = snapshot.Split();

        splitSettings.Should().BeEquivalentTo(settings);
        splitSecrets.Should().BeEquivalentTo(secrets);
    }

    [Fact]
    public void Section61_Settings_JsonRoundtrip()
    {
        var settings = CreateSampleSettings();
        var json = JsonSerializer.Serialize(settings, CrptSettingsStore.JsonOptions);
        var restored = JsonSerializer.Deserialize<CrptSettings>(json, CrptSettingsStore.JsonOptions);
        restored.Should().BeEquivalentTo(settings);
    }

    [Fact]
    public void Section62_CatalogItem_HasAllSpecProperties()
    {
        var properties = GetPublicPropertyNames(typeof(CrptProductCatalogItem));
        properties.Should().Contain(Section62CatalogItemProperties);
    }

    [Fact]
    public void Section62_CatalogSyncProgress_HasStageProcessedTotalCurrentGtin()
    {
        typeof(CrptCatalogSyncProgress).GetProperties().Select(p => p.Name).Should()
            .BeEquivalentTo(["Stage", "Processed", "Total", "CurrentGtin"]);
    }

    [Fact]
    public void Section62_CatalogSyncProgress_JsonRoundtrip()
    {
        var progress = new CrptCatalogSyncProgress("product-list", 10, 100, "00000000000000");
        var json = JsonSerializer.Serialize(progress, CrptDataModelJson.Options);
        var restored = JsonSerializer.Deserialize<CrptCatalogSyncProgress>(json, CrptDataModelJson.Options);
        restored.Should().BeEquivalentTo(progress);
    }

    [Fact]
    public void Section63_Order_HasAllSpecProperties()
    {
        typeof(CrptSuzOrder).GetProperties().Select(p => p.Name).Should()
            .Contain(Section63OrderProperties);
    }

    [Fact]
    public void Section63_MarkingCodeItem_HasAllSpecProperties()
    {
        typeof(CrptMarkingCodeItem).GetProperties().Select(p => p.Name).Should()
            .Contain(Section63CodeProperties);
    }

    [Fact]
    public void Section63_Order_JsonRoundtrip()
    {
        var order = CreateSampleOrder();
        var json = JsonSerializer.Serialize(order, CrptDataModelJson.Options);
        var restored = JsonSerializer.Deserialize<CrptSuzOrder>(json, CrptDataModelJson.Options);
        restored.Should().BeEquivalentTo(order);
    }

    [Fact]
    public void Section63_MarkingCodeItem_JsonRoundtrip()
    {
        var code = CreateSampleCodeItem();
        var json = JsonSerializer.Serialize(code, CrptDataModelJson.Options);
        var restored = JsonSerializer.Deserialize<CrptMarkingCodeItem>(json, CrptDataModelJson.Options);
        restored.Should().BeEquivalentTo(code);
    }

    [Fact]
    public void Section63_CodeLifecycleStatus_HasAllSpecValues()
    {
        Enum.GetNames<CrptCodeLifecycleStatus>().Should().BeEquivalentTo(
        [
            nameof(CrptCodeLifecycleStatus.Received),
            nameof(CrptCodeLifecycleStatus.QueuedForPrint),
            nameof(CrptCodeLifecycleStatus.Printed),
            nameof(CrptCodeLifecycleStatus.UtilisationSent),
            nameof(CrptCodeLifecycleStatus.InCirculation),
            nameof(CrptCodeLifecycleStatus.Error),
        ]);
    }

    [Fact]
    public void Section64_AuthToken_HasIsUnitedUuidToken()
    {
        typeof(CrptAuthToken).GetProperties().Select(p => p.Name).Should()
            .Contain(nameof(CrptAuthToken.IsUnitedUuidToken));
    }

    [Fact]
    public void Section64_AuthToken_JsonRoundtrip()
    {
        var token = new CrptAuthToken("synthetic-token", DateTimeOffset.UtcNow.AddHours(10), IsUnitedUuidToken: true);
        var json = JsonSerializer.Serialize(token, CrptJson.Compact);
        var restored = JsonSerializer.Deserialize<CrptAuthToken>(json, CrptJson.Compact);
        restored.Should().BeEquivalentTo(token);
    }

    [Fact]
    public void Section64_ParseSuzToken_SetsIsUnitedUuidTokenFalse()
    {
        const string json = """{ "token": "suz-token-value" }""";
        var token = CrptAuthResponseParser.ParseSuzToken(json);
        token.IsUnitedUuidToken.Should().BeFalse();
    }

    [Fact]
    public void Section64_ParseJwtToken_SetsIsUnitedUuidTokenFalse()
    {
        const string json = """
            {
              "token": "jwt-token-value",
              "expireDate": "2024-06-01T18:00:00+00:00"
            }
            """;

        var token = CrptAuthResponseParser.ParseJwtToken(json);
        token.IsUnitedUuidToken.Should().BeFalse();
    }

    [Fact]
    public void Section64_ParseUnitedToken_SetsIsUnitedUuidTokenTrue()
    {
        const string json = """{ "uuidToken": "00000000-0000-4000-8000-000000000099" }""";
        var token = CrptAuthResponseParser.ParseUnitedToken(json);
        token.IsUnitedUuidToken.Should().BeTrue();
    }

    [Fact]
    public async Task Section63_OrderRepository_StoresTypedOrdersAndCodes()
    {
        var repository = new CrptOrderRepository();
        var order = CreateSampleOrder();

        await repository.SaveAsync(order);
        var listed = await repository.ListAsync();
        listed.Should().ContainSingle().Which.Should().BeEquivalentTo(order);

        await repository.SaveCodesAsync(order.LocalId, ["synthetic-payload-1", "synthetic-payload-2"]);
        var codes = await repository.ListCodesByOrderAsync(order.LocalId);
        codes.Should().HaveCount(2);
        codes.Should().OnlyContain(c => c.OrderLocalId == order.LocalId);
        codes.Should().OnlyContain(c => c.Status == CrptCodeLifecycleStatus.Received);
    }

    private static HashSet<string> GetPublicPropertyNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

    private static CrptSettings CreateSampleSettings() => new()
    {
        Environment = CrptEnvironment.Sandbox,
        Roles = [CrptOrganizationRole.Manufacturer],
        ProductGroups = ["chemistry"],
        Inn = "0000000000",
        Gs1OrganizationNumber = "0000000000000",
        SuzBaseUrl = CrptSettings.DefaultSuzBaseUrl,
        TrueApiBaseUrl = CrptSettings.DefaultTrueApiBaseUrl,
        AutoRefreshToken = true,
        ContactPerson = "Test Contact",
        NkBaseUrl = CrptSettings.DefaultNkBaseUrl,
        NkUseJwtFromTrueApi = true,
        NkSyncOnlyPublished = true,
        NkSyncOnlySigned = true,
        ProductGroupTemplateDefaults = new Dictionary<string, int> { ["chemistry"] = 46 },
    };

    private static CrptSecrets CreateSampleSecrets() => new()
    {
        OmsId = "00000000-0000-4000-8000-000000000001",
        ConnectionId = "00000000-0000-4000-8000-000000000002",
        CertificateThumbprint = "ABCDEF1234567890",
    };

    private static CrptSuzOrder CreateSampleOrder() => new(
        LocalId: "local-order-001",
        RemoteOrderId: "remote-order-001",
        Gtin: "00000000000000",
        RequestedQuantity: 100,
        ReceivedQuantity: 100,
        ProductGroup: "chemistry",
        RemoteStatus: SuzOrderRemoteStatus.Ready,
        CreatedAt: new DateTimeOffset(2024, 6, 1, 10, 0, 0, TimeSpan.Zero),
        CompletedAt: new DateTimeOffset(2024, 6, 1, 11, 0, 0, TimeSpan.Zero),
        ErrorMessage: null);

    private static CrptMarkingCodeItem CreateSampleCodeItem() => new(
        Id: 1,
        OrderLocalId: "local-order-001",
        RawPayload: "synthetic-marking-code-payload",
        Status: CrptCodeLifecycleStatus.Received,
        PrintedAt: null,
        LastError: null);
}
