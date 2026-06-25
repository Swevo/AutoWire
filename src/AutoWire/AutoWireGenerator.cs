using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace AutoWire;

[Generator]
public sealed class AutoWireGenerator : IIncrementalGenerator
{
    // ── Attribute FQNs ─────────────────────────────────────────────────────────
    private const string ScopedFqn            = "AutoWire.ScopedAttribute";
    private const string SingletonFqn         = "AutoWire.SingletonAttribute";
    private const string TransientFqn         = "AutoWire.TransientAttribute";
    private const string TryScopedFqn         = "AutoWire.TryScopedAttribute";
    private const string TrySingletonFqn      = "AutoWire.TrySingletonAttribute";
    private const string TryTransientFqn      = "AutoWire.TryTransientAttribute";
    private const string DecorateScopedFqn    = "AutoWire.DecorateScopedAttribute";
    private const string DecorateSingletonFqn = "AutoWire.DecorateSingletonAttribute";
    private const string DecorateTransientFqn = "AutoWire.DecorateTransientAttribute";
    private const string HostedServiceFqn     = "AutoWire.HostedServiceAttribute";
    private const string OptionsFqn           = "AutoWire.AutoWireOptionsAttribute";
    private const string AutoWireScanFqn      = "AutoWire.AutoWireScanAttribute";
    private const string AutoWireExcludeFqn   = "AutoWire.AutoWireExcludeAttribute";
    private const string FactoryFqn           = "AutoWire.FactoryAttribute";
    private const string OptionsBindingFqn    = "AutoWire.OptionsAttribute";
    private const string HttpClientFqn        = "AutoWire.HttpClientAttribute";
    private const string ValidateFqn          = "AutoWire.ValidateAttribute";
    private const string InterceptorFqn       = "AutoWire.InterceptorAttribute";

