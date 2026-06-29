namespace DoubleMark.Core.Crpt;

/// <summary>
/// Scope of an organization role relative to the CRPT integration roadmap (spec §1.4).
/// </summary>
public enum CrptRoleScope
{
    Mvp,
    Phase2,
    OutOfScope,
}

/// <summary>
/// CRPT integration capabilities referenced in spec §1.2 (MVP) and §1.3 (phase 2).
/// </summary>
public enum CrptIntegrationFeature
{
    ConnectionSettings,
    TrueApiTokenRefresh,
    NkCatalogFullSync,
    SuzOrderCreate,
    SuzCodeDownload,
    LabelPrint,
    UtilisationReport,

    IntroduceToCirculation,
    NkIncrementalSync,
    NkCardCreateEdit,
    Gs1Aggregation,
    DiadocUpd,
    Marketplaces,
}

/// <summary>
/// Documents MVP vs phase 2 vs out-of-scope boundaries for CRPT integration (spec §1).
/// </summary>
public static class CrptMvpScope
{
    private static readonly CrptOrganizationRole[] MvpRoleList =
        [CrptOrganizationRole.Manufacturer];

    private static readonly CrptOrganizationRole[] Phase2RoleList =
        [CrptOrganizationRole.Importer];

    private static readonly CrptOrganizationRole[] OutOfScopeRoleList =
    [
        CrptOrganizationRole.Wholesaler,
        CrptOrganizationRole.Retailer,
        CrptOrganizationRole.Seller,
        CrptOrganizationRole.Exporter,
        CrptOrganizationRole.Government,
        CrptOrganizationRole.HoReCa,
    ];

    private static readonly CrptIntegrationFeature[] MvpFeatureList =
    [
        CrptIntegrationFeature.ConnectionSettings,
        CrptIntegrationFeature.TrueApiTokenRefresh,
        CrptIntegrationFeature.NkCatalogFullSync,
        CrptIntegrationFeature.SuzOrderCreate,
        CrptIntegrationFeature.SuzCodeDownload,
        CrptIntegrationFeature.LabelPrint,
        CrptIntegrationFeature.UtilisationReport,
    ];

    private static readonly CrptIntegrationFeature[] Phase2FeatureList =
    [
        CrptIntegrationFeature.IntroduceToCirculation,
        CrptIntegrationFeature.NkIncrementalSync,
        CrptIntegrationFeature.NkCardCreateEdit,
        CrptIntegrationFeature.Gs1Aggregation,
        CrptIntegrationFeature.DiadocUpd,
    ];

    private static readonly CrptIntegrationFeature[] OutOfScopeFeatureList =
        [CrptIntegrationFeature.Marketplaces];

    public static IReadOnlyList<CrptOrganizationRole> MvpRoles => MvpRoleList;

    public static IReadOnlyList<CrptIntegrationFeature> MvpFeatures => MvpFeatureList;

    public static IReadOnlyList<CrptIntegrationFeature> Phase2Features => Phase2FeatureList;

    public static IReadOnlyList<CrptIntegrationFeature> OutOfScopeFeatures => OutOfScopeFeatureList;

    public static CrptRoleScope GetRoleScope(CrptOrganizationRole role) =>
        role switch
        {
            CrptOrganizationRole.Manufacturer => CrptRoleScope.Mvp,
            CrptOrganizationRole.Importer => CrptRoleScope.Phase2,
            _ => CrptRoleScope.OutOfScope,
        };

    public static bool IsMvpSupported(CrptOrganizationRole role) =>
        GetRoleScope(role) == CrptRoleScope.Mvp;

    public static bool IsPhase2Role(CrptOrganizationRole role) =>
        GetRoleScope(role) == CrptRoleScope.Phase2;

    public static bool IsOutOfScopeRole(CrptOrganizationRole role) =>
        GetRoleScope(role) == CrptRoleScope.OutOfScope;

    public static bool IsInMvp(CrptIntegrationFeature feature) =>
        MvpFeatureList.Contains(feature);

    public static bool IsPhase2(CrptIntegrationFeature feature) =>
        Phase2FeatureList.Contains(feature);

    public static bool IsOutOfScope(CrptIntegrationFeature feature) =>
        OutOfScopeFeatureList.Contains(feature);
}
