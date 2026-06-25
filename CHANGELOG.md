# Changelog

All notable changes to **AutoWire** are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.17.0] — 2026-06-25

### Added
- **AW010 diagnostic** — warns when two or more `[Interceptor]` attributes on the same class target the same interface.
  Only the first will be registered; the duplicate is silently dropped, which is almost never intentional.
- **Code fix for AW009** — "Replace Scoped dependency with IServiceScopeFactory".
  When AW009 fires, a Roslyn quick-fix rewrites the constructor to accept `IServiceScopeFactory`, stores it in a private field, and removes the problematic scoped parameter.
- **`Timeout` property** on `[HttpClient]` — sets `HttpClient.Timeout` via `TimeSpan.FromSeconds(n)`.
- **`DefaultHeaders` property** on `[HttpClient]` — accepts `string[]` entries in `"Key:Value"` format; emits `c.DefaultRequestHeaders.Add(...)` calls inside the configure lambda.

---



### Added
- **AW009 diagnostic** — warns when a `[HostedService]`-decorated class injects a Scoped service directly.
  Background services live for the application lifetime (singleton scope); injecting a scoped dependency causes a captive dependency bug.
  Fix: inject `IServiceScopeFactory` and create a scope per execution cycle.
- **`[Validate]` attribute** — register FluentValidation validators with a single attribute.
  Decorate your `AbstractValidator<T>` subclass; AutoWire emits `services.AddScoped<IValidator<T>, MyValidator>()` automatically.
  No direct FluentValidation package reference required in AutoWire itself — registration code is emitted as source.
- **`[Interceptor]` attribute** — compile-time AOP proxy generation.
  Apply `[Interceptor(typeof(IMyService))]` to a class that implements `IAutoWireInterceptor`; AutoWire generates a `file sealed` proxy class implementing every non-generic instance method of the interface, routing each call through `interceptor.Intercept(IAutoWireInvocation)`.
  Supports `void`, `Task`, `Task<T>`, and value-returning methods. Generic methods and `ref`/`out` parameters are skipped.

---

## [1.15.0] — 2026-06-25

### Added
- **`Module` property** on all six registration attributes (`[Scoped]`, `[Singleton]`, `[Transient]` and their `Try*` variants).
  Services tagged with a module (e.g. `[Scoped(Module = "Payments")]`) are **excluded from `AddAutoWireServices()`** and instead get their own generated extension method: `services.AddPaymentsModule()`.
  Use this to split a large composition root into opt-in feature modules.
- **`Resilience` property** on `[HttpClient]`.
  Setting `Resilience = true` chains `.AddStandardResilienceHandler()` after `AddHttpClient<T>()`, adding retry, circuit-breaker and timeout policies.
  Requires `Microsoft.Extensions.Http.Resilience`.
- **AW008 diagnostic** — warns when a `[Singleton]` or `[TrySingleton]` class constructor injects `IHttpContextAccessor` or any `DbContext`-derived type.
  Both are request-scoped or unit-of-work objects and are unsafe to capture inside a singleton.
- **`AutoWireRegistrationSummary.g.cs`** — a compile-time generated `internal static class RegistrationSummary` containing:
  `TotalCount`, `ScopedCount`, `SingletonCount`, `TransientCount`, `HostedServiceCount`, `FactoryCount`, `HttpClientCount`, `ModuleServiceCount`, and a `string[] RegisteredImplementations` array.
  Emit these to your startup logs with one line: `logger.LogInformation("AutoWire registered {N} services", RegistrationSummary.TotalCount);`

---

## [1.14.0] — 2026-06-25

### Added
- **`[HttpClient]` attribute** — generates `services.AddHttpClient<T>()` for typed clients.
  Optional `Name` and `BaseAddress` properties produce a named-client chain with `AddTypedClient<T>()`.
  Requires `Microsoft.Extensions.Http`.
- **AW007 diagnostic** (Info) — fires when a registered class implements no non-system interfaces and has no explicit `ServiceType`.
  Encourages programming to abstractions for testability.
- **Roslyn code fix providers** (new `AutoWire.CodeFixes` project, packed alongside the generator):
  - **AW001** — *Remove attribute*: removes the AutoWire registration attribute from an abstract class.
  - **AW003** — *Remove ServiceType*: removes the explicit `ServiceType` named argument when the class does not implement it.
  - **AW006** — *Change to Scoped*: replaces `[Transient]` / `[TryTransient]` with `[Scoped]` / `[TryScoped]` when the service implements `IDisposable` or `IAsyncDisposable`.

