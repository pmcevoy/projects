using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Wh40kArmyEnricher.Contracts;
using Wh40kArmyEnricher.Core.BsData;
using Wh40kArmyEnricher.Core.Models;

namespace Wh40kArmyEnricher.Core;

/// <summary>
/// Resolves each unit/model/weapon in an <see cref="ArmyList"/> against loaded BSData catalogues
/// and produces enriched <see cref="AttackerProfile"/> / <see cref="DefenderProfile"/> pairs.
/// </summary>
public class Enricher
{
    private static readonly Regex RapidFireRegex = new(@"Rapid\s+Fire\s+(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SustainedHitsRegex = new(@"Sustained\s+Hits?\s+(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MeltaRegex = new(@"Melta\s+(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AntiRegex = new(@"Anti-([\w][\w\s]*?)\s+(\d+)\+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InvulnRegex = new(@"(\d)\+\+(?!\+)", RegexOptions.Compiled);
    private static readonly Regex FnpRegex = new(@"(\d)\+\+\+", RegexOptions.Compiled);

    private readonly CatalogueStore _store;
    private readonly NameResolver _resolver;
    private readonly ILogger<Enricher> _logger;

    public Enricher(CatalogueStore store, NameResolver resolver, ILogger<Enricher> logger)
    {
        _store = store;
        _resolver = resolver;
        _logger = logger;
    }

    // ---------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------

    public IReadOnlyList<EnrichedUnit> Enrich(ArmyList army)
    {
        return army.Units
            .Select(u => EnrichUnit(u, army.Faction))
            .ToList();
    }

    // ---------------------------------------------------------------------------
    // Unit enrichment
    // ---------------------------------------------------------------------------

    private EnrichedUnit EnrichUnit(UnitEntry unit, string faction)
    {
        var unitEntry = _resolver.ResolveUnit(unit.Name, _store);
        if (unitEntry == null)
        {
            _logger.LogWarning("Skipping unresolved unit '{Name}'", unit.Name);
            return new EnrichedUnit
            {
                ArmyListEntry = unit,
                Profile = new UnitProfile { Name = unit.Name, Faction = faction }
            };
        }

        var modelProfiles = new List<ModelProfile>();
        var defenderStatline = unitEntry.Statline;
        int defenderWounds = defenderStatline?.Wounds ?? 1;

        foreach (var modelEntry in unit.Models)
        {
            var bsModel = _resolver.ResolveModel(modelEntry.Name, unitEntry, _store)
                          ?? unitEntry; // Fall back to unit entry for single-model units

            // Use model's own statline if available, otherwise use unit statline
            var statline = bsModel.Statline ?? unitEntry.Statline;

            // Apply invuln/FNP granted by selected ability upgrades (e.g. Shield Dome)
            statline = ApplySelectedAbilityUpgrades(statline, modelEntry.Weapons);

            // The first model's statline is used for defender profile (representative)
            if (defenderStatline == null && statline != null)
                defenderStatline = statline;
            if (statline != null)
                defenderWounds = statline.Wounds;

            var weaponProfiles = BuildWeaponProfiles(modelEntry.Weapons, unitEntry, bsModel);

            modelProfiles.Add(new ModelProfile
            {
                ModelName = modelEntry.Name,
                Count = modelEntry.Count,
                Weapons = weaponProfiles
            });
        }

        int totalModels = unit.Models.Sum(m => m.Count);

        var abilities = unitEntry.Abilities.Select(a => new AbilityProfile
        {
            Name = a.Name,
            Text = a.Text
        }).ToList();

        var profile = new UnitProfile
        {
            Name = unit.Name,
            Faction = faction,
            ModelCount = totalModels,
            Keywords = unitEntry.Keywords.ToList(),
            Abilities = abilities,
            Enhancements = unit.Enhancements.ToList(),
            Rerolls = new RerollOptions(),
            CriticalHitsOn = 6,
            Models = modelProfiles,
            Toughness = defenderStatline?.Toughness ?? 4,
            Save = defenderStatline?.Save ?? 7,
            InvulnerableSave = defenderStatline?.InvulnerableSave,
            Wounds = defenderWounds,
            FeelNoPain = defenderStatline?.FeelNoPain
        };

        return new EnrichedUnit
        {
            ArmyListEntry = unit,
            Profile = profile
        };
    }

    // ---------------------------------------------------------------------------
    // Ability-upgrade enrichment (invuln / FNP from selected non-weapon entries)
    // ---------------------------------------------------------------------------

    private UnitStatline? ApplySelectedAbilityUpgrades(
        UnitStatline? statline, IReadOnlyList<WeaponEntry> selectedEntries)
    {
        if (statline == null) return null;

        int? invuln = statline.InvulnerableSave;
        int? fnp = statline.FeelNoPain;

        foreach (var entry in selectedEntries)
        {
            // Look for catalogue entries with this name that have abilities but no weapon profiles
            var abilityEntry = _store.GetAllEntries().FirstOrDefault(e =>
                string.Equals(e.Name, entry.Name, StringComparison.OrdinalIgnoreCase)
                && e.Weapons.Count == 0
                && e.Abilities.Count > 0);

            if (abilityEntry == null) continue;

            foreach (var ability in abilityEntry.Abilities)
            {
                var text = ability.Text + " " + ability.Name;
                if (invuln == null)
                {
                    var m = InvulnRegex.Match(text);
                    if (m.Success) invuln = int.Parse(m.Groups[1].Value);
                }
                if (fnp == null)
                {
                    var m = FnpRegex.Match(text);
                    if (m.Success) fnp = int.Parse(m.Groups[1].Value);
                }
            }
        }

        if (invuln == statline.InvulnerableSave && fnp == statline.FeelNoPain)
            return statline;

        return statline with { InvulnerableSave = invuln, FeelNoPain = fnp };
    }

    // ---------------------------------------------------------------------------
    // Weapon enrichment
    // ---------------------------------------------------------------------------

    private List<WeaponProfile> BuildWeaponProfiles(
        IReadOnlyList<WeaponEntry> weapons,
        CatalogueEntry unitEntry,
        CatalogueEntry modelEntry)
    {
        var result = new List<WeaponProfile>();

        // Group weapon entries by name to detect duplicates (same weapon, different counts)
        var grouped = weapons
            .GroupBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        foreach (var weapon in grouped)
        {
            var profiles = _resolver.ResolveWeaponProfiles(weapon.Name, unitEntry, modelEntry, _store);
            if (profiles == null || profiles.Count == 0)
            {
                // Suppress warning for ability-only entries (e.g. Shield Dome) — these appear
                // as ◦ bullets in the army list alongside weapons but have no weapon profiles.
                // Their invuln/FNP grants are handled by ApplySelectedAbilityUpgrades.
                var isAbilityOnly = _store.GetAllEntries().Any(e =>
                    string.Equals(e.Name, weapon.Name, StringComparison.OrdinalIgnoreCase)
                    && e.Weapons.Count == 0
                    && e.Abilities.Count > 0);
                if (!isAbilityOnly)
                    _logger.LogWarning("Could not resolve weapon '{Name}' for unit/model context", weapon.Name);
                continue;
            }

            var variantProfiles = profiles.Count == 1
                ? [BuildVariantProfile(profiles[0], "default")]
                : profiles
                    .Select(p => BuildVariantProfile(p, ExtractVariantName(p.Name, weapon.Name)))
                    .ToList();

            var first = profiles[0];
            bool isMelee = string.Equals(first.TypeName, "Melee Weapons", StringComparison.OrdinalIgnoreCase)
                || first.Range.Equals("Melee", StringComparison.OrdinalIgnoreCase);

            result.Add(new WeaponProfile
            {
                WeaponName = weapon.Name,
                Type = isMelee ? "Melee" : "Ranged",
                Range = ParseRange(first.Range),
                Profiles = variantProfiles
            });
        }

        return result;
    }

    /// <summary>
    /// Derives a short variant label from a BSData profile name.
    /// E.g. "➤ Hellforged weapons - strike" with entry "Hellforged weapons" → "strike".
    /// </summary>
    private static string ExtractVariantName(string profileName, string weaponEntryName)
    {
        // Strip leading BSData arrow prefix characters
        var trimmed = profileName.TrimStart().TrimStart('➤', '►', '●', '•').Trim();

        // Strip the weapon entry name prefix, leaving just the variant part
        if (trimmed.StartsWith(weaponEntryName, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = trimmed[weaponEntryName.Length..].TrimStart(' ', '-').Trim();
            if (!string.IsNullOrEmpty(remainder)) return remainder;
        }

        return trimmed.Length > 0 ? trimmed : profileName;
    }

    private static WeaponVariantProfile BuildVariantProfile(WeaponProfileData data, string variantName)
    {
        var abilities = ParseWeaponAbilities(data.Keywords);

        return new WeaponVariantProfile
        {
            Variant = variantName,
            Attacks = ParseScalar(data.Attacks),
            Skill = ParseStatWithPlus(data.Skill),
            Strength = ParseStat(data.Strength),
            Ap = ParseApValue(data.AP),
            Damage = ParseScalar(data.Damage),
            Abilities = abilities
        };
    }

    // ---------------------------------------------------------------------------
    // Keyword / ability parsing
    // ---------------------------------------------------------------------------

    private static WeaponAbilities ParseWeaponAbilities(string keywordsRaw)
    {
        if (string.IsNullOrWhiteSpace(keywordsRaw) || keywordsRaw.Trim() == "-")
            return new WeaponAbilities();

        var keywords = keywordsRaw
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        bool torrent = false, blast = false, lethalHits = false, devastatingWounds = false, twinLinked = false;
        int rapidFire = 0, sustainedHits = 0, melta = 0;
        var anti = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var kw in keywords)
        {
            var k = kw.Trim();

            if (k.Equals("Torrent", StringComparison.OrdinalIgnoreCase)) { torrent = true; continue; }
            if (k.Equals("Blast", StringComparison.OrdinalIgnoreCase)) { blast = true; continue; }
            if (k.Equals("Lethal Hits", StringComparison.OrdinalIgnoreCase)) { lethalHits = true; continue; }
            if (k.Equals("Devastating Wounds", StringComparison.OrdinalIgnoreCase)) { devastatingWounds = true; continue; }
            if (k.Equals("Twin-linked", StringComparison.OrdinalIgnoreCase)
                || k.Equals("Twin Linked", StringComparison.OrdinalIgnoreCase)) { twinLinked = true; continue; }

            var rfMatch = RapidFireRegex.Match(k);
            if (rfMatch.Success) { rapidFire = int.Parse(rfMatch.Groups[1].Value); continue; }

            var shMatch = SustainedHitsRegex.Match(k);
            if (shMatch.Success) { sustainedHits = int.Parse(shMatch.Groups[1].Value); continue; }

            var meltaMatch = MeltaRegex.Match(k);
            if (meltaMatch.Success) { melta = int.Parse(meltaMatch.Groups[1].Value); continue; }

            var antiMatch = AntiRegex.Match(k);
            if (antiMatch.Success)
            {
                var targetKeyword = antiMatch.Groups[1].Value.Trim().ToUpperInvariant();
                var threshold = int.Parse(antiMatch.Groups[2].Value);
                anti[targetKeyword] = threshold;
            }
        }

        return new WeaponAbilities
        {
            Torrent = torrent,
            Blast = blast,
            Melta = melta,
            RapidFire = rapidFire,
            SustainedHits = sustainedHits,
            LethalHits = lethalHits,
            DevastatingWounds = devastatingWounds,
            TwinLinked = twinLinked,
            Anti = anti
        };
    }

    // ---------------------------------------------------------------------------
    // Stat parsing helpers
    // ---------------------------------------------------------------------------

    private static ScalarValue ParseScalar(string raw)
    {
        if (int.TryParse(raw, out var n)) return new ScalarValue(n);
        if (string.IsNullOrWhiteSpace(raw)) return new ScalarValue(0);
        return new ScalarValue(raw.Trim());
    }

    private static int ParseStat(string raw)
    {
        if (int.TryParse(raw, out var n)) return n;
        return 0;
    }

    private static int ParseStatWithPlus(string raw)
    {
        var trimmed = raw.TrimEnd('+').Trim();
        return int.TryParse(trimmed, out var n) ? n : 3;
    }

    private static int ParseApValue(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed == "-" || trimmed == "0") return 0;
        if (int.TryParse(trimmed, out var n)) return n; // Already negative in BSData
        return 0;
    }

    private static int ParseRange(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Equals("Melee", StringComparison.OrdinalIgnoreCase))
            return 0;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : 0;
    }
}
