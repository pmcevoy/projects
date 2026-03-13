using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wh40kArmyEnricher.Core.Models;

namespace Wh40kArmyEnricher.Core.BsData;

/// <summary>
/// Holds loaded catalogues keyed by catalogue ID.
/// Lazily downloads and parses catalogues on first access, following catalogueLinks transitively.
///
/// Catalogue-ID → filename resolution strategy (in order):
///   1. GST catalogueLinks (works if the .gst lists faction catalogues)
///   2. GitHub contents API file listing (fallback: builds name → filename map)
///   3. Bare name + ".cat" guess (last resort)
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

    // Maps catalogue targetId → filename (from GST catalogueLinks, if present)
    private readonly Dictionary<string, string> _idToFilename = new();

    // Maps catalogue short name → filename (from GitHub file listing)
    // e.g. "Death Guard" → "Chaos - Death Guard.cat"
    //      "Chaos - Death Guard" → "Chaos - Death Guard.cat"
    private readonly Dictionary<string, string> _nameToFilename =
        new(StringComparer.OrdinalIgnoreCase);

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
    // Initialisation
    // ---------------------------------------------------------------------------

    public async Task InitialiseAsync(CancellationToken ct = default)
    {
        if (_initialised) return;
        _initialised = true;

        _logger.LogInformation("Loading game system file: {Filename}", GstFilename);
        var stream = await _fetcher.FetchAsync(GstFilename, _forceRefresh, ct);
        var gst = await _parser.ParseAsync(stream, GstFilename, ct);
        _loaded[gst.Id] = gst;

        // Attempt to build ID→filename from GST catalogueLinks.
        // In practice the WH40K .gst may not list faction catalogues here.
        foreach (var link in gst.CatalogueLinks)
        {
            if (string.IsNullOrEmpty(link.TargetId) || string.IsNullOrEmpty(link.Name)) continue;
            _idToFilename[link.TargetId] = link.Name.Trim() + ".cat";
        }

        _logger.LogInformation("GST catalogue links: {Count}", _idToFilename.Count);

        // If the GST gave us nothing, fall back to GitHub file listing.
        if (_idToFilename.Count == 0)
            await BuildNameMapFromGitHubAsync(ct);
    }

    // ---------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------

    /// <summary>Loads a specific faction catalogue by filename and all its parent catalogues.</summary>
    public async Task<CatalogueData> LoadCatalogueAsync(string filename, CancellationToken ct = default)
    {
        if (!_initialised)
            await InitialiseAsync(ct);

        var existing = _loaded.Values.FirstOrDefault(c =>
            string.Equals(c.Name + ".cat", filename, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.Name + ".gst", filename, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        var catStream = await _fetcher.FetchAsync(filename, _forceRefresh, ct);
        var catalogue = await _parser.ParseAsync(catStream, filename, ct);
        _loaded[catalogue.Id] = catalogue;

        _logger.LogInformation("Loaded catalogue '{Name}' ({Id})", catalogue.Name, catalogue.Id);

        await LoadLinkedCataloguesAsync(catalogue, ct);

        return catalogue;
    }

    /// <summary>Returns all loaded catalogue entries of a given type (flat across all catalogues, all depths).</summary>
    public IEnumerable<CatalogueEntry> GetAllEntriesOfType(string entryType)
        => GetAllEntries()
            .Where(e => string.Equals(e.EntryType, entryType, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns all loaded catalogue entries (flat across all catalogues).</summary>
    public IEnumerable<CatalogueEntry> GetAllEntries()
        => _loaded.Values.SelectMany(GetAllEntriesFlat);

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Fetches the GitHub repository contents listing and builds a
    /// short-name → filename map for all .cat files.
    /// </summary>
    private async Task BuildNameMapFromGitHubAsync(CancellationToken ct)
    {
        _logger.LogInformation("Building catalogue name map from GitHub file listing…");
        try
        {
            var json = await _fetcher.FetchRawAsync(GitHubContentsApi, ct);
            using var doc = JsonDocument.Parse(json);

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var name = item.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith(".cat", StringComparison.OrdinalIgnoreCase)) continue;

                var baseName = name[..^4]; // strip .cat
                _nameToFilename[baseName] = name;           // "Chaos - Death Guard" → file

                var dash = baseName.IndexOf(" - ", StringComparison.Ordinal);
                if (dash >= 0)
                    _nameToFilename.TryAdd(baseName[(dash + 3)..], name); // "Death Guard" → file
            }

            _logger.LogInformation("Name map built: {Count} entries", _nameToFilename.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not build catalogue name map from GitHub; linked catalogues may not load");
        }
    }

    private async Task LoadLinkedCataloguesAsync(CatalogueData catalogue, CancellationToken ct)
    {
        foreach (var link in catalogue.CatalogueLinks)
        {
            if (_loaded.ContainsKey(link.TargetId)) continue;

            string? filename = null;

            // 1. ID-based lookup (from GST)
            _idToFilename.TryGetValue(link.TargetId, out filename);

            // 2. Name-based lookup (from GitHub file listing)
            if (filename == null)
                _nameToFilename.TryGetValue(link.Name.Trim(), out filename);

            // 3. Bare-name guess
            if (filename == null)
            {
                filename = link.Name.Trim() + ".cat";
                _logger.LogWarning(
                    "No mapping for catalogue link '{Name}' (targetId={Id}); guessing '{Filename}'",
                    link.Name, link.TargetId, filename);
            }

            try
            {
                var stream = await _fetcher.FetchAsync(filename, _forceRefresh, ct);
                var linked = await _parser.ParseAsync(stream, filename, ct);
                _loaded[linked.Id] = linked;
                _logger.LogInformation("Loaded linked catalogue '{Name}'", linked.Name);
                await LoadLinkedCataloguesAsync(linked, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load linked catalogue '{Filename}'", filename);
            }
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
