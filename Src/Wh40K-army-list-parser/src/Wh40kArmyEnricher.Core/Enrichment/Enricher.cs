using System.Text.Json;
using System.Text.RegularExpressions;
using FuzzySharp;
using Microsoft.Extensions.Logging;
using Wh40kArmyEnricher.Core.Catalogue;
using Wh40kArmyEnricher.Core.Contracts;

namespace Wh40kArmyEnricher.Core.Enrichment;

public class Enricher
{
    private static readonly Regex CountPrefixRegex =
        new(@"^\d+x\s+(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly CatalogueStore _store;
    private readonly ILogger<Enricher> _logger;
    private readonly IReadOnlyDictionary<string, string> _nameOverrides;

    public Enricher(CatalogueStore store, ILogger<Enricher> logger)
    {
        _store = store;
        _logger = logger;
        _nameOverrides = LoadNameOverrides();
    }

    public (IReadOnlyList<UnitProfile> Units, IReadOnlySet<string> UsedCatalogueIds) Enrich(ArmyList army)
    {
        var units = new List<UnitProfile>();
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var unitEntry in army.Units)
        {
            var (catalogueEntry, catalogueId) = FindUnitEntry(unitEntry.Name);
            if (catalogueEntry == null)
            {
                _logger.LogWarning("Unit not resolved in catalogue: {Name}", unitEntry.Name);
                continue;
            }

            if (!string.IsNullOrEmpty(catalogueId))
                usedIds.Add(catalogueId);

            var profile = BuildUnitProfile(unitEntry, catalogueEntry, army.Faction);
            units.Add(profile);
        }

        return (units, usedIds);
    }

    // ── Unit / entry lookup ───────────────────────────────────────────────────

    private (CatalogueEntry? entry, string catalogueId) FindUnitEntry(string displayName)
    {
        var name = ApplyOverride(displayName);

        foreach (var catalogue in _store.GetAllCatalogues())
        {
            // Top-level entries of type unit or model
            var topLevel = catalogue.Entries.Where(IsUnitOrModel);
            var match = FindInList(name, topLevel, allowPrefix: false);
            if (match != null)
                return (match, catalogue.Id);

            // Children of top-level entries (nested unit definitions)
            foreach (var entry in catalogue.Entries)
            {
                match = FindInList(name, entry.Children.Where(IsUnitOrModel), allowPrefix: false);
                if (match != null)
                    return (match, catalogue.Id);
            }
        }

        return (null, "");
    }

    private static bool IsUnitOrModel(CatalogueEntry e) =>
        e.EntryType.Equals("unit", StringComparison.OrdinalIgnoreCase) ||
        e.EntryType.Equals("model", StringComparison.OrdinalIgnoreCase);

