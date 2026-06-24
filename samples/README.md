# AutoWire Samples

Two runnable projects that demonstrate every major AutoWire feature.

## AutoWire.Sample.Api — Web API

Demonstrates:
- **Convention scanning** (`[AutoWireScan]`) — `WeatherService`, `MemoryCacheService` registered automatically
- **Decorator** (`[DecorateScoped]` with `Order`) — `LoggingWeatherDecorator` wraps `IWeatherService`
- **Profile-based registration** (`Profile = "production"`) — `RedisCacheService` vs `MemoryCacheService`
- **Keyed services** (`Key = "formal"` / `Key = "casual"`) — `FormalGreeter` / `CasualGreeter`
- **Singleton** — `RequestCounter` across all requests

```bash
cd Api
dotnet run
# Open https://localhost:{port}/scalar/v1 for the Swagger UI
```

Set `AutoWire:Profile` in `appsettings.json` to `"production"` to activate `RedisCacheService`.

## AutoWire.Sample.Worker — Worker Service

Demonstrates:
- **`[HostedService]`** — `DataSyncWorker` and `ReportWorker` run in the background
- **`[Singleton]`** — `InMemoryDataRepository` shared across both workers
- **`[Transient]`** — `CsvReportService` gets a fresh instance per use

```bash
cd Worker
dotnet run
```

## Key point: `using AutoWire;`

Since AutoWire generates the `ServiceCollectionExtensions.AddAutoWireServices()` method inside the `AutoWire` namespace, your project needs:

```csharp
// GlobalUsings.cs
global using AutoWire;
```

Or per-file: `using AutoWire;` in any file that calls `services.AddAutoWireServices()`.
