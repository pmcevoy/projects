using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace ProbHammer.Core.Catalogue;

public sealed class CatalogueStore
{
    private readonly ICatalogueFetcher _fetcher;
    private readonly ILogger<CatalogueStore> _logger;

    private Dictionary<string, CatalogueData> _catalogues = new(StringComparer.OrdinalIgnoreCase);

    // Retained after InitialiseAsync — required by RefreshCataloguesAsync
    private Dictionary<string, XElement> _globalProfiles = new(StringComparer.OrdinalIgnoreCase);

    public bool IsInitialised { get; private set; }

    public CatalogueStore(ICatalogueFetcher fetcher, ILogger<CatalogueStore> logger)
    {
        _fetcher = fetcher;
        _logger = logger;
    }

    public async Task InitialiseAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Initialising catalogue store");

        var fileList = await _fetcher.GetCatalogueListAsync(ct);
        _logger.LogInformation("Loading {Count} catalogue files", fileList.Count);

        // Pass 1: load all documents and collect shared profiles for cross-catalogue resolution
        var docs = new List<(string Filename, XDocument Doc)>(fileList.Count);
        foreach (var info in fileList)
        {
            try
            {
                var bytes = await _fetcher.FetchRawAsync(info.Name, forceRefresh: false, ct);
                var doc = await CatalogueParser.LoadDocumentAsync(bytes, info.Name, ct);
                docs.Add((info.Name, doc));

                var profiles = CatalogueParser.ExtractSharedProfiles(doc);
                foreach (var (id, el) in profiles)
                    _globalProfiles.TryAdd(id, el);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load catalogue file {Filename}", info.Name);
            }
        }

        // Pass 2: parse each document with the global profiles map
        foreach (var (filename, doc) in docs)
        {
            try
            {
                var data = CatalogueParser.Parse(doc, filename, _globalProfiles, _logger);
                if (!string.IsNullOrEmpty(data.Id))
                    _catalogues[data.Id] = data;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse catalogue {Filename}", filename);
            }
        }

        IsInitialised = true;
        _logger.LogInformation("Catalogue store initialised with {Count} catalogues", _catalogues.Count);
    }

    public async Task RefreshCataloguesAsync(IEnumerable<string> catalogueIds, CancellationToken ct = default)
    {
        foreach (var id in catalogueIds)
        {
            if (!_catalogues.TryGetValue(id, out var existing) || string.IsNullOrEmpty(existing.Filename))
            {
                _logger.LogWarning("Cannot refresh catalogue id={Id}: filename not known", id);
                continue;
            }

            try
            {
                var bytes = await _fetcher.FetchRawAsync(existing.Filename, forceRefresh: true, ct);
                var doc = await CatalogueParser.LoadDocumentAsync(bytes, existing.Filename, ct);

                // Merge any new/updated shared profiles into the global map
                var updated = CatalogueParser.ExtractSharedProfiles(doc);
                foreach (var (pid, el) in updated)
                    _globalProfiles[pid] = el;

                var data = CatalogueParser.Parse(doc, existing.Filename, _globalProfiles, _logger);
                _catalogues[id] = data;
                _logger.LogInformation("Refreshed catalogue {Name} (rev {Revision})", data.Name, data.Revision);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh catalogue id={Id}", id);
            }
        }
    }

    public IReadOnlyList<CatalogueData> GetAllCatalogues() =>
        _catalogues.Values.ToList();

    public CatalogueData? GetCatalogue(string id) =>
        _catalogues.TryGetValue(id, out var c) ? c : null;

    /// <summary>All top-level entries across every loaded catalogue.</summary>
    public IReadOnlyList<CatalogueEntry> GetAllTopLevelEntries() =>
        _catalogues.Values.SelectMany(c => c.Entries).ToList();
}
