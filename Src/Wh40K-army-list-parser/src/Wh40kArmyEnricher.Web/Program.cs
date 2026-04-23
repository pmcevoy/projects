using System.Text.Json;
using Wh40kArmyEnricher.Core.Catalogue;
using Wh40kArmyEnricher.Core.Enrichment;
using Wh40kArmyEnricher.Core.Parsing;
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
