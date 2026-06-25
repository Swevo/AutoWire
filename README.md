# AutoWire

[![NuGet](https://img.shields.io/nuget/v/AutoWire.svg)](https://www.nuget.org/packages/AutoWire/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/AutoWire.svg)](https://www.nuget.org/packages/AutoWire/)
[![CI](https://github.com/Swevo/AutoWire/actions/workflows/build.yml/badge.svg)](https://github.com/Swevo/AutoWire/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**Compile-time dependency injection auto-registration for .NET** — add `[Scoped]`, `[Singleton]`, or `[Transient]` to your services and AutoWire generates the `IServiceCollection` registration code at build time.

**Zero runtime overhead. No reflection. No startup cost.**

### Benchmark — 20 services, AMD Ryzen 9 5900X, .NET 9

| Method | Mean | vs Manual | Allocated |
|---|---|---|---|
| Manual (baseline) | 3.01 µs | 1.0× | 11.24 KB |
| **AutoWire** | **3.68 µs** | **1.2×** | **11.24 KB** |
| Scrutor | 57.43 µs | 19.1× | 32.42 KB |

AutoWire is **~19× faster than Scrutor** at registration time and allocates **65% less memory** — because all work happens at compile time with zero reflection.

---

## The problem

Every .NET project accumulates this:

```csharp
// Program.cs — grows forever, breaks when you forget to update it
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddSingleton<ICacheService, RedisCacheService>();
builder.Services.AddTransient<IReportGenerator, PdfReportGenerator>();
// ... 40 more lines
```

## The AutoWire solution

```csharp
// Put the registration intent next to the class — where it belongs.

[Scoped]
public class OrderService : IOrderService { ... }

[Scoped]
public class ProductService : IProductService { ... }

[Singleton]
public class RedisCacheService : ICacheService { ... }

[Transient]
public class PdfReportGenerator : IReportGenerator { ... }
```

```csharp
// Program.cs — one line, forever.
builder.Services.AddAutoWireServices();
```

AutoWire generates the full registration code **at compile time** — the code in `obj/` is exactly what you'd have written by hand.

---

## Installation

```
dotnet add package AutoWire
```

That's it. No other packages required.

---

## Quick start

### 1. Decorate your services

```csharp
using AutoWire;

// Register against all implemented non-system interfaces (auto-discovery)
[Scoped]
public class OrderService : IOrderService, IAuditableService { }
// → generates: services.AddScoped<IOrderService, OrderService>();
//              services.AddScoped<IAuditableService, OrderService>();

// Register against a specific interface only
[Singleton(typeof(ICache))]
public class RedisCache : ICache, IDisposable { }
// → generates: services.AddSingleton<ICache, RedisCache>();

// Register as concrete type (no interface)
[Transient]
public class PdfExporter { }
// → generates: services.AddTransient<PdfExporter>();

// Keyed service (.NET 8+)
[Scoped(Key = "primary")]
public class SqlOrderRepository : IOrderRepository { }
// → generates: services.AddKeyedScoped<IOrderRepository, SqlOrderRepository>("primary");
```

### 2. Register once

```csharp
// ASP.NET Core
builder.Services.AddAutoWireServices();

// Generic Host / Worker Service
services.AddAutoWireServices();
```

### 3. Inject normally

```csharp
public class CheckoutController(IOrderService orders, ICache cache)
{
    // resolved from AutoWire-generated registrations
}
```

---

## Attribute reference

| Attribute | DI lifetime | Generated call |
|---|---|---|
| `[Scoped]` | Scoped | `services.AddScoped<TService, TImpl>()` |
| `[Singleton]` | Singleton | `services.AddSingleton<TService, TImpl>()` |
| `[Transient]` | Transient | `services.AddTransient<TService, TImpl>()` |
| `[TryScoped]` | Scoped | `services.TryAddScoped<TService, TImpl>()` |
| `[TrySingleton]` | Singleton | `services.TryAddSingleton<TService, TImpl>()` |
| `[TryTransient]` | Transient | `services.TryAddTransient<TService, TImpl>()` |
| `[HostedService]` | Singleton | `services.AddHostedService<T>()` |
| `[Factory(typeof(IFoo))]` | Singleton (factory) + Scoped (product) | `services.AddSingleton<FooFactory>()` + `services.AddScoped<IFoo>(sp => ...)` |
| `[Options("Section")]` | — | `services.AddOptions<T>().BindConfiguration("Section").ValidateDataAnnotations().ValidateOnStart()` |
| `[HttpClient]` | — | `services.AddHttpClient<T>()` |
| `[Validate]` | Scoped | `services.AddScoped<IValidator<T>, ValidatorClass>()` |
| `[Interceptor(typeof(IFoo))]` | Scoped (proxy) | Generates a `file sealed` proxy class; registers it as `IFoo` implementation |

All attributes (except `[HostedService]` and `[Factory]`) support these shared properties:

| Property | Type | Description |
|---|---|---|
| `ServiceType` | `Type?` | Explicit service type. Default: all non-system interfaces |
| `Key` | `object?` | Keyed service key — accepts a `string` or any `enum` value (.NET 8+) |
| `Duplicate` | `DuplicateStrategy` | `Add` / `Skip` / `Replace` |
| `IncludeSelf` | `bool` | Also register as concrete type |
| `Profile` | `string?` | Only register when profile matches |
| `Condition` | `string?` | Wrap in `#if SYMBOL ... #endif` at compile time |
| `IncludeLazy` | `bool` | Also register `Lazy<T>` via `AddTransient` |
| `Module` | `string?` | Place service in a named module — excluded from `AddAutoWireServices()`, gets its own `Add{Module}Module()` method |

```csharp
// Auto-discover all non-system interfaces
[Scoped]

// Register against one specific interface
[Scoped(typeof(IMyService))]

// Keyed service (.NET 8+)
[Scoped(Key = "keyName")]

// Keyed + explicit interface (.NET 8+)
[Scoped(typeof(IMyService), Key = "keyName")]

// Also register the concrete type alongside the interface(s)
[Scoped(IncludeSelf = true)]
```

---

## IncludeSelf — register as concrete type too

When you want a class available both via its interface **and** directly by its concrete type, use `IncludeSelf = true`:

```csharp
[Scoped(IncludeSelf = true)]
public class AnalyticsService : IAnalyticsService { }
// → services.AddScoped<IAnalyticsService, AnalyticsService>();
// → services.AddScoped<AnalyticsService>();   ← additional
```

### When is this useful?

- **Decorator pattern** — the inner concrete type needs to be injectable separately
- **Integration tests** — resolve the real implementation by type while still respecting interface registrations
- **Mixed consumers** — one consumer needs `IAnalyticsService`; another needs `AnalyticsService` directly

### With an explicit ServiceType

`IncludeSelf = true` always adds the **concrete type**, regardless of how many interfaces were registered:

```csharp
[Scoped(typeof(INotificationService), IncludeSelf = true)]
public class NotificationService : INotificationService, IEmailSender { }
// → services.AddScoped<INotificationService, NotificationService>();
// → services.AddScoped<NotificationService>();
// IEmailSender is NOT registered — explicit ServiceType controls which interfaces win.
```

---

## Multiple attributes on one class

All attributes support `AllowMultiple = true`. Use this to register a single class against **several explicitly-specified interfaces**:

```csharp
// Without AllowMultiple you'd need a single attribute and auto-discovery.
// With AllowMultiple you can pick exactly which interfaces win:
[Scoped(typeof(IOrderReader))]
[Scoped(typeof(IOrderWriter))]
public class OrderService : IOrderReader, IOrderWriter, IDisposable { }
// → services.AddScoped<IOrderReader, OrderService>();
// → services.AddScoped<IOrderWriter, OrderService>();
// IDisposable is excluded from both registrations.
```

---

## DuplicateStrategy

Control what happens when the same service type is registered more than once using the `Duplicate` property:

```csharp
public enum DuplicateStrategy
{
    Add,     // (default) AddScoped — last registration wins
    Skip,    // TryAddScoped — skipped if service type already registered
    Replace  // RemoveAll + AddScoped — removes all prior registrations, then adds
}
```

### Replace — make the winner explicit

```csharp
[Scoped]
public class DefaultOrderService : IOrderService { }   // registered first

[Scoped(Duplicate = DuplicateStrategy.Replace)]
public class PremiumOrderService : IOrderService { }   // removes prior, registers itself
// → services.AddScoped<IOrderService, DefaultOrderService>();   // then...
// → services.RemoveAll<IOrderService>();
// → services.AddScoped<IOrderService, PremiumOrderService>();
// Result: only PremiumOrderService is registered for IOrderService.
```

### Skip — provide a default, respect consumer overrides

```csharp
[Scoped]
public class ProductionMessageBus : IMessageBus { }   // always registered

[Scoped(Duplicate = DuplicateStrategy.Skip)]
public class FallbackMessageBus : IMessageBus { }     // skipped — IMessageBus already taken
```

---

## TryScoped / TrySingleton / TryTransient

Convenience attributes that always use TryAdd semantics — ideal for **NuGet library authors** who want to provide sensible defaults without overriding the consumer's registrations:

```csharp
// In your library:
[TryScoped]
public class DefaultRetryPolicy : IRetryPolicy { }
// → services.TryAddScoped<IRetryPolicy, DefaultRetryPolicy>();

// Consumer's host:
services.AddAutoWireServices();           // DefaultRetryPolicy registered
services.AddScoped<IRetryPolicy, AggressiveRetryPolicy>(); // consumer overrides — fine

// Or consumer pre-registers before your library:
services.AddScoped<IRetryPolicy, AggressiveRetryPolicy>(); // registered first
services.AddAutoWireServices();           // TryAdd is skipped — no override
```

`[TryScoped]` / `[TrySingleton]` / `[TryTransient]` are equivalent to `[Scoped(Duplicate = DuplicateStrategy.Skip)]`.

---

## Hosted services (Worker Services)

`[HostedService]` registers a background service with a single attribute — no manual `AddHostedService<T>()` required.

```csharp
using AutoWire;
using Microsoft.Extensions.Hosting;

[HostedService]
public class DataSyncWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await SyncDataAsync();
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
```

```csharp
// Program.cs — one call registers everything, including hosted services
builder.Services.AddAutoWireServices();
```

AutoWire generates:

```csharp
services.AddHostedService<global::DataSyncWorker>();
```

`AddHostedService` uses `TryAddEnumerable` internally — calling `AddAutoWireServices()` multiple times is safe and idempotent.

---

## Roslyn diagnostics

AutoWire ships ten built-in diagnostics that surface problems **as squiggles in the IDE** — no runtime surprises.

| ID | Severity | Condition |
|---|---|---|
| AW001 | ⚠ Warning | `[Scoped]` / `[HostedService]` etc. applied to an **abstract class** — will never be registered |
| AW002 | ℹ Info | **Multiple non-keyed** `Add`-strategy registrations for the same service type |
| AW003 | ❌ Error | Explicit `ServiceType` in `[Scoped(typeof(IFoo))]` is **not implemented** by the decorated class |
| AW004 | ⚠ Warning | `[Singleton]` depends on a `[Scoped]` service — **captive dependency** that bypasses scope disposal |
| AW005 | ⚠ Warning | A type matches **more than one** `[AutoWireScan]` configuration — first match wins |
| AW006 | ⚠ Warning | `[Transient]` service implements `IDisposable` / `IAsyncDisposable` — container won't track disposal |
| AW007 | ℹ Info | Registered class has **no non-system interfaces** — consider extracting an abstraction |
| AW008 | ⚠ Warning | `[Singleton]` injects `IHttpContextAccessor` or `DbContext` — both are request/scope-bound |
| AW009 | ⚠ Warning | `[HostedService]` injects a `[Scoped]` service — **captive dependency** in a long-lived background worker |
| AW010 | ⚠ Warning | **Duplicate `[Interceptor]` targets** — two attributes on the same class target the same interface |

### AW001 example

```csharp
// ⚠ AW001: Abstract class 'BaseHandler' decorated with [Scoped] will not be registered.
[Scoped]
public abstract class BaseHandler : IHandler { }
```

### AW003 example

```csharp
// ❌ AW003 Error: 'ReportService' does not implement 'IOrderService'.
[Scoped(typeof(IOrderService))]
public class ReportService : IReportService { }  // wrong type!

// ✅ Fix:
[Scoped(typeof(IReportService))]
public class ReportService : IReportService { }
```

### AW004 example

```csharp
// ⚠ AW004: Singleton 'ReportingService' depends on Scoped service 'IOrderService'.
// The scoped service will be captured for the singleton's lifetime, bypassing scope disposal.
[Singleton]
public class ReportingService
{
    public ReportingService(IOrderService orders) { _orders = orders; }  // ← captive!
}

// ✅ Fix — inject IServiceScopeFactory and create a scope explicitly:
[Singleton]
public class ReportingService
{
    private readonly IServiceScopeFactory _scopeFactory;
    public ReportingService(IServiceScopeFactory scopeFactory) { _scopeFactory = scopeFactory; }

    public void GenerateReport()
    {
        using var scope = _scopeFactory.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
        // ... use orders within the scope
    }
}
```

### AW009 example

```csharp
// ⚠ AW009: 'DataSyncWorker' is a HostedService (effectively Singleton) and injects 'IOrderService' which is Scoped.
[HostedService]
public class DataSyncWorker : BackgroundService
{
    public DataSyncWorker(IOrderService orders) { }  // ← captive!
}

// ✅ Fix — inject IServiceScopeFactory (quick-fix available via Alt+Enter):
[HostedService]
public class DataSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    public DataSyncWorker(IServiceScopeFactory scopeFactory) { _scopeFactory = scopeFactory; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
            await orders.SyncAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
```

### AW010 example

```csharp
// ⚠ AW010: Multiple [Interceptor] attributes on 'LoggingInterceptor' target the same interface 'IOrderService'.
[Interceptor(typeof(IOrderService))]
[Interceptor(typeof(IOrderService))]  // ← duplicate!
public class LoggingInterceptor : IAutoWireInterceptor { ... }

// ✅ Fix — remove the duplicate, or use separate interceptor classes:
[Interceptor(typeof(IOrderService))]
public class LoggingInterceptor : IAutoWireInterceptor { ... }
```

AW004 covers both `[Singleton]` and `[TrySingleton]`, and detects scoped services registered via `[Scoped]` or `[TryScoped]`.

---

## Compile-time conditional — `Condition`

Use `Condition` to gate a registration behind a preprocessor symbol. AutoWire wraps the generated line(s) in `#if ... #endif`:

```csharp
// Only registered in DEBUG builds
[Scoped(Condition = "DEBUG")]
public class MockEmailService : IEmailService { }

// Only registered when FEATURE_REDIS is defined
[Singleton(typeof(ICacheService), Condition = "FEATURE_REDIS")]
public class RedisCache : ICacheService { }
```

Generated output:

```csharp
#if DEBUG
services.AddScoped<IEmailService, MockEmailService>();
#endif

#if FEATURE_REDIS
services.AddSingleton<ICacheService, RedisCache>();
#endif
```

`Condition` combines naturally with `Profile`:

```csharp
[Scoped(Profile = "staging", Condition = "DEBUG")]
public class StagingMockService : IMyService { }
// → only registered when profile == "staging" AND DEBUG is defined
```

---

## Lazy dependencies — `IncludeLazy`

Use `IncludeLazy = true` to also register `Lazy<T>` so that services can take optional or deferred dependencies:

```csharp
[Singleton(IncludeLazy = true)]
public class HeavyService : IHeavyService { }
// Generates:
// services.AddSingleton<IHeavyService, HeavyService>();
// services.AddTransient<Lazy<IHeavyService>>(sp => new Lazy<IHeavyService>(() => sp.GetRequiredService<IHeavyService>()));
```

Consumers can then inject `Lazy<IHeavyService>` to defer instantiation until first use:

```csharp
public class ReportController(Lazy<IHeavyService> heavy)
{
    public void OnDemand() => heavy.Value.Load();
}
```

---

## Factory pattern — `[Factory]`

Use `[Factory]` when a service can't be directly instantiated by the DI container — for example when it needs runtime parameters, connection strings, or configuration values that require non-trivial construction logic.

```csharp
[Factory(typeof(IDbConnection))]
public class DbConnectionFactory
{
    private readonly IConfiguration _config;
    public DbConnectionFactory(IConfiguration config) { _config = config; }

    public IDbConnection Create() =>
        new SqlConnection(_config.GetConnectionString("Default"));
}
```

AutoWire generates two registrations:

```csharp
// The factory class itself — Singleton so it's created once
services.AddSingleton<DbConnectionFactory>();

// The product — resolved via the factory's Create() method
services.AddScoped<IDbConnection>(sp =>
    sp.GetRequiredService<DbConnectionFactory>().Create());
```

### Controlling lifetimes

| Property | Default | What it controls |
|---|---|---|
| `Lifetime` | `"Scoped"` | Lifetime of the **product** (`IDbConnection`) |
| `FactoryLifetime` | `"Singleton"` | Lifetime of the **factory class** itself |

```csharp
// Singleton product (e.g. read-once config reader)
[Factory(typeof(IConfigReader), Lifetime = "Singleton")]
public class ConfigReaderFactory { ... }

// Transient product with scoped factory
[Factory(typeof(IToken), Lifetime = "Transient", FactoryLifetime = "Scoped")]
public class TokenFactory { ... }
```

---

## Convention-based scanning

For large codebases where adding an attribute to every class is impractical, AutoWire supports **convention-based scanning** via an assembly-level attribute.

```csharp
// At the top of any .cs file in your project (e.g. ScanConfig.cs)
[assembly: AutoWire.AutoWireScan("MyApp.Services")]
```

AutoWire will scan every non-abstract class in that namespace (and sub-namespaces by default) and register it **as if you had written `[Scoped]` on each one**.

### What is automatically registered?

- **Concrete, non-abstract classes** in the target namespace
- Classes **without** an existing `[Scoped]`, `[Singleton]`, `[Transient]` etc. attribute (explicit always wins)
- Classes **without** `[AutoWireExclude]`
- Open-generic classes are **skipped** (they need an explicit registration)

### Opting out

```csharp
[AutoWireExclude]
public class InternalHelper : IHelper { }   // skipped by scanning
```

### Changing the default lifetime

```csharp
// All classes in the namespace registered as Singleton
[assembly: AutoWire.AutoWireScan("MyApp.Services", Lifetime = "Singleton")]

// Or Transient
[assembly: AutoWire.AutoWireScan("MyApp.Adapters", Lifetime = "Transient")]
```

### Sub-namespace control

Sub-namespaces (`MyApp.Services.Impl`, `MyApp.Services.Adapters`, etc.) are included by default. Opt out with `IncludeSubNamespaces = false`:

```csharp
[assembly: AutoWire.AutoWireScan("MyApp.Services", IncludeSubNamespaces = false)]
```

### Multiple scan configurations

Stack multiple `[AutoWireScan]` attributes for different lifetimes or namespaces:

```csharp
[assembly: AutoWire.AutoWireScan("MyApp.Services")]                              // Scoped (default)
[assembly: AutoWire.AutoWireScan("MyApp.Repositories", Lifetime = "Singleton")]  // Singletons
[assembly: AutoWire.AutoWireScan("MyApp.Adapters", Lifetime = "Transient")]      // Transients
```

### Mixing explicit and convention registration

Scanning and explicit attributes compose naturally. Explicit `[Scoped]` etc. on a class **always takes priority** — scanned registrations won't duplicate it:

```csharp
[assembly: AutoWire.AutoWireScan("MyApp.Services")]

// This class is in MyApp.Services but has an explicit key — the explicit wins.
[Scoped(Key = "v2")]
public class OrderServiceV2 : IOrderService { }

// This class has no attribute — picked up by scanning.
public class InvoiceService : IInvoiceService { }
```

---

## Profile-based registration

Use `Profile` to conditionally register services based on environment or deployment context:

```csharp
// Always registered (no profile)
[Scoped]
public class InMemoryCache : ICache { }

// Only registered when profile matches
[Scoped(Profile = "production")]
public class RedisCache : ICache { }

[Scoped(Profile = "testing")]
public class NullCache : ICache { }
```

```csharp
// Register all unprofiled services + "production" services
builder.Services.AddAutoWireServices(profile: "production");

// Register only unprofiled services (default — unchanged behaviour)
builder.Services.AddAutoWireServices();
```

The `profile` parameter defaults to `null`. All existing projects that call `AddAutoWireServices()` without arguments are unaffected.

### Generated code

```csharp
public static IServiceCollection AddAutoWireServices(
    this IServiceCollection services,
    string? profile = null)
{
    services.AddScoped<ICache, InMemoryCache>();  // always

    if (profile == "production")
        services.AddScoped<ICache, RedisCache>();

    if (profile == "testing")
        services.AddScoped<ICache, NullCache>();

    return services;
}
```

> **Note:** Profile-specific services use last-registration-wins by default. Use `Duplicate = DuplicateStrategy.Replace` to explicitly remove the default before adding the profile service.

---

AutoWire fully supports open generic registrations. The correct `typeof()` overload is generated automatically — no reflection needed.

```csharp
// Auto-discovers compatible generic interfaces
[Scoped]
public class Repository<T> : IRepository<T> { }
// → services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

// Explicit service type
[Singleton(typeof(IReadOnlyRepository<>))]
public class CachedRepository<T> : IRepository<T>, IReadOnlyRepository<T> { }
// → services.AddSingleton(typeof(IReadOnlyRepository<>), typeof(CachedRepository<>));

// No interface — registers as concrete open generic
[Transient]
public class EventProcessor<T> { }
// → services.AddTransient(typeof(EventProcessor<>));
```

Resolving closed generics from DI works automatically:

```csharp
// All of these work after a single AddAutoWireServices() call:
provider.GetRequiredService<IRepository<Order>>();
provider.GetRequiredService<IRepository<Product>>();
provider.GetRequiredService<EventProcessor<EmailMessage>>();
```
---

## Decorator pattern

`[DecorateScoped]` / `[DecorateSingleton]` / `[DecorateTransient]` wraps an existing service registration with a decorator class at **compile time** — no Scrutor dependency required.

```csharp
// Inner service — registered normally
[Scoped]
public class OrderService : IOrderService
{
    public string GetStatus() => "pending";
}

// Decorator — wraps IOrderService
[DecorateScoped(typeof(IOrderService))]
public class LoggingOrderService : IOrderService
{
    private readonly IOrderService _inner;

    // Constructor takes the SERVICE TYPE (IOrderService) — AutoWire injects the concrete inner
    public LoggingOrderService(IOrderService inner) { _inner = inner; }

    public string GetStatus()
    {
        Console.WriteLine("GetStatus called");
        return _inner.GetStatus();
    }
}
```

```csharp
// Program.cs — unchanged
builder.Services.AddAutoWireServices();
```

AutoWire generates:

```csharp
// 1. Normal registration for inner service
services.AddScoped<IOrderService, OrderService>();

// 2. Decorator wiring (generated at the end, after all normal registrations)
services.RemoveAll<IOrderService>();
services.AddScoped<OrderService>();  // inner concrete self-registered — injectable directly
services.AddScoped<IOrderService>(sp =>
    (IOrderService)ActivatorUtilities.CreateInstance(
        sp, typeof(LoggingOrderService), sp.GetRequiredService<OrderService>()));
```

### What this gives you

- `provider.GetRequiredService<IOrderService>()` → `LoggingOrderService` wrapping `OrderService` ✓
- `provider.GetRequiredService<OrderService>()` → `OrderService` directly (useful in tests) ✓
- The decorator is the **same lifetime** as the `[DecorateScoped/Singleton/Transient]` attribute
- **No reflection** — the inner type is resolved at compile time from AutoWire's own registration map

### Multiple decorators

Apply `[Decorate*]` to a class that decorates multiple service types using `AllowMultiple`:

```csharp
[DecorateScoped(typeof(IOrderService))]
[DecorateScoped(typeof(IReadOnlyOrderService))]
public class CachingOrderService : IOrderService, IReadOnlyOrderService { ... }
```

### Ordered decorator stacking

When multiple decorators target the same service type, use `Order` to control which is innermost (closest to the original) and which is outermost (what consumers receive):

```csharp
[Scoped]
public class OrderService : IOrderService { ... }

// Order = 1 → applied first, wraps OrderService directly (inner)
[DecorateScoped(typeof(IOrderService), Order = 1)]
public class LoggingOrderService : IOrderService
{
    public LoggingOrderService(IOrderService inner) { _inner = inner; }
}

// Order = 2 → applied second, wraps LoggingOrderService (outer — what you receive)
[DecorateScoped(typeof(IOrderService), Order = 2)]
public class CachingOrderService : IOrderService
{
    public CachingOrderService(IOrderService inner) { _inner = inner; }
}
```

Result: `provider.GetRequiredService<IOrderService>()` → `CachingOrderService(LoggingOrderService(OrderService))`

All intermediate types are self-registered so you can inject them directly:
```csharp
provider.GetRequiredService<LoggingOrderService>(); // ✓ inner layer
provider.GetRequiredService<OrderService>();         // ✓ original
```

### Decorating manually-registered services

If the inner service wasn't registered via AutoWire (e.g. registered manually in `Program.cs`), AutoWire generates a runtime fallback that scans the `IServiceCollection` to find and wrap the existing registration automatically.

---

## Options binding — `[Options]`

`[Options]` generates the full `AddOptions<T>().BindConfiguration().ValidateDataAnnotations().ValidateOnStart()` chain — eliminating boilerplate for every configuration class.

Requires `Microsoft.Extensions.Options.ConfigurationExtensions`, `Microsoft.Extensions.Options.DataAnnotations`, and `Microsoft.Extensions.Hosting.Abstractions`.

```csharp
// Section name is "Database" (explicit)
[Options("Database")]
public class DatabaseOptions
{
    [Required] public string ConnectionString { get; set; } = "";
    public int MaxConnections { get; set; } = 10;
}

// Section name derived from class name: "EmailOptions" → "Email"
[Options]
public class EmailOptions
{
    public string SmtpHost { get; set; } = "localhost";
}

// Opt out of validation
[Options("Minimal", ValidateDataAnnotations = false, ValidateOnStart = false)]
public class MinimalOptions { }
```

Generated:

```csharp
services.AddOptions<DatabaseOptions>()
    .BindConfiguration("Database")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

| Property | Default | Description |
|---|---|---|
| Constructor arg | class name (sans "Options") | Configuration section key |
| `ValidateDataAnnotations` | `true` | Chain `.ValidateDataAnnotations()` |
| `ValidateOnStart` | `true` | Chain `.ValidateOnStart()` — throws on invalid config at startup |

---

## HTTP clients — `[HttpClient]`

`[HttpClient]` generates `services.AddHttpClient<T>()` with optional named-client configuration. Requires `Microsoft.Extensions.Http`.

```csharp
// Simple typed client
[HttpClient]
public class WeatherApiClient
{
    public WeatherApiClient(HttpClient http) { Http = http; }
    public HttpClient Http { get; }
}
// → services.AddHttpClient<WeatherApiClient>();

// Named client with base address
[HttpClient(Name = "GitHub", BaseAddress = "https://api.github.com")]
public class GitHubApiClient
{
    public GitHubApiClient(HttpClient http) { Http = http; }
    public HttpClient Http { get; }
}
// → services.AddHttpClient("GitHub", c => c.BaseAddress = new Uri("https://api.github.com"))
//           .AddTypedClient<GitHubApiClient>();
```

### Resilience policies

Set `Resilience = true` to chain `.AddStandardResilienceHandler()`, which adds retry, circuit-breaker, and timeout policies backed by `Microsoft.Extensions.Http.Resilience`:

```csharp
[HttpClient(Resilience = true)]
public class PaymentApiClient
{
    public PaymentApiClient(HttpClient http) { Http = http; }
    public HttpClient Http { get; }
}
// → services.AddHttpClient<PaymentApiClient>()
//           .AddStandardResilienceHandler();
```

| Property | Default | Description |
|---|---|---|
| `Name` | `null` (typed client) | Named-client name |
| `BaseAddress` | `null` | Sets `HttpClient.BaseAddress` |
| `Resilience` | `false` | Chains `.AddStandardResilienceHandler()` |
| `Timeout` | `0` (no timeout set) | Sets `HttpClient.Timeout` via `TimeSpan.FromSeconds(n)` |
| `DefaultHeaders` | `null` | `string[]` of `"Key:Value"` pairs — emits `c.DefaultRequestHeaders.Add(...)` |

```csharp
// Timeout + default headers
[HttpClient(
    BaseAddress = "https://api.myservice.com",
    Timeout = 30,
    DefaultHeaders = new[] { "Accept:application/json", "X-App-Id:myapp" })]
public class MyApiClient
{
    public MyApiClient(HttpClient http) { Http = http; }
    public HttpClient Http { get; }
}
// → services.AddHttpClient<MyApiClient>(static c => {
//       c.BaseAddress = new Uri("https://api.myservice.com");
//       c.Timeout = TimeSpan.FromSeconds(30);
//       c.DefaultRequestHeaders.Add("Accept", "application/json");
//       c.DefaultRequestHeaders.Add("X-App-Id", "myapp");
//   });
```

---

## FluentValidation — `[Validate]`

`[Validate]` auto-registers a FluentValidation validator with a single attribute — no manual `services.AddScoped<IValidator<T>, MyValidator>()` required.

Requires `FluentValidation` (or `FluentValidation.DependencyInjectionExtensions`). AutoWire only emits the registration code; it does not depend on FluentValidation itself.

```csharp
using FluentValidation;
using AutoWire;

[Validate]
public class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Items).NotEmpty();
    }
}
// → services.AddScoped<IValidator<CreateOrderRequest>, CreateOrderRequestValidator>();
```

AutoWire walks the inheritance chain to find `AbstractValidator<T>` and extracts `T` automatically. Registration is Scoped (matching FluentValidation conventions).

Inject `IValidator<T>` normally:

```csharp
public class OrderController(IValidator<CreateOrderRequest> validator, IOrderService orders)
{
    public async Task<IActionResult> Create(CreateOrderRequest request)
    {
        var result = await validator.ValidateAsync(request);
        if (!result.IsValid) return BadRequest(result.Errors);
        // ...
    }
}
```

---

## AOP interceptors — `[Interceptor]`

`[Interceptor(typeof(IMyService))]` generates a **compile-time proxy class** that wraps every method of the target interface through an `IAutoWireInterceptor` implementation — no Castle.DynamicProxy, no Autofac required.

### Defining an interceptor

```csharp
using AutoWire;

[Interceptor(typeof(IOrderService))]
public class LoggingInterceptor : IAutoWireInterceptor
{
    private readonly ILogger<LoggingInterceptor> _logger;
    public LoggingInterceptor(ILogger<LoggingInterceptor> logger) { _logger = logger; }

    public void Intercept(IAutoWireInvocation invocation)
    {
        _logger.LogInformation("Calling {Method}", invocation.MethodName);
        // Proceed is implicit — the proxy calls this interceptor once per method.
        // To return a value: invocation.Result = /* computed value */;
    }
}
```

AutoWire generates a `file sealed` proxy class and registers it as the `IOrderService` implementation:

```csharp
// Generated:
services.AddScoped<IOrderService>(sp =>
    new AutoWire_Proxy_OrderService_with_LoggingInterceptor(
        sp.GetRequiredService<LoggingInterceptor>()));
services.AddScoped<LoggingInterceptor>();

file sealed class AutoWire_Proxy_OrderService_with_LoggingInterceptor : IOrderService
{
    private readonly LoggingInterceptor _interceptor;
    // ... proxy methods that call _interceptor.Intercept(invocation)
}
```

### `IAutoWireInterceptor` and `IAutoWireInvocation`

```csharp
public interface IAutoWireInterceptor
{
    void Intercept(IAutoWireInvocation invocation);
}

public interface IAutoWireInvocation
{
    string MethodName { get; }
    object?[] Arguments { get; }
    object? Result { get; set; }   // set this to return a value from a non-void method
}
```

### Controlling lifetime

The proxy is registered with the same lifetime as the interceptor. Override with `Lifetime`:

```csharp
[Interceptor(typeof(IOrderService), Lifetime = "Singleton")]
public class CachingInterceptor : IAutoWireInterceptor { ... }
```

### Supported method signatures

| Method kind | Supported |
|---|---|
| `void` methods | ✅ |
| Value-returning methods | ✅ — set `invocation.Result` |
| `Task` / `ValueTask` | ✅ |
| `Task<T>` / `ValueTask<T>` | ✅ — set `invocation.Result` |
| Generic methods | ⛔ Skipped (proxy emits no wrapper) |
| `ref` / `out` parameters | ⛔ Skipped |

### AW010 — duplicate interceptor targets

AW010 warns when two `[Interceptor]` attributes on the **same class** target the **same interface**. Only the first is registered; the duplicate is dropped silently.

---

## Feature modules — `Module`

Use the `Module` property to group related services into an opt-in named module. Module services are **excluded from `AddAutoWireServices()`** and instead get their own generated extension method.

```csharp
// These services are NOT in AddAutoWireServices() — they're in AddPaymentsModule()
[Scoped(Module = "Payments")]
public class BankTransferService : IPaymentService { }

[Scoped(Module = "Payments")]
public class StripePaymentService : IPaymentService { }

// Different module
[Singleton(Module = "Notifications")]
public class SmsChannel : INotificationChannel { }
```

```csharp
// Program.cs
builder.Services.AddAutoWireServices();  // core services only

// Enable modules you want
builder.Services.AddPaymentsModule();
builder.Services.AddNotificationsModule();
```

AutoWire generates a separate extension method for each unique module name:

```csharp
public static IServiceCollection AddPaymentsModule(this IServiceCollection services)
{
    services.AddScoped<IPaymentService, BankTransferService>();
    services.AddScoped<IPaymentService, StripePaymentService>();
    return services;
}
```

### When to use modules

- **Feature flags** — ship code for a feature but only activate it when the module is registered
- **Optional integrations** — separate "core" from "Azure Storage", "Stripe", "SendGrid" modules
- **Microservice extraction** — move a module to its own project incrementally without breaking callers

---

## Multi-assembly scanning

`[AutoWireScan]` can scan a **different assembly** by pointing its `AssemblyOf` property at any public type from that assembly:

```csharp
// Scan the "External.Services" namespace in the assembly that contains MarkerType
[assembly: AutoWireScan("External.Services", AssemblyOf = typeof(External.MarkerType))]
```

AutoWire scans only `public` non-abstract classes from the external assembly and respects `[AutoWireExclude]` for classes in your own assembly.

---

## Registration summary — `RegistrationSummary`

AutoWire generates `AutoWireRegistrationSummary.g.cs` alongside the extension method — a compile-time snapshot of every service count, useful for startup logging and diagnostics:

```csharp
using AutoWire;

// In your startup code:
logger.LogInformation(
    "AutoWire registered {Total} services ({Scoped} scoped, {Singleton} singleton, {Transient} transient)",
    RegistrationSummary.TotalCount,
    RegistrationSummary.ScopedCount,
    RegistrationSummary.SingletonCount,
    RegistrationSummary.TransientCount);
```

Available constants: `TotalCount` · `ScopedCount` · `SingletonCount` · `TransientCount` · `HostedServiceCount` · `FactoryCount` · `HttpClientCount` · `ModuleServiceCount` · `RegisteredImplementations` (string array).

---

## Roslyn code fix providers

AutoWire ships **IDE light-bulb fixes** for four diagnostics — click the squiggle, press `Alt+Enter`, and the fix is applied automatically:

| Diagnostic | Fix |
|---|---|
| AW001 — abstract class with attribute | *Remove the AutoWire attribute* |
| AW003 — ServiceType not implemented | *Remove the explicit ServiceType argument* |
| AW006 — Transient disposable | *Change `[Transient]` → `[Scoped]`* |
| AW009 — Scoped in HostedService | *Replace with `IServiceScopeFactory` (rewrites constructor + adds private field)* |

---

## How it works

AutoWire is a [Roslyn incremental source generator](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview). At **build time** it:

1. Finds all non-abstract, non-generic classes decorated with `[Scoped]`, `[Singleton]`, or `[Transient]`
2. Resolves the correct service type(s) — explicit or auto-discovered
3. Emits `ServiceCollectionExtensions.AddAutoWireServices()` into your compilation

The generated file lives in `obj/` and looks exactly like hand-written code:

```csharp
// <auto-generated by AutoWire/>
public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddAutoWireServices(this IServiceCollection services)
    {
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IAuditableService, OrderService>();
        services.AddSingleton<ICache, RedisCache>();
        services.AddTransient<PdfExporter>();
        return services;
    }
}
```

**No reflection. No assembly scanning. No startup cost.**

---

## vs. the alternatives

| Approach | Registration | Runtime overhead | Refactor-safe |
|---|---|---|---|
| Manual | Write by hand | None | ❌ Easy to forget |
| [Scrutor](https://github.com/khellang/Scrutor) | Assembly scanning | Reflection at startup | ✅ |
| [Injectio](https://github.com/loresoft/Injectio) | Source generator | **None** | ✅ |
| **AutoWire** | Source generator | **None** | ✅ |

AutoWire differs from Scrutor in that registration happens **at compile time** — there is no assembly scanning, no reflection, and no startup cost. It also differs from Scrutor's convention-based scanning in that intent is expressed directly on the class, making it easy to understand what is registered without reading `Startup.cs`.

---

## Interface auto-discovery rules

When no `ServiceType` is specified, AutoWire registers the class against **every interface it implements**, excluding `System.*` interfaces (e.g. `IDisposable`, `IComparable`). If the class has no non-system interfaces, it is registered as its own concrete type.

```csharp
[Scoped]
public class OrderService : IOrderService, IDisposable
{
    public void Dispose() { }
}
// → services.AddScoped<IOrderService, OrderService>();
// IDisposable is excluded — "System.IDisposable" is a System.* interface
```

---

## Keyed services (.NET 8+)

```csharp
public interface IPaymentGateway { }

