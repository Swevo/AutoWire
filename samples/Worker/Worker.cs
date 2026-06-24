using AutoWire;
using AutoWire.Sample.Worker.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AutoWire.Sample.Worker;

/// <summary>
/// Background worker that processes pending items every 10 seconds.
/// [HostedService] generates: services.AddHostedService&lt;DataSyncWorker&gt;()
/// </summary>
[HostedService]
public class DataSyncWorker(
    IDataRepository repository,
    ILogger<DataSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DataSyncWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var items = await repository.GetPendingItemsAsync(stoppingToken);

            foreach (var item in items)
            {
                logger.LogInformation("Processing {Item}", item);
                await repository.MarkProcessedAsync(item, stoppingToken);
            }

            if (items.Count == 0)
                logger.LogInformation("No pending items — sleeping");

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}

/// <summary>
/// Background worker that generates a report every 30 seconds.
/// Requests a fresh IReportService (Transient) via IServiceScopeFactory
/// to demonstrate correct HostedService → Transient usage.
/// </summary>
[HostedService]
public class ReportWorker(
    IServiceScopeFactory scopeFactory,
    IDataRepository repository,
    ILogger<ReportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ReportWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            using var scope = scopeFactory.CreateScope();
            var reportService = scope.ServiceProvider.GetRequiredService<IReportService>();

            var items = await repository.GetPendingItemsAsync(stoppingToken);
            var report = reportService.GenerateReport(items);
            logger.LogInformation("Report: {Report}", report);
        }
    }
}
