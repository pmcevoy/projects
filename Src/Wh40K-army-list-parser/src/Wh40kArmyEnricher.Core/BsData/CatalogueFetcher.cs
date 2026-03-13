using Microsoft.Extensions.Logging;

namespace Wh40kArmyEnricher.Core.BsData;

/// <summary>
/// Downloads BSData .cat files from the wh40k-10e GitHub repository and caches
/// them locally under ~/.wh40k-enricher/cache/.
/// Cached files are reused on every run; pass forceRefresh=true (--refresh-cache) to re-download.
/// </summary>
public class CatalogueFetcher : ICatalogueFetcher
{
    private const string RawBaseUrl = "https://raw.githubusercontent.com/BSData/wh40k-10e/main/";

    private readonly HttpClient _http;
    private readonly string _cacheDir;
    private readonly ILogger<CatalogueFetcher> _logger;

    public CatalogueFetcher(IHttpClientFactory httpClientFactory, ILogger<CatalogueFetcher> logger,
        string? cacheDir = null)
    {
        _http = httpClientFactory.CreateClient("bsdata");
        _logger = logger;
        _cacheDir = cacheDir
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".wh40k-enricher", "cache");
        Directory.CreateDirectory(_cacheDir);
    }

    public async Task<Stream> FetchAsync(string filename, bool forceRefresh, CancellationToken ct = default)
    {
        var localPath = Path.Combine(_cacheDir, filename);

        if (!forceRefresh && File.Exists(localPath))
        {
            _logger.LogDebug("Cache hit for {Filename}", filename);
            return File.OpenRead(localPath);
        }

        _logger.LogInformation("Downloading {Filename}", filename);
        await DownloadAsync(filename, localPath, ct);
        return File.OpenRead(localPath);
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    public async Task<string> FetchRawAsync(string url, CancellationToken ct = default)
    {
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task DownloadAsync(string filename, string localPath, CancellationToken ct)
    {
        var url = RawBaseUrl + Uri.EscapeDataString(filename);
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

        await using var fs = File.Create(localPath);
        await response.Content.CopyToAsync(fs, ct);
    }
}
