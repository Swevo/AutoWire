// AutoWire Sample — Web API
// ──────────────────────────────────────────────────────────────────────────────
// This file demonstrates every major AutoWire feature.
// AutoWire generates AddAutoWireServices() at compile time — no reflection,
// no assembly scanning, zero startup cost.
//
// Generated file lives at:
//   obj/Debug/net9.0/generated/AutoWire/AutoWire.AutoWireGenerator/
//     AutoWireServiceCollectionExtensions.g.cs
// ──────────────────────────────────────────────────────────────────────────────

using AutoWire.Sample.Api.Endpoints;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── Profile from config ───────────────────────────────────────────────────────
// Set AUTOWIRE_PROFILE=production in environment or appsettings to activate
// profile-specific registrations (e.g. RedisCacheService instead of MemoryCacheService).
var profile = builder.Configuration["AutoWire:Profile"];

// ── ONE LINE: AutoWire registers everything ───────────────────────────────────
// Replaces dozens of services.AddScoped<IFoo, Foo>() calls.
// Includes: [AutoWireScan] services, [Singleton] RequestCounter,
//           keyed [Scoped] greeters, [DecorateScoped] logging decorator,
//           profile-based RedisCacheService (when profile == "production")
builder.Services.AddAutoWireServices(profile: profile);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Scalar UI: http://localhost:{port}/scalar/v1
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

// ── Map all endpoint groups ───────────────────────────────────────────────────
app.MapWeatherEndpoints();   // GET /weather/forecast/{days}
app.MapCacheEndpoints();     // GET|POST /cache
app.MapGreetingEndpoints();  // GET /greet/formal/{name}, /greet/casual/{name}
app.MapStatsEndpoints();     // GET /stats

app.Run();
