namespace DoubleMark.Core.Crpt;

/// <summary>
/// Manufacturer business process stages from spec §3 sequence diagram.
/// </summary>
public enum CrptBusinessProcessStage
{
    SettingsCheck,
    Auth,
    CatalogSync,
    OrderCreate,
    OrderPoll,
    CodesDownload,
    Print,
    Utilisation,
    IntroduceGoodsPhase2,
}

/// <summary>
/// Ordered manufacturer workflow stages and MVP vs phase 2 boundaries (spec §3).
/// </summary>
public static class CrptManufacturerWorkflow
{
    private static readonly CrptBusinessProcessStage[] FullSequence =
    [
        CrptBusinessProcessStage.SettingsCheck,
        CrptBusinessProcessStage.Auth,
        CrptBusinessProcessStage.CatalogSync,
        CrptBusinessProcessStage.OrderCreate,
        CrptBusinessProcessStage.OrderPoll,
        CrptBusinessProcessStage.CodesDownload,
        CrptBusinessProcessStage.Print,
        CrptBusinessProcessStage.Utilisation,
        CrptBusinessProcessStage.IntroduceGoodsPhase2,
    ];

    private static readonly CrptBusinessProcessStage[] Phase2StageList =
        [CrptBusinessProcessStage.IntroduceGoodsPhase2];

    public static IReadOnlyList<CrptBusinessProcessStage> FullStages => FullSequence;

    public static IReadOnlyList<CrptBusinessProcessStage> MvpStages =>
        FullSequence.Where(stage => !IsPhase2Stage(stage)).ToArray();

    public static IReadOnlyList<CrptBusinessProcessStage> Phase2Stages => Phase2StageList;

    public static bool IsPhase2Stage(CrptBusinessProcessStage stage) =>
        Phase2StageList.Contains(stage);

    public static int GetStageIndex(CrptBusinessProcessStage stage) =>
        Array.IndexOf(FullSequence, stage);

    /// <summary>
    /// Returns true when every stage appears at the same or later position as in spec §3.
    /// </summary>
    public static bool ValidateStageOrder(IReadOnlyList<CrptBusinessProcessStage> stages)
    {
        var lastIndex = -1;
        foreach (var stage in stages)
        {
            var index = GetStageIndex(stage);
            if (index < 0 || index <= lastIndex)
                return false;

            lastIndex = index;
        }

        return true;
    }

    /// <summary>
    /// Returns true when <paramref name="stage"/> may start after <paramref name="completedStages"/>.
    /// </summary>
    public static bool CanStartStage(
        CrptBusinessProcessStage stage,
        IReadOnlyCollection<CrptBusinessProcessStage> completedStages)
    {
        var prerequisites = CrptManufacturerWorkflowDescriptor.GetPrerequisites(stage);
        return prerequisites.All(completedStages.Contains);
    }
}

/// <summary>
/// Stage dependency graph for manufacturer workflow (spec §3, no HTTP orchestration).
/// </summary>
public static class CrptManufacturerWorkflowDescriptor
{
    private static readonly IReadOnlyDictionary<CrptBusinessProcessStage, CrptBusinessProcessStage[]> PrerequisitesMap =
        new Dictionary<CrptBusinessProcessStage, CrptBusinessProcessStage[]>
        {
            [CrptBusinessProcessStage.SettingsCheck] = [],
            [CrptBusinessProcessStage.Auth] = [CrptBusinessProcessStage.SettingsCheck],
            [CrptBusinessProcessStage.CatalogSync] = [CrptBusinessProcessStage.Auth],
            [CrptBusinessProcessStage.OrderCreate] = [CrptBusinessProcessStage.CatalogSync],
            [CrptBusinessProcessStage.OrderPoll] = [CrptBusinessProcessStage.OrderCreate],
            [CrptBusinessProcessStage.CodesDownload] = [CrptBusinessProcessStage.OrderPoll],
            [CrptBusinessProcessStage.Print] = [CrptBusinessProcessStage.CodesDownload],
            [CrptBusinessProcessStage.Utilisation] = [CrptBusinessProcessStage.Print],
            [CrptBusinessProcessStage.IntroduceGoodsPhase2] = [CrptBusinessProcessStage.Utilisation],
        };

    public static IReadOnlyDictionary<CrptBusinessProcessStage, CrptBusinessProcessStage[]> Prerequisites =>
        PrerequisitesMap;

    public static IReadOnlyList<CrptBusinessProcessStage> GetPrerequisites(CrptBusinessProcessStage stage) =>
        PrerequisitesMap.TryGetValue(stage, out var prerequisites)
            ? prerequisites
            : Array.Empty<CrptBusinessProcessStage>();

    public static bool CanReachStage(
        CrptBusinessProcessStage target,
        IReadOnlyCollection<CrptBusinessProcessStage> completedStages) =>
        CrptManufacturerWorkflow.CanStartStage(target, completedStages);
}
