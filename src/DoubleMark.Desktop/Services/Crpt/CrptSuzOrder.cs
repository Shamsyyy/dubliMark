using DoubleMark.Core.Crpt;

namespace DoubleMark.Desktop.Services.Crpt;

/// <summary>
/// Local representation of a SUZ code order (spec §6.3).
/// </summary>
public sealed record CrptSuzOrder(
    string LocalId,
    string? RemoteOrderId,
    string Gtin,
    int RequestedQuantity,
    int ReceivedQuantity,
    string ProductGroup,
    SuzOrderRemoteStatus RemoteStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage);

/// <summary>
/// Downloaded marking code payload (spec §6.3). RawPayload must not be logged.
/// </summary>
public sealed record CrptMarkingCodeItem(
    int Id,
    string OrderLocalId,
    string RawPayload,
    CrptCodeLifecycleStatus Status,
    DateTimeOffset? PrintedAt,
    string? LastError);
