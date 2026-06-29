namespace DoubleMark.Core.Crpt;

/// <summary>
/// Normalized SUZ 3.0 order status (spec §9, §14.1).
/// </summary>
public enum SuzOrderRemoteStatus
{
    Unknown,
    Pending,
    Ready,
    Closed,
    Error,
}

/// <summary>
/// Maps raw SUZ status strings to <see cref="SuzOrderRemoteStatus"/>.
/// </summary>
public static class SuzOrderRemoteStatusMapper
{
    public static SuzOrderRemoteStatus FromRemoteStatus(string? remoteStatus)
    {
        if (string.IsNullOrWhiteSpace(remoteStatus))
            return SuzOrderRemoteStatus.Unknown;

        return remoteStatus.Trim().ToUpperInvariant() switch
        {
            "PENDING" or "CREATED" or "IN_PROGRESS" or "PROCESSING" or "NEW" => SuzOrderRemoteStatus.Pending,
            "READY" or "COMPLETED" or "DONE" or "ACTIVE" => SuzOrderRemoteStatus.Ready,
            "CLOSED" or "CLOSE" => SuzOrderRemoteStatus.Closed,
            "ERROR" or "FAILED" or "REJECTED" or "CANCELLED" or "CANCELED" => SuzOrderRemoteStatus.Error,
            _ => SuzOrderRemoteStatus.Unknown,
        };
    }

    public static bool IsReadyForDownload(SuzOrderRemoteStatus status) =>
        status == SuzOrderRemoteStatus.Ready;

    public static bool IsTerminalFailure(SuzOrderRemoteStatus status) =>
        status == SuzOrderRemoteStatus.Error;

    public static bool IsTerminalSuccess(SuzOrderRemoteStatus status) =>
        status is SuzOrderRemoteStatus.Closed or SuzOrderRemoteStatus.Ready;
}
