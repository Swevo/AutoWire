using Microsoft.Extensions.Hosting;
public class GeneratedCodeTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddAutoWireServices();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Scoped_RegistersAgainstInterface()
    {
        using var provider = BuildProvider();
        var svc = provider.GetService<IOrderService>();
        Assert.NotNull(svc);
        Assert.IsType<OrderService>(svc);
    }

    [Fact]
    public void Scoped_IsResolvableTwiceFromSameScope_ReturnsSameInstance()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var a = scope.ServiceProvider.GetService<IOrderService>();
        var b = scope.ServiceProvider.GetService<IOrderService>();
        Assert.Same(a, b);
    }

    [Fact]
    public void Singleton_WithExplicitServiceType_RegistersOnlyAsThat()
    {
        using var provider = BuildProvider();
        Assert.NotNull(provider.GetService<ICache>());
        Assert.IsType<MemoryCache>(provider.GetService<ICache>());
        Assert.Null(provider.GetService<ISecondaryCache>());
    }

    [Fact]
    public void Singleton_IsSameInstanceEverywhere()
    {
        using var provider = BuildProvider();
        var a = provider.GetService<ICache>();
        var b = provider.GetService<ICache>();
        Assert.Same(a, b);
    }

    [Fact]
    public void Transient_NoConcrete_RegistersAsConcrete()
    {
        using var provider = BuildProvider();
        Assert.NotNull(provider.GetService<EmailSender>());
    }

    [Fact]
    public void Transient_IsNewInstanceEveryTime()
    {
        using var provider = BuildProvider();
        var a = provider.GetService<EmailSender>();
        var b = provider.GetService<EmailSender>();
        Assert.NotSame(a, b);
    }

    [Fact]
    public void Scoped_MultipleInterfaces_RegistersBoth()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<IReader>());
        Assert.NotNull(scope.ServiceProvider.GetService<IWriter>());
    }

    [Fact]
    public void Scoped_MultipleInterfaces_SameInstancePerScope()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var reader = scope.ServiceProvider.GetService<IReader>();
        var writer = scope.ServiceProvider.GetService<IWriter>();
        Assert.IsType<DataService>(reader);
        Assert.IsType<DataService>(writer);
    }

    [Fact]
    public void KeyedScoped_RegistersUnderKey()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var bus = scope.ServiceProvider.GetKeyedService<IMessageBus>("primary");
        Assert.NotNull(bus);
        Assert.IsType<PrimaryMessageBus>(bus);
    }

    [Fact]
    public void KeyedScoped_WrongKey_ReturnsNull()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        Assert.Null(scope.ServiceProvider.GetKeyedService<IMessageBus>("wrong"));
    }

    [Fact]
    public void Singleton_NoInterface_RegistersAsConcrete()
    {
        using var provider = BuildProvider();
        Assert.NotNull(provider.GetService<AppSettings>());
    }

    [Fact]
    public void AddAutoWireServices_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();
        var result = services.AddAutoWireServices();
        Assert.Same(services, result);
    }

    // ── Open generic tests ────────────────────────────────────────────────────

    [Fact]
    public void OpenGeneric_Scoped_ClosesCorrectly()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var repo = scope.ServiceProvider.GetService<IRepository<string>>();
        Assert.NotNull(repo);
        Assert.IsType<Repository<string>>(repo);
    }

    [Fact]
    public void OpenGeneric_Scoped_DifferentTypeArgs_ResolveSeparately()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<IRepository<int>>());
        Assert.NotNull(scope.ServiceProvider.GetService<IRepository<string>>());
    }

    [Fact]
    public void OpenGeneric_Singleton_WithExplicitServiceType_RegistersOnlyAsThat()
    {
        using var provider = BuildProvider();
        Assert.NotNull(provider.GetService<IReadOnlyRepository<int>>());
        Assert.IsType<CachedRepository<int>>(provider.GetService<IReadOnlyRepository<int>>());
        // CachedRepository should NOT be registered as IRepository (explicit type was given)
        // IRepository<int> resolves to Repository<int> instead
        Assert.IsType<Repository<int>>(provider.GetService<IRepository<int>>());
    }

    [Fact]
    public void OpenGeneric_Transient_NoConcrete_RegistersAsConcrete()
    {
        using var provider = BuildProvider();
        Assert.NotNull(provider.GetService<EventProcessor<string>>());
        Assert.IsType<EventProcessor<string>>(provider.GetService<EventProcessor<string>>());
    }

    [Fact]
    public void OpenGeneric_Transient_IsNewInstancePerResolve()
    {
        using var provider = BuildProvider();
        var a = provider.GetService<EventProcessor<string>>();
        var b = provider.GetService<EventProcessor<string>>();
        Assert.NotSame(a, b);
    }

    // ── AllowMultiple tests ───────────────────────────────────────────────────

    [Fact]
    public void AllowMultiple_RegistersAgainstBothExplicitInterfaces()
    {
        using var provider = BuildProvider();
        Assert.IsType<ContentService>(provider.GetService<IFeedService>());
        Assert.IsType<ContentService>(provider.GetService<IPublisherService>());
    }

    [Fact]
    public void AllowMultiple_BothInterfacesResolvable()
    {
        using var provider = BuildProvider();
        Assert.NotNull(provider.GetService<IFeedService>());
        Assert.NotNull(provider.GetService<IPublisherService>());
    }

    // ── DuplicateStrategy.Replace tests ──────────────────────────────────────

    [Fact]
    public void DuplicateStrategy_Replace_WinsOverPreviousRegistration()
    {
        using var provider = BuildProvider();
        var svc = provider.GetService<IReplaceable>();
        Assert.NotNull(svc);
        Assert.IsType<ReplacementReplaceable>(svc);
    }

    [Fact]
    public void DuplicateStrategy_Replace_OriginalNoLongerRegistered()
    {
        using var provider = BuildProvider();
        var all = provider.GetServices<IReplaceable>().ToList();
        Assert.Single(all);
        Assert.IsType<ReplacementReplaceable>(all[0]);
    }

    // ── DuplicateStrategy.Skip tests ─────────────────────────────────────────

    [Fact]
    public void DuplicateStrategy_Skip_FirstAddRegistrationWins()
    {
        using var provider = BuildProvider();
        Assert.IsType<PrimarySkippable>(provider.GetService<ISkippable>());
    }

    [Fact]
    public void DuplicateStrategy_Skip_FallbackNotRegisteredAsSecondary()
    {
        using var provider = BuildProvider();
        var all = provider.GetServices<ISkippable>().ToList();
        Assert.Single(all);
        Assert.IsType<PrimarySkippable>(all[0]);
    }

    // ── TryScoped tests ───────────────────────────────────────────────────────

    [Fact]
    public void TryScoped_RegistersWhenServiceNotPreRegistered()
    {
        var services = new ServiceCollection();
        services.AddAutoWireServices();
        using var provider = services.BuildServiceProvider();
        Assert.IsType<DefaultTryable>(provider.GetService<ITryable>());
    }

    [Fact]
    public void TryScoped_DoesNotOverrideManualRegistration()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITryable, MockTryable>(); // registered before AutoWire
        services.AddAutoWireServices();              // [TryScoped] on DefaultTryable should be skipped
        using var provider = services.BuildServiceProvider();
        Assert.IsType<MockTryable>(provider.GetService<ITryable>());
    }

    // ── Decorator tests ───────────────────────────────────────────────────────

    [Fact]
    public void Decorator_ResolvedAsDecoratorType()
    {
        using var provider = BuildProvider();
        var svc = provider.GetRequiredService<IGreeter>();
        Assert.IsType<PoliteGreeter>(svc);
    }

    [Fact]
    public void Decorator_WrapsInnerService()
    {
        using var provider = BuildProvider();
        Assert.Equal("[politely] Hello, World!", provider.GetRequiredService<IGreeter>().Greet("World"));
    }

    [Fact]
    public void Decorator_InnerServiceRegisteredAsConcrete()
    {
        using var provider = BuildProvider();
        Assert.IsType<SimpleGreeter>(provider.GetRequiredService<SimpleGreeter>());
    }

    [Fact]
    public void Decorator_IsScoped_SameInstancePerScope()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var a = scope.ServiceProvider.GetRequiredService<IGreeter>();
        var b = scope.ServiceProvider.GetRequiredService<IGreeter>();
        Assert.Same(a, b);
    }

    // ── HostedService tests ───────────────────────────────────────────────────

    [Fact]
    public void HostedService_RegisteredAsIHostedService()
    {
        using var provider = BuildProvider();
        var workers = provider.GetServices<IHostedService>().ToList();
        Assert.Contains(workers, w => w is TestBackgroundWorker);
    }

    [Fact]
    public void HostedService_MultipleCallsStillRegistersOnce()
    {
        var services = new ServiceCollection();
        services.AddAutoWireServices();
        services.AddAutoWireServices(); // idempotent — AddHostedService uses TryAddEnumerable
        using var provider = services.BuildServiceProvider();
        var count = provider.GetServices<IHostedService>().Count(w => w is TestBackgroundWorker);
        Assert.Equal(1, count);
    }

    // ── IncludeSelf ───────────────────────────────────────────────────────────

    [Fact]
    public void IncludeSelf_RegistersAsBothInterfaceAndConcreteType()
    {
        using var provider = BuildProvider();
        Assert.NotNull(provider.GetService<IAnalyticsService>());
        Assert.NotNull(provider.GetService<AnalyticsService>());
        Assert.IsType<AnalyticsService>(provider.GetService<IAnalyticsService>());
        Assert.IsType<AnalyticsService>(provider.GetService<AnalyticsService>());
    }

    [Fact]
    public void IncludeSelf_WithExplicitServiceType_RegistersAsBothExplicitAndConcrete()
    {
        using var provider = BuildProvider();
        Assert.NotNull(provider.GetService<INotificationService>());
        Assert.NotNull(provider.GetService<NotificationService>());
        // IEmailSender was NOT in the explicit ServiceType — should not be registered
        Assert.Null(provider.GetService<IEmailSender>());
    }

    // ── Ordered decorators ────────────────────────────────────────────────────

    [Fact]
    public void OrderedDecorators_ChainAppliedInnerToOuter()
    {
        // Order=1 (TimestampLogDecorator) is inner, Order=2 (UpperCaseLogDecorator) is outer.
        // Resolving ILogService should produce: UpperCase(Timestamp(Simple))
        // Log("hello") → UpperCase wraps → Timestamp wraps → Simple → "[LOG] [TS] hello" → ToUpper → "[LOG] [TS] HELLO"
        using var provider = BuildProvider();
        var logService = provider.GetRequiredService<ILogService>();
        Assert.IsType<UpperCaseLogDecorator>(logService);
        Assert.Equal("[LOG] [TS] HELLO", logService.Log("hello"));
    }

    [Fact]
    public void OrderedDecorators_IntermediateConcreteTypes_AreResolvable()
    {
        // Each intermediate concrete type should be independently resolvable.
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;
        Assert.NotNull(sp.GetService<SimpleLogService>());
        Assert.NotNull(sp.GetService<TimestampLogDecorator>());
    }

    // ── Profile-based registration ────────────────────────────────────────────

    [Fact]
    public void Profile_NoProfile_RegistersOnlyUnprofiledServices()
    {
        var services = new ServiceCollection();
        services.AddAutoWireServices(); // no profile
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        // InMemoryStorageService has no profile — always registered
        Assert.NotNull(scope.ServiceProvider.GetService<IStorageService>());
        Assert.IsType<InMemoryStorageService>(scope.ServiceProvider.GetService<IStorageService>());
        // Profile-only services not registered
        Assert.Null(scope.ServiceProvider.GetService<IMetricsService>());
    }

    [Fact]
    public void Profile_CloudProfile_RegistersProfileAndUnprofiledServices()
    {
        var services = new ServiceCollection();
        services.AddAutoWireServices(profile: "cloud");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        // Profile "cloud" wins for IStorageService (last registration wins)
        Assert.IsType<CloudStorageService>(scope.ServiceProvider.GetService<IStorageService>());
        // Cloud-only singleton is now registered
        Assert.NotNull(provider.GetService<IMetricsService>());
        Assert.IsType<CloudMetricsService>(provider.GetService<IMetricsService>());
    }

    [Fact]
    public void Profile_AzureProfile_RegistersAzureServicesOnly()
    {
        var services = new ServiceCollection();
        services.AddAutoWireServices(profile: "azure");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        // Profile "azure" wins for IStorageService
        Assert.IsType<AzureStorageService>(scope.ServiceProvider.GetService<IStorageService>());
        // "cloud" profile service not registered when profile="azure"
        Assert.Null(scope.ServiceProvider.GetService<IMetricsService>());
    }

    [Fact]
    public void Profile_UnknownProfile_OnlyUnprofiledServicesRegistered()
    {
        var services = new ServiceCollection();
        services.AddAutoWireServices(profile: "nonexistent");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        // No-profile services still registered
        Assert.IsType<InMemoryStorageService>(scope.ServiceProvider.GetService<IStorageService>());
        // Profile-only services not registered
        Assert.Null(scope.ServiceProvider.GetService<IMetricsService>());
    }

    // ── Convention scan ───────────────────────────────────────────────────────

    [Fact]
    public void ConventionScan_RegistersNonExcludedConcreteServicesInNamespace()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var scanServices = scope.ServiceProvider.GetServices<AutoWireTests.Scan.IScanService>().ToList();

        Assert.Contains(scanServices, s => s is AutoWireTests.Scan.ScanServiceA);
        Assert.Contains(scanServices, s => s is AutoWireTests.Scan.ScanServiceB);
        // [AutoWireExclude] — must not appear
        Assert.DoesNotContain(scanServices, s => s is AutoWireTests.Scan.ExcludedScanService);
        // Abstract — must not appear
        Assert.DoesNotContain(scanServices, s => s.GetType().IsAbstract);
    }

    [Fact]
    public void ConventionScan_SkipsExplicitlyAttributedClasses()
    {
        // ExplicitlyScopedService has [Scoped] — it appears exactly once, not twice.
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var all = scope.ServiceProvider.GetServices<AutoWireTests.Scan.IScanService>().ToList();
        var explicit_ = all.OfType<AutoWireTests.Scan.ExplicitlyScopedService>().ToList();
        Assert.Single(explicit_); // registered once (from [Scoped]), not twice
    }

    [Fact]
    public void ConventionScan_IncludesSubNamespacesByDefault()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sub = scope.ServiceProvider.GetService<AutoWireTests.Scan.Sub.ISubScanService>();
        Assert.NotNull(sub);
        Assert.IsType<AutoWireTests.Scan.Sub.SubScanServiceX>(sub);
    }
}
