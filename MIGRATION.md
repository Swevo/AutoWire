# Migrating to AutoWire

The `Swevo.AutoWire.Migrator` dotnet tool automates most of this (Scrutor, Autofac and Microsoft DI). See `tools/AutoWire.Migrator/README.md`.

## From manual Microsoft DI

```csharp
// Before
services.AddScoped<IOrderService, OrderService>();
// After
[Scoped] public class OrderService : IOrderService { }
```

Analyzer AW018 flags manual registrations and offers a code fix.

## From Scrutor

```csharp
// Before
services.Scan(s => s.FromAssemblyOf<Program>().AddClasses().AsImplementedInterfaces().WithScopedLifetime());
// After
[assembly: AutoWireScan(...)]   // convention scanning, see README
```

See [SCRUTOR-COMPARISON.md](SCRUTOR-COMPARISON.md) and diagnostic AW026.

## From Autofac

```csharp
// Before
builder.RegisterType<OrderService>().As<IOrderService>().InstancePerLifetimeScope();
// After
[Scoped] public class OrderService : IOrderService { }
```

Modules map to `[Module]`, named/keyed registrations map to `Key = "..."`, decorators map to `[DecorateScoped]`.
