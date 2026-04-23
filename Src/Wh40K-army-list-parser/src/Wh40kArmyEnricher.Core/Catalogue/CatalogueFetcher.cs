using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Wh40kArmyEnricher.Core.Catalogue;

public sealed class CatalogueFetcher : ICatalogueFetcher
{
    private const string GithubApiContents = "https://api.github.com/repos/BSData/wh40k-10e/contents/";
    private const string GithubRawBase = "https://raw.githubusercontent.com/BSData/wh40k-10e/main/";
    private const string CatalogueListFile = "catalogue-list.json";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _cachePath;
    private readonly ILogger<CatalogueFetcher> _logger;

    public CatalogueFetcher(IHttpClientFactory httpClientFactory, string cachePath, ILogger<CatalogueFetcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cachePath = cachePath;
        _logger = logger;
        Directory.CreateDirectory(cachePath);
    }

    public async Task<IReadOnlyList<CatalogueFileInfo>> GetCatalogueListAsync(CancellationToken ct = default)
    {
        var cacheFile = Path.Combine(_cachePath, CatalogueListFile);
        if (File.Exists(cacheFile))
        {
            var cached = await File.ReadAllTextAsync(cacheFile, ct);
            var result = JsonSerializer.Deserialize<List<CatalogueFileInfo>>(cached);
            if (result is { Count: > 0 }) return result;
        }

        _logger.LogInformation("Fetching BSData catalogue list from GitHub");
        var client = _httpClientFactory.CreateClient("github");
        var json = await client.GetStringAsync(GithubApiContents, ct);

        using var doc = JsonDocument.Parse(json);
        var files = new List<CatalogueFileInfo>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString() ?? "";
            if (name.EndsWith(".cat", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".catz", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".gst", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".gstz", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(new CatalogueFileInfo(name));
            }
        }

        await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(files), ct);
        _logger.LogInformation("Found {Count} catalogue files", files.Count);
        return files;
    }

    public async Task<byte[]> FetchRawAsync(string filename, bool forceRefresh = false, CancellationToken ct = default)
    {
        // Use sanitised filename as cache key (replace path separators)
        var cacheKey = filename.Replace('/', '_').Replace('\\', '_');
        var cacheFile = Path.Combine(_cachePath, cacheKey);

        if (!forceRefresh && File.Exists(cacheFile))
            return await File.ReadAllBytesAsync(cacheFile, ct);

        var url = GithubRawBase + Uri.EscapeDataString(filename);
        _logger.LogInformation("Downloading {Filename}", filename);
        var client = _httpClientFactory.CreateClient("github");
        var bytes = await client.GetByteArrayAsync(url, ct);
        await File.WriteAllBytesAsync(cacheFile, bytes, ct);
        return bytes;
    }
}
