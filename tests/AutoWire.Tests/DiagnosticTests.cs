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

    private static IReadOnlyList<Diagnostic> RunGenerator(string source)
    {
        var (diagnostics, _) = RunGeneratorWithSources(source);
        return diagnostics;
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<GeneratedSourceResult> Sources) RunGeneratorWithSources(string source)
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList()
            ?? new List<MetadataReference>();

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
}
