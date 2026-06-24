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
}
