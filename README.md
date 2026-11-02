# AutoWire

[![NuGet](https://img.shields.io/nuget/v/AutoWire.svg)](https://www.nuget.org/packages/AutoWire/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/AutoWire.svg)](https://www.nuget.org/packages/AutoWire/)
[![CI](https://github.com/Swevo/AutoWire/actions/workflows/build.yml/badge.svg)](https://github.com/Swevo/AutoWire/actions/workflows/build.yml)
[![Native AOT: verified in CI](https://img.shields.io/badge/Native%20AOT-verified%20in%20CI-2ea44f)](https://github.com/Swevo/AutoWire/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/Swevo/AutoWire/blob/master/LICENSE)

**Put `[Scoped]` on a class. AutoWire writes the `services.AddScoped<…>()` line for you at build time, and tells you before you run when your DI graph is wrong.**

- **No reflection, no assembly scanning.** Plain generated code, so it works with Native AOT and trimming (checked on every build).
- **DI mistakes become build errors and IDE warnings** — captive dependencies, circular dependencies, missing registrations, wrong service types — most with a one-click fix.
- **Migrate in one command** from Scrutor, Autofac or hand-written `AddScoped` lines.
- **Runs on any .NET 8 SDK or later** (Visual Studio 2022 17.8+).

## Install

```bash
dotnet add package AutoWire
```

## Before and after

```csharp
// Before: Program.cs grows forever, and breaks when someone forgets a line
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddSingleton<ICache, RedisCache>();
builder.Services.AddTransient<IReportGenerator, PdfReportGenerator>();
// ... 40 more lines
```

```csharp
// After: the registration lives next to the class
[Scoped]    public class OrderService : IOrderService { }
[Singleton] public class RedisCache : ICache { }
[Transient] public class PdfReportGenerator : IReportGenerator { }

// Program.cs: one line, forever
builder.Services.AddAutoWireServices();   // using AutoWire;
```

The generated code is ordinary `IServiceCollection` calls in `obj/` — exactly what you would have written by hand.

## Mistakes it catches while you type

```csharp
[Singleton]
public class ReportingService(IOrderService orders) { }
//        ⚠ AW004: Singleton 'ReportingService' depends on Scoped service 'IOrderService'
//          (captive dependency). Quick fix: inject IServiceScopeFactory.
```

| ID | What it catches |
|----|-----------------|
| AW003 ❌ | `[Scoped(typeof(IFoo))]` on a class that doesn't implement `IFoo` |
| AW004 / AW009 ⚠ | Singleton or hosted service capturing a scoped service |
| AW013 ⚠ | Constructor needs a service nobody registered |
| AW016 ❌ | Circular dependency between services |
| AW006 / AW008 ⚠ | Disposable transients; singletons holding `DbContext` or `IHttpContextAccessor` |

[All 26 diagnostics and their fixes →](https://github.com/Swevo/AutoWire/blob/master/docs/REFERENCE.md#roslyn-diagnostics)

## How it compares

| | Scrutor | Injectio | **AutoWire** |
|---|---|---|---|
| Registration happens | at startup (reflection) | at build time | **at build time** |
| Native AOT / trimming | ✗ | not stated | **✓ verified in CI** |
| Decorators (incl. open generic) | ✓ | ✓ | **✓** |
| Build-time checks for captive, circular and missing dependencies | ✗ | not stated | **✓ with code fixes** |
| Automated migration from Scrutor / Autofac | — | — | **✓** |
| Minimum toolchain | any | Roslyn 4.14 (VS 17.14 / .NET 9.0.300 SDK) | **.NET 8 SDK / VS 17.8** |

Based on each project's own documentation, November 2026. Corrections welcome via an issue.

## Migrating an existing app

```bash
dotnet tool install --global Swevo.AutoWire.Migrator
autowire-migrate --path .            # report only
autowire-migrate --path . --apply    # rewrite safe cases to attributes
```

It understands Scrutor `Scan(...)`/`Decorate(...)`, Autofac `RegisterType`/`RegisterAssemblyTypes`/`RegisterDecorator`, and plain `AddScoped/AddSingleton/AddTransient`, and rates each finding `AutoConverted`, `NeedsReview` or `Manual`. In the IDE, AW018 and AW026 offer the same migration as quick fixes. [Migration guide →](https://github.com/Swevo/AutoWire/blob/master/docs/REFERENCE.md#migrating-from-scrutor)

## Everything else it does

- **Lifetimes:** `[Scoped]`, `[Singleton]`, `[Transient]`, and `Try*` variants for library authors — [reference](https://github.com/Swevo/AutoWire/blob/master/docs/REFERENCE.md#attribute-reference)
- **Keyed services** with string or enum keys — [reference](https://github.com/Swevo/AutoWire/blob/master/docs/REFERENCE.md#keyed-services-net-8)
- **Decorators** with ordering, including open-generic decorators — [reference](https://github.com/Swevo/AutoWire/blob/master/docs/REFERENCE.md#decorator-pattern)
- **Open generics** — `Repository<T> : IRepository<T>` — [reference](https://github.com/Swevo/AutoWire/blob/master/docs/REFERENCE.md#open-generic-types)
- **Convention scanning** by namespace, and **other assemblies** — [reference](https://github.com/Swevo/AutoWire/blob/master/docs/REFERENCE.md#convention-based-scanning)
- **Profiles, conditions and modules** for per-environment and opt-in registration — [reference](https://github.com/Swevo/AutoWire/blob/master/docs/REFERENCE.md#profile-based-registration)
- **Factories, hosted services, options binding, typed HttpClients, FluentValidation validators** — [reference](https://github.com/Swevo/AutoWire/blob/master/docs/REFERENCE.md#attribute-reference)
- **Compile-time interceptors** (AOP without Castle) and **minimal API endpoints** — [reference](https://github.com/Swevo/AutoWire/blob/master/docs/REFERENCE.md#aop-interceptors--interceptor)
- **Registration manifest and Mermaid dependency graph** for CI diffs and docs — [reference](https://github.com/Swevo/AutoWire/blob/master/docs/REFERENCE.md#registration-manifest-export--registrationsummaryregistrationmanifest)
- **Test helpers** — `services.OverrideService<IFoo, FakeFoo>()` — [reference](https://github.com/Swevo/AutoWire/blob/master/docs/REFERENCE.md#testing--specflow)

## Requirements

- **Build:** .NET 8 SDK or later, Visual Studio 2022 17.8+ or Rider 2023.3+.
- **App:** anything using `Microsoft.Extensions.DependencyInjection` — ASP.NET Core, Worker Services, MAUI, Blazor, console. Targets `net6.0`+ and `netstandard2.0`+. Keyed services need DI 8.0+.
- AutoWire adds nothing to your output: it ships only a source generator and analyzers.

## Learn more

- [Full reference](https://github.com/Swevo/AutoWire/blob/master/docs/REFERENCE.md) — every attribute, option and diagnostic
- [Samples](https://github.com/Swevo/AutoWire/tree/master/samples) — Web API and Worker Service
- [Changelog](https://github.com/Swevo/AutoWire/blob/master/CHANGELOG.md)
- [Scrutor comparison and benchmark](https://github.com/Swevo/AutoWire/blob/master/SCRUTOR-COMPARISON.md) — registration is ~17× faster than Scrutor's runtime scanning

## Author

Made by Justin Bannister, who also writes [other compile-time .NET libraries](https://swevo.github.io/) and consults on Polly v8 resilience, Azure architecture and clean .NET design: [solidqualitysolutions.com](https://www.solidqualitysolutions.com/) · [LinkedIn](https://www.linkedin.com/in/justbannister/).

## License

MIT © Justin Bannister
