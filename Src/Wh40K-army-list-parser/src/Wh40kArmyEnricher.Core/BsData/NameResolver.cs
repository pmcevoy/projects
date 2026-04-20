using System.Text.Json;
using System.Text.RegularExpressions;
using FuzzySharp;
using Microsoft.Extensions.Logging;
using Wh40kArmyEnricher.Core.Models;

namespace Wh40kArmyEnricher.Core.BsData;

/// <summary>
/// Matches army list display names to BSData selectionEntry names.
/// Resolution order: manual override → exact → count-stripped → fuzzy (threshold 85).
/// </summary>
public class NameResolver
{
    private const int FuzzyThreshold = 85;

    private static readonly Regex CountPrefixRegex =
        new(@"^\d+x\s+", RegexOptions.Compiled);

    private readonly ILogger<NameResolver> _logger;
    private readonly Dictionary<string, string> _overrides;

    public NameResolver(ILogger<NameResolver> logger, string? overrideFilePath = null)
    {
        _logger = logger;
        _overrides = LoadOverrides(overrideFilePath ?? "name_overrides.json");
    }

    // ---------------------------------------------------------------------------
    // Unit resolution
    // ---------------------------------------------------------------------------

    public CatalogueEntry? ResolveUnit(string displayName, CatalogueStore store)
    {
        // BSData uses type="unit" for multi-model squads (statline is on child models, not the
        // squad entry itself) and type="model" for single-model datasheets (e.g. vehicles/drones)
        // where the statline IS on the entry. Include all type="unit" entries plus type="model"
        // entries that have their own statline.
        var candidates = store.GetAllEntries()
            .Where(e =>
                string.Equals(e.EntryType, "unit", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(e.EntryType, "model", StringComparison.OrdinalIgnoreCase)
                    && e.Statline != null))
            .ToList();
        var result = Resolve(displayName, candidates, "unit");
        if (result == null)
            _logger.LogWarning("[unit] Could not resolve '{Name}'", displayName);
        return result;
    }

    // ---------------------------------------------------------------------------
    // Model resolution
    // ---------------------------------------------------------------------------

    public CatalogueEntry? ResolveModel(string displayName, CatalogueEntry parentUnit, CatalogueStore store)
    {
        // Search within parent unit's children first
        var localCandidates = FlattenModels(parentUnit).ToList();
        var result = Resolve(displayName, localCandidates, "model (local)");
        if (result != null) return result;

        // BSData names loadout variants with a suffix: "Initiate w/Bolt Rifle", "Neophyte w/Shotgun".
        // The army list uses only the base name ("Initiate", "Neophyte"). A prefix match finds the
        // correct variant — all variants of the same model share the same statline.
        result = FindByPrefix(displayName, localCandidates);
        if (result != null)
        {
            _logger.LogDebug("[model] Prefix matched '{Input}' -> '{Match}'", displayName, result.Name);
            return result;
        }

        // Fall back to global model entries
        var globalCandidates = store.GetAllEntriesOfType("model").ToList();
        result = Resolve(displayName, globalCandidates, "model (global)");
        if (result != null) return result;

        result = FindByPrefix(displayName, globalCandidates);
        if (result != null)
        {
            _logger.LogDebug("[model] Prefix matched '{Input}' -> '{Match}' (global)", displayName, result.Name);
            return result;
        }

        _logger.LogWarning("[model] Could not resolve '{Name}'", displayName);
        return null;
    }

    // ---------------------------------------------------------------------------
    // Weapon resolution
    // ---------------------------------------------------------------------------

    public WeaponProfileData? ResolveWeapon(string weaponName, CatalogueEntry? unitEntry,
        CatalogueEntry? modelEntry, CatalogueStore store)
        => ResolveWeaponProfiles(weaponName, unitEntry, modelEntry, store)?.FirstOrDefault();

