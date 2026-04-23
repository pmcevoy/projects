using System.Text.Json;
using Wh40kArmyEnricher.Core.Catalogue;
using Wh40kArmyEnricher.Core.Contracts;
using Wh40kArmyEnricher.Core.Enrichment;
using Wh40kArmyEnricher.Core.Parsing;
using Wh40kArmyEnricher.Core.Simulation;
using Wh40kArmyEnricher.Web.Helpers;
using Wh40kArmyEnricher.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Named HTTP client — GitHub API requires a User-Agent header
builder.Services.AddHttpClient("github", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Wh40kArmyEnricher/1.0");
    client.Timeout = TimeSpan.FromMinutes(5);
});

// Resolve cache path, expanding ~ to home directory for local dev
var rawCachePath = builder.Configuration["Enricher:CachePath"] ?? "~/.wh40k-enricher/cache/";
var cachePath = rawCachePath.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

builder.Services.AddSingleton<ICatalogueFetcher>(sp =>
    new CatalogueFetcher(
        sp.GetRequiredService<IHttpClientFactory>(),
        cachePath,
        sp.GetRequiredService<ILogger<CatalogueFetcher>>()));

builder.Services.AddSingleton<CatalogueStore>();
builder.Services.AddSingleton<ArmyListParser>();
builder.Services.AddSingleton<Enricher>();

// Initialise catalogue store on application startup
builder.Services.AddHostedService<CatalogueStartupService>();

builder.Services.AddRazorPages();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(4);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.MapRazorPages();

// Run Monte Carlo simulation
app.MapPost("/api/simulate", async (HttpContext ctx, SimulationRequest simReq) =>
{
    await ctx.Session.LoadAsync();
    var attackerJson = ctx.Session.GetString("attacker_army");
    var defenderJson = ctx.Session.GetString("defender_army");
    if (attackerJson is null || defenderJson is null)
        return Results.BadRequest(new { error = "No armies in session — submit armies first" });

    var attackers = JsonSerializer.Deserialize<List<UnitProfile>>(attackerJson, SessionJson.Options)!;
    var defenders = JsonSerializer.Deserialize<List<UnitProfile>>(defenderJson, SessionJson.Options)!;

    var defender = defenders.FirstOrDefault(u =>
        string.Equals(u.Name, simReq.DefenderName, StringComparison.OrdinalIgnoreCase));
    if (defender is null)
        return Results.BadRequest(new { error = $"Defender '{simReq.DefenderName}' not found in session" });

    // Validate phase constraint: all weapons must be the same type
    var weaponTypes = simReq.WeaponSelections
        .Where(w => !string.IsNullOrEmpty(w.WeaponType))
        .Select(w => w.WeaponType)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    if (weaponTypes.Count > 1)
        return Results.BadRequest(new { error = "Cannot mix ranged and melee weapons in one simulation run" });

    var adapter = new SimulationAdapter();
    var response = adapter.Adapt(simReq, attackers, defender);
    return Results.Json(response, SessionJson.CamelCaseOptions);
});

// Re-download catalogues used in the current session
app.MapPost("/api/refresh-catalogues", async (HttpContext ctx, CatalogueStore store) =>
{
    await ctx.Session.LoadAsync();
    var json = ctx.Session.GetString("used_catalogue_ids");
    IEnumerable<string> ids = json is not null
        ? JsonSerializer.Deserialize<List<string>>(json) ?? []
        : [];
    await store.RefreshCataloguesAsync(ids);
    return Results.Ok(new { refreshed = true });
});

app.Run();
