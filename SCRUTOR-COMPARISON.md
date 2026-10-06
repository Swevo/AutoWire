# AutoWire vs Scrutor

This document provides a repeatable way to compare AutoWire and Scrutor in your environment.

## Latest benchmark snapshot

Source: `benchmarks/AutoWire.Benchmarks/BenchmarkDotNet.Artifacts/scrutor-vs-autowire-summary.md`

| Method | Mean | Allocated | Notes |
|---|---:|---:|---|
| Manual | 2.795 μs | 11.24 KB | Baseline |
| AutoWire | 3.722 μs | 11.24 KB | Compile-time registrations |
| Scrutor | 63.482 μs | 35.81 KB | Runtime reflection scanning |

In this run, Scrutor registration was ~22.9x slower than manual baseline and ~17x slower than AutoWire.

## Re-run the comparison

```powershell
pwsh .\tools\run-scrutor-comparison.ps1
```

This runs the benchmark project and writes a markdown summary.

## Migration confidence checks

1. Use the one-command migration tool:

```powershell
pwsh .\tools\migrate-scrutor.ps1 -Path . -ReportPath .\autowire-scrutor-migration-report.md
```

2. Validate parity with deterministic manifest diff:

```powershell
pwsh .\tools\compare-autowire-manifests.ps1 `
  -Baseline .\baseline\autowire-registrations.txt `
  -Candidate .\current\autowire-registrations.txt `
  -ReportPath .\autowire-manifest-diff.md
```
