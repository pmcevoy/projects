using System.Text.Json;
using System.Xml.Linq;
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
    // Retained after initialisation so selective refresh can re-parse with cross-catalogue resolution.
    private Dictionary<string, XElement> _globalProfiles = new();

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

        var catFilenames = await GetAllCatFilenamesAsync(ct);
        var allFilenames = new List<string>(catFilenames.Count + 1) { GstFilename };
        allFilenames.AddRange(catFilenames);
        _logger.LogInformation("Loading {Count} catalogue files", allFilenames.Count);

        // Pass 1: load every XML document and build a global shared-profile map.
        // This allows cross-catalogue infoLink resolution — for example, Black Templars units
        // reference the "Invulnerable Save" shared profile defined in Imperium - Space Marines.cat.
        var documents = new List<(string Filename, XDocument Doc)>(allFilenames.Count);
        var globalProfiles = new Dictionary<string, XElement>();

        foreach (var filename in allFilenames)
        {
            try
            {
                var stream = await _fetcher.FetchAsync(filename, _forceRefresh, ct);
                var doc = await _parser.LoadDocumentAsync(stream, filename, ct);
                documents.Add((filename, doc));
                foreach (var (id, profile) in _parser.GetSharedProfiles(doc))
                    globalProfiles.TryAdd(id, profile); // first-loaded wins on ID collision
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load catalogue '{Filename}' — skipping", filename);
            }
        }

        // Pass 2: parse each document using the complete global profile map so cross-catalogue
        // infoLink references resolve correctly.
        foreach (var (filename, doc) in documents)
        {
            try
            {
                var catalogue = _parser.Parse(doc, globalProfiles);
                _loaded[catalogue.Id] = catalogue with { Filename = filename };
                _logger.LogDebug("Loaded '{Name}'", catalogue.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not parse catalogue '{Filename}' — skipping", filename);
            }
        }

        _globalProfiles = globalProfiles;
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

    /// <summary>Name and revision of every loaded catalogue, sorted by name.</summary>
    public IReadOnlyList<(string Name, int Revision)> LoadedCatalogues
        => _loaded.Values
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => (c.Name, c.Revision))
            .ToList();

    /// <summary>Name and revision for the given catalogue IDs, sorted by name. Unknown IDs are silently skipped.</summary>
    public IReadOnlyList<(string Name, int Revision)> GetCataloguesByIds(IEnumerable<string> ids)
        => ids
            .Where(id => _loaded.ContainsKey(id))
            .Select(id => _loaded[id])
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => (c.Name, c.Revision))
            .Distinct()
            .ToList();

    /// <summary>
    /// Re-downloads and re-parses the catalogues identified by the given IDs, bypassing the disk cache.
    /// Uses the retained global shared-profile map so cross-catalogue infoLink resolution remains correct.
    /// Returns the names of the catalogues that were successfully refreshed.
    /// </summary>
    public async Task<IReadOnlyList<string>> RefreshCataloguesAsync(
        IEnumerable<string> catalogueIds, CancellationToken ct = default)
    {
        var refreshed = new List<string>();

        foreach (var id in catalogueIds.Distinct())
        {
            if (!_loaded.TryGetValue(id, out var existing) || string.IsNullOrEmpty(existing.Filename))
                continue;

            var filename = existing.Filename;
            try
            {
                var stream = await _fetcher.FetchAsync(filename, forceRefresh: true, ct);
                var doc = await _parser.LoadDocumentAsync(stream, filename, ct);

                // Merge any updated shared profiles back into the global map (local catalogue wins)
                foreach (var (profileId, profile) in _parser.GetSharedProfiles(doc))
                    _globalProfiles[profileId] = profile;

                var catalogue = _parser.Parse(doc, _globalProfiles);
                _loaded[catalogue.Id] = catalogue with { Filename = filename };

                refreshed.Add(catalogue.Name);
                _logger.LogInformation("Refreshed catalogue '{Name}' (r{Revision})", catalogue.Name, catalogue.Revision);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh catalogue '{Filename}'", filename);
            }
        }

        return refreshed;
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

    private async Task<List<string>> GetAllCatFilenamesAsync(CancellationToken ct)
    {
        // Use a cached file list so we never hit the GitHub Contents API on every run.
        // The cache is only bypassed when forceRefresh is true.
        var cacheFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".wh40k-enricher", "cache", "catalogue-list.json");

        if (!_forceRefresh && File.Exists(cacheFile))
        {
            _logger.LogDebug("Using cached catalogue list");
            var cached = await File.ReadAllTextAsync(cacheFile, ct);
            return JsonSerializer.Deserialize<List<string>>(cached) ?? [];
        }

        try
        {
            _logger.LogInformation("Fetching catalogue list from GitHub");
            var json = await _fetcher.FetchRawAsync(GitHubContentsApi, ct);
            using var doc = JsonDocument.Parse(json);
            var names = doc.RootElement.EnumerateArray()
                .Select(item => item.GetProperty("name").GetString() ?? "")
                .Where(name => name.EndsWith(".cat", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(names), ct);
            return names;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch catalogue list from GitHub");
            return [];
        }
    }

    private static IEnumerable<CatalogueEntry> GetAllEntriesFlat(CatalogueData catalogue)
        => catalogue.Entries.SelectMany(e => e.Flatten());
}
