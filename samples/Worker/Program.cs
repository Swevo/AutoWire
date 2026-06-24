// AutoWire Sample — Worker Service
// ──────────────────────────────────────────────────────────────────────────────
// Demonstrates [HostedService], [Singleton], [Transient], and [AutoWireScan].
// AutoWire generates AddAutoWireServices() at compile time.
// ──────────────────────────────────────────────────────────────────────────────

var builder = Host.CreateApplicationBuilder(args);

// ONE LINE: registers InMemoryDataRepository (Singleton), CsvReportService (Transient),
// DataSyncWorker (HostedService), and ReportWorker (HostedService).
builder.Services.AddAutoWireServices();

var host = builder.Build();
host.Run();
