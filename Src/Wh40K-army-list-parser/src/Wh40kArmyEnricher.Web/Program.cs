using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Wh40kArmyEnricher.Web.Helpers;
using Wh40kArmyEnricher.Contracts;
using Wh40kArmyEnricher.Core;
using Wh40kArmyEnricher.Core.BsData;
using Wh40kArmyEnricher.Core.Parser;
using Wh40kArmyEnricher.Web.Models;
using Wh40kArmyEnricher.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Core services
// ---------------------------------------------------------------------------

builder.Services.AddRazorPages();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(opts =>
{
    opts.IdleTimeout = TimeSpan.FromHours(2);
    opts.Cookie.HttpOnly = true;
    opts.Cookie.IsEssential = true;
});

// ---------------------------------------------------------------------------
// BSData / Enricher pipeline
// ---------------------------------------------------------------------------

builder.Services.AddHttpClient("bsdata", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "wh40k-army-enricher/1.0");
    client.Timeout = TimeSpan.FromMinutes(5);
});

string cacheDir = builder.Configuration["Enricher:CachePath"]
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".wh40k-enricher", "cache");

builder.Services.AddSingleton<CatalogueParser>();
builder.Services.AddSingleton<ICatalogueFetcher>(sp =>
    new CatalogueFetcher(sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<CatalogueFetcher>>(),
        cacheDir));
builder.Services.AddSingleton(sp =>
    new CatalogueStore(
        sp.GetRequiredService<ICatalogueFetcher>(),
        sp.GetRequiredService<CatalogueParser>(),
        sp.GetRequiredService<ILogger<CatalogueStore>>(),
        forceRefresh: false));
builder.Services.AddSingleton<NameResolver>();
builder.Services.AddSingleton<Enricher>();
builder.Services.AddSingleton<ArmyListParser>();
builder.Services.AddSingleton<EnrichmentService>();
builder.Services.AddSingleton<SimulationAdapter>();

// ---------------------------------------------------------------------------
// App pipeline
// ---------------------------------------------------------------------------

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();

app.MapRazorPages();

// ---------------------------------------------------------------------------
// Simulate API endpoint
// ---------------------------------------------------------------------------

app.MapPost("/api/simulate", (
    [FromBody] SimulationRequest request,
    HttpContext ctx,
    SimulationAdapter adapter) =>
{
    var attackerJson = ctx.Session.GetString("attacker_army");
    var defenderJson = ctx.Session.GetString("defender_army");

    if (attackerJson == null || defenderJson == null)
        return Results.Json(new SimulationResponse { Success = false, Error = "Session expired. Please re-upload your army lists." });

    var attackerArmy = JsonSerializer.Deserialize<List<UnitProfile>>(attackerJson, SessionJson.Options);
    var defenderArmy = JsonSerializer.Deserialize<List<UnitProfile>>(defenderJson, SessionJson.Options);

    if (attackerArmy == null || defenderArmy == null)
        return Results.Json(new SimulationResponse { Success = false, Error = "Failed to read armies from session." });

    var validAttackerIndices = request.AttackerUnitIndices
        .Where(i => i >= 0 && i < attackerArmy.Count)
        .ToList();
    if (validAttackerIndices.Count == 0)
        return Results.Json(new SimulationResponse { Success = false, Error = "No valid attacker units selected." });

    if (request.DefenderUnitIndex < 0 || request.DefenderUnitIndex >= defenderArmy.Count)
        return Results.Json(new SimulationResponse { Success = false, Error = "Invalid defender unit index." });

    var attackerUnits = validAttackerIndices.Select(i => attackerArmy[i]).ToList();
    var defenderUnit  = defenderArmy[request.DefenderUnitIndex];

    var response = adapter.Run(attackerUnits, defenderUnit, request);
    return Results.Json(response);
});

// ---------------------------------------------------------------------------
// Selective catalogue refresh endpoint
// ---------------------------------------------------------------------------

app.MapPost("/api/refresh-catalogues", async (
    HttpContext ctx,
    CatalogueStore store) =>
{
    var idsJson = ctx.Session.GetString("used_catalogue_ids");
    if (idsJson == null)
        return Results.Json(new { success = false, error = "No catalogues in session." });

    var ids = System.Text.Json.JsonSerializer.Deserialize<List<string>>(idsJson) ?? [];
    if (ids.Count == 0)
        return Results.Json(new { success = false, error = "No catalogue IDs to refresh." });

    var refreshed = await store.RefreshCataloguesAsync(ids, ctx.RequestAborted);
    return Results.Json(new { success = true, refreshed });
});

// ---------------------------------------------------------------------------
// Initialise catalogue store before serving requests
// ---------------------------------------------------------------------------

var store = app.Services.GetRequiredService<CatalogueStore>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Initialising BSData catalogue store (this may take a moment on first run)...");
await store.InitialiseAsync();
logger.LogInformation("Catalogue store ready.");

await app.RunAsync();