    /// <summary>
    /// Resolves a weapon by profile name first, then by entry name (for multi-mode weapons
    /// like "Hellforged weapons" whose profiles are named "- strike" / "- sweep").
    /// Returns all matching profiles so the caller can build variants.
    /// Returns null if the weapon cannot be resolved.
    /// </summary>
    public IReadOnlyList<WeaponProfileData>? ResolveWeaponProfiles(string weaponName,
        CatalogueEntry? unitEntry, CatalogueEntry? modelEntry, CatalogueStore store)
    {
        // --- Pass 1: search by profile name ---
        // Recurse the full model/unit hierarchy so weapons nested inside selectionEntryGroups
        // (e.g. Sword Brother's "Master-crafted Power Weapon" lives under a "Melee Option"
        // selectionEntryGroup, not directly on the model entry) are found before the global
        // search, which may return the same weapon name from a different catalogue with different
        // keywords (e.g. Space Marines has "Master-crafted Power Weapon" with "Precision" while
        // Black Templars has the same weapon with "Lethal Hits").
        var profileCandidates = new List<WeaponProfileData>();
        if (modelEntry != null)
            profileCandidates.AddRange(modelEntry.Flatten().SelectMany(e => e.Weapons));
        if (unitEntry != null)
            profileCandidates.AddRange(unitEntry.Flatten().SelectMany(e => e.Weapons));
        var byProfile = ResolveWeaponByProfile(weaponName, profileCandidates);
        if (byProfile != null) return [byProfile];

        var globalProfiles = store.GetAllEntries().SelectMany(e => e.Weapons).ToList();
        byProfile = ResolveWeaponByProfile(weaponName, globalProfiles);
        if (byProfile != null) return [byProfile];

        // --- Pass 2: search by entry name (handles weapons whose profile names differ) ---
        var scopedEntries = new List<CatalogueEntry>();
        if (modelEntry != null) scopedEntries.AddRange(modelEntry.Flatten());
        if (unitEntry != null) scopedEntries.AddRange(unitEntry.Flatten());

        var byEntry = ResolveEntryByName(weaponName, scopedEntries.Where(e => e.Weapons.Count > 0).ToList());
        if (byEntry?.Weapons.Count > 0) return byEntry.Weapons.ToList();

        var globalEntries = store.GetAllEntries().Where(e => e.Weapons.Count > 0).ToList();
        byEntry = ResolveEntryByName(weaponName, globalEntries);
        if (byEntry?.Weapons.Count > 0) return byEntry.Weapons.ToList();

        return null;
    }

    // ---------------------------------------------------------------------------
    // Core matching logic
    // ---------------------------------------------------------------------------

    private CatalogueEntry? Resolve(string displayName, List<CatalogueEntry> candidates, string context)
        => ResolveByName(displayName, candidates, e => e.Name, context);

    private WeaponProfileData? ResolveWeaponByProfile(string weaponName, List<WeaponProfileData> candidates)
        => ResolveByName(weaponName, candidates, w => w.Name, "Weapon");

    private CatalogueEntry? ResolveEntryByName(string name, List<CatalogueEntry> candidates)
        => ResolveByName(name, candidates, e => e.Name, "Weapon/entry");

    private T? ResolveByName<T>(string displayName, List<T> candidates, Func<T, string> getName, string context)
        where T : class
    {
        if (candidates.Count == 0) return null;

        // 1. Override
        if (_overrides.TryGetValue(displayName, out var overrideName))
            displayName = overrideName;

        // 2. Exact match
        var exact = candidates.FirstOrDefault(e =>
            string.Equals(getName(e), displayName, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // 3. Count-stripped exact match
        var stripped = CountPrefixRegex.Replace(displayName, "");
        if (stripped != displayName)
        {
            exact = candidates.FirstOrDefault(e =>
                string.Equals(getName(e), stripped, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
        }

        // 4. Fuzzy match
        var best = candidates
            .Select(e => (item: e, score: Fuzz.TokenSortRatio(displayName, getName(e))))
            .Where(x => x.score >= FuzzyThreshold)
            .OrderByDescending(x => x.score)
            .FirstOrDefault();

        if (best.item != null)
        {
            var msg = "[{Context}] Fuzzy matched '{Input}' -> '{Match}' (score: {Score})";
            if (best.score >= 90)
                _logger.LogInformation(msg, context, displayName, getName(best.item), best.score);
            else
                _logger.LogWarning(msg, context, displayName, getName(best.item), best.score);
            return best.item;
        }

        return null;
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static CatalogueEntry? FindByPrefix(string displayName, List<CatalogueEntry> candidates)
    {
        return candidates.FirstOrDefault(e =>
            e.Name.Length > displayName.Length
            && e.Name.StartsWith(displayName, StringComparison.OrdinalIgnoreCase)
            && !char.IsLetterOrDigit(e.Name[displayName.Length]));
    }

    private static IEnumerable<CatalogueEntry> FlattenModels(CatalogueEntry unit)
    {
        foreach (var child in unit.ChildEntries)
        {
            if (string.Equals(child.EntryType, "model", StringComparison.OrdinalIgnoreCase))
                yield return child;
            foreach (var grandchild in FlattenModels(child))
                yield return grandchild;
        }
    }

    private static Dictionary<string, string> LoadOverrides(string path)
    {
        if (!File.Exists(path)) return new();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }
}
