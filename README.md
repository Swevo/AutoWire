# AutoWire

[![NuGet](https://img.shields.io/nuget/v/AutoWire.svg)](https://www.nuget.org/packages/AutoWire/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/AutoWire.svg)](https://www.nuget.org/packages/AutoWire/)
[![CI](https://github.com/Swevo/AutoWire/actions/workflows/build.yml/badge.svg)](https://github.com/Swevo/AutoWire/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**Compile-time dependency injection auto-registration for .NET** — add `[Scoped]`, `[Singleton]`, or `[Transient]` to your services and AutoWire generates the `IServiceCollection` registration code at build time.

**Zero runtime overhead. No reflection. No startup cost.**

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

All attributes share the same constructor overloads:

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

AutoWire ships four built-in diagnostics that surface problems **as squiggles in the IDE** — no runtime surprises.

| ID | Severity | Condition |
|---|---|---|
| AW001 | ⚠ Warning | `[Scoped]` / `[HostedService]` etc. applied to an **abstract class** — will never be registered |
| AW002 | ℹ Info | **Multiple non-keyed** `Add`-strategy registrations for the same service type |
| AW003 | ❌ Error | Explicit `ServiceType` in `[Scoped(typeof(IFoo))]` is **not implemented** by the decorated class |
| AW004 | ⚠ Warning | `[Singleton]` depends on a `[Scoped]` service — **captive dependency** that bypasses scope disposal |

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

AW004 covers both `[Singleton]` and `[TrySingleton]`, and detects scoped services registered via `[Scoped]` or `[TryScoped]`.



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

---

## Supported frameworks

`net6.0` · `net7.0` · `net8.0` · `net9.0` · `netstandard2.0` · `netstandard2.1`

Works with ASP.NET Core, Worker Services, MAUI, Blazor, console apps — any project using `Microsoft.Extensions.DependencyInjection`.

---

## FAQ

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

**Q: Can I see the generated code?**
Yes — look in `obj/Debug/net9.0/generated/AutoWire/AutoWire.AutoWireGenerator/AutoWireServiceCollectionExtensions.g.cs`.

**Q: Does it slow down my build?**
Incremental source generators only re-run when a decorated class changes. The overhead on a clean build is negligible — far less than assembly scanning at runtime.

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

## 💼 Need .NET consulting?

I'm the author of AutoWire and a suite of 28+ Polly v8 resilience packages. I'm available for consulting on **Polly v8 resilience**, **Azure cloud architecture**, and **clean .NET design**.

**[→ solidqualitysolutions.com](https://www.solidqualitysolutions.com/)** · **[LinkedIn](https://www.linkedin.com/in/justbannister/)**
## License

MIT © Justin Bannister