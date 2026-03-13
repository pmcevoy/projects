using Microsoft.Extensions.Logging;
using Wh40kArmyEnricher.Core.Models;

namespace Wh40kArmyEnricher.Core.BsData;

/// <summary>
/// Holds loaded catalogues keyed by catalogue ID.
/// Lazily downloads and parses catalogues on first access, following catalogueLinks transitively.
/// Uses the game-system file (Warhammer 40,000.gst) to map catalogue IDs to filenames.
/// </summary>
public class CatalogueStore
{
    private const string GstFilename = "Warhammer 40,000.gst";

    private readonly ICatalogueFetcher _fetcher;
    private readonly CatalogueParser _parser;
    private readonly ILogger<CatalogueStore> _logger;
    private readonly bool _forceRefresh;

    private readonly Dictionary<string, CatalogueData> _loaded = new();

    // Maps catalogue ID -> filename (built from the .gst file)
    private readonly Dictionary<string, string> _idToFilename = new();

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

    /// <summary>
    /// Loads the game-system file and builds the catalogue ID → filename index.
    /// Must be called once before any <see cref="LoadCatalogueAsync"/> calls.
    /// </summary>
    public async Task InitialiseAsync(CancellationToken ct = default)
    {
        if (_initialised) return;
        _initialised = true;

        _logger.LogInformation("Loading game system file: {Filename}", GstFilename);
        var stream = await _fetcher.FetchAsync(GstFilename, _forceRefresh, ct);
        var gst = await _parser.ParseAsync(stream, GstFilename, ct);
        _loaded[gst.Id] = gst;

        // The GST catalogueLinks map catalogue IDs to human-readable names.
        // Convention: the human-readable name IS the base filename (without .cat).
        foreach (var link in gst.CatalogueLinks)
        {
            if (string.IsNullOrEmpty(link.TargetId) || string.IsNullOrEmpty(link.Name)) continue;
            var filename = link.Name.Trim() + ".cat";
            _idToFilename[link.TargetId] = filename;
        }

        _logger.LogInformation("Game system index built: {Count} catalogues registered", _idToFilename.Count);
    }

    // ---------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------

    /// <summary>Loads a specific faction catalogue and all its parent catalogues.</summary>
    public async Task<CatalogueData> LoadCatalogueAsync(string filename, CancellationToken ct = default)
    {
        if (!_initialised)
            await InitialiseAsync(ct);

        // Check if already loaded by filename match
        var existing = _loaded.Values.FirstOrDefault(c =>
            string.Equals(c.Name + ".cat", filename, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.Name + ".gst", filename, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        var stream = await _fetcher.FetchAsync(filename, _forceRefresh, ct);
        var catalogue = await _parser.ParseAsync(stream, filename, ct);
        _loaded[catalogue.Id] = catalogue;

        _logger.LogInformation("Loaded catalogue '{Name}' ({Id})", catalogue.Name, catalogue.Id);

        // Recursively load parent catalogues
        await LoadLinkedCataloguesAsync(catalogue, ct);

        return catalogue;
    }

    /// <summary>Returns all loaded catalogue entries of a given type (flat across all catalogues).</summary>
    public IEnumerable<CatalogueEntry> GetAllEntriesOfType(string entryType)
    {
        return _loaded.Values
            .SelectMany(c => c.Entries)
            .Where(e => string.Equals(e.EntryType, entryType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns all loaded catalogue entries (flat across all catalogues).</summary>
    public IEnumerable<CatalogueEntry> GetAllEntries()
    {
        return _loaded.Values.SelectMany(GetAllEntriesFlat);
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

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    private async Task LoadLinkedCataloguesAsync(CatalogueData catalogue, CancellationToken ct)
    {
        foreach (var link in catalogue.CatalogueLinks)
        {
            if (_loaded.ContainsKey(link.TargetId)) continue;

            if (!_idToFilename.TryGetValue(link.TargetId, out var filename))
            {
                // Fallback: the link name itself might be the filename base
                filename = link.Name.Trim() + ".cat";
                _logger.LogWarning(
                    "No filename mapping for catalogue ID '{TargetId}'; guessing '{Filename}'",
                    link.TargetId, filename);
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
}
