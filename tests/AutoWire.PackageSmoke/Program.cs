using AutoWire;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddAutoWireServices();
using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
using var scope = provider.CreateScope();

var failures = new List<string>();
void Check(bool ok, string what) { if (!ok) failures.Add(what); }

var greeter = scope.ServiceProvider.GetRequiredService<IGreeter>();
Check(greeter.Greet("AutoWire") == "[Hello, AutoWire!]", "decorated scoped service");
Check(scope.ServiceProvider.GetRequiredService<Greeter>() is not null, "decorator inner type self-registered");
Check(provider.GetRequiredKeyedService<IClock>("utc") is UtcClock, "keyed singleton");
Check(ReferenceEquals(provider.GetRequiredService<Counter>(), provider.GetRequiredService<Counter>()), "singleton without interface");
Check(RegistrationSummary.TotalCount > 0, "RegistrationSummary generated");

if (failures.Count > 0)
{
    Console.Error.WriteLine("AutoWire package smoke test FAILED: " + string.Join(", ", failures));
    return 1;
}

Console.WriteLine($"AutoWire package smoke test passed ({RegistrationSummary.TotalCount} registrations).");
return 0;

public interface IGreeter { string Greet(string name); }

[Scoped]
public sealed class Greeter : IGreeter
{
    public string Greet(string name) => $"Hello, {name}!";
}

[DecorateScoped(typeof(IGreeter))]
public sealed class BracketGreeter(IGreeter inner) : IGreeter
{
    public string Greet(string name) => $"[{inner.Greet(name)}]";
}

public interface IClock { DateTimeOffset Now { get; } }

[Singleton(Key = "utc")]
public sealed class UtcClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
}

[Singleton]
public sealed class Counter
{
    public int Value { get; set; }
}
