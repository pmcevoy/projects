using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wh40kArmyEnricher.Core.Models;

namespace Wh40kArmyEnricher.Core.BsData;

/// <summary>
/// Holds loaded catalogues keyed by catalogue ID.
/// On initialisation, downloads and parses every .cat file in the BSData/wh40k-10e
/// GitHub repository. All catalogues are available immediately after InitialiseAsync
/// completes; no further lazy loading or name-to-filename mapping is required.
/// </summary>
public class CatalogueStore
{
    private const string GstFilename = "Warhammer 40,000.gst";
    private const string GitHubContentsApi = "https://api.github.com/repos/BSData/wh40k-10e/contents/";

    private readonly ICatalogueFetcher _fetcher;
    private readonly CatalogueParser _parser;
    private readonly ILogger<CatalogueStore> _logger;
    private readonly bool _forceRefresh;

    private readonly Dictionary<string, CatalogueData> _loaded = new();

    private bool _initialised;

    public CatalogueStore(ICatalogueFetcher fetcher, CatalogueParser parser,
        ILogger<CatalogueStore> logger, bool forceRefresh = false)
    {
        _fetcher = fetcher;
        _parser = parser;
        _logger = logger;
        _forceRefresh = forceRefresh;
    }

    // ---------------------------------------------------------------------------
    // Initialisation — downloads every .cat file once, caches to disk
    // ---------------------------------------------------------------------------

    public async Task InitialiseAsync(CancellationToken ct = default)
    {
        if (_initialised) return;
        _initialised = true;

        // Load game system root
        _logger.LogInformation("Loading game system file: {Filename}", GstFilename);
        await LoadOneAsync(GstFilename, ct);

        // Fetch the full file listing and download every .cat
        var catFilenames = await GetAllCatFilenamesAsync(ct);
        _logger.LogInformation("Downloading {Count} catalogue files", catFilenames.Count);

        foreach (var filename in catFilenames)
            await LoadOneAsync(filename, ct);

        _logger.LogInformation("Catalogue store ready: {Count} catalogues loaded", _loaded.Count);
    }

    // ---------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------

    /// <summary>
    /// No-op if InitialiseAsync has already been called (all catalogues are pre-loaded).
    /// Kept for API compatibility; callers no longer need to specify a filename.
    /// </summary>
    public async Task LoadCatalogueAsync(string filename, CancellationToken ct = default)
    {
        if (!_initialised)
            await InitialiseAsync(ct);
    }

    /// <summary>Returns all loaded catalogue entries of a given type (all depths, all catalogues).</summary>
    public IEnumerable<CatalogueEntry> GetAllEntriesOfType(string entryType)
        => GetAllEntries()
            .Where(e => string.Equals(e.EntryType, entryType, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns all loaded catalogue entries (flat across all catalogues, all depths).</summary>
    public IEnumerable<CatalogueEntry> GetAllEntries()
        => _loaded.Values.SelectMany(GetAllEntriesFlat);

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    private async Task LoadOneAsync(string filename, CancellationToken ct)
    {
        try
        {
            var stream = await _fetcher.FetchAsync(filename, _forceRefresh, ct);
            var catalogue = await _parser.ParseAsync(stream, filename, ct);
            _loaded[catalogue.Id] = catalogue;
            _logger.LogDebug("Loaded '{Name}'", catalogue.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load catalogue '{Filename}' — skipping", filename);
        }
    }

    private async Task<List<string>> GetAllCatFilenamesAsync(CancellationToken ct)
    {
        try
        {
            var json = await _fetcher.FetchRawAsync(GitHubContentsApi, ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray()
                .Select(item => item.GetProperty("name").GetString() ?? "")
                .Where(name => name.EndsWith(".cat", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch catalogue list from GitHub");
            return [];
        }
    }

    private static IEnumerable<CatalogueEntry> GetAllEntriesFlat(CatalogueData catalogue)
    {
        foreach (var entry in catalogue.Entries)
        {
            yield return entry;
            foreach (var child in FlattenChildren(entry))
                yield return child;
        }
    }

    private static IEnumerable<CatalogueEntry> FlattenChildren(CatalogueEntry entry)
    {
        foreach (var child in entry.ChildEntries)
        {
            yield return child;
            foreach (var grandchild in FlattenChildren(child))
                yield return grandchild;
        }
    }
}
