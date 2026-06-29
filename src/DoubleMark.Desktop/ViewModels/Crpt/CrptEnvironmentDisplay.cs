using DoubleMark.Core.Crpt;

namespace DoubleMark.Desktop.ViewModels.Crpt;

public sealed class CrptEnvironmentOption
{
    public CrptEnvironment Value { get; init; }
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
}

public static class CrptEnvironmentDisplay
{
    public static IReadOnlyList<CrptEnvironmentOption> All { get; } =
    [
        new CrptEnvironmentOption
        {
            Value = CrptEnvironment.Production,
            DisplayName = "Промышленный контур (Production)",
            Description =
                "Реальная маркировка и продакшен API ЦРПТ. Нужен доступ к серверам CRPT — часто через VPN или корпоративную сеть.",
        },
        new CrptEnvironmentOption
        {
            Value = CrptEnvironment.Sandbox,
            DisplayName = "Тестовый контур (Sandbox)",
            Description =
                "Проверка интеграции без реальных кодов маркировки. Другие URL и тестовые ключи; production УКЭП на sandbox не подходит.",
        },
    ];

    public static string WhenToUseHint =>
        "Sandbox — для настройки и отладки. Production — когда готовы заказывать реальные коды и работать с промышленным ГИС МТ.";
}
