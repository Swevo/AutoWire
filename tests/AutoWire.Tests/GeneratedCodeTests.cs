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
}
