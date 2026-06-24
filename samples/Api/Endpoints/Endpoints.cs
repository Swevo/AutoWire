using AutoWire.Sample.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AutoWire.Sample.Api.Endpoints;

public static class WeatherEndpoints
{
    public static void MapWeatherEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/weather").WithTags("Weather");

        group.MapGet("/forecast/{days:int}", (
            [FromRoute] int days,
            IWeatherService weather,
            IRequestCounter counter) =>
        {
            counter.Increment();
            return Results.Ok(weather.GetForecast(days));
        })
        .WithName("GetWeatherForecast")
        .WithSummary("Get a weather forecast (decorated with logging)");
    }
}

public static class CacheEndpoints
{
    public static void MapCacheEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/cache").WithTags("Cache");

        group.MapGet("/{key}", (string key, ICacheService cache) =>
        {
            var value = cache.Get(key);
            return value is null ? Results.NotFound() : Results.Ok(new { key, value });
        })
        .WithSummary("Get a cached value by key");

        group.MapPost("/", ([FromBody] CacheEntry entry, ICacheService cache) =>
        {
            cache.Set(entry.Key, entry.Value);
            return Results.Created($"/cache/{entry.Key}", entry);
        })
        .WithSummary("Store a value in the cache");
    }
}

public record CacheEntry(string Key, string Value);

public static class GreetingEndpoints
{
    public static void MapGreetingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/greet").WithTags("Greetings");

        group.MapGet("/formal/{name}", (string name,
            [FromKeyedServices("formal")] IGreeter greeter) =>
            Results.Ok(greeter.Greet(name)))
        .WithSummary("Formal greeting (keyed service: 'formal')");

        group.MapGet("/casual/{name}", (string name,
            [FromKeyedServices("casual")] IGreeter greeter) =>
            Results.Ok(greeter.Greet(name)))
        .WithSummary("Casual greeting (keyed service: 'casual')");
    }
}

public static class StatsEndpoints
{
    public static void MapStatsEndpoints(this WebApplication app)
    {
        app.MapGet("/stats", (IRequestCounter counter) =>
            Results.Ok(new { totalRequests = counter.Total }))
        .WithTags("Stats")
        .WithSummary("Total requests handled (Singleton counter)");
    }
}
