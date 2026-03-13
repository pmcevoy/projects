using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Wh40kArmyEnricher.Core.BsData;

/// <summary>
/// Downloads BSData .cat files from the wh40k-10e GitHub repository and caches
/// them locally under ~/.wh40k-enricher/cache/.
/// Staleness is checked via the GitHub Commits API (SHA comparison).
/// </summary>
public class CatalogueFetcher : ICatalogueFetcher
{
    private const string RawBaseUrl = "https://raw.githubusercontent.com/BSData/wh40k-10e/main/";
    private const string CommitsApiUrl = "https://api.github.com/repos/BSData/wh40k-10e/commits";

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
        var shaPath = localPath + ".sha";

        if (!forceRefresh && File.Exists(localPath))
        {
            // Check if the cached file is still current
            if (await IsCurrentAsync(filename, shaPath, ct))
            {
                _logger.LogDebug("Cache hit for {Filename}", filename);
                return File.OpenRead(localPath);
            }
        }

        _logger.LogInformation("Downloading {Filename}", filename);
        await DownloadAsync(filename, localPath, shaPath, ct);
        return File.OpenRead(localPath);
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    private async Task<bool> IsCurrentAsync(string filename, string shaPath, CancellationToken ct)
    {
        if (!File.Exists(shaPath)) return false;

        try
        {
            var cachedSha = await File.ReadAllTextAsync(shaPath, ct);
            var remoteSha = await GetLatestShaAsync(filename, ct);
            return string.Equals(cachedSha.Trim(), remoteSha.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check staleness for {Filename}; using cached copy", filename);
            return true;
        }
    }

    private async Task<string> GetLatestShaAsync(string filename, CancellationToken ct)
    {
        var url = $"{CommitsApiUrl}?path={Uri.EscapeDataString(filename)}&per_page=1";
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement[0].GetProperty("sha").GetString() ?? "";
    }

    private async Task DownloadAsync(string filename, string localPath, string shaPath, CancellationToken ct)
    {
        var url = RawBaseUrl + Uri.EscapeDataString(filename);
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

        await using (var fs = File.Create(localPath))
            await response.Content.CopyToAsync(fs, ct);

        // Persist the SHA alongside the cached file
        try
        {
            var sha = await GetLatestShaAsync(filename, ct);
            await File.WriteAllTextAsync(shaPath, sha, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist SHA for {Filename}", filename);
        }
    }
}