[Scoped(Key = "stripe")]
public class StripeGateway : IPaymentGateway { }

[Scoped(Key = "paypal")]
public class PayPalGateway : IPaymentGateway { }
```

```csharp
// Resolve by key
var stripe = serviceProvider.GetRequiredKeyedService<IPaymentGateway>("stripe");

// Or inject via [FromKeyedServices]
public class CheckoutService([FromKeyedServices("stripe")] IPaymentGateway gateway) { }
```

> **Note:** Keyed services require `Microsoft.Extensions.DependencyInjection` 8.0+. The `[Keyed]` property is available on all frameworks; the generated `AddKeyedScoped/Singleton/Transient` calls only compile on .NET 8+.

### Enum-keyed services

The `Key` property accepts **any enum value**, not just strings. AutoWire emits the fully-qualified enum member expression so resolution is type-safe at compile time:

```csharp
public enum PaymentProvider { Stripe = 1, PayPal = 2 }

[Scoped(Key = PaymentProvider.Stripe)]
public class StripeGateway : IPaymentGateway { }

[Scoped(Key = PaymentProvider.PayPal)]
public class PayPalGateway : IPaymentGateway { }

// Generated:
// services.AddKeyedScoped<IPaymentGateway, StripeGateway>(global::PaymentProvider.Stripe);
// services.AddKeyedScoped<IPaymentGateway, PayPalGateway>(global::PaymentProvider.PayPal);
```

```csharp
// Resolve — no magic strings
var gateway = provider.GetKeyedService<IPaymentGateway>(PaymentProvider.Stripe);
```

---

## Supported frameworks

`net6.0` · `net7.0` · `net8.0` · `net9.0` · `netstandard2.0` · `netstandard2.1`

Works with ASP.NET Core, Worker Services, MAUI, Blazor, console apps — any project using `Microsoft.Extensions.DependencyInjection`.

---

## FAQ

**Q: Can I use [DecorateScoped] with open generic types like IRepository<>?**
Not currently — Microsoft.Extensions.DependencyInjection doesn't support factory-based registrations for open generic types, which is required for the decorator pattern. Decorators work with closed generic types (e.g. `[DecorateScoped(typeof(IRepository<Order>))]`). Register the inner concrete type explicitly for open-generic scenarios.

**Q: I'm getting AW004 — what is a captive dependency?**
A captive dependency occurs when a Singleton holds a reference to a Scoped service. Since singletons live for the application's lifetime, the scoped service is never released when its scope ends. Fix it by injecting `IServiceScopeFactory` and creating a short-lived scope inside the method that needs the scoped service.

**Q: Can I resolve a service by both its interface and its concrete type?**
Yes — use `IncludeSelf = true`: `[Scoped(IncludeSelf = true)]`. AutoWire emits an extra `services.AddScoped<ConcreteType>()` in addition to the normal interface registrations. Useful for tests and the decorator pattern.

**Q: Does it work with Worker Services and background jobs?**
Yes — use `[HostedService]` on any class implementing `IHostedService` or extending `BackgroundService`. AutoWire generates `services.AddHostedService<T>()` and handles idempotency automatically.

**Q: What if I accidentally specify the wrong service type?**
AW003 catches it at compile time with a build error. `[Scoped(typeof(IFoo))]` on a class that doesn't implement `IFoo` will fail the build with a clear message rather than exploding at runtime.

**Q: What if I have two services implementing the same interface?**
AutoWire registers each independently and emits an AW002 info diagnostic. Use `DuplicateStrategy.Replace` to make the winner explicit, `DuplicateStrategy.Skip` to keep the first, or keyed services to disambiguate.

**Q: Can I register different implementations per environment (e.g. Production vs Testing)?**
Yes — use `Profile`: `[Scoped(Profile = "production")]`. Call `AddAutoWireServices(profile: "production")` to activate profile-specific services alongside unprofiled ones. Services with no `Profile` are always registered regardless.

**Q: Can I scan an entire namespace without adding attributes to every class?**
Yes — use `[assembly: AutoWire.AutoWireScan("MyApp.Services")]`. AutoWire registers every non-abstract class in that namespace (Scoped by default). Use `Lifetime = "Singleton"` or `"Transient"` to change the lifetime. Use `[AutoWireExclude]` on individual classes to opt out. Explicit `[Scoped]` etc. attributes always take priority over scanning.

**Q: Does it work with open generic types?**
Yes — AutoWire auto-discovers compatible open generic interfaces and emits `services.AddScoped(typeof(IRepo<>), typeof(Repo<>))`. See the [Open generic types](#open-generic-types) section.

**Q: Does it support the decorator pattern?**
Yes — use `[DecorateScoped(typeof(IService))]`, `[DecorateSingleton]`, or `[DecorateTransient]` on a class to wrap an existing registration. AutoWire generates compile-time code that self-registers the inner type and wires the decorator. No Scrutor required.

**Q: I'm writing a NuGet library and don't want to override my consumer's registrations. What should I use?**
Use `[TryScoped]`, `[TrySingleton]`, or `[TryTransient]`. These generate `TryAddScoped/Singleton/Transient` calls — the registration is silently skipped if the service type is already registered by the consumer.

**Q: Can I register one class against multiple interfaces?**
Yes — since `AllowMultiple = true`, you can stack attributes:
```csharp
[Scoped(typeof(IOrderReader))]
[Scoped(typeof(IOrderWriter))]
public class OrderService : IOrderReader, IOrderWriter { }
```
Without stacking, AutoWire auto-discovers ALL non-system interfaces, which achieves the same result when you want all of them.

**Q: My test project also uses AutoWire and I'm getting an ambiguous method error.**
Add `[assembly: AutoWire.AutoWireOptions(MethodName = "AddTestServices")]` to your test project. This renames the generated method so each project has a unique one.

**Q: Does it work with SpecFlow / xUnit / NUnit test fixtures?**
Yes. Call `AddAutoWireServices()` first, then register stubs/fakes after — last registration wins. See the [Testing & SpecFlow](#testing--specflow) section for a full example.

**Q: I'm getting CS1061 — 'AddAutoWireServices' not found. What's wrong?**
The generated `AddAutoWireServices()` extension method lives in the `AutoWire` namespace. Add `using AutoWire;` to any file that calls it, or add a global using to your project:
```csharp
// GlobalUsings.cs
global using AutoWire;
```

**Q: Can I see the generated code?**
Yes — look in `obj/Debug/net9.0/generated/AutoWire/AutoWire.AutoWireGenerator/AutoWireServiceCollectionExtensions.g.cs`.

**Q: Does it slow down my build?**
Incremental source generators only re-run when a decorated class changes. The overhead on a clean build is negligible — far less than assembly scanning at runtime.

**Q: Can I register FluentValidation validators automatically?**
Yes — use `[Validate]` on your `AbstractValidator<T>` subclass. AutoWire walks the inheritance chain, extracts `T`, and emits `services.AddScoped<IValidator<T>, MyValidator>()`. No manual registration required. AutoWire itself doesn't depend on FluentValidation.

**Q: Can I add AOP-style interception without a DI framework like Autofac?**
Yes — use `[Interceptor(typeof(IMyService))]` on a class implementing `IAutoWireInterceptor`. AutoWire generates a compile-time proxy class (no reflection) that routes every non-generic instance method through `Intercept(IAutoWireInvocation)`. Set `invocation.Result` to return a value from value-returning methods.

**Q: I'm getting AW009 — what does it mean?**
`[HostedService]` classes run for the application's lifetime (equivalent to Singleton scope). Injecting a `[Scoped]` service directly creates a captive dependency — the scoped service is never released when its scope ends. Use the AW009 code fix (Alt+Enter) to automatically rewrite the constructor to accept `IServiceScopeFactory` instead, then create a short-lived scope inside `ExecuteAsync`.

**Q: What frameworks are supported?**
`net6.0` · `net7.0` · `net8.0` · `net9.0` · `netstandard2.0` · `netstandard2.1` — any project using `Microsoft.Extensions.DependencyInjection`. Keyed services require .NET 8+.

---



---

## Testing & SpecFlow

### Replacing implementations in tests

Microsoft DI uses **last-registration-wins**, so you can always override production services after calling `AddAutoWireServices()`:

```csharp
// SpecFlow [BeforeScenario] hook or xUnit fixture
services.AddAutoWireServices();          // production registrations

