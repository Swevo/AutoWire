using AutoWire;

namespace AutoWire.Sample.Api.Services;

// ── Weather feature ──────────────────────────────────────────────────────────

public interface IWeatherService
{
    IEnumerable<WeatherForecast> GetForecast(int days);
}

public record WeatherForecast(DateOnly Date, int TemperatureC, string Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

/// <summary>
/// Picked up automatically by [AutoWireScan] — no [Scoped] attribute needed.
/// AutoWire registers this as: services.AddScoped&lt;IWeatherService, WeatherService&gt;()
/// </summary>
public class WeatherService : IWeatherService
{
    private static readonly string[] Summaries =
    [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild",
        "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    ];

    public IEnumerable<WeatherForecast> GetForecast(int days) =>
        Enumerable.Range(1, days).Select(index => new WeatherForecast(
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            Summaries[Random.Shared.Next(Summaries.Length)]));
}

// ── Logging decorator ────────────────────────────────────────────────────────

/// <summary>
/// Decorator: wraps IWeatherService and logs every call.
/// Order = 1 → innermost (closest to WeatherService).
/// AutoWire generates compile-time wiring — no Scrutor required.
/// </summary>
[DecorateScoped(typeof(IWeatherService), Order = 1)]
public class LoggingWeatherDecorator(IWeatherService inner, ILogger<LoggingWeatherDecorator> logger)
    : IWeatherService
{
    public IEnumerable<WeatherForecast> GetForecast(int days)
    {
        logger.LogInformation("Fetching {Days}-day forecast", days);
        var result = inner.GetForecast(days).ToList();
        logger.LogInformation("Returned {Count} forecasts", result.Count);
        return result;
    }
}

// ── Cache feature ────────────────────────────────────────────────────────────

public interface ICacheService
{
    string? Get(string key);
    void Set(string key, string value);
}

/// <summary>
/// Default (no-profile) in-memory cache — always registered.
/// Picked up by [AutoWireScan].
/// </summary>
public class MemoryCacheService : ICacheService
{
    private readonly Dictionary<string, string> _store = new();
    public string? Get(string key) => _store.GetValueOrDefault(key);
    public void Set(string key, string value) => _store[key] = value;
}

/// <summary>
/// "Production" cache — registered only when profile == "production".
/// Simulates a Redis-backed implementation.
/// </summary>
[Scoped(typeof(ICacheService), Profile = "production")]
public class RedisCacheService : ICacheService
{
    public string? Get(string key) => $"[redis:{key}]";
    public void Set(string key, string value) { /* write to Redis */ }
}

// ── Greeting feature (keyed services) ────────────────────────────────────────

public interface IGreeter
{
    string Greet(string name);
}

/// <summary>Formal greeter — resolved with key "formal".</summary>
[Scoped(typeof(IGreeter), Key = "formal")]
public class FormalGreeter : IGreeter
{
    public string Greet(string name) => $"Good day, {name}.";
}

/// <summary>Casual greeter — resolved with key "casual".</summary>
[Scoped(typeof(IGreeter), Key = "casual")]
public class CasualGreeter : IGreeter
{
    public string Greet(string name) => $"Hey {name}! 👋";
}

// ── Stats feature (Singleton) ─────────────────────────────────────────────────

public interface IRequestCounter
{
    int Increment();
    int Total { get; }
}

/// <summary>Thread-safe request counter — Singleton lifetime, one instance for the app.</summary>
[Singleton]
public class RequestCounter : IRequestCounter
{
    private int _count;
    public int Increment() => Interlocked.Increment(ref _count);
    public int Total => _count;
}
