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
        Assert.True(generatedSources.Any(s => s.HintName.Contains("ServiceCollectionExtensions")));
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
        Assert.True(generatedSources.Any(s => s.HintName.Contains("ServiceCollectionExtensions")));
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
        Assert.True(generatedSources.Any(s => s.HintName.Contains("ServiceCollectionExtensions")));
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
        Assert.True(generatedSources.Any(s => s.HintName.Contains("ServiceCollectionExtensions")));
        var generated = generatedSources.First(s => s.HintName.Contains("ServiceCollectionExtensions"));
        var code = generated.SourceText.ToString();
        Assert.Contains("global::ServiceKey.Primary", code);
        // Should NOT have a quoted string key
        Assert.DoesNotContain("\"Primary\"", code);
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