services.AddScoped<IOrderService, FakeOrderService>();   // overrides — last wins
services.AddScoped<IEmailSender, NullEmailSender>();
```

### Fixing ambiguous method errors in test projects

If your **test project** also references AutoWire and decorates test doubles with `[Scoped]` etc., both assemblies generate `AddAutoWireServices()`, causing an ambiguous extension method error.

Fix it by adding one line to your test project:

```csharp
// Any .cs file in the test project, e.g. GlobalUsings.cs
[assembly: AutoWire.AutoWireOptions(MethodName = "AddTestServices")]
```

Now each project has a distinct method:

```csharp
services.AddAutoWireServices();  // production project
services.AddTestServices();      // test project — no ambiguity
```

### Full SpecFlow example

```csharp
// SpecFlow startup class
public class TestDependencies : IDependencyInjectionContainerBuilder
{
    public IServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();

        // Register all production services
        services.AddAutoWireServices();

        // Swap specific services with test doubles
        services.AddScoped<IPaymentGateway, StubPaymentGateway>();
        services.AddScoped<IEmailSender, SpyEmailSender>();

        return services;
    }
}
```
---

## Samples

Runnable examples in the [`samples/`](samples/) folder:

| Sample | What it shows |
|--------|---------------|
| [AutoWire.Sample.Api](samples/Api/) | Convention scanning, decorators, profiles, keyed services, Scalar UI |
| [AutoWire.Sample.Worker](samples/Worker/) | `[HostedService]`, Singleton, Transient in a background Worker |

```bash
cd samples/Api && dotnet run
cd samples/Worker && dotnet run
```

## 💼 Need .NET consulting?

I'm the author of AutoWire and a suite of 28+ Polly v8 resilience packages. I'm available for consulting on **Polly v8 resilience**, **Azure cloud architecture**, and **clean .NET design**.

**[→ solidqualitysolutions.com](https://www.solidqualitysolutions.com/)** · **[LinkedIn](https://www.linkedin.com/in/justbannister/)**
## License

MIT © Justin Bannister