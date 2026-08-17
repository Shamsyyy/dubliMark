namespace DoubleMark.Core.Crpt;

/// <summary>
/// NK LK product state (spec CRPT_CATALOG_UI § Product State).
/// </summary>
public enum NkProductState
{
    Unknown = 0,
    Published,
    Draft,
    Moderation,
    Errors,
    Archived,
}