    private CatalogueEntry? FindInList(
        string name, IEnumerable<CatalogueEntry> candidates, bool allowPrefix)
    {
        var list = candidates.ToList();
        if (list.Count == 0) return null;

        var stripped = StripCountPrefix(name);

        // Exact match
        var exact = list.FirstOrDefault(e =>
            string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.Name, stripped, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // Fuzzy match
        int bestScore = 0;
        CatalogueEntry? bestMatch = null;
        foreach (var candidate in list)
        {
            int score = Fuzz.TokenSortRatio(stripped, candidate.Name);
            if (score > bestScore)
            {
                bestScore = score;
                bestMatch = candidate;
            }
        }

        if (bestScore >= 85 && bestMatch != null)
        {
            if (bestScore >= 90)
                _logger.LogInformation(
                    "Fuzzy match: '{Input}' → '{Match}' (score {Score})", stripped, bestMatch.Name, bestScore);
            else
                _logger.LogWarning(
                    "Fuzzy match (low confidence): '{Input}' → '{Match}' (score {Score})", stripped, bestMatch.Name, bestScore);
            return bestMatch;
        }

        // Prefix match (models only)
        if (allowPrefix)
        {
            var prefix = list.FirstOrDefault(e =>
                e.Name.Length > stripped.Length &&
                e.Name.StartsWith(stripped, StringComparison.OrdinalIgnoreCase) &&
                !char.IsLetterOrDigit(e.Name[stripped.Length]));
            if (prefix != null) return prefix;
        }

        return null;
    }

    // ── Profile building ──────────────────────────────────────────────────────

    private UnitProfile BuildUnitProfile(UnitEntry unitEntry, CatalogueEntry catalogueEntry, string faction)
    {
        var (statline, invuln, fnp) = ResolveStatline(catalogueEntry);
        var allAbilities = GatherAbilities(catalogueEntry);
        int totalModels = unitEntry.Models.Count > 0 ? unitEntry.Models.Sum(m => m.Count) : 1;

        var models = new List<ModelProfile>();
        bool defenderStatlineSet = false;

        foreach (var modelEntry in unitEntry.Models)
        {
            var childEntry = FindInList(
                ApplyOverride(modelEntry.Name), catalogueEntry.Children, allowPrefix: true);
            var sourceEntry = childEntry ?? catalogueEntry;

            // For single-model units: child-level invuln/fnp overrides unit-level values
            if (!defenderStatlineSet)
            {
                if (sourceEntry.EntryInvulnerableSave.HasValue)
                    invuln = sourceEntry.EntryInvulnerableSave;
                if (sourceEntry.EntryFeelNoPain.HasValue)
                    fnp = sourceEntry.EntryFeelNoPain;
                defenderStatlineSet = true;
            }

            var weapons = BuildModelWeapons(modelEntry, sourceEntry, catalogueEntry);
            models.Add(new ModelProfile
            {
                ModelName = modelEntry.Name,
                Count = modelEntry.Count,
                Weapons = weapons,
            });
        }

        return new UnitProfile
        {
            Name = catalogueEntry.Name,
            Faction = faction,
            ModelCount = totalModels,
            Keywords = GatherKeywords(catalogueEntry),
            Abilities = allAbilities.Where(a => !IsLeadingAbility(a)).ToList(),
            LeadingAbilities = allAbilities.Where(IsLeadingAbility).ToList(),
            Enhancements = unitEntry.Enhancements.ToList(),
            Rerolls = new RerollProfile(),
            CriticalHitsOn = 6,
            Models = models,
            Toughness = statline?.Toughness ?? 0,
            Save = statline?.Save ?? 0,
            InvulnerableSave = invuln,
            Wounds = statline?.Wounds ?? 0,
            FeelNoPain = fnp,
        };
    }

    private static (CatalogueStatline? statline, int? invuln, int? fnp) ResolveStatline(CatalogueEntry entry)
    {
        if (entry.Statline != null)
            return (entry.Statline, entry.EntryInvulnerableSave, entry.EntryFeelNoPain);

        foreach (var child in entry.Children)
        {
            var (sl, inv, fp) = ResolveStatline(child);
            if (sl != null)
                return (sl, inv ?? entry.EntryInvulnerableSave, fp ?? entry.EntryFeelNoPain);
        }

        return (null, entry.EntryInvulnerableSave, entry.EntryFeelNoPain);
    }

    // ── Weapon building ───────────────────────────────────────────────────────

    private List<WeaponProfile> BuildModelWeapons(
        ModelEntry modelEntry, CatalogueEntry modelCatalogueEntry, CatalogueEntry unitEntry)
    {
        var weapons = new List<WeaponProfile>();

        foreach (var weaponEntry in modelEntry.Weapons)
        {
            var name = ApplyOverride(weaponEntry.Name);
            var catWeapon = FindWeaponEntry(name, modelCatalogueEntry, unitEntry);

            if (catWeapon == null)
            {
                if (!IsAbilityUpgrade(name, modelCatalogueEntry, unitEntry))
                    _logger.LogDebug("Weapon not found: {Name}", weaponEntry.Name);
                continue;
            }

            weapons.Add(BuildWeaponProfile(catWeapon));
        }

        return weapons;
    }

    private WeaponProfile BuildWeaponProfile(CatalogueWeaponEntry catWeapon) =>
        new()
        {
            WeaponName = catWeapon.Name,
            Type = catWeapon.Type,
            Range = catWeapon.Range,
            Profiles = catWeapon.Variants.Select(v => new WeaponVariantProfile
            {
                Variant = v.VariantName,
                Attacks = new ScalarValue(v.AttacksRaw),
                Skill = v.Skill,
                Strength = v.Strength,
                Ap = v.Ap,
                Damage = new ScalarValue(v.DamageRaw),
                Abilities = new WeaponAbilities
                {
                    Torrent = v.Abilities.Torrent,
                    Blast = v.Abilities.Blast,
                    Melta = v.Abilities.Melta,
                    RapidFire = v.Abilities.RapidFire,
                    SustainedHits = v.Abilities.SustainedHits,
                    LethalHits = v.Abilities.LethalHits,
                    DevastatingWounds = v.Abilities.DevastatingWounds,
                    TwinLinked = v.Abilities.TwinLinked,
                    Anti = new Dictionary<string, int>(v.Abilities.Anti),
                },
            }).ToList(),
        };

    private CatalogueWeaponEntry? FindWeaponEntry(
        string name, CatalogueEntry primaryScope, CatalogueEntry unitScope)
    {
        var stripped = StripCountPrefix(name);

        // 1. Primary scope (the resolved model entry)
        var collected = new List<CatalogueWeaponEntry>();
        CollectWeapons(primaryScope, collected, 0);
        var match = FindWeaponInList(stripped, collected);
        if (match != null) return match;

        // 2. Unit scope (the squad/unit entry)
        if (!ReferenceEquals(primaryScope, unitScope))
        {
            collected.Clear();
            CollectWeapons(unitScope, collected, 0);
            match = FindWeaponInList(stripped, collected);
            if (match != null) return match;
        }

        // 3. Global fallback
        return FindWeaponGlobal(stripped);
    }

    private CatalogueWeaponEntry? FindWeaponGlobal(string name)
    {
        foreach (var catalogue in _store.GetAllCatalogues())
        {
            var collected = new List<CatalogueWeaponEntry>();
            foreach (var entry in catalogue.Entries)
                CollectWeapons(entry, collected, 0);
            var match = FindWeaponInList(name, collected);
            if (match != null) return match;
        }
        return null;
    }

    private static CatalogueWeaponEntry? FindWeaponInList(string name, List<CatalogueWeaponEntry> weapons)
    {
        if (weapons.Count == 0) return null;

        var exact = weapons.FirstOrDefault(w =>
            string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        int bestScore = 0;
        CatalogueWeaponEntry? bestMatch = null;
        foreach (var w in weapons)
        {
            int score = Fuzz.TokenSortRatio(name, w.Name);
            if (score > bestScore) { bestScore = score; bestMatch = w; }
        }
        return bestScore >= 85 ? bestMatch : null;
    }

    private static void CollectWeapons(CatalogueEntry entry, List<CatalogueWeaponEntry> weapons, int depth)
    {
        if (depth > 6) return;
        weapons.AddRange(entry.Weapons);
        foreach (var child in entry.Children)
            CollectWeapons(child, weapons, depth + 1);
    }

    // ── Ability helpers ───────────────────────────────────────────────────────

    private static List<AbilityProfile> GatherAbilities(CatalogueEntry entry)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<AbilityProfile>();

        void Add(AbilityProfile a)
        {
            if (seen.Add(a.Name)) result.Add(a);
        }

        foreach (var a in entry.Abilities) Add(a);
        foreach (var child in entry.Children)
            foreach (var a in child.Abilities) Add(a);

        return result;
    }

    private static bool IsLeadingAbility(AbilityProfile ability) =>
        ability.Text.StartsWith("While this model is leading a unit",
            StringComparison.OrdinalIgnoreCase);

    private static List<string> GatherKeywords(CatalogueEntry entry)
    {
        var kws = new HashSet<string>(entry.Keywords, StringComparer.OrdinalIgnoreCase);
        foreach (var child in entry.Children)
            foreach (var kw in child.Keywords)
                kws.Add(kw);
        return kws.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ── Ability upgrade detection ─────────────────────────────────────────────

    private static bool IsAbilityUpgrade(string name, CatalogueEntry modelEntry, CatalogueEntry unitEntry)
    {
        var stripped = StripCountPrefix(name);
        return IsAbilityInTree(stripped, modelEntry) || IsAbilityInTree(stripped, unitEntry);
    }

    private static bool IsAbilityInTree(string name, CatalogueEntry entry)
    {
        if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
        if (entry.Abilities.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))) return true;
        foreach (var child in entry.Children)
            if (IsAbilityInTree(name, child)) return true;
        return false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string ApplyOverride(string name) =>
        _nameOverrides.TryGetValue(name, out var ov) ? ov : name;

    private static string StripCountPrefix(string name)
    {
        var m = CountPrefixRegex.Match(name);
        return m.Success ? m.Groups[1].Value : name;
    }

    private static IReadOnlyDictionary<string, string> LoadNameOverrides()
    {
        const string file = "name_overrides.json";
        if (!File.Exists(file)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var json = File.ReadAllText(file);
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return raw is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(raw, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
