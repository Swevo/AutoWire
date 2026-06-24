# AutoWire Benchmarks

Measures the cost of registering 20 services (14 Scoped, 3 Singleton, 3 Transient) and building the `IServiceProvider` using three approaches:

| Method | What it measures |
|--------|-----------------|
| **Manual** | Hand-written `services.AddScoped<>()` calls — the theoretical minimum |
| **AutoWire** | `services.AddAutoWireServices()` — compile-time generated, equivalent to Manual |
| **Scrutor** | `services.Scan(...)` — runtime assembly scanning |

## Results (AMD Ryzen 9 5900X, .NET 9)

| Method | Mean | Ratio | Allocated |
|--------|------|-------|-----------|
| Manual | 3.01 µs | 1.0× | 11.24 KB |
| **AutoWire** | **3.68 µs** | **1.2×** | **11.24 KB** |
| Scrutor | 57.43 µs | 19.1× | 32.42 KB |

AutoWire is ~19× faster than Scrutor and allocates 65% less memory.
The small overhead vs Manual is `BuildServiceProvider()` non-determinism, not AutoWire.

## Running

```bash
cd benchmarks/AutoWire.Benchmarks
dotnet run -c Release
```

Results are written to `BenchmarkDotNet.Artifacts/results/`.