---

## [1.13.0] — 2026-06-25

### Added
- **AW006 diagnostic** — warns when a `[Transient]` or `[TryTransient]` service implements `IDisposable` or `IAsyncDisposable`.
  The DI container does not track transient disposables; they will not be disposed at end-of-scope, causing resource leaks.
- **`[Options]` attribute** — generates `services.AddOptions<T>().BindConfiguration("Section").ValidateDataAnnotations().ValidateOnStart()`.
  Section defaults to the class name with the `Options` suffix stripped.
  Requires `Microsoft.Extensions.Options.ConfigurationExtensions`, `Microsoft.Extensions.Options.DataAnnotations`, and `Microsoft.Extensions.Hosting.Abstractions`.
- **Enum-keyed services** — the `Key` property on all registration attributes now accepts any `enum` value (not just strings).
  The generator emits the fully-qualified enum member expression (e.g. `global::PaymentProvider.Stripe`) ensuring type-safe resolution via `GetKeyedService<T>(PaymentProvider.Stripe)`.
- **Multi-assembly scanning** — `[AutoWireScan]` now has an `AssemblyOf` property.
  Point it at any public type in an external assembly to scan that assembly's namespaces: `[assembly: AutoWireScan("External.Services", AssemblyOf = typeof(External.MarkerType))]`.

---

## [1.12.0] — 2026-06-24

### Added
- **`Condition` property** on all six registration attributes — wraps the generated registration in a `#if SYMBOL … #endif` preprocessor block.
  Example: `[Scoped(Condition = "DEBUG")]` registers the service only in debug builds.
- **`IncludeLazy` property** on all six registration attributes — additionally registers `Lazy<T>` (and `Lazy<IService>`) via `AddTransient` so consumers can take a lazy dependency on the service.

---

## [1.11.0] — 2026-06-24

### Added
- **`[Factory]` attribute** — marks a class as a factory.
  AutoWire emits two registrations: the factory itself (default lifetime: Singleton) and its product type (default lifetime: Scoped), resolved via `sp => sp.GetRequiredService<Factory>().Create()`.
  `Lifetime` and `FactoryLifetime` properties control both sides independently.
- **AW005 diagnostic** — warns when a type is matched by more than one `[AutoWireScan]` configuration (ambiguous scan).
- **BenchmarkDotNet project** — micro-benchmarks added under `benchmarks/` to track generator throughput.

---

## [1.10.0] — 2026-06-24

### Added
- **Convention scanning** via `[assembly: AutoWireScan("MyApp.Services")]` — scans the specified namespace and registers all non-abstract, non-excluded classes that do not already carry an explicit AutoWire attribute.
  - `Lifetime` property: `"Scoped"` (default), `"Singleton"`, or `"Transient"`.
  - `IncludeSubNamespaces` property: `true` by default (scans child namespaces).
  - `[AutoWireExclude]` attribute: opts a specific class out of scanning.
- **`AutoWireOptionsAttribute`** (assembly-level) — sets a custom name for the generated extension method via `MethodName`. Useful in test projects to avoid ambiguous extension method errors when both production and test assemblies reference AutoWire.

### Fixed
- **v1.10.1** — corrected the NuGet package layout so the generator DLL is placed in `analyzers/dotnet/cs/` and is correctly loaded by the SDK.

---

## [1.9.0] — 2026-06-24

### Added
- **`Profile` property** on all six registration attributes — services with a profile are only registered when `AddAutoWireServices(profile: "cloud")` is called with a matching value.
  Services with no profile are always registered.
  Use this to swap implementations between environments (development, staging, cloud) at startup.
- Updated package icon.

---

## [1.8.0] — 2026-06-24

### Added
- **`Order` property** on `[DecorateScoped]`, `[DecorateSingleton]`, and `[DecorateTransient]` — controls the order in which multiple decorators targeting the same service are applied.
  Lower values are innermost (closer to the original implementation); higher values are outermost (the final resolved type).

---

## [1.7.0] — 2026-06-24

### Added
- **AW004 diagnostic** — warns when a `[Singleton]` service's constructor injects a parameter whose type is registered as `[Scoped]`.
  Singleton-captures-Scoped is a classic lifetime mismatch: the scoped service is held for the entire application lifetime, bypassing disposal and potentially causing stale state.

---

## [1.6.0] — 2026-06-24

### Added
- **`IncludeSelf` property** on all six registration attributes — additionally registers the concrete implementation type itself alongside its interface(s).
  Useful when tests or internal code need to inject by concrete type while production code uses the interface.

