using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<Diagnostic> RunGenerator(string source)
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

        return driver.GetRunResult().Diagnostics;
    }
}
