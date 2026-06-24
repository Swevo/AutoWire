using AutoWire;

namespace AutoWire.Sample.Worker.Services;

// ── Data repository (Singleton) ───────────────────────────────────────────────

public interface IDataRepository
{
    Task<IReadOnlyList<string>> GetPendingItemsAsync(CancellationToken ct);
    Task MarkProcessedAsync(string id, CancellationToken ct);
}

/// <summary>
/// Single shared repository — registered as Singleton so all workers share state.
/// </summary>
[Singleton]
public class InMemoryDataRepository : IDataRepository
{
    private readonly List<string> _pending = ["item-001", "item-002", "item-003"];

    public Task<IReadOnlyList<string>> GetPendingItemsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(_pending.ToList());

    public Task MarkProcessedAsync(string id, CancellationToken ct)
    {
        _pending.Remove(id);
        return Task.CompletedTask;
    }
}

// ── Report service (Transient) ────────────────────────────────────────────────

public interface IReportService
{
    string GenerateReport(IReadOnlyList<string> items);
}

/// <summary>New instance per use — safe to hold mutable state during one operation.</summary>
[Transient]
public class CsvReportService : IReportService
{
    public string GenerateReport(IReadOnlyList<string> items) =>
        string.Join(",", items);
}
