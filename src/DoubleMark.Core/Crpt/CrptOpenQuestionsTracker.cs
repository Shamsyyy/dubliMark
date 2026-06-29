namespace DoubleMark.Core.Crpt;

/// <summary>
/// Tracks unresolved CRPT integration questions (spec §15 — fill before Phase B₀ / B).
/// </summary>
public static class CrptOpenQuestionsTracker
{
    private static readonly CrptOpenQuestion[] BeforePhaseB0Questions =
    [
        CrptOpenQuestion.NkApiKeyOrJwtOnly,
        CrptOpenQuestion.SandboxGtinForSyncTest,
        CrptOpenQuestion.TemplateIdTableAllProductGroups,
    ];

    private static readonly CrptOpenQuestion[] BeforePhaseBQuestions =
    [
        CrptOpenQuestion.ExactManufacturerProductGroup,
        CrptOpenQuestion.SandboxCredentialsAndTestGtin,
        CrptOpenQuestion.ReleaseMethodTypeEnumName,
        CrptOpenQuestion.SuzCreateOrderPathForContour,
    ];

    public static IReadOnlyList<CrptOpenQuestion> BeforePhaseB0 => BeforePhaseB0Questions;

    public static IReadOnlyList<CrptOpenQuestion> BeforePhaseB => BeforePhaseBQuestions;

    public static IReadOnlyList<CrptOpenQuestion> All => BeforePhaseB0Questions
        .Concat(BeforePhaseBQuestions)
        .ToArray();

    public static CrptQuestionResolutionStatus GetStatus(CrptOpenQuestion question) =>
        ResolutionOverrides.TryGetValue(question, out var status)
            ? status
            : CrptQuestionResolutionStatus.Unresolved;

    public static bool IsResolved(CrptOpenQuestion question) =>
        GetStatus(question) == CrptQuestionResolutionStatus.Resolved;

    public static bool IsUnresolved(CrptOpenQuestion question) =>
        GetStatus(question) == CrptQuestionResolutionStatus.Unresolved;

    public static CrptOpenQuestionPhase GetPhase(CrptOpenQuestion question) =>
        BeforePhaseB0Questions.Contains(question)
            ? CrptOpenQuestionPhase.BeforePhaseB0
            : CrptOpenQuestionPhase.BeforePhaseB;

    public static string Describe(CrptOpenQuestion question) =>
        question switch
        {
            CrptOpenQuestion.NkApiKeyOrJwtOnly => "API KEY НК или только JWT?",
            CrptOpenQuestion.SandboxGtinForSyncTest => "Sandbox GTIN для теста sync",
            CrptOpenQuestion.TemplateIdTableAllProductGroups =>
                "Таблица templateId по productGroup для всех ТГ производителя",
            CrptOpenQuestion.ExactManufacturerProductGroup => "Точная товарная группа производителя",
            CrptOpenQuestion.SandboxCredentialsAndTestGtin => "Sandbox credentials и тестовый GTIN",
            CrptOpenQuestion.ReleaseMethodTypeEnumName => "Имя enum releaseMethodType для ТГ",
            CrptOpenQuestion.SuzCreateOrderPathForContour =>
                "Точный path create order в API СУЗ 3.0 для вашего контура",
            _ => throw new ArgumentOutOfRangeException(nameof(question), question, null),
        };

    private static readonly Dictionary<CrptOpenQuestion, CrptQuestionResolutionStatus> ResolutionOverrides = new();
}

public enum CrptOpenQuestionPhase
{
    BeforePhaseB0,
    BeforePhaseB,
}

public enum CrptOpenQuestion
{
    NkApiKeyOrJwtOnly,
    SandboxGtinForSyncTest,
    TemplateIdTableAllProductGroups,
    ExactManufacturerProductGroup,
    SandboxCredentialsAndTestGtin,
    ReleaseMethodTypeEnumName,
    SuzCreateOrderPathForContour,
}

public enum CrptQuestionResolutionStatus
{
    Unresolved,
    Resolved,
}
