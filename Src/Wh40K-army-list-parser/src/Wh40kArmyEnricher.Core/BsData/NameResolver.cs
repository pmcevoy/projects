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
    {
        // Search model scope first, then unit scope, then globally
        var candidates = new List<WeaponProfileData>();

        if (modelEntry != null)
            candidates.AddRange(modelEntry.Weapons);

        if (unitEntry != null)
        {
            candidates.AddRange(unitEntry.Weapons);
            foreach (var child in unitEntry.ChildEntries)
                candidates.AddRange(child.Weapons);
        }

        var result = ResolveWeapon(weaponName, candidates, "scoped");
        if (result != null) return result;

        // Global search
        var globalWeapons = store.GetAllEntries()
            .SelectMany(e => e.Weapons)
            .ToList();
        return ResolveWeapon(weaponName, globalWeapons, "global");
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

    private WeaponProfileData? ResolveWeapon(string weaponName, List<WeaponProfileData> candidates, string context)
    {
        if (candidates.Count == 0) return null;

        // 1. Override
        if (_overrides.TryGetValue(weaponName, out var overrideName))
            weaponName = overrideName;

        // 2. Exact
        var exact = candidates.FirstOrDefault(w =>
            string.Equals(w.Name, weaponName, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // 3. Count-stripped
        var stripped = CountPrefixRegex.Replace(weaponName, "");
        if (stripped != weaponName)
        {
            exact = candidates.FirstOrDefault(w =>
                string.Equals(w.Name, stripped, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
        }

        // 4. Fuzzy
        var best = candidates
            .Select(w => (weapon: w, score: Fuzz.TokenSortRatio(weaponName, w.Name)))
            .Where(x => x.score >= FuzzyThreshold)
            .OrderByDescending(x => x.score)
            .FirstOrDefault();

        if (best.weapon != null)
        {
            _logger.LogWarning(
                "[Weapon/{Context}] Fuzzy matched '{Input}' -> '{Match}' (score: {Score})",
                context, weaponName, best.weapon.Name, best.score);
            return best.weapon;
        }

        _logger.LogWarning("[Weapon/{Context}] Could not resolve '{Name}'", context, weaponName);
        return null;
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
