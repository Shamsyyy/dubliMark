using DoubleMark.Core.Crpt;

namespace DoubleMark.Crpt;

public sealed record CreateSuzOrderRequest
{
    public required string ProductGroup { get; init; }
    public required IReadOnlyList<CreateSuzOrderProduct> Products { get; init; }
    public Dictionary<string, object>? Attributes { get; init; }
    public string? ContactPerson { get; init; }
}

public sealed record CreateSuzOrderProduct
{
    public required string Gtin { get; init; }
    public required int Quantity { get; init; }
    public string? SerialNumberType { get; init; }
    public int? TemplateId { get; init; }
    public string? CisType { get; init; }
}

public sealed record CrptSuzOrderStatus(
    SuzOrderRemoteStatus Status,
    string? ErrorMessage,
    string RawJson)
{
    public bool IsReadyForDownload => SuzOrderRemoteStatusMapper.IsReadyForDownload(Status);

    public bool IsTerminalFailure => SuzOrderRemoteStatusMapper.IsTerminalFailure(Status);
}

public sealed record CrptSuzCodesBlock(
    IReadOnlyList<string> Codes,
    string? BlockId,
    bool IsLast);

public sealed record SuzOrderProgress(string Stage, int PercentComplete);

public sealed class CrptSuzException : Exception
{
    public CrptSuzException(string message) : base(message)
    {
    }

    public CrptSuzException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
