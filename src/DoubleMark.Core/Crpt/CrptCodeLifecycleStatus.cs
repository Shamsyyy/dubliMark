namespace DoubleMark.Core.Crpt;

/// <summary>
/// Lifecycle of a marking code from SUZ download through circulation (spec §6.3).
/// </summary>
public enum CrptCodeLifecycleStatus
{
    Received,
    QueuedForPrint,
    Printed,
    UtilisationSent,
    InCirculation,
    Error,
}

/// <summary>
/// Validates allowed lifecycle transitions for manufacturer MVP workflow.
/// </summary>
public static class CrptCodeLifecycleTransitions
{
    private static readonly IReadOnlyDictionary<CrptCodeLifecycleStatus, CrptCodeLifecycleStatus[]> AllowedNext =
        new Dictionary<CrptCodeLifecycleStatus, CrptCodeLifecycleStatus[]>
        {
            [CrptCodeLifecycleStatus.Received] =
                [CrptCodeLifecycleStatus.QueuedForPrint, CrptCodeLifecycleStatus.Error],
            [CrptCodeLifecycleStatus.QueuedForPrint] =
                [CrptCodeLifecycleStatus.Printed, CrptCodeLifecycleStatus.Error],
            [CrptCodeLifecycleStatus.Printed] =
                [CrptCodeLifecycleStatus.UtilisationSent, CrptCodeLifecycleStatus.Error],
            [CrptCodeLifecycleStatus.UtilisationSent] =
                [CrptCodeLifecycleStatus.InCirculation, CrptCodeLifecycleStatus.Error],
            [CrptCodeLifecycleStatus.InCirculation] = [],
            [CrptCodeLifecycleStatus.Error] = [],
        };

    public static bool CanTransition(CrptCodeLifecycleStatus from, CrptCodeLifecycleStatus to)
    {
        if (from == to)
            return true;

        return AllowedNext.TryGetValue(from, out var next) && next.Contains(to);
    }

    public static IReadOnlyList<CrptCodeLifecycleStatus> GetAllowedNext(CrptCodeLifecycleStatus from) =>
        AllowedNext.TryGetValue(from, out var next)
            ? next
            : Array.Empty<CrptCodeLifecycleStatus>();
}
