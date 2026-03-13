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
        // BSData inconsistently uses type="unit" and type="model" for full unit datasheets.
        // A reliable indicator of a full datasheet is the presence of a statline.
        var candidates = store.GetAllEntries()
            .Where(e => e.Statline != null
                && (string.Equals(e.EntryType, "unit", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(e.EntryType, "model", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return Resolve(displayName, candidates, "unit");
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

        // Fall back to global model entries
        var globalCandidates = store.GetAllEntriesOfType("model").ToList();
        return Resolve(displayName, globalCandidates, "model (global)");
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
        var profileCandidates = new List<WeaponProfileData>();
        if (modelEntry != null)
            profileCandidates.AddRange(modelEntry.Weapons);
        if (unitEntry != null)
        {
            profileCandidates.AddRange(unitEntry.Weapons);
            foreach (var child in unitEntry.ChildEntries)
                profileCandidates.AddRange(child.Weapons);
        }
        var byProfile = ResolveWeaponByProfile(weaponName, profileCandidates);
        if (byProfile != null) return [byProfile];

        var globalProfiles = store.GetAllEntries().SelectMany(e => e.Weapons).ToList();
        byProfile = ResolveWeaponByProfile(weaponName, globalProfiles);
        if (byProfile != null) return [byProfile];

        // --- Pass 2: search by entry name (handles weapons whose profile names differ) ---
        var scopedEntries = new List<CatalogueEntry>();
        if (modelEntry != null) scopedEntries.AddRange(FlattenEntry(modelEntry));
        if (unitEntry != null) scopedEntries.AddRange(FlattenEntry(unitEntry));

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
    {
        if (candidates.Count == 0) return null;

        // 1. Override
        if (_overrides.TryGetValue(displayName, out var overrideName))
            displayName = overrideName;

        // 2. Exact match
        var exact = candidates.FirstOrDefault(e =>
            string.Equals(e.Name, displayName, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // 3. Count-stripped exact match
        var stripped = CountPrefixRegex.Replace(displayName, "");
        if (stripped != displayName)
        {
            exact = candidates.FirstOrDefault(e =>
                string.Equals(e.Name, stripped, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
        }

        // 4. Fuzzy match
        var best = candidates
            .Select(e => (entry: e, score: Fuzz.TokenSortRatio(displayName, e.Name)))
            .Where(x => x.score >= FuzzyThreshold)
            .OrderByDescending(x => x.score)
            .FirstOrDefault();

        if (best.entry != null)
        {
            _logger.LogWarning(
                "[{Context}] Fuzzy matched '{Input}' -> '{Match}' (score: {Score})",
                context, displayName, best.entry.Name, best.score);
            return best.entry;
        }

        _logger.LogWarning("[{Context}] Could not resolve '{Name}'", context, displayName);
        return null;
    }

    private WeaponProfileData? ResolveWeaponByProfile(string weaponName, List<WeaponProfileData> candidates)
    {
        if (candidates.Count == 0) return null;

        if (_overrides.TryGetValue(weaponName, out var overrideName))
            weaponName = overrideName;

        var exact = candidates.FirstOrDefault(w =>
            string.Equals(w.Name, weaponName, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        var stripped = CountPrefixRegex.Replace(weaponName, "");
        if (stripped != weaponName)
        {
            exact = candidates.FirstOrDefault(w =>
                string.Equals(w.Name, stripped, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
        }

        var best = candidates
            .Select(w => (weapon: w, score: Fuzz.TokenSortRatio(weaponName, w.Name)))
            .Where(x => x.score >= FuzzyThreshold)
            .OrderByDescending(x => x.score)
            .FirstOrDefault();

        if (best.weapon != null)
        {
            _logger.LogWarning("[Weapon] Fuzzy matched '{Input}' -> '{Match}' (score: {Score})",
                weaponName, best.weapon.Name, best.score);
            return best.weapon;
        }

        return null;
    }

    private CatalogueEntry? ResolveEntryByName(string name, List<CatalogueEntry> candidates)
    {
        if (candidates.Count == 0) return null;

        if (_overrides.TryGetValue(name, out var overrideName))
            name = overrideName;

        var exact = candidates.FirstOrDefault(e =>
            string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        var stripped = CountPrefixRegex.Replace(name, "");
        if (stripped != name)
        {
            exact = candidates.FirstOrDefault(e =>
                string.Equals(e.Name, stripped, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
        }

        var best = candidates
            .Select(e => (entry: e, score: Fuzz.TokenSortRatio(name, e.Name)))
            .Where(x => x.score >= FuzzyThreshold)
            .OrderByDescending(x => x.score)
            .FirstOrDefault();

        if (best.entry != null)
        {
            _logger.LogWarning("[Weapon/entry] Fuzzy matched '{Input}' -> '{Match}' (score: {Score})",
                name, best.entry.Name, best.score);
            return best.entry;
        }

        return null;
    }

    private static IEnumerable<CatalogueEntry> FlattenEntry(CatalogueEntry entry)
    {
        yield return entry;
        foreach (var child in entry.ChildEntries)
            foreach (var desc in FlattenEntry(child))
                yield return desc;
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

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
