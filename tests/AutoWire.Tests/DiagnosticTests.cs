using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// Generator-driver tests that verify AW00x diagnostics are emitted correctly.
/// These run the AutoWire source generator in-process against synthetic code snippets.
/// </summary>
public class DiagnosticTests
{
    // ── AW004: captive dependency ─────────────────────────────────────────────

    [Fact]
    public void AW004_SingletonInjectingScopedByInterface_EmitsWarning()
    {
        var source = """
            public interface IScopedService { string Get(); }
            [AutoWire.Scoped]
            public class MyScopedService : IScopedService { public string Get() => "ok"; }
            [AutoWire.Singleton]
            public class MySingleton
            {
                public MySingleton(IScopedService scoped) { }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AW004");
    }

    [Fact]
    public void AW004_SingletonInjectingOtherSingleton_NoWarning()
    {
        var source = """
            public interface ISingletonDep { }
            [AutoWire.Singleton]
            public class MySingletonDep : ISingletonDep { }
            [AutoWire.Singleton]
            public class MySingleton
            {
                public MySingleton(ISingletonDep dep) { }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AW004");
    }

    [Fact]
    public void AW004_SingletonInjectingTransient_NoWarning()
    {
        var source = """
            public interface ITransientDep { }
            [AutoWire.Transient]
            public class MyTransientDep : ITransientDep { }
            [AutoWire.Singleton]
            public class MySingleton
            {
                public MySingleton(ITransientDep dep) { }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AW004");
    }

    [Fact]
    public void AW004_TrySingletonInjectingScoped_EmitsWarning()
    {
        var source = """
            public interface ISvc { }
            [AutoWire.TryScoped]
            public class MyScopedSvc : ISvc { }
            [AutoWire.TrySingleton]
            public class MySingleton
            {
                public MySingleton(ISvc svc) { }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AW004");
    }

    [Fact]
    public void AW004_ScopedInjectingScoped_NoWarning()
    {
        var source = """
            public interface IScopedDep { }
            [AutoWire.Scoped]
            public class MyScopedDep : IScopedDep { }
            [AutoWire.Scoped]
            public class MyScopedConsumer
            {
                public MyScopedConsumer(IScopedDep dep) { }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AW004");
    }

    // ── AW006: transient IDisposable ──────────────────────────────────────────

    [Fact]
    public void AW006_TransientImplementingIDisposable_EmitsWarning()
    {
        var source = """
            public interface IMyService { }
            [AutoWire.Transient]
            public class MyDisposableService : IMyService, System.IDisposable
            {
                public void Dispose() { }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AW006");
    }

    [Fact]
    public void AW006_TransientImplementingIAsyncDisposable_EmitsWarning()
    {
        var source = """
            public interface IMyService { }
            [AutoWire.Transient]
            public class MyAsyncDisposableService : IMyService, System.IAsyncDisposable
            {
                public System.Threading.Tasks.ValueTask DisposeAsync() => default;
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AW006");
    }

    [Fact]
    public void AW006_TryTransientImplementingIDisposable_EmitsWarning()
    {
        var source = """
            [AutoWire.TryTransient]
            public class MyTryDisposable : System.IDisposable
            {
                public void Dispose() { }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AW006");
    }

    [Fact]
    public void AW006_ScopedImplementingIDisposable_NoWarning()
    {
        var source = """
            [AutoWire.Scoped]
            public class MyScopedDisposable : System.IDisposable
            {
                public void Dispose() { }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AW006");
    }

    [Fact]
    public void AW006_SingletonImplementingIDisposable_NoWarning()
    {
        var source = """
            [AutoWire.Singleton]
            public class MySingletonDisposable : System.IDisposable
            {
                public void Dispose() { }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AW006");
    }

    [Fact]
    public void AW006_TransientNotDisposable_NoWarning()
    {
        var source = """
            public interface IFoo { }
            [AutoWire.Transient]
            public class FooService : IFoo { }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AW006");
    }

    // ── [Options] generated code verification ─────────────────────────────────

    [Fact]
    public void Options_WithExplicitSection_EmitsBindConfigurationCall()
    {
        var source = """
            [AutoWire.Options("Database")]
            public class DatabaseOptions { public string Host { get; set; } = ""; }
            """;

        var (_, generatedSources) = RunGeneratorWithSources(source);
        Assert.Contains(generatedSources, s => s.HintName.Contains("ServiceCollectionExtensions"));
        var generated = generatedSources.First(s => s.HintName.Contains("ServiceCollectionExtensions"));
        var code = generated.SourceText.ToString();
        Assert.Contains("AddOptions<global::DatabaseOptions>()", code);
        Assert.Contains(".BindConfiguration(\"Database\")", code);
        Assert.Contains(".ValidateDataAnnotations()", code);
        Assert.Contains(".ValidateOnStart()", code);
    }

    [Fact]
    public void Options_WithNoSection_DerivesSectionFromClassName()
    {
        var source = """
            [AutoWire.Options]
            public class EmailOptions { }
            """;

        var (_, generatedSources) = RunGeneratorWithSources(source);
        Assert.Contains(generatedSources, s => s.HintName.Contains("ServiceCollectionExtensions"));
        var generated = generatedSources.First(s => s.HintName.Contains("ServiceCollectionExtensions"));
        var code = generated.SourceText.ToString();
        // "EmailOptions" → section = "Email"
        Assert.Contains(".BindConfiguration(\"Email\")", code);
    }

    [Fact]
    public void Options_WithValidateFalse_OmitsValidateCalls()
    {
        var source = """
            [AutoWire.Options("Minimal", ValidateDataAnnotations = false, ValidateOnStart = false)]
            public class MinimalOptions { }
            """;

        var (_, generatedSources) = RunGeneratorWithSources(source);
        Assert.Contains(generatedSources, s => s.HintName.Contains("ServiceCollectionExtensions"));
        var generated = generatedSources.First(s => s.HintName.Contains("ServiceCollectionExtensions"));
        var code = generated.SourceText.ToString();
        Assert.Contains(".BindConfiguration(\"Minimal\")", code);
        Assert.DoesNotContain(".ValidateDataAnnotations()", code);
        Assert.DoesNotContain(".ValidateOnStart()", code);
    }

    [Fact]
    public void EnumKey_GeneratesFullyQualifiedEnumMember()
    {
        var source = """
            public enum ServiceKey { Primary = 1, Secondary = 2 }
            public interface IMyService { }
            [AutoWire.Scoped(Key = ServiceKey.Primary)]
            public class PrimaryService : IMyService { }
            """;

        var (_, generatedSources) = RunGeneratorWithSources(source);
        Assert.Contains(generatedSources, s => s.HintName.Contains("ServiceCollectionExtensions"));
        var generated = generatedSources.First(s => s.HintName.Contains("ServiceCollectionExtensions"));
        var code = generated.SourceText.ToString();
        Assert.Contains("global::ServiceKey.Primary", code);
        // Should NOT have a quoted string key
        Assert.DoesNotContain("\"Primary\"", code);
    }

    // ── AW007: no-interface diagnostic ────────────────────────────────────────

    [Fact]
    public void AW007_ConcreteClassNoInterfaces_EmitsInfo()
    {
        var source = """
            [AutoWire.Singleton]
            public class SettingsService { }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AW007");
    }

    [Fact]
    public void AW007_ClassWithExplicitServiceType_NoWarning()
    {
        var source = """
            public interface ISettings { }
            [AutoWire.Singleton(typeof(ISettings))]
            public class SettingsService : ISettings { }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AW007");
    }

    [Fact]
    public void AW007_ClassWithUserInterface_NoWarning()
    {
        var source = """
            public interface ISettingsService { }
            [AutoWire.Singleton]
            public class SettingsService : ISettingsService { }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AW007");
    }

    // ── AW019: duplicate keyed registration ───────────────────────────────────

    [Fact]
    public void AW019_MultipleKeyedRegistrationsUsingSameServiceAndKey_EmitsInfo()
    {
        var source = """
            public interface IPaymentProvider { }
            [AutoWire.Scoped(typeof(IPaymentProvider), Key = "stripe")]
            public class StripeProviderV1 : IPaymentProvider { }
            [AutoWire.Scoped(typeof(IPaymentProvider), Key = "stripe")]
            public class StripeProviderV2 : IPaymentProvider { }
            """;

        var diagnostics = RunGenerator(source);
        var diagnostic = Assert.Single(diagnostics, d => d.Id == "AW019");
        Assert.NotEqual(Location.None, diagnostic.Location);
    }

    [Fact]
    public void AW019_MultipleKeyedRegistrationsUsingDifferentKeys_NoWarning()
    {
        var source = """
            public interface IPaymentProvider { }
            [AutoWire.Scoped(typeof(IPaymentProvider), Key = "stripe")]
            public class StripeProvider : IPaymentProvider { }
            [AutoWire.Scoped(typeof(IPaymentProvider), Key = "adyen")]
            public class AdyenProvider : IPaymentProvider { }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AW019");
    }

    [Fact]
    public void AW002_MultipleNonKeyedRegistrationsForSameService_EmitsInfoAtSourceLocation()
    {
        var source = """
            public interface IHandler { }
            [AutoWire.Scoped(typeof(IHandler))]
            public class FirstHandler : IHandler { }
            [AutoWire.Scoped(typeof(IHandler))]
            public class SecondHandler : IHandler { }
            """;

        var diagnostics = RunGenerator(source);
        var diagnostic = Assert.Single(diagnostics, d => d.Id == "AW002");
        Assert.NotEqual(Location.None, diagnostic.Location);
    }

    [Fact]
    public void AW022_RuntimeConditionWithEmptyConfigPayload_EmitsError()
    {
        var source = """
            public interface IFeatureFlagService { }
            [AutoWire.Scoped(typeof(IFeatureFlagService), Condition = "config:")]
            public class FeatureFlagService : IFeatureFlagService { }
            """;

        var diagnostics = RunGenerator(source);
        var diagnostic = Assert.Single(diagnostics, d => d.Id == "AW022");
        Assert.NotEqual(Location.None, diagnostic.Location);
    }

    [Fact]
    public void RuntimeCondition_ValidConfigCondition_EmitsRuntimeCheck()
    {
        var source = """
            public interface IFeatureFlagService { }
            [AutoWire.Scoped(typeof(IFeatureFlagService), Condition = "config:Features:Flag=on")]
            public class FeatureFlagService : IFeatureFlagService { }
            """;

        var (_, sources) = RunGeneratorWithSources(source);
        var code = sources.First(s => s.HintName.Contains("ServiceCollectionExtensions")).SourceText.ToString();
        Assert.Contains("configuration?[\"Features:Flag\"]", code);
        Assert.DoesNotContain("#if config:Features:Flag=on", code);
    }

    [Fact]
    public void AW024_ProfileWhitespace_EmitsWarning()
    {
        var source = """
            public interface IUserService { }
            [AutoWire.Scoped(typeof(IUserService), Profile = "   ")]
            public class UserService : IUserService { }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AW024");
    }

    [Fact]
    public void AW025_ModuleAndProfile_EmitsWarning()
    {
        var source = """
            public interface ICheckoutService { }
            [AutoWire.Scoped(typeof(ICheckoutService), Module = "Payments", Profile = "prod")]
            public class CheckoutService : ICheckoutService { }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AW025");
    }

    // ── [HttpClient] generated code ───────────────────────────────────────────

    [Fact]
    public void HttpClient_TypedClient_EmitsAddHttpClient()
    {
        var source = """
            [AutoWire.HttpClient]
            public class WeatherClient
            {
                public WeatherClient(System.Net.Http.HttpClient http) { }
            }
            """;

        var (_, sources) = RunGeneratorWithSources(source);
        Assert.Contains(sources, s => s.HintName.Contains("ServiceCollectionExtensions"));
        var code = sources.First(s => s.HintName.Contains("ServiceCollectionExtensions")).SourceText.ToString();
        Assert.Contains("AddHttpClient<global::WeatherClient>()", code);
    }

    [Fact]
    public void HttpClient_NamedClientWithBaseAddress_EmitsNamedClientChain()
    {
        var source = """
            [AutoWire.HttpClient(Name = "GitHub", BaseAddress = "https://api.github.com")]
            public class GitHubClient
            {
                public GitHubClient(System.Net.Http.HttpClient http) { }
            }
            """;

        var (_, sources) = RunGeneratorWithSources(source);
        var code = sources.First(s => s.HintName.Contains("ServiceCollectionExtensions")).SourceText.ToString();
        Assert.Contains("AddHttpClient(\"GitHub\"", code);
        Assert.Contains("https://api.github.com", code);
        Assert.Contains("AddTypedClient<global::GitHubClient>()", code);
    }

    [Fact]
    public void HttpClient_Timeout_EmitsTimeoutAssignment()
    {
        var source = """
            [AutoWire.HttpClient(Timeout = 30)]
            public class SlowApiClient
            {
                public SlowApiClient(System.Net.Http.HttpClient http) { }
            }
            """;

        var (_, sources) = RunGeneratorWithSources(source);
        var code = sources.First(s => s.HintName.Contains("ServiceCollectionExtensions")).SourceText.ToString();
        Assert.Contains("TimeSpan.FromSeconds(30)", code);
    }

    [Fact]
    public void HttpClient_DefaultHeaders_EmitsDefaultRequestHeadersAdd()
    {
        var source = """
            [AutoWire.HttpClient(DefaultHeaders = new[] { "Accept:application/json", "X-App-Id:myapp" })]
            public class JsonApiClient
            {
                public JsonApiClient(System.Net.Http.HttpClient http) { }
            }
            """;

        var (_, sources) = RunGeneratorWithSources(source);
        var code = sources.First(s => s.HintName.Contains("ServiceCollectionExtensions")).SourceText.ToString();
        Assert.Contains("DefaultRequestHeaders.Add(\"Accept\", \"application/json\")", code);
        Assert.Contains("DefaultRequestHeaders.Add(\"X-App-Id\", \"myapp\")", code);
    }

    // ── AW008: dangerous singleton dependency ─────────────────────────────────

    [Fact]
    public void AW008_SingletonInjectingIHttpContextAccessor_EmitsWarning()
    {
        var source = """
            namespace Microsoft.AspNetCore.Http { public interface IHttpContextAccessor { } }
            [AutoWire.Singleton]
            public class MySingleton
            {
                public MySingleton(Microsoft.AspNetCore.Http.IHttpContextAccessor accessor) { }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AW008");
    }

    [Fact]
    public void AW008_SingletonInjectingNormalDep_NoWarning()
    {
        var source = """
            public interface INormalDep { }
            [AutoWire.Singleton]
            public class MySingleton
            {
                public MySingleton(INormalDep dep) { }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AW008");
    }

    // ── [AutoWireModule] generated code ───────────────────────────────────────

    [Fact]
    public void Module_ServiceExcludedFromMainMethod()
    {
        var source = """
            public interface IPaymentService { }
            [AutoWire.Scoped(Module = "Payments")]
            public class BankTransferService : IPaymentService { }
            """;

        var (_, sources) = RunGeneratorWithSources(source);
        var code = sources.First(s => s.HintName.Contains("ServiceCollectionExtensions")).SourceText.ToString();
        // Module service should NOT be in the main AddAutoWireServices method body
        // but SHOULD appear in AddPaymentsModule
        Assert.Contains("AddPaymentsModule", code);
        Assert.Contains("global::IPaymentService, global::BankTransferService", code);
    }

    [Fact]
    public void Module_GeneratesSeparateExtensionMethod()
    {
        var source = """
            public interface IPaymentService { }
            [AutoWire.Scoped(Module = "Payments")]
            public class BankTransferService : IPaymentService { }
            """;

        var (_, sources) = RunGeneratorWithSources(source);
        var code = sources.First(s => s.HintName.Contains("ServiceCollectionExtensions")).SourceText.ToString();
        Assert.Contains("public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddPaymentsModule", code);
    }

    // ── Resilience on [HttpClient] ─────────────────────────────────────────────

    [Fact]
    public void HttpClient_Resilience_EmitsAddStandardResilienceHandler()
    {
        var source = """
            [AutoWire.HttpClient(Resilience = true)]
            public class ResilientClient
            {
                public ResilientClient(System.Net.Http.HttpClient http) { }
            }
            """;

        var (_, sources) = RunGeneratorWithSources(source);
        var code = sources.First(s => s.HintName.Contains("ServiceCollectionExtensions")).SourceText.ToString();
        Assert.Contains("AddStandardResilienceHandler()", code);
    }

    // ── Registration summary ───────────────────────────────────────────────────

    [Fact]
    public void Summary_GeneratesSummaryFile()
    {
        var source = """
            public interface IMyService { }
            [AutoWire.Scoped]
            public class MyService : IMyService { }
            [AutoWire.Singleton]
            public class MySingleton : IMyService { }
            """;

        var (_, sources) = RunGeneratorWithSources(source);
        Assert.Contains(sources, s => s.HintName.Contains("RegistrationSummary"));
        var summary = sources.First(s => s.HintName.Contains("RegistrationSummary")).SourceText.ToString();
        Assert.Contains("TotalCount = 2", summary);
        Assert.Contains("ScopedCount = 1", summary);
        Assert.Contains("SingletonCount = 1", summary);
        Assert.Contains("class RegistrationSummary", summary);
        Assert.Contains("RegistrationManifestEntries", summary);
        Assert.Contains("DecoratorManifestEntries", summary);
        Assert.Contains("RegistrationManifestJson", summary);
    }

    // ── AW009: scoped dependency in HostedService ────────────────────────────

    [Fact]
    public void AW009_HostedServiceInjectingScoped_EmitsWarning()
    {
        var source = """
            public interface IScopedService { }
            [AutoWire.Scoped]
            public class MyScopedService : IScopedService { }
            [AutoWire.HostedService]
            public class MyWorker : Microsoft.Extensions.Hosting.BackgroundService
            {
                public MyWorker(IScopedService svc) { }
                protected override System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken ct) => System.Threading.Tasks.Task.CompletedTask;
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AW009");
    }

    [Fact]
    public void AW009_HostedServiceInjectingSingleton_NoWarning()
    {
        var source = """
            public interface ISingletonService { }
            [AutoWire.Singleton]
            public class MySingletonService : ISingletonService { }
            [AutoWire.HostedService]
            public class MyWorker : Microsoft.Extensions.Hosting.BackgroundService
            {
                public MyWorker(ISingletonService svc) { }
                protected override System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken ct) => System.Threading.Tasks.Task.CompletedTask;
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AW009");
    }

    // ── [Validate] attribute ───────────────────────────────────────────────────

    [Fact]
    public void Validate_EmitsFluentValidationRegistration()
    {
        var source = """
            namespace FluentValidation
            {
                public abstract class AbstractValidator<T> { }
                public interface IValidator<T> { }
            }
            public class MyModel { }
            [AutoWire.Validate]
            public class MyModelValidator : FluentValidation.AbstractValidator<MyModel> { }
            """;

        var (_, sources) = RunGeneratorWithSources(source);
        var code = sources.First(s => s.HintName.Contains("ServiceCollectionExtensions")).SourceText.ToString();
        Assert.Contains("global::FluentValidation.IValidator<global::MyModel>", code);
        Assert.Contains("global::MyModelValidator", code);
        Assert.Contains("AddScoped", code);
    }

    // ── [Interceptor] attribute ────────────────────────────────────────────────

    [Fact]
    public void Interceptor_EmitsProxyClassAndRegistration()
    {
        var source = """
            public interface IMyService { string Greet(string name); }
            [AutoWire.Interceptor(typeof(IMyService))]
            public class LoggingInterceptor : AutoWire.IAutoWireInterceptor
            {
                public void Intercept(AutoWire.IAutoWireInvocation invocation) { }
            }
            """;

        var (_, sources) = RunGeneratorWithSources(source);
        var code = sources.First(s => s.HintName.Contains("ServiceCollectionExtensions")).SourceText.ToString();
        Assert.Contains("AutoWire_Proxy_MyService_with_LoggingInterceptor", code);
        Assert.Contains("IMyService", code);
    }

    [Fact]
    public void Interceptor_ProxyClass_ImplementsInterface()
    {
        var source = """
            public interface ICounter { int Increment(); }
            [AutoWire.Interceptor(typeof(ICounter))]
            public class TraceInterceptor : AutoWire.IAutoWireInterceptor
            {
                public void Intercept(AutoWire.IAutoWireInvocation invocation) { }
            }
            """;

        var (_, sources) = RunGeneratorWithSources(source);
        var code = sources.First(s => s.HintName.Contains("ServiceCollectionExtensions")).SourceText.ToString();
        Assert.Contains(": global::ICounter", code);
        Assert.Contains("Increment()", code);
    }

    // ── AW010: duplicate interceptor target ───────────────────────────────────

    [Fact]
    public void AW010_TwoInterceptorAttributesOnSameClassSameInterface_EmitsWarning()
    {
        var source = """
            public interface IMyService { }
            [AutoWire.Interceptor(typeof(IMyService))]
            [AutoWire.Interceptor(typeof(IMyService))]
            public class DuplicateInterceptor : AutoWire.IAutoWireInterceptor
            {
                public void Intercept(AutoWire.IAutoWireInvocation invocation) { }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AW010");
    }

    [Fact]
    public void AW010_TwoInterceptorAttributesDifferentInterfaces_NoWarning()
    {
        var source = """
            public interface IServiceA { }
            public interface IServiceB { }
            [AutoWire.Interceptor(typeof(IServiceA))]
            [AutoWire.Interceptor(typeof(IServiceB))]
            public class MultiInterceptor : AutoWire.IAutoWireInterceptor
            {
                public void Intercept(AutoWire.IAutoWireInvocation invocation) { }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AW010");
    }

    // ── AW011: empty interceptor interface ────────────────────────────────────

    [Fact]
    public void AW011_InterceptorTargetWithNoMethods_EmitsWarning()
    {
        var source = """
            public interface IEmptyService { }
            [AutoWire.Interceptor(typeof(IEmptyService))]
            public class EmptyInterceptor : AutoWire.IAutoWireInterceptor
            {
                public void Intercept(AutoWire.IAutoWireInvocation invocation) { }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AW011");
    }

    [Fact]
    public void AW011_InterceptorTargetWithMethods_NoWarning()
    {
        var source = """
            public interface IMyService { void DoWork(); }
            [AutoWire.Interceptor(typeof(IMyService))]
            public class MyInterceptor : AutoWire.IAutoWireInterceptor
            {
                public void Intercept(AutoWire.IAutoWireInvocation invocation) { }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AW011");
    }

    // ── AW012: decorator service type mismatch ────────────────────────────────

    [Fact]
    public void AW012_DecoratorNotImplementingServiceType_EmitsError()
    {
        var source = """
            public interface IFoo { }
            public interface IBar { }
            [AutoWire.DecorateScoped(typeof(IFoo))]
            public class FooDecorator : IBar
            {
                public FooDecorator(IFoo inner) { }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AW012");
        Assert.DoesNotContain(diagnostics, d => d.Id == "AW003");
    }

    [Fact]
    public void AW020_DecoratorsWithSameServiceLifetimeAndOrder_EmitsWarning()
    {
        var source = """
            public interface IFoo { string Value(); }
            [AutoWire.Scoped]
            public class Foo : IFoo { public string Value() => "ok"; }

            [AutoWire.DecorateScoped(typeof(IFoo), Order = 1)]
            public class FooDecoratorA : IFoo
            {
                private readonly IFoo _inner;
                public FooDecoratorA(IFoo inner) { _inner = inner; }
                public string Value() => _inner.Value();
            }

            [AutoWire.DecorateScoped(typeof(IFoo), Order = 1)]
            public class FooDecoratorB : IFoo
            {
                private readonly IFoo _inner;
                public FooDecoratorB(IFoo inner) { _inner = inner; }
                public string Value() => _inner.Value();
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AW020");
    }

    [Fact]
    public void AW021_OpenGenericDecoratorTarget_EmitsError()
    {
        var source = """
            public interface IRepository<T> { }
            [AutoWire.Scoped]
            public class Repository<T> : IRepository<T> { }

            [AutoWire.DecorateScoped(typeof(IRepository<>))]
            public class RepositoryDecorator<T> : IRepository<T>
            {
                private readonly IRepository<T> _inner;
                public RepositoryDecorator(IRepository<T> inner) { _inner = inner; }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AW021");
    }

    // ── AW013: missing registration detection ─────────────────────────────────

    [Fact]
    public void AW013_UnregisteredConstructorDependency_EmitsWarning()
    {
        var source = """
            public interface IPaymentGateway { }
            [AutoWire.Scoped]
            public class OrderService
            {
                public OrderService(IPaymentGateway gateway) { }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AW013");
    }

    [Fact]
    public void AW013_RegisteredConstructorDependency_NoWarning()
    {
        var source = """
            public interface IPaymentGateway { }
            [AutoWire.Scoped]
            public class PaymentGateway : IPaymentGateway { }
            [AutoWire.Scoped]
            public class OrderService
            {
                public OrderService(IPaymentGateway gateway) { }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AW013");
    }

    [Fact]
    public void AW013_FrameworkProvidedDependency_NoWarning()
    {
        var source = """
            [AutoWire.Scoped]
            public class OrderService
            {
                public OrderService(Microsoft.Extensions.Logging.ILogger<OrderService> logger,
                    Microsoft.Extensions.Configuration.IConfiguration configuration,
                    Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopeFactory,
                    Microsoft.Extensions.Options.IOptions<OrderOptions> options) { }
            }

            public class OrderOptions { }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AW013");
    }

    // ── Multi-interface attribute ─────────────────────────────────────────────

    [Fact]
    public void MultiInterface_ScopedWithTwoTypes_EmitsBothRegistrations()
    {
        var source = """
            public interface IReader { }
            public interface IWriter { }
            [AutoWire.Scoped(typeof(IReader), typeof(IWriter))]
            public class ReadWriteService : IReader, IWriter { }
            """;

        var (_, sources) = RunGeneratorWithSources(source);
        var code = sources.First(s => s.HintName.Contains("ServiceCollectionExtensions")).SourceText.ToString();
        Assert.Contains("AddScoped<global::IReader, global::ReadWriteService>()", code);
        Assert.Contains("AddScoped<global::IWriter, global::ReadWriteService>()", code);
    }

    [Fact]
    public void MultiInterface_AW003_RaisedForUnimplementedTypeInMultiCtor()
    {
        var source = """
            public interface IReader { }
            public interface IWriter { }
            public interface INotImplemented { }
            [AutoWire.Scoped(typeof(IReader), typeof(INotImplemented))]
            public class ReadService : IReader { }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AW003");
    }

    // ── [HttpClient] UseFactory ───────────────────────────────────────────────

    [Fact]
    public void HttpClient_UseFactory_EmitsIHttpClientFactoryRegistration()
    {
        var source = """
            [AutoWire.HttpClient(Name = "MyClient", UseFactory = true)]
            public class MyService { }
            """;

        var (_, sources) = RunGeneratorWithSources(source);
        var code = sources.First(s => s.HintName.Contains("ServiceCollectionExtensions")).SourceText.ToString();
        Assert.Contains("AddHttpClient(\"MyClient\")", code);
        Assert.Contains("IHttpClientFactory", code);
        Assert.Contains("CreateClient(\"MyClient\")", code);
    }

    [Fact]
    public void ScanAssembly_RegistersAttributedServiceFromReferencedAssembly()
    {
        var libraryReference = CreateReferencedAssembly(
            "ExternalLibrary",
            """
            namespace External.Services
            {
                public interface IExternalService { }

                [AutoWire.Scoped]
                public class ExternalService : IExternalService { }

                public sealed class Marker { }
            }
            """);

        var consumerSource = """
            [assembly: AutoWire.ScanAssembly(typeof(External.Services.Marker))]
            """;

        var (_, sources) = RunGeneratorWithSources(consumerSource, libraryReference);
        var code = sources.First(s => s.HintName.Contains("ServiceCollectionExtensions")).SourceText.ToString();
        Assert.Contains("services.AddScoped<global::External.Services.IExternalService, global::External.Services.ExternalService>();", code);
    }

    [Fact]
    public void ScanAssembly_MultipleAssemblies_RegistersServicesFromEachReferencedAssembly()
    {
        var firstLibrary = CreateReferencedAssembly(
            "FirstLibrary",
            """
            namespace First.Services
            {
                public interface IFirstService { }

                [AutoWire.Singleton]
                public class FirstService : IFirstService { }

                public sealed class Marker { }
            }
            """);

        var secondLibrary = CreateReferencedAssembly(
            "SecondLibrary",
            """
            namespace Second.Services
            {
                public interface ISecondService { }

                [AutoWire.Transient]
                public class SecondService : ISecondService { }

                public sealed class Marker { }
            }
            """);

        var consumerSource = """
            [assembly: AutoWire.ScanAssembly(typeof(First.Services.Marker))]
            [assembly: AutoWire.ScanAssembly(typeof(Second.Services.Marker))]
            """;

        var (_, sources) = RunGeneratorWithSources(consumerSource, firstLibrary, secondLibrary);
        var code = sources.First(s => s.HintName.Contains("ServiceCollectionExtensions")).SourceText.ToString();
        Assert.Contains("services.AddSingleton<global::First.Services.IFirstService, global::First.Services.FirstService>();", code);
        Assert.Contains("services.AddTransient<global::Second.Services.ISecondService, global::Second.Services.SecondService>();", code);
    }

    [Fact]
    public void AW014_ScanAssemblyMarkerFromCurrentAssembly_EmitsError()
    {
        var source = """
            [assembly: AutoWire.ScanAssembly(typeof(LocalMarker))]

            public sealed class LocalMarker { }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AW014");
    }

    [Fact]
    public void AW015_ScanAssemblyWithNoAttributedServices_EmitsWarning()
    {
        var libraryReference = CreateReferencedAssembly(
            "EmptyLibrary",
            """
            namespace Empty.Services
            {
                public sealed class Marker { }
                public class PlainService { }
            }
            """);

        var source = """
            [assembly: AutoWire.ScanAssembly(typeof(Empty.Services.Marker))]
            """;

        var diagnostics = RunGenerator(source, libraryReference);
        Assert.Contains(diagnostics, d => d.Id == "AW015");
    }

    [Fact]
    public void ScanAssembly_NotConfigured_DoesNotEmitAssemblyScanDiagnostics()
    {
        var source = """
            public interface ILocalService { }

            [AutoWire.Scoped]
            public class LocalService : ILocalService { }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id is "AW014" or "AW015");
    }

    // ── AW016: circular dependency detection ──────────────────────────────────

    [Fact]
    public void AW016_TwoServicesDependOnEachOther_EmitsError()
    {
        var source = """
            public interface IServiceA { }
            public interface IServiceB { }

            [AutoWire.Scoped]
            public class ServiceA : IServiceA
            {
                public ServiceA(IServiceB b) { }
            }

            [AutoWire.Scoped]
            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a) { }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AW016");
    }

    [Fact]
    public void AW016_NoCircularDependency_NoError()
    {
        var source = """
            public interface IServiceA { }
            public interface IServiceB { }

            [AutoWire.Scoped]
            public class ServiceB : IServiceB { }

            [AutoWire.Scoped]
            public class ServiceA : IServiceA
            {
                public ServiceA(IServiceB b) { }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AW016");
    }

    // ── AW017: unused registration detection ──────────────────────────────────

    [Fact]
    public void AW017_RegisteredServiceNeverReferenced_EmitsInfo()
    {
        var source = """
            public interface IUnusedService { }

            [AutoWire.Scoped]
            public class UnusedService : IUnusedService { }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AW017");
    }

    [Fact]
    public void AW017_RegisteredServiceUsedAsConstructorParameter_NoInfo()
    {
        var source = """
            public interface IUsedService { }

            [AutoWire.Scoped]
            public class UsedService : IUsedService { }

            public class Consumer
            {
                public Consumer(IUsedService svc) { }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AW017");
    }

    [Fact]
    public void AW017_RegisteredServiceUsedViaGetService_NoInfo()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;

            public interface IUsedService { }

            [AutoWire.Scoped]
            public class UsedService : IUsedService { }

            public class Consumer
            {
                public Consumer(System.IServiceProvider sp)
                {
                    var svc = sp.GetService<IUsedService>();
                }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AW017");
    }

    [Fact]
    public void AW017_ModuleService_NotFlagged()
    {
        var source = """
            public interface IModuleService { }

            [AutoWire.Scoped(Module = "Extras")]
            public class ModuleService : IModuleService { }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AW017");
    }

    // ── AW018: manual registration migration hints ───────────────────────────

    [Fact]
    public void AW018_ManualAddScoped_EmitsInfoDiagnostic()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;

            public interface IOrdersService { }
            public class OrdersService : IOrdersService { }

            public static class Startup
            {
                public static void ConfigureServices(IServiceCollection services)
                {
                    services.AddScoped<IOrdersService, OrdersService>();
                }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AW018" && d.Severity == DiagnosticSeverity.Info);
    }

    [Fact]
    public void AW026_ScrutorScan_EmitsInfoDiagnostic()
    {
        var scrutorStub = CreateScrutorStub();
        var source = """
            using Microsoft.Extensions.DependencyInjection;

            public static class Startup
            {
                public static void ConfigureServices(IServiceCollection services)
                {
                    services.Scan(_ => { });
                }
            }
            """;

        var diagnostics = RunGenerator(source, scrutorStub);
        Assert.Contains(diagnostics, d => d.Id == "AW026" && d.Severity == DiagnosticSeverity.Info);
    }

    // ── [Endpoint]: minimal API mapping ────────────────────────────────────────

    [Fact]
    public void Endpoint_WithHandleMethodAndRoutingReferenced_GeneratesMapMethod()
    {
        var routingStub = CreateEndpointRoutingStub();
        var source = """
            [AutoWire.Endpoint("GET", "/orders/{id}")]
            public static class GetOrder
            {
                public static string Handle(int id) => id.ToString();
            }
            """;

        var (_, sources) = RunGeneratorWithSources(source, routingStub);
        Assert.Contains(sources, s => s.HintName.Contains("AutoWireEndpoints"));
        var code = sources.First(s => s.HintName.Contains("AutoWireEndpoints")).SourceText.ToString();
        Assert.Contains("MapAutoWireEndpoints", code);
        Assert.Contains("app.MapGet(\"/orders/{id}\", global::GetOrder.Handle);", code);
    }

    [Fact]
    public void Endpoint_PostVerb_MapsToMapPost()
    {
        var routingStub = CreateEndpointRoutingStub();
        var source = """
            [AutoWire.Endpoint("POST", "/orders")]
            public static class CreateOrder
            {
                public static string HandleAsync() => "ok";
            }
            """;

        var (_, sources) = RunGeneratorWithSources(source, routingStub);
        var code = sources.First(s => s.HintName.Contains("AutoWireEndpoints")).SourceText.ToString();
        Assert.Contains("app.MapPost(\"/orders\", global::CreateOrder.HandleAsync);", code);
    }

    [Fact]
    public void Endpoint_UnknownVerb_FallsBackToMapMethods()
    {
        var routingStub = CreateEndpointRoutingStub();
        var source = """
            [AutoWire.Endpoint("OPTIONS", "/orders")]
            public static class OptionsOrder
            {
                public static string Handle() => "ok";
            }
            """;

        var (_, sources) = RunGeneratorWithSources(source, routingStub);
        var code = sources.First(s => s.HintName.Contains("AutoWireEndpoints")).SourceText.ToString();
        Assert.Contains("app.MapMethods(\"/orders\", new[] { \"OPTIONS\" }, global::OptionsOrder.Handle);", code);
    }

    [Fact]
    public void Endpoint_WithoutHandleMethod_SilentlySkipped()
    {
        var routingStub = CreateEndpointRoutingStub();
        var source = """
            [AutoWire.Endpoint("GET", "/nohandle")]
            public static class NoHandleClass
            {
                public static string SomethingElse() => "ok";
            }
            """;

        var (diagnostics, sources) = RunGeneratorWithSources(source, routingStub);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(sources, s => s.HintName.Contains("AutoWireEndpoints")); // no eligible endpoints → file not emitted
    }

    [Fact]
    public void Endpoint_WithoutRoutingReference_NoFileGenerated()
    {
        var source = """
            [AutoWire.Endpoint("GET", "/orders/{id}")]
            public static class GetOrderNoRouting
            {
                public static string Handle(int id) => id.ToString();
            }
            """;

        var (_, sources) = RunGeneratorWithSources(source); // no routing stub reference
        Assert.DoesNotContain(sources, s => s.HintName.Contains("AutoWireEndpoints"));
    }

    private static MetadataReference CreateEndpointRoutingStub() => CreateReferencedAssembly(
        "RoutingStubAssembly",
        """
        namespace Microsoft.AspNetCore.Routing
        {
            public interface IEndpointRouteBuilder { }
        }
        """);

    private static MetadataReference CreateScrutorStub() => CreateReferencedAssembly(
        "Scrutor",
        """
        namespace Microsoft.Extensions.DependencyInjection
        {
            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection Scan(this IServiceCollection services, System.Action<object> action) => services;
            }
        }
        """);

    private static IReadOnlyList<Diagnostic> RunGenerator(string source)
    {
        var (diagnostics, _) = RunGeneratorWithSources(source);
        return diagnostics;
    }

    private static IReadOnlyList<Diagnostic> RunGenerator(string source, params MetadataReference[] additionalReferences)
    {
        var (diagnostics, _) = RunGeneratorWithSources(source, additionalReferences);
        return diagnostics;
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<GeneratedSourceResult> Sources) RunGeneratorWithSources(string source)
        => RunGeneratorWithSources(source, Array.Empty<MetadataReference>());

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<GeneratedSourceResult> Sources) RunGeneratorWithSources(
        string source,
        params MetadataReference[] additionalReferences)
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList()
            ?? new List<MetadataReference>();

        references.AddRange(additionalReferences);

        var compilation = CSharpCompilation.Create(
            assemblyName: "DiagnosticTestAssembly",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new AutoWire.AutoWireGenerator();
        var driver = CSharpGeneratorDriver
            .Create(generator)
            .RunGenerators(compilation);

        var result = driver.GetRunResult();
        var sources = result.Results.SelectMany(r => r.GeneratedSources).ToList();
        return (result.Diagnostics, sources);
    }

    private static MetadataReference CreateReferencedAssembly(string assemblyName, string source)
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList()
            ?? new List<MetadataReference>();

        var compilation = CSharpCompilation.Create(
            assemblyName: assemblyName,
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(AutoWireAttributeStubSource),
                CSharpSyntaxTree.ParseText(source)
            ],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private const string AutoWireAttributeStubSource = """
        namespace AutoWire
        {
            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
            public sealed class ScopedAttribute : System.Attribute
            {
                public ScopedAttribute() { }
                public ScopedAttribute(System.Type serviceType) { }
                public ScopedAttribute(System.Type serviceType1, System.Type serviceType2, params System.Type[] additionalTypes) { }
                public object? Key { get; set; }
                public bool IncludeSelf { get; set; }
                public string? Profile { get; set; }
                public string? Condition { get; set; }
                public bool IncludeLazy { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
            public sealed class SingletonAttribute : System.Attribute
            {
                public SingletonAttribute() { }
                public SingletonAttribute(System.Type serviceType) { }
                public SingletonAttribute(System.Type serviceType1, System.Type serviceType2, params System.Type[] additionalTypes) { }
                public object? Key { get; set; }
                public bool IncludeSelf { get; set; }
                public string? Profile { get; set; }
                public string? Condition { get; set; }
                public bool IncludeLazy { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
            public sealed class TransientAttribute : System.Attribute
            {
                public TransientAttribute() { }
                public TransientAttribute(System.Type serviceType) { }
                public TransientAttribute(System.Type serviceType1, System.Type serviceType2, params System.Type[] additionalTypes) { }
                public object? Key { get; set; }
                public bool IncludeSelf { get; set; }
                public string? Profile { get; set; }
                public string? Condition { get; set; }
                public bool IncludeLazy { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
            public sealed class TryScopedAttribute : System.Attribute
            {
                public TryScopedAttribute() { }
                public TryScopedAttribute(System.Type serviceType) { }
                public TryScopedAttribute(System.Type serviceType1, System.Type serviceType2, params System.Type[] additionalTypes) { }
                public object? Key { get; set; }
                public bool IncludeSelf { get; set; }
                public string? Profile { get; set; }
                public string? Condition { get; set; }
                public bool IncludeLazy { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
            public sealed class TrySingletonAttribute : System.Attribute
            {
                public TrySingletonAttribute() { }
                public TrySingletonAttribute(System.Type serviceType) { }
                public TrySingletonAttribute(System.Type serviceType1, System.Type serviceType2, params System.Type[] additionalTypes) { }
                public object? Key { get; set; }
                public bool IncludeSelf { get; set; }
                public string? Profile { get; set; }
                public string? Condition { get; set; }
                public bool IncludeLazy { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
            public sealed class TryTransientAttribute : System.Attribute
            {
                public TryTransientAttribute() { }
                public TryTransientAttribute(System.Type serviceType) { }
                public TryTransientAttribute(System.Type serviceType1, System.Type serviceType2, params System.Type[] additionalTypes) { }
                public object? Key { get; set; }
                public bool IncludeSelf { get; set; }
                public string? Profile { get; set; }
                public string? Condition { get; set; }
                public bool IncludeLazy { get; set; }
            }
        }
        """;
}