    // ── Diagnostics ────────────────────────────────────────────────────────────
    private static readonly DiagnosticDescriptor AW001AbstractClass = new(
        id: "AW001",
        title: "Abstract class decorated with AutoWire attribute",
        messageFormat: "Abstract class '{0}' decorated with [{1}] will not be registered. Abstract classes cannot be instantiated.",
        category: "AutoWire",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Remove the AutoWire attribute from this class, or make the class non-abstract.");

    private static readonly DiagnosticDescriptor AW002DuplicateService = new(
        id: "AW002",
        title: "Multiple non-keyed registrations for the same service type",
        messageFormat: "Multiple non-keyed implementations are registered for '{0}'. The last registration wins. Use [Key] to distinguish, or set Duplicate = DuplicateStrategy.Replace to make the winner explicit.",
        category: "AutoWire",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Use keyed services or DuplicateStrategy.Replace/Skip to make your intent clear.");

    private static readonly DiagnosticDescriptor AW003ServiceTypeMismatch = new(
        id: "AW003",
        title: "Class does not implement the specified service type",
        messageFormat: "'{0}' does not implement '{1}'. The registration will fail at runtime.",
        category: "AutoWire",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Ensure the class implements the type passed to the attribute constructor.");

    private static readonly DiagnosticDescriptor AW004CaptiveDependency = new(
        id: "AW004",
        title: "Singleton depends on a Scoped service",
        messageFormat: "Singleton '{0}' depends on Scoped service '{1}'. The scoped service will be captured for the singleton's lifetime, bypassing scope disposal. Inject IServiceScopeFactory and create a scope explicitly instead.",
        category: "AutoWire",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Inject IServiceScopeFactory and create a scope explicitly, or change the dependency's lifetime to Singleton or Transient.");

    private static readonly DiagnosticDescriptor AW005AmbiguousScan = new(
        id: "AW005",
        title: "Type matched by multiple [AutoWireScan] configurations",
        messageFormat: "'{0}' matches more than one [AutoWireScan] configuration. The first matching configuration wins. Consider using [AutoWireExclude] or narrowing namespace patterns to remove the ambiguity.",
        category: "AutoWire",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Use [AutoWireExclude] on the type or narrow the namespace patterns so each type matches exactly one [AutoWireScan] configuration.");

    private static readonly DiagnosticDescriptor AW006TransientDisposable = new(
        id: "AW006",
        title: "Transient service implements IDisposable or IAsyncDisposable",
        messageFormat: "'{0}' is registered as Transient and implements '{1}'. The DI container does not track transient disposables — they will not be disposed at the end of a scope, causing resource leaks. Consider changing the lifetime to Scoped, or manage the instance lifetime manually.",
        category: "AutoWire",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Change the lifetime to Scoped so the container disposes the service when the scope ends, or implement manual lifetime management.");

    private static readonly DiagnosticDescriptor AW007NoInterface = new(
        id: "AW007",
        title: "Service registered without an interface abstraction",
        messageFormat: "'{0}' is registered as a DI service but implements no non-system interfaces. Consider extracting an interface so consumers depend on an abstraction rather than the concrete type.",
        category: "AutoWire",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Extract an interface from this class so it can be replaced in tests or with alternate implementations without changing call sites.");

    private static readonly DiagnosticDescriptor AW008DangerousSingletonDependency = new(
        id: "AW008",
        title: "Singleton injects a request-bound or context-sensitive dependency",
        messageFormat: "Singleton '{0}' injects '{1}'. IHttpContextAccessor exposes per-request state (null outside a request), and DbContext is a unit-of-work designed for short-lived scopes. Capture these in a Scoped service, or resolve them via IServiceScopeFactory.",
        category: "AutoWire",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "IHttpContextAccessor and DbContext are not safe for Singleton injection. Move the consuming class to Scoped lifetime, or resolve the dependency inside a manually created scope via IServiceScopeFactory.");

    private static readonly DiagnosticDescriptor AW009ScopedInHostedService = new(
        id: "AW009",
        title: "HostedService injects a Scoped service",
        messageFormat: "'{0}' is a HostedService (effectively Singleton) and injects '{1}' which is registered as Scoped. The Scoped service will be captured for the application lifetime, bypassing disposal. Inject IServiceScopeFactory and create a scope inside ExecuteAsync instead.",
        category: "AutoWire",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "HostedService / BackgroundService runs for the application lifetime and acts like a Singleton. Injecting a Scoped service directly creates a captive dependency. Use IServiceScopeFactory to create a fresh scope per unit of work.");

    private static readonly DiagnosticDescriptor AW010DuplicateInterceptorTarget = new(
        id: "AW010",
        title: "Multiple [Interceptor] attributes target the same interface",
        messageFormat: "Multiple [Interceptor] attributes on '{0}' target the same interface '{1}'. Only the first will be registered; remove the duplicate or use separate interceptor classes.",
        category: "AutoWire",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Each interface can only be wrapped by one proxy class per registration. Having two [Interceptor] attributes on the same class targeting the same interface results in a duplicate registration. Use a single interceptor that composes both concerns.");

    // ── Attribute source ───────────────────────────────────────────────────────
    private const string AttributeSource = """
        // <auto-generated by AutoWire/>
        #nullable enable
        using System;

        namespace AutoWire
        {
            /// <summary>Controls how duplicate registrations for the same service type are handled.</summary>
            public enum DuplicateStrategy
            {
                /// <summary>Add the registration regardless of existing registrations (default). Last registration wins.</summary>
                Add = 0,
                /// <summary>Only register if no registration already exists for the service type (TryAdd semantics). Ideal for NuGet library authors.</summary>
                Skip = 1,
                /// <summary>Remove all existing registrations for the service type before adding this one. Makes the winner explicit.</summary>
                Replace = 2
            }

            /// <summary>Registers the decorated class as a <b>scoped</b> service via AutoWire source generation.</summary>
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
            public sealed class ScopedAttribute : Attribute
            {
                /// <summary>The explicit service type to register against. When null, all non-system interfaces are used.</summary>
                public Type? ServiceType { get; }
                /// <summary>Optional keyed-service key. Accepts a string or any enum value. Requires .NET 8+ / Microsoft.Extensions.DependencyInjection 8+.</summary>
                public object? Key { get; set; }
                /// <summary>Controls how duplicate registrations for the same service type are handled.</summary>
                public DuplicateStrategy Duplicate { get; set; } = DuplicateStrategy.Add;
                /// <summary>Also registers the concrete type itself in addition to the interface(s). Useful when you need to inject by concrete type in tests or internal code.</summary>
                public bool IncludeSelf { get; set; }
                /// <summary>Only registers this service when <c>AddAutoWireServices(profile: "...")</c> is called with a matching profile. When null, the service is always registered.</summary>
                public string? Profile { get; set; }
                /// <summary>When set, wraps the registration in a <c>#if SYMBOL ... #endif</c> preprocessor block. Example: <c>Condition = "DEBUG"</c>.</summary>
                public string? Condition { get; set; }
                /// <summary>Also registers <c>Lazy&lt;T&gt;</c> via <c>AddTransient</c> so consumers can take a lazy dependency on this service.</summary>
                public bool IncludeLazy { get; set; }
                /// <summary>When set, this service belongs to the named module. Module services are registered via the generated <c>Add{Module}Module()</c> extension method instead of <c>AddAutoWireServices()</c>.</summary>
                public string? Module { get; set; }
                /// <summary>Registers against all non-system interfaces the class implements (or as concrete type if none).</summary>
                public ScopedAttribute() { }
                /// <summary>Registers against the specified service type only.</summary>
                public ScopedAttribute(Type serviceType) { ServiceType = serviceType; }
            }
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
            public sealed class SingletonAttribute : Attribute
            {
                /// <summary>The explicit service type to register against. When null, all non-system interfaces are used.</summary>
                public Type? ServiceType { get; }
                /// <summary>Optional keyed-service key. Accepts a string or any enum value. Requires .NET 8+ / Microsoft.Extensions.DependencyInjection 8+.</summary>
                public object? Key { get; set; }
                /// <summary>Controls how duplicate registrations for the same service type are handled.</summary>
                public DuplicateStrategy Duplicate { get; set; } = DuplicateStrategy.Add;
                /// <summary>Also registers the concrete type itself in addition to the interface(s). Useful when you need to inject by concrete type in tests or internal code.</summary>
                public bool IncludeSelf { get; set; }
                /// <summary>Only registers this service when <c>AddAutoWireServices(profile: "...")</c> is called with a matching profile. When null, the service is always registered.</summary>
                public string? Profile { get; set; }
                /// <summary>When set, wraps the registration in a <c>#if SYMBOL ... #endif</c> preprocessor block.</summary>
                public string? Condition { get; set; }
                /// <summary>Also registers <c>Lazy&lt;T&gt;</c> via <c>AddTransient</c> so consumers can take a lazy dependency on this service.</summary>
                public bool IncludeLazy { get; set; }
                /// <summary>When set, this service belongs to the named module. Module services are registered via the generated <c>Add{Module}Module()</c> extension method instead of <c>AddAutoWireServices()</c>.</summary>
                public string? Module { get; set; }
                /// <summary>Registers against all non-system interfaces the class implements (or as concrete type if none).</summary>
                public SingletonAttribute() { }
                /// <summary>Registers against the specified service type only.</summary>
                public SingletonAttribute(Type serviceType) { ServiceType = serviceType; }
            }

            /// <summary>Registers the decorated class as a <b>transient</b> service via AutoWire source generation.</summary>
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
            public sealed class TransientAttribute : Attribute
            {
                /// <summary>The explicit service type to register against. When null, all non-system interfaces are used.</summary>
                public Type? ServiceType { get; }
                /// <summary>Optional keyed-service key. Accepts a string or any enum value. Requires .NET 8+ / Microsoft.Extensions.DependencyInjection 8+.</summary>
                public object? Key { get; set; }
                /// <summary>Controls how duplicate registrations for the same service type are handled.</summary>
                public DuplicateStrategy Duplicate { get; set; } = DuplicateStrategy.Add;
                /// <summary>Also registers the concrete type itself in addition to the interface(s). Useful when you need to inject by concrete type in tests or internal code.</summary>
                public bool IncludeSelf { get; set; }
                /// <summary>Only registers this service when <c>AddAutoWireServices(profile: "...")</c> is called with a matching profile. When null, the service is always registered.</summary>
                public string? Profile { get; set; }
                /// <summary>When set, wraps the registration in a <c>#if SYMBOL ... #endif</c> preprocessor block.</summary>
                public string? Condition { get; set; }
                /// <summary>Also registers <c>Lazy&lt;T&gt;</c> via <c>AddTransient</c> so consumers can take a lazy dependency on this service.</summary>
                public bool IncludeLazy { get; set; }
                /// <summary>When set, this service belongs to the named module. Module services are registered via the generated <c>Add{Module}Module()</c> extension method instead of <c>AddAutoWireServices()</c>.</summary>
                public string? Module { get; set; }
                /// <summary>Registers against all non-system interfaces the class implements (or as concrete type if none).</summary>
                public TransientAttribute() { }
                /// <summary>Registers against the specified service type only.</summary>
                public TransientAttribute(Type serviceType) { ServiceType = serviceType; }
            }

            /// <summary>
            /// Registers the decorated class as a <b>scoped</b> service using TryAdd semantics —
            /// the registration is skipped if the service type is already registered.
            /// Ideal for NuGet library authors who want to provide defaults without overriding consumer registrations.
            /// </summary>
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
            public sealed class TryScopedAttribute : Attribute
            {
                /// <summary>The explicit service type to register against. When null, all non-system interfaces are used.</summary>
                public Type? ServiceType { get; }
                /// <summary>Optional keyed-service key. Accepts a string or any enum value. Requires .NET 8+ / Microsoft.Extensions.DependencyInjection 8+.</summary>
                public object? Key { get; set; }
                /// <summary>Also registers the concrete type itself in addition to the interface(s). Useful when you need to inject by concrete type in tests or internal code.</summary>
                public bool IncludeSelf { get; set; }
                /// <summary>Only registers this service when <c>AddAutoWireServices(profile: "...")</c> is called with a matching profile. When null, the service is always registered.</summary>
                public string? Profile { get; set; }
                /// <summary>When set, wraps the registration in a <c>#if SYMBOL ... #endif</c> preprocessor block.</summary>
                public string? Condition { get; set; }
                /// <summary>Also registers <c>Lazy&lt;T&gt;</c> via <c>AddTransient</c> so consumers can take a lazy dependency on this service.</summary>
                public bool IncludeLazy { get; set; }
                /// <summary>When set, this service belongs to the named module. Module services are registered via the generated <c>Add{Module}Module()</c> extension method instead of <c>AddAutoWireServices()</c>.</summary>
                public string? Module { get; set; }
                public TryScopedAttribute() { }
                public TryScopedAttribute(Type serviceType) { ServiceType = serviceType; }
            }

            /// <summary>
            /// Registers the decorated class as a <b>singleton</b> service using TryAdd semantics —
            /// the registration is skipped if the service type is already registered.
            /// Ideal for NuGet library authors who want to provide defaults without overriding consumer registrations.
            /// </summary>
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
            public sealed class TrySingletonAttribute : Attribute
            {
                /// <summary>The explicit service type to register against. When null, all non-system interfaces are used.</summary>
                public Type? ServiceType { get; }
                /// <summary>Optional keyed-service key. Accepts a string or any enum value. Requires .NET 8+ / Microsoft.Extensions.DependencyInjection 8+.</summary>
                public object? Key { get; set; }
                /// <summary>Also registers the concrete type itself in addition to the interface(s). Useful when you need to inject by concrete type in tests or internal code.</summary>
                public bool IncludeSelf { get; set; }
                /// <summary>Only registers this service when <c>AddAutoWireServices(profile: "...")</c> is called with a matching profile. When null, the service is always registered.</summary>
                public string? Profile { get; set; }
                /// <summary>When set, wraps the registration in a <c>#if SYMBOL ... #endif</c> preprocessor block.</summary>
                public string? Condition { get; set; }
                /// <summary>Also registers <c>Lazy&lt;T&gt;</c> via <c>AddTransient</c> so consumers can take a lazy dependency on this service.</summary>
                public bool IncludeLazy { get; set; }
                /// <summary>When set, this service belongs to the named module. Module services are registered via the generated <c>Add{Module}Module()</c> extension method instead of <c>AddAutoWireServices()</c>.</summary>
                public string? Module { get; set; }
                public TrySingletonAttribute() { }
                public TrySingletonAttribute(Type serviceType) { ServiceType = serviceType; }
            }

            /// <summary>
            /// Registers the decorated class as a <b>transient</b> service using TryAdd semantics —
            /// the registration is skipped if the service type is already registered.
            /// Ideal for NuGet library authors who want to provide defaults without overriding consumer registrations.
            /// </summary>
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
            public sealed class TryTransientAttribute : Attribute
            {
                /// <summary>The explicit service type to register against. When null, all non-system interfaces are used.</summary>
                public Type? ServiceType { get; }
                /// <summary>Optional keyed-service key. Accepts a string or any enum value. Requires .NET 8+ / Microsoft.Extensions.DependencyInjection 8+.</summary>
                public object? Key { get; set; }
                /// <summary>Also registers the concrete type itself in addition to the interface(s). Useful when you need to inject by concrete type in tests or internal code.</summary>
                public bool IncludeSelf { get; set; }
                /// <summary>Only registers this service when <c>AddAutoWireServices(profile: "...")</c> is called with a matching profile. When null, the service is always registered.</summary>
                public string? Profile { get; set; }
                /// <summary>When set, wraps the registration in a <c>#if SYMBOL ... #endif</c> preprocessor block.</summary>
                public string? Condition { get; set; }
                /// <summary>Also registers <c>Lazy&lt;T&gt;</c> via <c>AddTransient</c> so consumers can take a lazy dependency on this service.</summary>
                public bool IncludeLazy { get; set; }
                /// <summary>When set, this service belongs to the named module. Module services are registered via the generated <c>Add{Module}Module()</c> extension method instead of <c>AddAutoWireServices()</c>.</summary>
                public string? Module { get; set; }
                public TryTransientAttribute() { }
                public TryTransientAttribute(Type serviceType) { ServiceType = serviceType; }
            }

            /// <summary>
            /// Registers the decorated class as a hosted background service via
            /// <c>services.AddHostedService&lt;T&gt;()</c>.
            /// The class must implement <c>Microsoft.Extensions.Hosting.IHostedService</c>
            /// or extend <c>Microsoft.Extensions.Hosting.BackgroundService</c>.
            /// </summary>
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class HostedServiceAttribute : Attribute { }

            /// <summary>
            /// Wraps the existing <b>scoped</b> registration for <paramref name="serviceType"/> with this decorator class.
            /// The inner implementation is self-registered so that the decorator can be injected with the concrete type.
            /// </summary>
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
            public sealed class DecorateScopedAttribute : Attribute
            {
                public Type ServiceType { get; }
                /// <summary>Controls the order in which decorators are applied when multiple decorators target the same service type. Lower values are applied first (closer to the inner service). Default is 0.</summary>
                public int Order { get; set; }
                public DecorateScopedAttribute(Type serviceType) { ServiceType = serviceType; }
            }

            /// <summary>Wraps the existing <b>singleton</b> registration for <paramref name="serviceType"/> with this decorator class.</summary>
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
            public sealed class DecorateSingletonAttribute : Attribute
            {
                public Type ServiceType { get; }
                /// <summary>Controls the order in which decorators are applied when multiple decorators target the same service type. Lower values are applied first (closer to the inner service). Default is 0.</summary>
                public int Order { get; set; }
                public DecorateSingletonAttribute(Type serviceType) { ServiceType = serviceType; }
            }

            /// <summary>Wraps the existing <b>transient</b> registration for <paramref name="serviceType"/> with this decorator class.</summary>
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
            public sealed class DecorateTransientAttribute : Attribute
            {
                public Type ServiceType { get; }
                /// <summary>Controls the order in which decorators are applied when multiple decorators target the same service type. Lower values are applied first (closer to the inner service). Default is 0.</summary>
                public int Order { get; set; }
                public DecorateTransientAttribute(Type serviceType) { ServiceType = serviceType; }
            }

            /// <summary>Assembly-level options for AutoWire source generation.</summary>
            [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
            public sealed class AutoWireOptionsAttribute : Attribute
            {
                /// <summary>
                /// The name of the generated extension method. Defaults to <c>AddAutoWireServices</c>.
                /// Override this in test projects to avoid ambiguous extension method errors when both
                /// production and test assemblies use AutoWire.
                /// <example>[assembly: AutoWire.AutoWireOptions(MethodName = "AddTestServices")]</example>
                /// </summary>
                public string MethodName { get; set; } = "AddAutoWireServices";
            }

            /// <summary>
            /// Scans the specified namespace and registers all non-abstract, non-excluded classes
            /// that do not already carry an explicit AutoWire registration attribute.
            /// Apply at the assembly level — can be used multiple times for different namespaces or lifetimes.
            /// </summary>
            /// <example>[assembly: AutoWire.AutoWireScan("MyApp.Services")]</example>
            [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class AutoWireScanAttribute : Attribute
            {
                /// <summary>The namespace to scan. Sub-namespaces are included by default.</summary>
                public string Namespace { get; }
                /// <summary>The DI lifetime for all scanned services. Accepts "Scoped" (default), "Singleton", or "Transient".</summary>
                public string Lifetime { get; set; } = "Scoped";
                /// <summary>When true (default), also scans child namespaces such as MyApp.Services.Impl.</summary>
                public bool IncludeSubNamespaces { get; set; } = true;
                /// <summary>
                /// When set, scans the assembly that contains this type rather than the current assembly.
                /// Use any public type from the target assembly as a marker.
                /// </summary>
                /// <example>[assembly: AutoWire.AutoWireScan("External.Services", AssemblyOf = typeof(External.Services.MarkerType))]</example>
                public Type? AssemblyOf { get; set; }
                public AutoWireScanAttribute(string @namespace) { Namespace = @namespace; }
            }

            /// <summary>Opts this class out of convention-based scanning via <see cref="AutoWireScanAttribute"/>.</summary>
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class AutoWireExcludeAttribute : Attribute { }

            /// <summary>
            /// Generates options binding boilerplate for the decorated class:
            /// <c>services.AddOptions&lt;T&gt;().BindConfiguration("Section").ValidateDataAnnotations().ValidateOnStart()</c>.
            /// Requires <c>Microsoft.Extensions.Options.ConfigurationExtensions</c>,
            /// <c>Microsoft.Extensions.Options.DataAnnotations</c>, and <c>Microsoft.Extensions.Hosting.Abstractions</c>.
            /// </summary>
            /// <example>
            /// [Options("Database")]
            /// public class DatabaseOptions { ... }
            /// </example>
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class OptionsAttribute : Attribute
            {
                /// <summary>The configuration section key. When null, uses the class name (with "Options" suffix stripped if present).</summary>
                public string? Section { get; }
                /// <summary>When true (default), chains <c>.ValidateDataAnnotations()</c>. Requires <c>Microsoft.Extensions.Options.DataAnnotations</c>.</summary>
                public bool ValidateDataAnnotations { get; set; } = true;
                /// <summary>When true (default), chains <c>.ValidateOnStart()</c>. Requires <c>Microsoft.Extensions.Hosting.Abstractions</c>.</summary>
                public bool ValidateOnStart { get; set; } = true;
                /// <summary>Uses the class name (minus "Options" suffix) as the section key.</summary>
                public OptionsAttribute() { }
                /// <summary>Binds configuration from the specified section key.</summary>
                public OptionsAttribute(string section) { Section = section; }
            }

            /// <summary>
            /// Registers the decorated class as a factory and its <c>Create()</c> return type as a DI service.
            /// AutoWire emits two registrations:
            /// <list type="bullet">
            ///   <item>The factory class itself, with <see cref="FactoryLifetime"/> (default: Singleton).</item>
            ///   <item>The product type (<see cref="ServiceType"/>), with <see cref="Lifetime"/> (default: Scoped),
            ///         resolved via <c>sp =&gt; sp.GetRequiredService&lt;FactoryClass&gt;().Create()</c>.</item>
            /// </list>
            /// </summary>
            /// <example>
            /// [Factory(typeof(IDbConnection))]
            /// public class DbConnectionFactory { public IDbConnection Create() => ...; }
            /// </example>
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
            public sealed class FactoryAttribute : Attribute
            {
                /// <summary>The product type to register via the factory's <c>Create()</c> method.</summary>
                public Type ServiceType { get; }
                /// <summary>The DI lifetime for the <b>product</b>. Default is <c>"Scoped"</c>.</summary>
                public string Lifetime { get; set; } = "Scoped";
                /// <summary>The DI lifetime for the <b>factory class itself</b>. Default is <c>"Singleton"</c>.</summary>
                public string FactoryLifetime { get; set; } = "Singleton";
                public FactoryAttribute(Type serviceType) { ServiceType = serviceType; }
            }

            /// <summary>
            /// Registers the decorated class as a typed <c>HttpClient</c> via
            /// <c>services.AddHttpClient&lt;T&gt;()</c>.
            /// Requires <c>Microsoft.Extensions.Http</c>.
            /// </summary>
            /// <example>
            /// [HttpClient]
            /// public class GitHubClient { public GitHubClient(HttpClient http) { } }
            ///
            /// [HttpClient(Name = "GitHub", BaseAddress = "https://api.github.com")]
            /// public class GitHubClient { }
            /// </example>
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class HttpClientAttribute : Attribute
            {
                /// <summary>Optional named-client name. When null the type name is used (typed client).</summary>
                public string? Name { get; set; }
                /// <summary>Optional base address set on the underlying <c>HttpClient</c>.</summary>
                public string? BaseAddress { get; set; }
                /// <summary>
                /// When true, chains <c>.AddStandardResilienceHandler()</c> which adds retry, circuit breaker,
                /// and timeout policies. Requires <c>Microsoft.Extensions.Http.Resilience</c>.
                /// </summary>
                public bool Resilience { get; set; }
                /// <summary>Optional HTTP client timeout in seconds. Maps to <c>HttpClient.Timeout</c>.</summary>
                public int Timeout { get; set; }
                /// <summary>
                /// Optional default request headers in "Key:Value" format, e.g. <c>new[] { "Accept:application/json" }</c>.
                /// Each entry is split on the first colon.
                /// </summary>
                public string[]? DefaultHeaders { get; set; }
            }

            /// <summary>
            /// Registers the decorated <see cref="FluentValidation.AbstractValidator{T}"/> subclass as
            /// <c>services.AddScoped&lt;IValidator&lt;T&gt;, ValidatorClass&gt;()</c>.
            /// Requires <c>FluentValidation</c>.
            /// </summary>
            /// <example>
            /// [Validate]
            /// public class CreateOrderCommandValidator : AbstractValidator&lt;CreateOrderCommand&gt; { }
            /// // → services.AddScoped&lt;IValidator&lt;CreateOrderCommand&gt;, CreateOrderCommandValidator&gt;();
            /// </example>
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class ValidateAttribute : Attribute { }

            /// <summary>
            /// Registers the decorated class as a compile-time interceptor for the specified service type.
            /// AutoWire generates a proxy class implementing <typeparamref name="TService"/> that routes every
            /// method call through this class's <see cref="IAutoWireInterceptor.Intercept"/> implementation.
            /// No runtime proxy library required — the proxy is emitted as C# source at build time.
            /// </summary>
            /// <example>
            /// [Interceptor(typeof(IOrderService))]
            /// public class LoggingInterceptor : IAutoWireInterceptor
            /// {
            ///     public void Intercept(IAutoWireInvocation inv)
            ///     {
            ///         Console.WriteLine($"Calling {inv.MethodName}");
            ///         inv.Proceed();
            ///     }
            /// }
            /// </example>
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
            public sealed class InterceptorAttribute : Attribute
            {
                /// <summary>The interface type to intercept. A compile-time proxy implementing this interface will be generated.</summary>
                public Type ServiceType { get; }
                /// <summary>The DI lifetime for both the proxy and the interceptor. Default is <c>"Scoped"</c>.</summary>
                public string Lifetime { get; set; } = "Scoped";
                public InterceptorAttribute(Type serviceType) { ServiceType = serviceType; }
            }

            /// <summary>
            /// Implement this interface on your interceptor class to intercept method calls routed through
            /// the AutoWire-generated proxy.
            /// </summary>
            public interface IAutoWireInterceptor
            {
                void Intercept(IAutoWireInvocation invocation);
            }

            /// <summary>Represents a single method invocation being intercepted.</summary>
            public interface IAutoWireInvocation
            {
                /// <summary>The name of the intercepted method.</summary>
                string MethodName { get; }
                /// <summary>The arguments passed to the method. You may modify them before calling <see cref="Proceed"/>.</summary>
                object?[] Arguments { get; }
                /// <summary>Gets or sets the return value. Set this before <see cref="Proceed"/> to short-circuit; or override after Proceed to change the result.</summary>
                object? ReturnValue { get; set; }
                /// <summary>Calls the actual inner implementation. Must be called to complete the invocation unless you want to short-circuit.</summary>
                void Proceed();
            }
        }
        """;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
            ctx.AddSource("AutoWireAttributes.g.cs", SourceText.From(AttributeSource, Encoding.UTF8)));

        // ── Registration pipelines ─────────────────────────────────────────────
        var scoped       = CollectRegistrations(context, ScopedFqn,       "Scoped",    DuplicateStrategy.Add);
        var singleton    = CollectRegistrations(context, SingletonFqn,    "Singleton", DuplicateStrategy.Add);
        var transient    = CollectRegistrations(context, TransientFqn,    "Transient", DuplicateStrategy.Add);
        var tryScoped    = CollectRegistrations(context, TryScopedFqn,    "Scoped",    DuplicateStrategy.Skip);
        var trySingleton = CollectRegistrations(context, TrySingletonFqn, "Singleton", DuplicateStrategy.Skip);
        var tryTransient = CollectRegistrations(context, TryTransientFqn, "Transient", DuplicateStrategy.Skip);

        // ── AW001: abstract class diagnostics ─────────────────────────────────
        RegisterAbstractDiagnostics(context, ScopedFqn,          "Scoped");
        RegisterAbstractDiagnostics(context, SingletonFqn,       "Singleton");
        RegisterAbstractDiagnostics(context, TransientFqn,       "Transient");
        RegisterAbstractDiagnostics(context, TryScopedFqn,       "TryScoped");
        RegisterAbstractDiagnostics(context, TrySingletonFqn,    "TrySingleton");
        RegisterAbstractDiagnostics(context, TryTransientFqn,    "TryTransient");
        RegisterAbstractDiagnostics(context, DecorateScopedFqn,    "DecorateScoped");
        RegisterAbstractDiagnostics(context, DecorateSingletonFqn, "DecorateSingleton");
        RegisterAbstractDiagnostics(context, DecorateTransientFqn, "DecorateTransient");
        RegisterAbstractDiagnostics(context, HostedServiceFqn,     "HostedService");

        // ── AW003: service type not implemented ───────────────────────────────
        RegisterServiceTypeMismatchDiagnostics(context, ScopedFqn);
        RegisterServiceTypeMismatchDiagnostics(context, SingletonFqn);
        RegisterServiceTypeMismatchDiagnostics(context, TransientFqn);
        RegisterServiceTypeMismatchDiagnostics(context, TryScopedFqn);
        RegisterServiceTypeMismatchDiagnostics(context, TrySingletonFqn);
        RegisterServiceTypeMismatchDiagnostics(context, TryTransientFqn);
        RegisterServiceTypeMismatchDiagnostics(context, DecorateScopedFqn);
        RegisterServiceTypeMismatchDiagnostics(context, DecorateSingletonFqn);
        RegisterServiceTypeMismatchDiagnostics(context, DecorateTransientFqn);

        // ── Hosted service pipeline ────────────────────────────────────────────
        var hostedServices = CollectHostedServices(context);

        // ── Decorator pipelines ────────────────────────────────────────────────
        var decoScoped    = CollectDecorators(context, DecorateScopedFqn,    "Scoped");
        var decoSingleton = CollectDecorators(context, DecorateSingletonFqn, "Singleton");
        var decoTransient = CollectDecorators(context, DecorateTransientFqn, "Transient");

        // ── Factory pipeline ───────────────────────────────────────────────────
        var factories = CollectFactories(context);

        // ── AutoWireOptions assembly attribute ────────────────────────────────
        var methodName = context.CompilationProvider.Select(static (compilation, _) =>
        {
            var attr = compilation.Assembly.GetAttributes()
                .FirstOrDefault(static a => a.AttributeClass?.ToDisplayString() == OptionsFqn);
            if (attr is null) return "AddAutoWireServices";
            foreach (var na in attr.NamedArguments)
                if (na.Key == "MethodName" && na.Value.Value is string s && s.Length > 0)
                    return s;
            return "AddAutoWireServices";
        });

        // ── AW006: transient IDisposable diagnostics ──────────────────────────
        RegisterTransientDisposableDiagnostics(context, TransientFqn);
        RegisterTransientDisposableDiagnostics(context, TryTransientFqn);

        // ── AW007: no-interface diagnostics ───────────────────────────────────
        RegisterNoInterfaceDiagnostics(context, ScopedFqn);
        RegisterNoInterfaceDiagnostics(context, SingletonFqn);
        RegisterNoInterfaceDiagnostics(context, TransientFqn);
        RegisterNoInterfaceDiagnostics(context, TryScopedFqn);
        RegisterNoInterfaceDiagnostics(context, TrySingletonFqn);
        RegisterNoInterfaceDiagnostics(context, TryTransientFqn);

        // ── AW008: dangerous singleton dependencies ────────────────────────────
        RegisterDangerousSingletonDependencyDiagnostics(context, SingletonFqn);
        RegisterDangerousSingletonDependencyDiagnostics(context, TrySingletonFqn);

        // ── Convention scan pipeline ───────────────────────────────────────────
        var scannedRegs = CollectScanRegistrations(context);

        // ── AW004: captive dependency detection ───────────────────────────────
        RegisterCaptiveDependencyDiagnostics(context);
        RegisterAmbiguousScanDiagnostics(context);

        // ── AW009: Scoped in HostedService diagnostics ────────────────────────
        RegisterScopedInHostedServiceDiagnostics(context);
        RegisterDuplicateInterceptorTargetDiagnostics(context);

        // ── Options pipeline ───────────────────────────────────────────────────
        var optionsRegs = CollectOptions(context);

        // ── HttpClient pipeline ────────────────────────────────────────────────
        var httpClients = CollectHttpClients(context);

        // ── Validator pipeline ─────────────────────────────────────────────────
        var validators = CollectValidators(context);

        // ── Interceptor pipeline ───────────────────────────────────────────────
        var interceptors = CollectInterceptors(context);

        // ── Combine all registrations + generate ──────────────────────────────
        var all = scoped.Collect()
            .Combine(singleton.Collect())
            .Combine(transient.Collect())
            .Combine(tryScoped.Collect())
            .Combine(trySingleton.Collect())
            .Combine(tryTransient.Collect())
            .Combine(decoScoped.Collect())
            .Combine(decoSingleton.Collect())
            .Combine(decoTransient.Collect())
            .Combine(hostedServices.Collect())
            .Combine(scannedRegs.Collect())
            .Combine(factories.Collect())
            .Combine(optionsRegs.Collect())
            .Combine(httpClients.Collect())
            .Combine(validators.Collect())
            .Combine(interceptors.Collect())
            .Combine(methodName);

        context.RegisterSourceOutput(all, static (ctx, combined) =>
        {
            var ((((((((((((((((s, si), t), ts), tsi), tt), ds), dsi), dt), hs), sr), f), opts), hc), val), intc), name) = combined;
            var registrations = s.AddRange(si).AddRange(t).AddRange(ts).AddRange(tsi).AddRange(tt).AddRange(sr);
            var decorators    = ds.AddRange(dsi).AddRange(dt);
            if (registrations.IsEmpty && decorators.IsEmpty && hs.IsEmpty && f.IsEmpty && opts.IsEmpty && hc.IsEmpty && val.IsEmpty && intc.IsEmpty) return;

            ReportDuplicateServiceDiagnostics(ctx, registrations);

            ctx.AddSource(
                "AutoWireServiceCollectionExtensions.g.cs",
                SourceText.From(GenerateSource(registrations, decorators, hs, f, opts, hc, val, intc, name), Encoding.UTF8));

            ctx.AddSource(
                "AutoWireRegistrationSummary.g.cs",
                SourceText.From(GenerateSummary(registrations, hs, f, hc), Encoding.UTF8));
        });
    }

    // ── AW001 helper ──────────────────────────────────────────────────────────

    private static void RegisterAbstractDiagnostics(
        IncrementalGeneratorInitializationContext context,
        string attributeFqn,
        string attributeName)
    {
        var abstracts = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: (ctx, _) =>
                {
                    if (ctx.TargetSymbol is not INamedTypeSymbol { IsAbstract: true } sym) return null;
                    var loc = sym.Locations.Length > 0 ? sym.Locations[0] : Location.None;
                    return new DiagnosticInfo("AW001", loc, new[] { sym.Name, attributeName });
                })
            .Where(static d => d is not null)
            .Select(static (d, _) => d!);

        context.RegisterSourceOutput(abstracts, static (ctx, d) =>
            ctx.ReportDiagnostic(d.Create(AW001AbstractClass)));
    }

    // ── AW003 helper ──────────────────────────────────────────────────────────

    private static void RegisterServiceTypeMismatchDiagnostics(
        IncrementalGeneratorInitializationContext context,
        string attributeFqn)
    {
        var mismatches = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol) return null;

                    foreach (var attr in ctx.Attributes)
                    {
                        if (attr.ConstructorArguments.Length == 0) continue;
                        if (attr.ConstructorArguments[0].Value is not ITypeSymbol serviceType) continue;
                        if (!ImplementsServiceType(classSymbol, serviceType))
                        {
                            var loc = classSymbol.Locations.Length > 0 ? classSymbol.Locations[0] : Location.None;
                            return new DiagnosticInfo("AW003", loc,
                                new[] { classSymbol.Name, serviceType.ToDisplayString() });
                        }
                    }
                    return null;
                })
            .Where(static d => d is not null)
            .Select(static (d, _) => d!);

        context.RegisterSourceOutput(mismatches, static (ctx, d) =>
            ctx.ReportDiagnostic(d.Create(AW003ServiceTypeMismatch)));
    }

    // ── AW004 helper ──────────────────────────────────────────────────────────

    private static void RegisterCaptiveDependencyDiagnostics(
        IncrementalGeneratorInitializationContext context)
    {
        var captive = context.CompilationProvider
            .Select(static (compilation, _) => FindCaptiveDependencies(compilation))
            .SelectMany(static (arr, _) => arr);

        context.RegisterSourceOutput(captive, static (ctx, d) =>
            ctx.ReportDiagnostic(d.Create(AW004CaptiveDependency)));
    }

    private static ImmutableArray<DiagnosticInfo> FindCaptiveDependencies(Compilation compilation)
    {
        // Step 1: collect all service FQNs that are registered as Scoped (via [Scoped] or [TryScoped])
        var scopedFqns = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in GetAllNamedTypesInNamespace(compilation.Assembly.GlobalNamespace))
        {
            if (type.IsAbstract) continue;
            if (!TypeHasAutoWireAttribute(type, ScopedFqn) &&
                !TypeHasAutoWireAttribute(type, TryScopedFqn)) continue;

            // All non-system interfaces the class implements are potential scoped service types
            foreach (var iface in type.AllInterfaces)
            {
                var ns = iface.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                if (IsSystemNamespace(ns)) continue;
                scopedFqns.Add(iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                // Also add the open-generic original definition so IRepo<Order> matches IRepo<>
                if (iface.IsGenericType)
                    scopedFqns.Add(iface.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }
            // Include the concrete type itself
            scopedFqns.Add(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        if (scopedFqns.Count == 0) return ImmutableArray<DiagnosticInfo>.Empty;

        // Step 2: for each Singleton, check constructor parameters against scoped set
        var results = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        foreach (var type in GetAllNamedTypesInNamespace(compilation.Assembly.GlobalNamespace))
        {
            if (type.IsAbstract) continue;
            if (!TypeHasAutoWireAttribute(type, SingletonFqn) &&
                !TypeHasAutoWireAttribute(type, TrySingletonFqn)) continue;

            foreach (var ctor in type.InstanceConstructors)
            {
                if (ctor.DeclaredAccessibility == Accessibility.Private) continue;

                foreach (var param in ctor.Parameters)
                {
                    if (param.Type is not INamedTypeSymbol paramType) continue;

                    var fqn = paramType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (!scopedFqns.Contains(fqn)) continue;

                    var loc = param.Locations.Length > 0
                        ? param.Locations[0]
                        : (type.Locations.Length > 0 ? type.Locations[0] : Location.None);

                    results.Add(new DiagnosticInfo("AW004", loc,
                        new[] { type.Name, paramType.Name }));
                }
            }
        }

        return results.ToImmutable();
    }

    // ── AW005 helper ──────────────────────────────────────────────────────────

    private static void RegisterAmbiguousScanDiagnostics(
        IncrementalGeneratorInitializationContext context)
    {
        var ambiguous = context.CompilationProvider
            .Select(static (compilation, _) => FindAmbiguousScans(compilation))
            .SelectMany(static (arr, _) => arr);

        context.RegisterSourceOutput(ambiguous, static (ctx, d) =>
            ctx.ReportDiagnostic(d.Create(AW005AmbiguousScan)));
    }

    private static ImmutableArray<DiagnosticInfo> FindAmbiguousScans(Compilation compilation)
    {
        var scanConfigs = new List<(string Namespace, bool IncludeSubNs)>();
        foreach (var attr in compilation.Assembly.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != AutoWireScanFqn) continue;
            if (attr.ConstructorArguments.Length == 0) continue;
            if (attr.ConstructorArguments[0].Value is not string ns || string.IsNullOrEmpty(ns)) continue;

            var includeSubNs = true;
            foreach (var na in attr.NamedArguments)
                if (na.Key == "IncludeSubNamespaces" && na.Value.Value is bool b)
                    includeSubNs = b;

            scanConfigs.Add((ns, includeSubNs));
        }

        if (scanConfigs.Count < 2) return ImmutableArray<DiagnosticInfo>.Empty;

        var results = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        foreach (var type in GetAllNamedTypesInNamespace(compilation.Assembly.GlobalNamespace))
        {
            if (type.IsAbstract || type.IsGenericType) continue;
            if (TypeHasAutoWireAttribute(type, AutoWireExcludeFqn)) continue;
            if (HasAnyRegistrationAttribute(type)) continue;

            var typeNs = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            var matchCount = 0;
            foreach (var (scanNs, includeSubNs) in scanConfigs)
            {
                var matches = includeSubNs
                    ? typeNs == scanNs || typeNs.StartsWith(scanNs + ".", StringComparison.Ordinal)
                    : typeNs == scanNs;
                if (matches) matchCount++;
            }

            if (matchCount < 2) continue;

            var loc = type.Locations.Length > 0 ? type.Locations[0] : Location.None;
            results.Add(new DiagnosticInfo("AW005", loc, new[] { type.Name }));
        }

        return results.ToImmutable();
    }

    // ── AW006 helper ──────────────────────────────────────────────────────────

    private static void RegisterTransientDisposableDiagnostics(
        IncrementalGeneratorInitializationContext context,
        string attributeFqn)
    {
        var disposables = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    if (ctx.TargetSymbol is not INamedTypeSymbol { IsAbstract: false } sym) return null;
                    foreach (var iface in sym.AllInterfaces)
                    {
                        var fqn = iface.ToDisplayString();
                        if (fqn is "System.IDisposable" or "System.IAsyncDisposable")
                        {
                            var loc = sym.Locations.Length > 0 ? sym.Locations[0] : Location.None;
                            return new DiagnosticInfo("AW006", loc, new[] { sym.Name, iface.Name });
                        }
                    }
                    return null;
                })
            .Where(static d => d is not null)
            .Select(static (d, _) => d!);

        context.RegisterSourceOutput(disposables, static (ctx, d) =>
            ctx.ReportDiagnostic(d.Create(AW006TransientDisposable)));
    }

    // ── AW007 helper ──────────────────────────────────────────────────────────

    private static void RegisterNoInterfaceDiagnostics(
        IncrementalGeneratorInitializationContext context,
        string attributeFqn)
    {
        var noInterface = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    if (ctx.TargetSymbol is not INamedTypeSymbol { IsAbstract: false } sym) return null;

                    // Check if explicit ServiceType is set — if so, no warning needed
                    foreach (var attr in sym.GetAttributes())
                    {
                        foreach (var na in attr.NamedArguments)
                            if (na.Key == "ServiceType" && na.Value.Value != null) return null;
                    }

                    // Check if the class has any non-system interfaces
                    foreach (var iface in sym.AllInterfaces)
                    {
                        var ns = iface.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                        if (!ns.StartsWith("System", StringComparison.Ordinal) &&
                            !ns.StartsWith("Microsoft", StringComparison.Ordinal))
                            return null;
                    }

                    var loc = sym.Locations.Length > 0 ? sym.Locations[0] : Location.None;
                    return new DiagnosticInfo("AW007", loc, new[] { sym.Name });
                })
            .Where(static d => d is not null)
            .Select(static (d, _) => d!);

        context.RegisterSourceOutput(noInterface, static (ctx, d) =>
            ctx.ReportDiagnostic(d.Create(AW007NoInterface)));
    }

