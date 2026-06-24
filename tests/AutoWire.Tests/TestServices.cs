// Services used to verify generator output — each case exercises a distinct code path.
using Microsoft.Extensions.Hosting;

// 1. Scoped registered against single implemented interface (auto-discovery)
public interface IOrderService { string GetStatus(); }
[Scoped]
public class OrderService : IOrderService { public string GetStatus() => "pending"; }

// 2. Singleton with explicit ServiceType (only registers as ICache, not ISecondaryCache)
public interface ICache { }
public interface ISecondaryCache { }
[Singleton(typeof(ICache))]
public class MemoryCache : ICache, ISecondaryCache { }

// 3. Transient with no interface — registers as concrete type
[Transient]
public class EmailSender { }

// 4. Scoped across two interfaces — registers as both
public interface IReader { }
public interface IWriter { }
[Scoped]
public class DataService : IReader, IWriter { }

// 5. Keyed scoped service (.NET 8+)
public interface IMessageBus { }
[Scoped(Key = "primary")]
public class PrimaryMessageBus : IMessageBus { }

// 6. Singleton with no interface — registers as concrete type
[Singleton]
public class AppSettings { }

// 7. Open generic - auto-discovers compatible generic interface
public interface IRepository<T> { T? Find(int id); }
[Scoped]
public class Repository<T> : IRepository<T> { public T? Find(int id) => default; }
// → services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

// 8. Open generic - explicit service type (IReadOnlyRepo only, not IRepository)
public interface IReadOnlyRepository<T> { }
[Singleton(typeof(IReadOnlyRepository<>))]
public class CachedRepository<T> : IRepository<T>, IReadOnlyRepository<T>
{
    public T? Find(int id) => default;
}
// → services.AddSingleton(typeof(IReadOnlyRepository<>), typeof(CachedRepository<>));

// 9. Open generic - no interface, registers as concrete type
[Transient]
public class EventProcessor<T> { }
// → services.AddTransient(typeof(EventProcessor<>));

// ── AllowMultiple: one class registered against two explicit interfaces ────────

public interface IFeedService { void Feed(); }
public interface IPublisherService { void Publish(); }
[Scoped(typeof(IFeedService))]
[Scoped(typeof(IPublisherService))]
public class ContentService : IFeedService, IPublisherService
{
    public void Feed() { }
    public void Publish() { }
}

// ── DuplicateStrategy.Replace: ReplacementReplaceable wins over OriginalReplaceable ──

public interface IReplaceable { }
[Scoped]
public class OriginalReplaceable : IReplaceable { }
[Scoped(Duplicate = DuplicateStrategy.Replace)]
public class ReplacementReplaceable : IReplaceable { }

// ── DuplicateStrategy.Skip: PrimarySkippable wins, FallbackSkippable is skipped ──

public interface ISkippable { }
[Scoped]
public class PrimarySkippable : ISkippable { }
[Scoped(Duplicate = DuplicateStrategy.Skip)]
public class FallbackSkippable : ISkippable { }

// ── TryScoped: DefaultTryable is a library-style fallback ─────────────────────

public interface ITryable { string GetValue(); }
[TryScoped]
public class DefaultTryable : ITryable { public string GetValue() => "default"; }
// Used to override DefaultTryable in TryScoped tests — no AutoWire attribute:
public class MockTryable : ITryable { public string GetValue() => "mock"; }

// ── Decorator: PoliteGreeter wraps SimpleGreeter for IGreeter ─────────────────

public interface IGreeter { string Greet(string name); }

[Scoped]
public class SimpleGreeter : IGreeter
{
    public string Greet(string name) => $"Hello, {name}!";
}

[DecorateScoped(typeof(IGreeter))]
public class PoliteGreeter : IGreeter
{
    private readonly IGreeter _inner;
    public PoliteGreeter(IGreeter inner) { _inner = inner; }
    public string Greet(string name) => $"[politely] {_inner.Greet(name)}";
}

// ── HostedService: background worker registration ─────────────────────────────

[HostedService]
public class TestBackgroundWorker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}

// ── IncludeSelf: registers as both interface and concrete type ────────────────

public interface IAnalyticsService { string Track(); }
[Scoped(IncludeSelf = true)]
public class AnalyticsService : IAnalyticsService
{
    public string Track() => "tracked";
}

// ── IncludeSelf with explicit ServiceType ─────────────────────────────────────

public interface INotificationService { void Notify(); }
public interface IEmailSender { void Send(); }
[Scoped(typeof(INotificationService), IncludeSelf = true)]
public class NotificationService : INotificationService, IEmailSender
{
    public void Notify() { }
    public void Send() { }
}

// ── Ordered decorators: two decorators on ILogService with explicit Order ─────

public interface ILogService { string Log(string msg); }

[Scoped]
public class SimpleLogService : ILogService
{
    public string Log(string msg) => $"[LOG] {msg}";
}

/// <summary>Order = 1 → inner decorator (wraps SimpleLogService directly).</summary>
[DecorateScoped(typeof(ILogService), Order = 1)]
public class TimestampLogDecorator : ILogService
{
    private readonly ILogService _inner;
    public TimestampLogDecorator(ILogService inner) { _inner = inner; }
    public string Log(string msg) => _inner.Log($"[TS] {msg}");
}

/// <summary>Order = 2 → outer decorator (wraps TimestampLogDecorator, exposes ILogService).</summary>
[DecorateScoped(typeof(ILogService), Order = 2)]
public class UpperCaseLogDecorator : ILogService
{
    private readonly ILogService _inner;
    public UpperCaseLogDecorator(ILogService inner) { _inner = inner; }
    public string Log(string msg) => _inner.Log(msg).ToUpper();
}