---

## [1.5.0] — 2026-06-24

### Added
- **`[HostedService]` attribute** — registers the decorated class via `services.AddHostedService<T>()`.
  The class must implement `IHostedService` or extend `BackgroundService`.
- **AW003 diagnostic** — error when the type passed as `ServiceType` is not implemented by the decorated class.
  Catches mis-configured registrations at compile time rather than at runtime.

---

## [1.4.0] — 2026-06-24

### Added
- **Decorator support** via `[DecorateScoped]`, `[DecorateSingleton]`, and `[DecorateTransient]`.
  Wraps an existing registration with a decorator class; the inner implementation is also registered as a concrete type so the decorator can receive it by injection.
  Multiple decorators on the same service type are chained in a single `RemoveAll` + re-register pass.

---

## [1.3.0] — 2026-06-24

### Added
- **`DuplicateStrategy` enum** and **`Duplicate` property** on all registration attributes.
  - `Add` (default) — registers regardless of existing registrations; last registration wins.
  - `Skip` — skips if the service type is already registered (TryAdd semantics; ideal for library authors).
  - `Replace` — removes all existing registrations for the service type before adding this one.
- **`[TryScoped]`, `[TryTransient]`, `[TrySingleton]`** — shorthand attributes that default to `DuplicateStrategy.Skip`.
- **AW002 diagnostic** (Info) — fires when multiple non-keyed `Add`-strategy registrations share the same service type (last one wins).

---

## [1.2.0] — 2026-06-24

### Added
- **Open generic registration** — `[Scoped]` on `Repository<T> : IRepository<T>` emits `services.AddScoped(typeof(IRepository<>), typeof(Repository<>))`.
  Explicit `ServiceType = typeof(IReadOnlyRepository<>)` is also supported for targeting a specific open generic interface.

---

## [1.0.1] — 2026-06-24

### Fixed
- Corrected assembly metadata so the generator is correctly identified by IDEs and Roslyn tooling.

---

## [1.0.0] — 2026-06-24

### Added
- Initial release.
- `[Scoped]`, `[Singleton]`, `[Transient]` attributes — decorate a class to have AutoWire generate an `IServiceCollection` registration at compile time via Roslyn source generation.
- **Auto-discovery** of service types: when no explicit `ServiceType` is provided, all non-system interfaces the class implements are used; falls back to the concrete type if none are found.
- **Explicit `ServiceType`** constructor argument — registers against the specified interface only.
- **`Key` property** (string) — generates a keyed-service registration via `AddKeyedScoped/Singleton/Transient`. Requires .NET 8+ / `Microsoft.Extensions.DependencyInjection` 8+.
- **AW001 diagnostic** — error when an abstract class carries a registration attribute (abstract classes cannot be instantiated by the DI container).
- Zero runtime overhead: no reflection, no startup scanning, no IL weaving — all work is done at build time.
- Compatible with ASP.NET Core, Worker Services, MAUI, and any project using `Microsoft.Extensions.DependencyInjection`.

---

[1.15.0]: https://github.com/Swevo/AutoWire/compare/v1.14.0...v1.15.0
[1.14.0]: https://github.com/Swevo/AutoWire/compare/v1.13.0...v1.14.0
[1.13.0]: https://github.com/Swevo/AutoWire/compare/v1.12.0...v1.13.0
[1.12.0]: https://github.com/Swevo/AutoWire/compare/v1.11.0...v1.12.0
[1.11.0]: https://github.com/Swevo/AutoWire/compare/v1.10.0...v1.11.0
[1.10.0]: https://github.com/Swevo/AutoWire/compare/v1.9.0...v1.10.0
[1.9.0]: https://github.com/Swevo/AutoWire/compare/v1.8.0...v1.9.0
[1.8.0]: https://github.com/Swevo/AutoWire/compare/v1.7.0...v1.8.0
[1.7.0]: https://github.com/Swevo/AutoWire/compare/v1.6.0...v1.7.0
[1.6.0]: https://github.com/Swevo/AutoWire/compare/v1.5.0...v1.6.0
[1.5.0]: https://github.com/Swevo/AutoWire/compare/v1.4.0...v1.5.0
[1.4.0]: https://github.com/Swevo/AutoWire/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/Swevo/AutoWire/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/Swevo/AutoWire/compare/v1.0.1...v1.2.0
[1.0.1]: https://github.com/Swevo/AutoWire/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/Swevo/AutoWire/releases/tag/v1.0.0
