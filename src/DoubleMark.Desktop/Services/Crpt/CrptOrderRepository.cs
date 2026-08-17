using System.Collections.Concurrent;
using DoubleMark.Core.Crpt;

namespace DoubleMark.Desktop.Services.Crpt;

/// <summary>
/// In-memory store for local SUZ orders and marking codes (spec §6.3).
/// File persistence arrives in section 9+.
/// </summary>
public sealed class CrptOrderRepository
{
    private readonly ConcurrentDictionary<string, CrptSuzOrder> _orders = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, CrptMarkingCodeItem> _codes = new();
    private int _nextCodeId = 1;

    public Task<IReadOnlyList<CrptSuzOrder>> ListAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return Task.FromResult<IReadOnlyList<CrptSuzOrder>>(_orders.Values.OrderBy(o => o.CreatedAt).ToList());
    }

    public Task<CrptSuzOrder?> GetByLocalIdAsync(string localId, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        _orders.TryGetValue(localId, out var order);
        return Task.FromResult(order);
    }

    public Task SaveAsync(CrptSuzOrder order, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        ArgumentException.ThrowIfNullOrWhiteSpace(order.LocalId);
        _orders[order.LocalId] = order;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CrptMarkingCodeItem>> ListCodesByOrderAsync(
        string orderLocalId,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var codes = _codes.Values
            .Where(c => c.OrderLocalId == orderLocalId)
            .OrderBy(c => c.Id)
            .ToList();
        return Task.FromResult<IReadOnlyList<CrptMarkingCodeItem>>(codes);
    }

    public Task<IReadOnlyList<CrptMarkingCodeItem>> GetCodesByIdsAsync(
        IReadOnlyList<int> codeIds,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        ArgumentNullException.ThrowIfNull(codeIds);

        var result = new List<CrptMarkingCodeItem>(codeIds.Count);
        foreach (var id in codeIds)
        {
            if (_codes.TryGetValue(id, out var code))
                result.Add(code);
        }

        return Task.FromResult<IReadOnlyList<CrptMarkingCodeItem>>(result);
    }

    public Task SaveCodesAsync(
        string orderLocalId,
        IReadOnlyList<string> rawPayloads,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        ArgumentException.ThrowIfNullOrWhiteSpace(orderLocalId);

        foreach (var payload in rawPayloads)
        {
            var item = new CrptMarkingCodeItem(
                Id: Interlocked.Increment(ref _nextCodeId),
                OrderLocalId: orderLocalId,
                RawPayload: payload,
                Status: CrptCodeLifecycleStatus.Received,
                PrintedAt: null,
                LastError: null);
            _codes[item.Id] = item;
        }

        return Task.CompletedTask;
    }

    public Task UpdateCodeAsync(CrptMarkingCodeItem code, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        _codes[code.Id] = code;
        return Task.CompletedTask;
    }
}
