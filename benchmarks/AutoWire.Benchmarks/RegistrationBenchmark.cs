// ──────────────────────────────────────────────────────────────────────────────
// RegistrationBenchmark
// ──────────────────────────────────────────────────────────────────────────────
// Compares the cost of wiring up 20 services using:
//   • AutoWire  — compile-time generated; AddAutoWireServices() is a plain list
//                 of services.AddScoped/Singleton/Transient calls. Zero reflection.
//   • Scrutor   — runtime assembly scan via services.Scan(...).
//   • Manual    — hand-written baseline (no overhead at all).
//
// Run with:  dotnet run -c Release
// ──────────────────────────────────────────────────────────────────────────────

namespace AutoWire.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(iterationCount: 20)]
public class RegistrationBenchmark
{
    [Benchmark(Baseline = true)]
    public IServiceProvider Manual()
    {
        var services = new ServiceCollection();
        services.AddScoped<Services.IOrderService, Services.OrderService>();
        services.AddScoped<Services.IProductService, Services.ProductService>();
        services.AddScoped<Services.ICustomerService, Services.CustomerService>();
        services.AddScoped<Services.IInventoryService, Services.InventoryService>();
        services.AddScoped<Services.IPaymentService, Services.PaymentService>();
        services.AddScoped<Services.IShippingService, Services.ShippingService>();
        services.AddScoped<Services.INotificationService, Services.NotificationService>();
        services.AddScoped<Services.IReportService, Services.ReportService>();
        services.AddScoped<Services.IAnalyticsService, Services.AnalyticsService>();
        services.AddScoped<Services.IAuditService, Services.AuditService>();
        services.AddSingleton<Services.ICacheService, Services.CacheService>();
        services.AddSingleton<Services.ISearchService, Services.SearchService>();
        services.AddTransient<Services.IEmailService, Services.EmailService>();
        services.AddTransient<Services.ISmsService, Services.SmsService>();
        services.AddTransient<Services.IPushService, Services.PushService>();
        services.AddScoped<Services.IUserService, Services.UserService>();
        services.AddScoped<Services.IRoleService, Services.RoleService>();
        services.AddScoped<Services.IPermissionService, Services.PermissionService>();
        services.AddSingleton<Services.ISettingsService, Services.SettingsService>();
        services.AddTransient<Services.ILocalizationService, Services.LocalizationService>();
        return services.BuildServiceProvider();
    }

    [Benchmark]
    public IServiceProvider AutoWire()
    {
        var services = new ServiceCollection();
        // Generated at compile time — no reflection, no assembly scanning
        services.AddAutoWireServices();
        return services.BuildServiceProvider();
    }

    [Benchmark]
    public IServiceProvider Scrutor()
    {
        var services = new ServiceCollection();
        services.Scan(scan => scan
            .FromAssemblyOf<Services.OrderService>()
            .AddClasses()
            .AsImplementedInterfaces()
            .WithScopedLifetime());
        return services.BuildServiceProvider();
    }
}