    // ── AW008 helper ──────────────────────────────────────────────────────────

    private static void RegisterDangerousSingletonDependencyDiagnostics(
        IncrementalGeneratorInitializationContext context,
        string attributeFqn)
    {
        var dangerous = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    if (ctx.TargetSymbol is not INamedTypeSymbol { IsAbstract: false } sym) return null;

                    // Scan all constructors for dangerous parameter types
                    foreach (var ctor in sym.Constructors)
                    {
                        if (ctor.IsStatic || ctor.DeclaredAccessibility == Accessibility.Private) continue;
                        foreach (var param in ctor.Parameters)
                        {
                            var paramType = param.Type;
                            if (IsDangerousSingletonDependency(paramType))
                            {
                                var loc = sym.Locations.Length > 0 ? sym.Locations[0] : Location.None;
                                return new DiagnosticInfo("AW008", loc, new[] { sym.Name, paramType.Name });
                            }
                        }
                    }
                    return null;
                })
            .Where(static d => d is not null)
            .Select(static (d, _) => d!);

        context.RegisterSourceOutput(dangerous, static (ctx, d) =>
            ctx.ReportDiagnostic(d.Create(AW008DangerousSingletonDependency)));
    }

    private static bool IsDangerousSingletonDependency(ITypeSymbol type)
    {
        var fqn = type.ToDisplayString();

        // IHttpContextAccessor — exposes per-request state, always null outside a request
        if (fqn == "Microsoft.AspNetCore.Http.IHttpContextAccessor") return true;

        // DbContext and any class that inherits from it (EF Core unit-of-work — not thread-safe as singleton)
        var current = type;
        while (current is not null)
        {
            if (current.ToDisplayString() == "Microsoft.EntityFrameworkCore.DbContext") return true;
            current = (current as INamedTypeSymbol)?.BaseType;
        }

        return false;
    }

    // ── AW009 helper ──────────────────────────────────────────────────────────

    private static void RegisterScopedInHostedServiceDiagnostics(
        IncrementalGeneratorInitializationContext context)
    {
        var issues = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                HostedServiceFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    if (ctx.TargetSymbol is not INamedTypeSymbol { IsAbstract: false } sym) return null;

                    var compilation = ctx.SemanticModel.Compilation;
                    var scopedAttributes = new HashSet<string> { "AutoWire.ScopedAttribute", "AutoWire.TryScopedAttribute" };

                    // Build a quick set of Scoped-registered type FQNs from the compilation
                    var scopedTypes = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var tree in compilation.SyntaxTrees)
                    {
                        var model = compilation.GetSemanticModel(tree);
                        foreach (var node in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                        {
                            if (model.GetDeclaredSymbol(node) is not INamedTypeSymbol typeSymbol) continue;
                            foreach (var attr in typeSymbol.GetAttributes())
                            {
                                var attrFqn = attr.AttributeClass?.ToDisplayString();
                                if (attrFqn is not null && scopedAttributes.Contains(attrFqn))
                                {
                                    // Register all service types this scoped class would expose
                                    foreach (var iface in typeSymbol.AllInterfaces)
                                        scopedTypes.Add(iface.ToDisplayString());
                                    scopedTypes.Add(typeSymbol.ToDisplayString());
                                    break;
                                }
                            }
                        }
                    }

                    foreach (var ctor in sym.Constructors)
                    {
                        if (ctor.IsStatic || ctor.DeclaredAccessibility == Accessibility.Private) continue;
                        foreach (var param in ctor.Parameters)
                        {
                            var paramFqn = param.Type.ToDisplayString();
                            if (scopedTypes.Contains(paramFqn))
                            {
                                var loc = sym.Locations.Length > 0 ? sym.Locations[0] : Location.None;
                                return new DiagnosticInfo("AW009", loc, new[] { sym.Name, param.Type.Name });
                            }
                        }
                    }
                    return null;
                })
            .Where(static d => d is not null)
            .Select(static (d, _) => d!);

        context.RegisterSourceOutput(issues, static (ctx, d) =>
            ctx.ReportDiagnostic(d.Create(AW009ScopedInHostedService)));
    }

    // ── AW010 helper ──────────────────────────────────────────────────────────

    private static void RegisterDuplicateInterceptorTargetDiagnostics(
        IncrementalGeneratorInitializationContext context)
    {
        var issues = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                InterceptorFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    if (ctx.TargetSymbol is not INamedTypeSymbol sym) return null;

                    // Collect all service types targeted by [Interceptor] on this class
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var attr in ctx.Attributes)
                    {
                        if (attr.ConstructorArguments.Length == 0) continue;
                        if (attr.ConstructorArguments[0].Value is not ITypeSymbol serviceType) continue;
                        var fqn = serviceType.ToDisplayString();
                        if (!seen.Add(fqn))
                        {
                            var loc = sym.Locations.Length > 0 ? sym.Locations[0] : Location.None;
                            return new DiagnosticInfo("AW010", loc, new[] { sym.Name, serviceType.Name });
                        }
                    }
                    return null;
                })
            .Where(static d => d is not null)
            .Select(static (d, _) => d!);

        context.RegisterSourceOutput(issues, static (ctx, d) =>
            ctx.ReportDiagnostic(d.Create(AW010DuplicateInterceptorTarget)));
    }

    private static bool TypeHasAutoWireAttribute(INamedTypeSymbol symbol, string attributeFqn)
    {
        foreach (var attr in symbol.GetAttributes())
            if (attr.AttributeClass?.ToDisplayString() == attributeFqn)
                return true;
        return false;
    }

    private static IEnumerable<INamedTypeSymbol> GetAllNamedTypesInNamespace(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in GetAllNestedTypes(type))
                yield return nested;
        }
        foreach (var childNs in ns.GetNamespaceMembers())
            foreach (var type in GetAllNamedTypesInNamespace(childNs))
                yield return type;
    }

    private static IEnumerable<INamedTypeSymbol> GetAllNestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var deep in GetAllNestedTypes(nested))
                yield return deep;
        }
    }

    private static bool ImplementsServiceType(INamedTypeSymbol classSymbol, ITypeSymbol serviceType)
    {
        // Self-registration
        if (SymbolEqualityComparer.Default.Equals(classSymbol.OriginalDefinition, serviceType.OriginalDefinition))
            return true;

        // Interface (direct or transitive, including from base classes)
        foreach (var iface in classSymbol.AllInterfaces)
            if (SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, serviceType.OriginalDefinition))
                return true;

        // Base type chain (for abstract base class registrations)
        var baseType = classSymbol.BaseType;
        while (baseType != null)
        {
            if (SymbolEqualityComparer.Default.Equals(baseType.OriginalDefinition, serviceType.OriginalDefinition))
                return true;
            baseType = baseType.BaseType;
        }

        return false;
    }

    // ── Hosted service collection ──────────────────────────────────────────────

    private static IncrementalValuesProvider<string> CollectHostedServices(
        IncrementalGeneratorInitializationContext context)
    {
        return context.SyntaxProvider
            .ForAttributeWithMetadataName(
                HostedServiceFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    if (ctx.TargetSymbol is not INamedTypeSymbol { IsAbstract: false } sym) return null;
                    return sym.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                })
            .Where(static s => s is not null)
            .Select(static (s, _) => s!);
    }

    // ── Convention scan collection ─────────────────────────────────────────────

    private static IncrementalValuesProvider<RegistrationInfo> CollectScanRegistrations(
        IncrementalGeneratorInitializationContext context)
    {
        return context.CompilationProvider
            .Select(static (compilation, _) => FindScannedRegistrations(compilation))
            .SelectMany(static (arr, _) => arr);
    }

    private static ImmutableArray<RegistrationInfo> FindScannedRegistrations(Compilation compilation)
    {
        // Parse all [AutoWireScan] assembly attributes
        var scanConfigs = new List<(string Namespace, string Lifetime, bool IncludeSubNs, IAssemblySymbol? Assembly)>();
        foreach (var attr in compilation.Assembly.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != AutoWireScanFqn) continue;
            if (attr.ConstructorArguments.Length == 0) continue;
            if (attr.ConstructorArguments[0].Value is not string ns || string.IsNullOrEmpty(ns)) continue;

            var lifetime = "Scoped";
            var includeSubNs = true;
            IAssemblySymbol? assemblySymbol = null;
            foreach (var na in attr.NamedArguments)
            {
                if (na.Key == "Lifetime" && na.Value.Value is string l &&
                    (l == "Singleton" || l == "Transient" || l == "Scoped"))
                    lifetime = l;
                else if (na.Key == "IncludeSubNamespaces" && na.Value.Value is bool b)
                    includeSubNs = b;
                else if (na.Key == "AssemblyOf" && na.Value.Value is ITypeSymbol markerType)
                    assemblySymbol = markerType.ContainingAssembly as IAssemblySymbol;
            }
            scanConfigs.Add((ns, lifetime, includeSubNs, assemblySymbol));
        }

        if (scanConfigs.Count == 0) return ImmutableArray<RegistrationInfo>.Empty;

        var results = ImmutableArray.CreateBuilder<RegistrationInfo>();
        var seenTypes = new HashSet<string>(StringComparer.Ordinal);

        var assembliesWithConfigs = new Dictionary<IAssemblySymbol, List<(string, string, bool)>>(
            SymbolEqualityComparer.Default);

        foreach (var config in scanConfigs)
        {
            var asmKey = (IAssemblySymbol)(config.Assembly ?? compilation.Assembly);
            if (!assembliesWithConfigs.TryGetValue(asmKey, out var list))
                assembliesWithConfigs[asmKey] = list = new List<(string, string, bool)>();
            list.Add((config.Namespace, config.Lifetime, config.IncludeSubNs));
        }

        foreach (var kvp in assembliesWithConfigs)
        {
            var asmSymbol = kvp.Key;
            var configs = kvp.Value;
            var isCurrentAssembly = SymbolEqualityComparer.Default.Equals(asmSymbol, compilation.Assembly);
            foreach (var type in GetAllNamedTypesInNamespace(asmSymbol.GlobalNamespace))
            {
                if (type.IsAbstract) continue;
                if (type.IsGenericType) continue;
                if (type.DeclaredAccessibility != Accessibility.Public) continue;

                // For the current assembly, respect [AutoWireExclude] and explicit attributes
                if (isCurrentAssembly)
                {
                    if (TypeHasAutoWireAttribute(type, AutoWireExcludeFqn)) continue;
                    if (HasAnyRegistrationAttribute(type)) continue;
                }

                var typeNs = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                var typeFqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (seenTypes.Contains(typeFqn)) continue;

                foreach (var config in configs)
                {
                    var scanNs = config.Item1;
                    var lifetime = config.Item2;
                    var includeSubNs = config.Item3;
                    var matches = includeSubNs
                        ? typeNs == scanNs || typeNs.StartsWith(scanNs + ".", StringComparison.Ordinal)
                        : typeNs == scanNs;

                    if (!matches) continue;

                    seenTypes.Add(typeFqn);

                    var serviceTypes = new List<string>();
                    foreach (var iface in type.AllInterfaces)
                    {
                        var ifNs = iface.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                        if (IsSystemNamespace(ifNs)) continue;
                        serviceTypes.Add(iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                    }
                    if (serviceTypes.Count == 0)
                        serviceTypes.Add(typeFqn);

                    results.Add(new RegistrationInfo(
                        typeFqn,
                        serviceTypes.ToImmutableArray(),
                        lifetime,
                        keyExpression: null,
                        isOpenGeneric: false,
                        duplicateStrategy: DuplicateStrategy.Add,
                        includeSelf: false,
                        profile: null,
                        isScanned: true));

                    break; // first matching scan config wins
                }
            }
        }

        return results.ToImmutable();
    }

    private static bool HasAnyRegistrationAttribute(INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            var fqn = attr.AttributeClass?.ToDisplayString();
            if (fqn == ScopedFqn || fqn == SingletonFqn || fqn == TransientFqn ||
                fqn == TryScopedFqn || fqn == TrySingletonFqn || fqn == TryTransientFqn ||
                fqn == HostedServiceFqn)
                return true;
        }
        return false;
    }

    // ── AW002 helper ──────────────────────────────────────────────────────────

    private static void ReportDuplicateServiceDiagnostics(
        SourceProductionContext ctx,
        ImmutableArray<RegistrationInfo> registrations)
    {
        // Only warn when multiple Add-strategy (non-Skip, non-Replace) registrations share a service type.
        // Skip and Replace explicitly acknowledge the duplicate — no noise.
        var seenServices = new HashSet<string>();
        foreach (var reg in registrations.Where(r => r.KeyExpression is null && r.DuplicateStrategy == DuplicateStrategy.Add && r.Profile is null && !r.IsScanned))
        {
            foreach (var svc in reg.ServiceTypes)
            {
                if (!seenServices.Add(svc))
                    ctx.ReportDiagnostic(Diagnostic.Create(AW002DuplicateService, Location.None, svc));
            }
        }
    }

    // ── Decorator collection ───────────────────────────────────────────────────

    private static IncrementalValuesProvider<DecoratorInfo> CollectDecorators(
        IncrementalGeneratorInitializationContext context,
        string attributeFqn,
        string lifetime)
    {
        return context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: (ctx, _) => TransformAllDecorators(ctx, lifetime))
            .SelectMany(static (arr, _) => arr);
    }

    private static ImmutableArray<DecoratorInfo> TransformAllDecorators(
        GeneratorAttributeSyntaxContext ctx,
        string lifetime)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol) return ImmutableArray<DecoratorInfo>.Empty;
        if (classSymbol.IsAbstract) return ImmutableArray<DecoratorInfo>.Empty;

        var builder = ImmutableArray.CreateBuilder<DecoratorInfo>(ctx.Attributes.Length);
        foreach (var attr in ctx.Attributes)
        {
            if (attr.ConstructorArguments.Length == 0) continue;
            if (attr.ConstructorArguments[0].Value is not ITypeSymbol serviceType) continue;

            var order = 0;
            foreach (var namedArg in attr.NamedArguments)
                if (namedArg.Key == "Order" && namedArg.Value.Value is int o)
                    order = o;

            builder.Add(new DecoratorInfo(
                ToFullyQualified(classSymbol),
                ToFullyQualified(serviceType),
                lifetime,
                order));
        }
        return builder.ToImmutable();
    }

    // ── Registration collection ────────────────────────────────────────────────

    private static IncrementalValuesProvider<FactoryInfo> CollectFactories(
        IncrementalGeneratorInitializationContext context)
    {
        return context.SyntaxProvider
            .ForAttributeWithMetadataName(
                FactoryFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => TransformAllFactories(ctx))
            .SelectMany(static (arr, _) => arr);
    }

    private static ImmutableArray<FactoryInfo> TransformAllFactories(
        GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol) return ImmutableArray<FactoryInfo>.Empty;
        if (classSymbol.IsAbstract) return ImmutableArray<FactoryInfo>.Empty;

        var factoryFqn = ToFullyQualified(classSymbol);
        var builder = ImmutableArray.CreateBuilder<FactoryInfo>(ctx.Attributes.Length);
        foreach (var attr in ctx.Attributes)
        {
            if (attr.ConstructorArguments.Length == 0) continue;
            if (attr.ConstructorArguments[0].Value is not ITypeSymbol productType) continue;

            var productLifetime = "Scoped";
            var factoryLifetime = "Singleton";
            foreach (var na in attr.NamedArguments)
            {
                if (na.Key == "Lifetime" && na.Value.Value is string l &&
                    (l == "Scoped" || l == "Singleton" || l == "Transient"))
                    productLifetime = l;
                else if (na.Key == "FactoryLifetime" && na.Value.Value is string fl &&
                    (fl == "Scoped" || fl == "Singleton" || fl == "Transient"))
                    factoryLifetime = fl;
            }

            builder.Add(new FactoryInfo(factoryFqn, ToFullyQualified(productType), productLifetime, factoryLifetime));
        }
        return builder.ToImmutable();
    }

    // ── Options collection ─────────────────────────────────────────────────────

    private static IncrementalValuesProvider<OptionsInfo> CollectOptions(
        IncrementalGeneratorInitializationContext context)
    {
        return context.SyntaxProvider
            .ForAttributeWithMetadataName(
                OptionsBindingFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol) return null;
                    var attr = ctx.Attributes[0];

                    // Section: constructor arg or derive from class name
                    string? section = null;
                    if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string s && s.Length > 0)
                        section = s;

                    if (section is null)
                    {
                        // Strip trailing "Options" from class name: DatabaseOptions → Database
                        var name = classSymbol.Name;
                        section = name.EndsWith("Options", StringComparison.Ordinal) && name.Length > 7
                            ? name.Substring(0, name.Length - 7)
                            : name;
                    }

                    var validateAnnotations = true;
                    var validateOnStart = true;
                    foreach (var na in attr.NamedArguments)
                    {
                        if (na.Key == "ValidateDataAnnotations" && na.Value.Value is bool va)
                            validateAnnotations = va;
                        else if (na.Key == "ValidateOnStart" && na.Value.Value is bool vs)
                            validateOnStart = vs;
                    }

                    return new OptionsInfo(
                        ToFullyQualified(classSymbol),
                        section,
                        validateAnnotations,
                        validateOnStart);
                })
            .Where(static o => o is not null)
            .Select(static (o, _) => o!);
    }

    // ── HttpClient collection ──────────────────────────────────────────────────

    private static IncrementalValuesProvider<HttpClientInfo> CollectHttpClients(
        IncrementalGeneratorInitializationContext context)
    {
        return context.SyntaxProvider
            .ForAttributeWithMetadataName(
                HttpClientFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol) return null;
                    var attr = ctx.Attributes[0];

                    string? name = null;
                    string? baseAddress = null;
                    var resilience = false;
                    int? timeoutSeconds = null;
                    var headersBuilder = ImmutableArray.CreateBuilder<string>();

                    foreach (var na in attr.NamedArguments)
                    {
                        if (na.Key == "Name" && na.Value.Value is string n && n.Length > 0)
                            name = n;
                        else if (na.Key == "BaseAddress" && na.Value.Value is string b && b.Length > 0)
                            baseAddress = b;
                        else if (na.Key == "Resilience" && na.Value.Value is bool r)
                            resilience = r;
                        else if (na.Key == "Timeout" && na.Value.Value is int t && t > 0)
                            timeoutSeconds = t;
                        else if (na.Key == "DefaultHeaders" && na.Value.Kind == TypedConstantKind.Array)
                        {
                            foreach (var item in na.Value.Values)
                                if (item.Value is string hdr && hdr.Length > 0)
                                    headersBuilder.Add(hdr);
                        }
                    }

                    return new HttpClientInfo(ToFullyQualified(classSymbol), name, baseAddress, resilience, timeoutSeconds, headersBuilder.ToImmutable());
                })
            .Where(static h => h is not null)
            .Select(static (h, _) => h!);
    }

    // ── Validator collection ───────────────────────────────────────────────────

    private static IncrementalValuesProvider<ValidatorInfo> CollectValidators(
        IncrementalGeneratorInitializationContext context)
    {
        return context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ValidateFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol) return null;

                    // Walk the inheritance chain to find AbstractValidator<T>
                    var baseType = classSymbol.BaseType;
                    while (baseType is not null)
                    {
                        var baseFqn = baseType.OriginalDefinition?.ToDisplayString();
                        if (baseFqn == "FluentValidation.AbstractValidator<T>" ||
                            (baseType.Name == "AbstractValidator" && baseType.IsGenericType && baseType.TypeArguments.Length == 1))
                        {
                            var validatedType = baseType.TypeArguments[0];
                            return new ValidatorInfo(
                                ToFullyQualified(classSymbol),
                                ToFullyQualified(validatedType));
                        }
                        baseType = baseType.BaseType;
                    }

                    return null;
                })
            .Where(static v => v is not null)
            .Select(static (v, _) => v!);
    }

    // ── Interceptor collection ─────────────────────────────────────────────────

    private static IncrementalValuesProvider<InterceptorInfo> CollectInterceptors(
        IncrementalGeneratorInitializationContext context)
    {
        return context.SyntaxProvider
            .ForAttributeWithMetadataName(
                InterceptorFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => TransformAllInterceptors(ctx))
            .SelectMany(static (arr, _) => arr);
    }

    private static ImmutableArray<InterceptorInfo> TransformAllInterceptors(
        GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol interceptorSymbol)
            return ImmutableArray<InterceptorInfo>.Empty;

        var builder = ImmutableArray.CreateBuilder<InterceptorInfo>(ctx.Attributes.Length);
        foreach (var attr in ctx.Attributes)
        {
            if (attr.ConstructorArguments.Length == 0) continue;
            if (attr.ConstructorArguments[0].Value is not ITypeSymbol serviceTypeSymbol) continue;
            if (serviceTypeSymbol is not INamedTypeSymbol namedService) continue;

            var lifetime = "Scoped";
            foreach (var na in attr.NamedArguments)
                if (na.Key == "Lifetime" && na.Value.Value is string l &&
                    (l == "Scoped" || l == "Singleton" || l == "Transient"))
                    lifetime = l;

            var interceptorFqn = ToFullyQualified(interceptorSymbol);
            var serviceFqn = ToFullyQualified(serviceTypeSymbol);

            // Build a safe proxy class name: AutoWire_Proxy_IMyService_with_LoggingInterceptor
            var interceptorSimple = interceptorSymbol.Name;
            var serviceSimple = namedService.Name.TrimStart('I');
            var proxyClassName = $"AutoWire_Proxy_{serviceSimple}_with_{interceptorSimple}";

            // Collect all non-generic instance methods from the interface
            var methods = ImmutableArray.CreateBuilder<MethodSignature>();
            foreach (var member in namedService.GetMembers().OfType<IMethodSymbol>())
            {
                if (member.IsStatic || member.MethodKind != MethodKind.Ordinary) continue;
                if (member.IsGenericMethod) continue; // skip generic methods for now

                var returnFqn = member.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var isVoid = member.ReturnsVoid;

                var isTask = !isVoid &&
                    (member.ReturnType.ToDisplayString() == "System.Threading.Tasks.Task" ||
                     member.ReturnType.ToDisplayString() == "System.Threading.Tasks.ValueTask");
                var isTaskOfT = !isVoid && !isTask &&
                    member.ReturnType is INamedTypeSymbol rt && rt.IsGenericType &&
                    (rt.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.Task<TResult>" ||
                     rt.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.ValueTask<TResult>");

                string? taskResultType = null;
                if (isTaskOfT && member.ReturnType is INamedTypeSymbol rtGeneric)
                    taskResultType = rtGeneric.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                var parameters = ImmutableArray.CreateBuilder<(string, string)>(member.Parameters.Length);
                var hasRefOrOut = false;
                foreach (var p in member.Parameters)
                {
                    if (p.RefKind != RefKind.None) { hasRefOrOut = true; break; }
                    parameters.Add((p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), p.Name));
                }
                if (hasRefOrOut) continue; // skip ref/out for now

                methods.Add(new MethodSignature(
                    member.Name, returnFqn, parameters.ToImmutable(),
                    isVoid, isTask, isTaskOfT, taskResultType));
            }

            builder.Add(new InterceptorInfo(interceptorFqn, serviceFqn, lifetime, proxyClassName, methods.ToImmutable()));
        }
        return builder.ToImmutable();
    }

    private static IncrementalValuesProvider<RegistrationInfo> CollectRegistrations(
        IncrementalGeneratorInitializationContext context,
        string attributeFqn,
        string lifetime,
        DuplicateStrategy defaultStrategy)
    {
        return context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: (ctx, _) => TransformAll(ctx, lifetime, defaultStrategy))
            .SelectMany(static (arr, _) => arr);
    }

    // ── Transform ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Produces one <see cref="RegistrationInfo"/> per attribute instance on the class,
    /// enabling AllowMultiple scenarios such as registering one class against several explicit types.
    /// </summary>
    private static ImmutableArray<RegistrationInfo> TransformAll(
        GeneratorAttributeSyntaxContext ctx,
        string lifetime,
        DuplicateStrategy defaultStrategy)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol) return ImmutableArray<RegistrationInfo>.Empty;
        if (classSymbol.IsAbstract) return ImmutableArray<RegistrationInfo>.Empty; // AW001 reported separately

        var builder = ImmutableArray.CreateBuilder<RegistrationInfo>(ctx.Attributes.Length);
        foreach (var attr in ctx.Attributes)
        {
            var info = TransformSingle(classSymbol, attr, lifetime, defaultStrategy);
            if (info is not null) builder.Add(info);
        }
        return builder.ToImmutable();
    }

    private static RegistrationInfo? TransformSingle(
        INamedTypeSymbol classSymbol,
        AttributeData attr,
        string lifetime,
        DuplicateStrategy defaultStrategy)
    {
        ITypeSymbol? explicitServiceType = null;
        if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is ITypeSymbol st)
            explicitServiceType = st;

        string? keyExpression = null;
        var duplicateStrategy = defaultStrategy;
        var includeSelf = false;
        string? profile = null;
        string? condition = null;
        var includeLazy = false;
        string? module = null;

        foreach (var namedArg in attr.NamedArguments)
        {
            if (namedArg.Key == "Key")
                keyExpression = BuildKeyExpression(namedArg.Value);
            else if (namedArg.Key == "Duplicate" && namedArg.Value.Value is int d)
                duplicateStrategy = (DuplicateStrategy)d;
            else if (namedArg.Key == "IncludeSelf" && namedArg.Value.Value is bool b)
                includeSelf = b;
            else if (namedArg.Key == "Profile" && namedArg.Value.Value is string p)
                profile = p;
            else if (namedArg.Key == "Condition" && namedArg.Value.Value is string c)
                condition = c;
            else if (namedArg.Key == "IncludeLazy" && namedArg.Value.Value is bool il)
                includeLazy = il;
            else if (namedArg.Key == "Module" && namedArg.Value.Value is string m)
                module = m;
        }

        // ── Open generic path ─────────────────────────────────────────────────
        if (classSymbol.IsGenericType)
        {
            var implName = GetOpenGenericName(classSymbol);
            var serviceTypes = new List<string>();

            if (explicitServiceType is INamedTypeSymbol namedExplicit && namedExplicit.IsGenericType)
            {
                serviceTypes.Add(GetOpenGenericName(namedExplicit));
            }
            else if (explicitServiceType is null)
            {
                foreach (var iface in classSymbol.AllInterfaces)
                {
                    var ns = iface.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                    if (IsSystemNamespace(ns)) continue;
                    if (!iface.IsGenericType) continue;
                    var compatible = true;
                    foreach (var ta in iface.TypeArguments)
                        if (ta is not ITypeParameterSymbol) { compatible = false; break; }
                    if (compatible)
                        serviceTypes.Add(GetOpenGenericName(iface));
                }
                if (serviceTypes.Count == 0)
                    serviceTypes.Add(implName);
            }

            return new RegistrationInfo(implName, serviceTypes.ToImmutableArray(), lifetime, keyExpression, true, duplicateStrategy, includeSelf, profile, isScanned: false, condition, includeLazy, module);
        }

        // ── Closed type path ──────────────────────────────────────────────────
        var closedServiceTypes = new List<string>();

        if (explicitServiceType is not null)
        {
            closedServiceTypes.Add(ToFullyQualified(explicitServiceType));
        }
        else
        {
            foreach (var iface in classSymbol.AllInterfaces)
            {
                var ns = iface.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                if (IsSystemNamespace(ns)) continue;
                closedServiceTypes.Add(ToFullyQualified(iface));
            }
            if (closedServiceTypes.Count == 0)
                closedServiceTypes.Add(ToFullyQualified(classSymbol));
        }

        return new RegistrationInfo(
            ToFullyQualified(classSymbol),
            closedServiceTypes.ToImmutableArray(),
            lifetime,
            keyExpression,
            false,
            duplicateStrategy,
            includeSelf,
            profile,
            isScanned: false,
            condition,
            includeLazy,
            module);
    }

    // ── Key expression builder ─────────────────────────────────────────────────

    /// <summary>
    /// Converts a <see cref="TypedConstant"/> Key value into the expression to emit verbatim in the generated code.
    /// Strings become quoted literals; enum values become their fully-qualified member access.
    /// </summary>
    private static string? BuildKeyExpression(TypedConstant tc)
    {
        if (tc.IsNull || tc.Value is null) return null;

        switch (tc.Kind)
        {
            case TypedConstantKind.Primitive when tc.Value is string s:
                // Escape backslashes and double-quotes, then wrap in double-quotes
                return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

            case TypedConstantKind.Enum when tc.Type is INamedTypeSymbol enumType:
            {
                var enumFqn = ToFullyQualified(enumType);
                // Find the enum member whose constant value matches
                foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
                {
                    if (member.IsConst && member.HasConstantValue && Equals(member.ConstantValue, tc.Value))
                        return $"{enumFqn}.{member.Name}";
                }
                // Fallback: cast the underlying integer
                return $"({enumFqn}){tc.Value}";
            }

            default:
                // Integral / bool primitives — emit as-is
                return tc.Value.ToString();
        }
    }

    // ── Code generation ────────────────────────────────────────────────────────

    private static string GenerateSource(
        ImmutableArray<RegistrationInfo> registrations,
        ImmutableArray<DecoratorInfo> decorators,
        ImmutableArray<string> hostedServices,
        ImmutableArray<FactoryInfo> factories,
        ImmutableArray<OptionsInfo> options,
        ImmutableArray<HttpClientInfo> httpClients,
        ImmutableArray<ValidatorInfo> validators,
        ImmutableArray<InterceptorInfo> interceptors,
        string methodName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by AutoWire/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
        sb.AppendLine();
        sb.AppendLine("namespace AutoWire;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Auto-generated service registration. Source-generated by AutoWire — do not edit manually.</summary>");
        sb.AppendLine("public static partial class ServiceCollectionExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Registers all services decorated with <see cref=\"ScopedAttribute\"/>,");
        sb.AppendLine("    /// <see cref=\"SingletonAttribute\"/>, <see cref=\"TransientAttribute\"/>,");
        sb.AppendLine("    /// <see cref=\"TryScopedAttribute\"/>, <see cref=\"TrySingletonAttribute\"/>,");
        sb.AppendLine("    /// or <see cref=\"TryTransientAttribute\"/>.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    /// <param name=\"services\">The service collection to populate.</param>");
        sb.AppendLine("    /// <param name=\"profile\">When provided, also registers services whose <c>Profile</c> matches this value. Services with no profile are always registered.</param>");
        sb.AppendLine($"    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection {methodName}(");
        sb.AppendLine("        this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services,");
        sb.AppendLine("        string? profile = null)");
        sb.AppendLine("    {");

        // ── No-profile, no-module registrations (always active) ───────────────
        // Ordering: Add(0) first so Skip/Replace see them already registered, then Skip(1), then Replace(2).
        foreach (var reg in registrations
            .Where(static r => r.Profile is null && r.Module is null)
            .OrderBy(static r => (int)r.DuplicateStrategy)
            .ThenBy(static r => r.Lifetime)
            .ThenBy(static r => r.ImplementationType))
        {
            if (reg.Condition is not null) sb.AppendLine($"#if {reg.Condition}");
            foreach (var svc in reg.ServiceTypes)
            {
                EmitLine(sb, reg, svc);
                if (reg.IncludeLazy && !reg.IsOpenGeneric)
                    sb.AppendLine($"        services.AddTransient<global::System.Lazy<{svc}>>(sp => new global::System.Lazy<{svc}>(() => sp.GetRequiredService<{svc}>()));");
            }
            if (reg.IncludeSelf && !reg.ServiceTypes.Contains(reg.ImplementationType))
                EmitLine(sb, reg, reg.ImplementationType);
            if (reg.Condition is not null) sb.AppendLine($"#endif");
        }

        // ── Profile-conditional registrations (excluding module services) ─────
        var profileGroups = new Dictionary<string, List<RegistrationInfo>>(StringComparer.Ordinal);
        foreach (var reg in registrations.Where(static r => r.Profile is not null && r.Module is null))
        {
            if (!profileGroups.TryGetValue(reg.Profile!, out var list))
                profileGroups[reg.Profile!] = list = new List<RegistrationInfo>();
            list.Add(reg);
        }

        foreach (var kvp in profileGroups.OrderBy(static g => g.Key))
        {
            sb.AppendLine();
            sb.AppendLine($"        if (profile == \"{kvp.Key}\")");
            sb.AppendLine("        {");
            foreach (var reg in kvp.Value
                .OrderBy(static r => (int)r.DuplicateStrategy)
                .ThenBy(static r => r.Lifetime)
                .ThenBy(static r => r.ImplementationType))
            {
                if (reg.Condition is not null) sb.AppendLine($"#if {reg.Condition}");
                foreach (var svc in reg.ServiceTypes)
                {
                    EmitLine(sb, reg, svc, "            ");
                    if (reg.IncludeLazy && !reg.IsOpenGeneric)
                        sb.AppendLine($"            services.AddTransient<global::System.Lazy<{svc}>>(sp => new global::System.Lazy<{svc}>(() => sp.GetRequiredService<{svc}>()));");
                }
                if (reg.IncludeSelf && !reg.ServiceTypes.Contains(reg.ImplementationType))
                    EmitLine(sb, reg, reg.ImplementationType, "            ");
                if (reg.Condition is not null) sb.AppendLine($"#endif");
            }
            sb.AppendLine("        }");
        }

        // ── Decorators (always after normal registrations so the inner service is present) ──
        if (!decorators.IsEmpty)
        {
            // Build compile-time map: service type → known concrete implementation type
            var serviceToConcreteMap = new Dictionary<string, string>();
            foreach (var reg in registrations.Where(static r => r.KeyExpression is null && !r.IsOpenGeneric))
                foreach (var svc in reg.ServiceTypes)
                    if (svc != reg.ImplementationType)
                        serviceToConcreteMap[svc] = reg.ImplementationType;

            // Group by (Lifetime, ServiceType) so multiple decorators targeting the same service
            // are emitted as a single chain sorted by Order (lowest = innermost).
            var groups = new Dictionary<string, List<DecoratorInfo>>(StringComparer.Ordinal);
            foreach (var dec in decorators)
            {
                var key = dec.Lifetime + "::" + dec.ServiceType;
                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = new List<DecoratorInfo>();
                list.Add(dec);
            }

            var idx = 0;
            foreach (var kvp in groups.OrderBy(static g => g.Key))
            {
                sb.AppendLine();
                var chain = kvp.Value.OrderBy(static d => d.Order).ThenBy(static d => d.DecoratorType).ToList();
                EmitDecoratorChain(sb, chain, serviceToConcreteMap, ref idx);
            }
        }

        // ── Hosted services ────────────────────────────────────────────────────
        if (!hostedServices.IsEmpty)
        {
            sb.AppendLine();
            foreach (var hs in hostedServices.OrderBy(static s => s))
                sb.AppendLine($"        services.AddHostedService<{hs}>();");
        }

        // ── Factory registrations ──────────────────────────────────────────────
        if (!factories.IsEmpty)
        {
            sb.AppendLine();
            foreach (var f in factories.OrderBy(static f => f.FactoryType).ThenBy(static f => f.ProductType))
            {
                sb.AppendLine($"        services.Add{f.FactoryLifetime}<{f.FactoryType}>();");
                sb.AppendLine($"        services.Add{f.ProductLifetime}<{f.ProductType}>(sp => sp.GetRequiredService<{f.FactoryType}>().Create());");
            }
        }

        // ── Options registrations ──────────────────────────────────────────────
        if (!options.IsEmpty)
        {
            sb.AppendLine();
            foreach (var opt in options.OrderBy(static o => o.ImplementationType))
            {
                var chain = $"        services.AddOptions<{opt.ImplementationType}>()";
                chain += $"\n            .BindConfiguration(\"{opt.Section}\")";
                if (opt.ValidateDataAnnotations)
                    chain += "\n            .ValidateDataAnnotations()";
                if (opt.ValidateOnStart)
                    chain += "\n            .ValidateOnStart()";
                sb.AppendLine(chain + ";");
            }
        }

        // ── HttpClient registrations ───────────────────────────────────────────
        if (!httpClients.IsEmpty)
        {
            sb.AppendLine();
            foreach (var hc in httpClients.OrderBy(static h => h.ImplementationType))
            {
                string clientChain;
                var hasConfig = hc.BaseAddress is not null || hc.TimeoutSeconds.HasValue || !hc.DefaultHeaders.IsEmpty;
                string configLambda;

                if (hasConfig)
                {
                    var lambdaLines = new System.Collections.Generic.List<string>();
                    if (hc.BaseAddress is not null)
                        lambdaLines.Add($"c.BaseAddress = new global::System.Uri(\"{hc.BaseAddress}\");");
                    if (hc.TimeoutSeconds.HasValue)
                        lambdaLines.Add($"c.Timeout = global::System.TimeSpan.FromSeconds({hc.TimeoutSeconds.Value});");
                    foreach (var header in hc.DefaultHeaders)
                    {
                        var sep = header.IndexOf(':');
                        if (sep <= 0) continue;
                        var key = header.Substring(0, sep).Trim();
                        var val = header.Substring(sep + 1).Trim();
                        lambdaLines.Add($"c.DefaultRequestHeaders.Add(\"{key}\", \"{val}\");");
                    }

                    if (lambdaLines.Count == 1)
                        configLambda = $"static c => {lambdaLines[0].TrimEnd(';')}";
                    else
                    {
                        var body = "static c => { " + string.Join(" ", lambdaLines) + " }";
                        configLambda = body;
                    }
                }
                else
                    configLambda = string.Empty;

                if (hc.Name is null && !hasConfig)
                {
                    clientChain = $"        services.AddHttpClient<{hc.ImplementationType}>()";
                }
                else if (hc.Name is not null && hasConfig)
                {
                    clientChain = $"        services.AddHttpClient(\"{hc.Name}\", {configLambda})\n            .AddTypedClient<{hc.ImplementationType}>()";
                }
                else if (hc.Name is not null)
                {
                    clientChain = $"        services.AddHttpClient(\"{hc.Name}\")\n            .AddTypedClient<{hc.ImplementationType}>()";
                }
                else
                {
                    clientChain = $"        services.AddHttpClient<{hc.ImplementationType}>({configLambda})";
                }

                if (hc.Resilience)
                    clientChain += "\n            .AddStandardResilienceHandler()";

                sb.AppendLine(clientChain + ";");
            }
        }

        if (!validators.IsEmpty)
        {
            sb.AppendLine();
            foreach (var v in validators.OrderBy(static v => v.ValidatorType))
                sb.AppendLine($"        services.AddScoped<global::FluentValidation.IValidator<{v.ValidatedType}>, {v.ValidatorType}>();");
        }

        if (!interceptors.IsEmpty)
        {
            sb.AppendLine();
            foreach (var intc in interceptors.OrderBy(static i => i.InterceptorType))
            {
                sb.AppendLine($"        services.Add{intc.Lifetime}<{intc.ServiceType}>(sp =>");
                sb.AppendLine($"            new {intc.ProxyClassName}(");
                sb.AppendLine($"                sp.GetRequiredService<{intc.InterceptorType}>()));");
                sb.AppendLine($"        services.Add{intc.Lifetime}<{intc.InterceptorType}>();");
            }
        }

        sb.AppendLine("        return services;");
        sb.AppendLine("    }");

        // ── Proxy classes ─────────────────────────────────────────────────────
        foreach (var intc in interceptors)
            EmitProxyClass(sb, intc);

        // ── Module methods ────────────────────────────────────────────────────
        var moduleGroups = new Dictionary<string, List<RegistrationInfo>>(StringComparer.Ordinal);
        foreach (var reg in registrations.Where(static r => r.Module is not null))
        {
            if (!moduleGroups.TryGetValue(reg.Module!, out var list))
                moduleGroups[reg.Module!] = list = new List<RegistrationInfo>();
            list.Add(reg);
        }

        foreach (var kvp in moduleGroups.OrderBy(static g => g.Key))
        {
            var modName = kvp.Key;
            // Sanitise module name for use as a C# identifier
            var safeModName = new string(modName.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            sb.AppendLine();
            sb.AppendLine($"    /// <summary>Registers the <b>{modName}</b> module services. Call this explicitly alongside <c>{methodName}()</c> or instead of it to selectively activate this module.</summary>");
            sb.AppendLine($"    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add{safeModName}Module(");
            sb.AppendLine("        this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
            sb.AppendLine("    {");

            foreach (var reg in kvp.Value
                .OrderBy(static r => (int)r.DuplicateStrategy)
                .ThenBy(static r => r.Lifetime)
                .ThenBy(static r => r.ImplementationType))
            {
                if (reg.Condition is not null) sb.AppendLine($"#if {reg.Condition}");
                foreach (var svc in reg.ServiceTypes)
                {
                    EmitLine(sb, reg, svc);
                    if (reg.IncludeLazy && !reg.IsOpenGeneric)
                        sb.AppendLine($"        services.AddTransient<global::System.Lazy<{svc}>>(sp => new global::System.Lazy<{svc}>(() => sp.GetRequiredService<{svc}>()));");
                }
                if (reg.IncludeSelf && !reg.ServiceTypes.Contains(reg.ImplementationType))
                    EmitLine(sb, reg, reg.ImplementationType);
                if (reg.Condition is not null) sb.AppendLine($"#endif");
            }

            sb.AppendLine("        return services;");
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── Registration summary generation ───────────────────────────────────────

    private static string GenerateSummary(
        ImmutableArray<RegistrationInfo> registrations,
        ImmutableArray<string> hostedServices,
        ImmutableArray<FactoryInfo> factories,
        ImmutableArray<HttpClientInfo> httpClients)
    {
        var all = registrations;
        var scopedCount     = all.Count(static r => r.Lifetime == "Scoped");
        var singletonCount  = all.Count(static r => r.Lifetime == "Singleton");
        var transientCount  = all.Count(static r => r.Lifetime == "Transient");
        var moduleCount     = all.Count(static r => r.Module is not null);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by AutoWire/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace AutoWire;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Compile-time summary of all AutoWire registrations. Useful for startup logging and diagnostics.</summary>");
        sb.AppendLine("internal static class RegistrationSummary");
        sb.AppendLine("{");
        sb.AppendLine($"    /// <summary>Total number of DI registrations emitted by AutoWire (excluding hosted services and factories).</summary>");
        sb.AppendLine($"    public const int TotalCount = {all.Length};");
        sb.AppendLine($"    public const int ScopedCount = {scopedCount};");
        sb.AppendLine($"    public const int SingletonCount = {singletonCount};");
        sb.AppendLine($"    public const int TransientCount = {transientCount};");
        sb.AppendLine($"    public const int HostedServiceCount = {hostedServices.Length};");
        sb.AppendLine($"    public const int FactoryCount = {factories.Length};");
        sb.AppendLine($"    public const int HttpClientCount = {httpClients.Length};");
        sb.AppendLine($"    public const int ModuleServiceCount = {moduleCount};");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Fully-qualified implementation type names for all registered services.</summary>");
        sb.Append("    public static readonly string[] RegisteredImplementations = {");
        var impls = all.Select(static r => r.ImplementationType).Distinct().OrderBy(static x => x).ToList();
        if (impls.Count > 0)
        {
            sb.AppendLine();
            foreach (var impl in impls)
                sb.AppendLine($"        \"{impl.Replace("global::", "")}\",");
            sb.Append("    }");
        }
        else
        {
            sb.Append(" }");
        }
        sb.AppendLine(";");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitDecoratorChain(
        StringBuilder sb,
        List<DecoratorInfo> chain,
        Dictionary<string, string> serviceToConcreteMap,
        ref int idx)
    {
        var serviceType = chain[0].ServiceType;
        var lifetime    = chain[0].Lifetime;

        sb.AppendLine($"        // [Decorate{lifetime}] chain for {serviceType} ({chain.Count} decorator(s), innermost first by Order)");

        if (serviceToConcreteMap.TryGetValue(serviceType, out var originalConcrete))
        {
            // Compile-time known inner type — emit the full chain type-safely.
            // chain[0] = innermost (lowest Order), chain[^1] = outermost (highest Order) → registered as IService.
            sb.AppendLine($"        services.RemoveAll<{serviceType}>();");
            sb.AppendLine($"        services.Add{lifetime}<{originalConcrete}>();");

            var previousType = originalConcrete;
            for (var i = 0; i < chain.Count - 1; i++)
            {
                var dec = chain[i];
                sb.AppendLine($"        services.Add{lifetime}<{dec.DecoratorType}>(sp =>");
                sb.AppendLine($"            ({dec.DecoratorType})global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(");
                sb.AppendLine($"                sp, typeof({dec.DecoratorType}), sp.GetRequiredService<{previousType}>()));");
                previousType = dec.DecoratorType;
            }

            var last = chain[chain.Count - 1];
            sb.AppendLine($"        services.Add{lifetime}<{serviceType}>(sp =>");
            sb.AppendLine($"            ({serviceType})global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(");
            sb.AppendLine($"                sp, typeof({last.DecoratorType}), sp.GetRequiredService<{previousType}>()));");
        }
        else
        {
            // Runtime fallback — apply decorators sequentially.
            // Each EmitDecorator call will self-register the prior concrete type, enabling the next in the chain to find it.
            foreach (var dec in chain)
            {
                EmitDecorator(sb, dec, serviceToConcreteMap, idx++);
                // After the first decorator, the concrete type it self-registered becomes the new inner for the next.
                serviceToConcreteMap[dec.ServiceType] = dec.DecoratorType;
            }
        }
    }

    private static void EmitDecorator(
        StringBuilder sb,
        DecoratorInfo dec,
        Dictionary<string, string> serviceToConcreteMap,
        int index)
    {
        sb.AppendLine($"        // [Decorate{dec.Lifetime}] {dec.DecoratorType} wraps {dec.ServiceType}");

        if (serviceToConcreteMap.TryGetValue(dec.ServiceType, out var concreteType))
        {
            // Compile-time known inner type — fully type-safe generated code.
            sb.AppendLine($"        services.RemoveAll<{dec.ServiceType}>();");
            sb.AppendLine($"        services.Add{dec.Lifetime}<{concreteType}>();");
            sb.AppendLine($"        services.Add{dec.Lifetime}<{dec.ServiceType}>(sp =>");
            sb.AppendLine($"            ({dec.ServiceType})global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(");
            sb.AppendLine($"                sp, typeof({dec.DecoratorType}), sp.GetRequiredService<{concreteType}>()));");
        }
        else
        {
            // Runtime fallback — inner type not registered via AutoWire (e.g. manually registered).
            sb.AppendLine($"        {{");
            sb.AppendLine($"            global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor? __aw_dec{index} = null;");
            sb.AppendLine($"            for (var __i = services.Count - 1; __i >= 0; __i--)");
            sb.AppendLine($"                if (services[__i].ServiceType == typeof({dec.ServiceType}) && services[__i].ImplementationType != null)");
            sb.AppendLine($"                {{ __aw_dec{index} = services[__i]; break; }}");
            sb.AppendLine($"            if (__aw_dec{index} != null)");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                var __aw_inner{index} = __aw_dec{index}.ImplementationType!;");
            sb.AppendLine($"                services.Remove(__aw_dec{index});");
            sb.AppendLine($"                services.Add(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Describe(");
            sb.AppendLine($"                    __aw_inner{index}, __aw_inner{index}, __aw_dec{index}.Lifetime));");
            sb.AppendLine($"                services.Add{dec.Lifetime}<{dec.ServiceType}>(sp =>");
            sb.AppendLine($"                    ({dec.ServiceType})global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(");
            sb.AppendLine($"                        sp, typeof({dec.DecoratorType}), sp.GetRequiredService(__aw_inner{index})));");
            sb.AppendLine($"            }}");
            sb.AppendLine($"            else");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                services.Add{dec.Lifetime}<{dec.ServiceType}, {dec.DecoratorType}>();");
            sb.AppendLine($"            }}");
            sb.AppendLine($"        }}");
        }
    }

    // ── Proxy class emitter ───────────────────────────────────────────────────

    private static void EmitProxyClass(StringBuilder sb, InterceptorInfo intc)
    {
        sb.AppendLine();
        sb.AppendLine($"file sealed class {intc.ProxyClassName} : {intc.ServiceType}");
        sb.AppendLine("{");
        sb.AppendLine($"    private readonly {intc.InterceptorType} _interceptor;");
        sb.AppendLine($"    public {intc.ProxyClassName}({intc.InterceptorType} interceptor)");
        sb.AppendLine("    {");
        sb.AppendLine("        _interceptor = interceptor;");
        sb.AppendLine("    }");

        foreach (var method in intc.Methods)
        {
            var paramDecl = string.Join(", ", method.Parameters.Select(static p => $"{p.Type} {p.Name}"));
            var paramNames = string.Join(", ", method.Parameters.Select(static p => p.Name));
            var retType = method.IsVoid ? "void" : method.ReturnType;
            sb.AppendLine();
            sb.AppendLine($"    public {retType} {method.Name}({paramDecl})");
            sb.AppendLine("    {");
            sb.AppendLine($"        var __inv = new global::AutoWire.AutoWireInvocation(\"{method.Name}\", new object?[] {{ {paramNames} }});");
            if (method.IsVoid)
            {
                sb.AppendLine("        _interceptor.Intercept(__inv);");
            }
            else if (method.IsTask)
            {
                sb.AppendLine("        _interceptor.Intercept(__inv);");
                sb.AppendLine($"        return __inv.Result is System.Threading.Tasks.Task t ? t : {method.ReturnType}.CompletedTask;");
            }
            else if (method.IsTaskOfT && method.TaskResultType is not null)
            {
                sb.AppendLine("        _interceptor.Intercept(__inv);");
                sb.AppendLine($"        return __inv.Result is System.Threading.Tasks.Task<{method.TaskResultType}> t ? t : System.Threading.Tasks.Task.FromResult(({method.TaskResultType})__inv.Result!);");
            }
            else
            {
                sb.AppendLine("        _interceptor.Intercept(__inv);");
                sb.AppendLine($"        return ({method.ReturnType})__inv.Result!;");
            }
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"file sealed class AutoWireInvocation : global::AutoWire.IAutoWireInvocation");
        sb.AppendLine("{");
        sb.AppendLine("    public string MethodName { get; }");
        sb.AppendLine("    public object?[] Arguments { get; }");
        sb.AppendLine("    public object? Result { get; set; }");
        sb.AppendLine("    public AutoWireInvocation(string methodName, object?[] arguments)");
        sb.AppendLine("    {");
        sb.AppendLine("        MethodName = methodName;");
        sb.AppendLine("        Arguments = arguments;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
    }

    private static void EmitLine(StringBuilder sb, RegistrationInfo reg, string svc, string indent = "        ")
    {
        var isSelf = svc == reg.ImplementationType;
        var key = reg.KeyExpression; // ready-to-emit expression (no extra quoting needed)

        switch (reg.DuplicateStrategy)
        {
            case DuplicateStrategy.Replace:
                // Remove all existing registrations for this service type, then add.
                if (reg.IsOpenGeneric)
                {
                    sb.AppendLine($"{indent}services.RemoveAll(typeof({svc}));");
                    sb.AppendLine(isSelf
                        ? $"{indent}services.Add{reg.Lifetime}(typeof({svc}));"
                        : $"{indent}services.Add{reg.Lifetime}(typeof({svc}), typeof({reg.ImplementationType}));");
                }
                else
                {
                    sb.AppendLine($"{indent}services.RemoveAll<{svc}>();");
                    sb.AppendLine(key is not null
                        ? (isSelf
                            ? $"{indent}services.AddKeyed{reg.Lifetime}<{svc}>({key});"
                            : $"{indent}services.AddKeyed{reg.Lifetime}<{svc}, {reg.ImplementationType}>({key});")
                        : (isSelf
                            ? $"{indent}services.Add{reg.Lifetime}<{svc}>();"
                            : $"{indent}services.Add{reg.Lifetime}<{svc}, {reg.ImplementationType}>();"));
                }
                break;

            case DuplicateStrategy.Skip:
                // TryAdd — skip if service type already registered.
                if (reg.IsOpenGeneric)
                {
                    sb.AppendLine(key is not null
                        ? (isSelf
                            ? $"{indent}services.TryAddKeyed{reg.Lifetime}(typeof({svc}), {key});"
                            : $"{indent}services.TryAddKeyed{reg.Lifetime}(typeof({svc}), {key}, typeof({reg.ImplementationType}));")
                        : (isSelf
                            ? $"{indent}services.TryAdd{reg.Lifetime}(typeof({svc}));"
                            : $"{indent}services.TryAdd{reg.Lifetime}(typeof({svc}), typeof({reg.ImplementationType}));"));
                }
                else
                {
                    sb.AppendLine(key is not null
                        ? (isSelf
                            ? $"{indent}services.TryAddKeyed{reg.Lifetime}<{svc}>({key});"
                            : $"{indent}services.TryAddKeyed{reg.Lifetime}<{svc}, {reg.ImplementationType}>({key});")
                        : (isSelf
                            ? $"{indent}services.TryAdd{reg.Lifetime}<{svc}>();"
                            : $"{indent}services.TryAdd{reg.Lifetime}<{svc}, {reg.ImplementationType}>();"));
                }
                break;

            default: // DuplicateStrategy.Add
                if (reg.IsOpenGeneric)
                {
                    sb.AppendLine(key is not null
                        ? (isSelf
                            ? $"{indent}services.AddKeyed{reg.Lifetime}(typeof({svc}), {key});"
                            : $"{indent}services.AddKeyed{reg.Lifetime}(typeof({svc}), {key}, typeof({reg.ImplementationType}));")
                        : (isSelf
                            ? $"{indent}services.Add{reg.Lifetime}(typeof({svc}));"
                            : $"{indent}services.Add{reg.Lifetime}(typeof({svc}), typeof({reg.ImplementationType}));"));
                }
                else if (key is not null)
                {
                    sb.AppendLine(isSelf
                        ? $"{indent}services.AddKeyed{reg.Lifetime}<{svc}>({key});"
                        : $"{indent}services.AddKeyed{reg.Lifetime}<{svc}, {reg.ImplementationType}>({key});");
                }
                else
                {
                    sb.AppendLine(isSelf
                        ? $"{indent}services.Add{reg.Lifetime}<{svc}>();"
                        : $"{indent}services.Add{reg.Lifetime}<{svc}, {reg.ImplementationType}>();");
                }
                break;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static bool IsSystemNamespace(string ns) =>
        ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal);

    private static string ToFullyQualified(ITypeSymbol symbol) =>
        symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>
    /// Returns the open-generic typeof() string, e.g. <c>global::MyApp.IRepository&lt;&gt;</c>.
    /// </summary>
    private static string GetOpenGenericName(INamedTypeSymbol symbol)
    {
        var ns = symbol.ContainingNamespace is { IsGlobalNamespace: false } nsSym
            ? nsSym.ToDisplayString()
            : null;
        var commas = new string(',', symbol.Arity - 1);
        var prefix = ns is not null ? $"global::{ns}." : "global::";
        return $"{prefix}{symbol.Name}<{commas}>";
    }
}
