using System.Text.RegularExpressions;
using Wh40kArmyEnricher.Contracts;

namespace Wh40kArmyEnricher.Core;

/// <summary>
/// Constructs all valid <see cref="AttachedUnit"/> combinations for an army.
/// For each non-CHARACTER bodyguard unit, enumerates:
///   - The 0-leader standalone baseline
///   - Each eligible 1-leader combination (primary leaders)
///   - Each eligible primary + support leader 2-leader combination
///   - Each eligible primary + primary 2-leader combination (with a verification note)
/// Every CHARACTER unit also receives its own standalone baseline.
/// </summary>
public class LeaderResolver
{
    // Extracts "Unit A, Unit B, Unit C" from "can be attached to the following units: …."
    private static readonly Regex EligibleUnitsRegex =
        new(@"can be attached to the following units?:\s*(.+?)(?:\s*\.|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Identifies support leaders (Lieutenants, Apothecaries, etc.)
    private const string SupportMarker = "already contains a Leader";

    // Leading-ability text patterns used to compute effective re-rolls and crit threshold.
    private static readonly Regex HitRerollOnesRegex =
        new(@"re-?roll hit rolls of 1", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HitRerollAllRegex =
        new(@"re-?roll (?:all )?hit rolls\b(?! of 1)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WoundRerollOnesRegex =
        new(@"re-?roll wound rolls of 1", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WoundRerollAllRegex =
        new(@"re-?roll (?:all )?wound rolls\b(?! of 1)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CritHitsOnRegex =
        new(@"Critical Hits? are scored on a (\d)\+|unmodified Hit roll of (\d)\+ scores a Critical Hit",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Weapon stat modifier: "add N to the {stats} characteristic(s) of melee/ranged/all weapons"
    private static readonly Regex WeaponModifierRegex =
        new(@"add (\d+) to the ([\w\s]+?) characteristics? of (melee|ranged|all) weapons",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Abilities that require every model in the unit to have them (intersection semantics).
    // Attaching a leader who lacks one of these removes it from the combined unit.
    private static readonly HashSet<string> IndividualAbilities =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Stealth", "Infiltrate", "Deep Strike", "Lone Operative"
        };

    private sealed record LeaderInfo(
        UnitProfile Unit,
        bool IsSupport,
        IReadOnlyList<string> EligibleBodyguards);

    private sealed record WeaponModifier(int Delta, string Stat, string WeaponFilter);

    // ---------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------

    public IReadOnlyList<AttachedUnit> BuildAttachedUnits(IReadOnlyList<UnitProfile> army)
    {
        var result = new List<AttachedUnit>();

        // Every unit gets a standalone (0-leader) baseline entry
        foreach (var unit in army)
            result.Add(Standalone(unit));

        var leaderInfos = army
            .Where(IsCharacter)
            .Select(ParseLeaderInfo)
            .OfType<LeaderInfo>()
            .ToList();

        var primaryLeaders = leaderInfos.Where(l => !l.IsSupport).ToList();
        var supportLeaders = leaderInfos.Where(l => l.IsSupport).ToList();

        foreach (var bodyguard in army.Where(u => !IsCharacter(u)))
        {
            var eligible = primaryLeaders.Where(l => CanLead(l, bodyguard.Name)).ToList();

            // 1-leader: each eligible primary
            foreach (var primary in eligible)
                result.Add(Combine(bodyguard, [primary.Unit], []));

            // 2-leader: primary + support leader
            foreach (var primary in eligible)
                foreach (var support in supportLeaders)
                    if (support.Unit != primary.Unit)
                        result.Add(Combine(bodyguard, [primary.Unit, support.Unit], []));

            // 2-leader: two primaries (eligibility of the second is unverified).
            // Skip pairs where both leaders share the same unit name — two units of the same
            // datasheet type cannot both attach to the same bodyguard.
            for (int i = 0; i < eligible.Count; i++)
                for (int j = i + 1; j < eligible.Count; j++)
                {
                    if (eligible[i].Unit.Name.Equals(eligible[j].Unit.Name, StringComparison.OrdinalIgnoreCase))
                        continue;
                    result.Add(Combine(
                        bodyguard,
                        [eligible[i].Unit, eligible[j].Unit],
                        ["Second leader eligibility unverified — confirm manually"]));
                }
        }

        return result;
    }

    // ---------------------------------------------------------------------------
    // AttachedUnit construction
    // ---------------------------------------------------------------------------

    private static AttachedUnit Standalone(UnitProfile unit) => new()
    {
        Bodyguard = unit,
        Leaders = [],
        EffectiveKeywords = unit.Keywords.ToList(),
        EffectiveRerolls = unit.Rerolls,
        EffectiveCritHitsOn = unit.CriticalHitsOn,
        EffectiveAbilities = unit.Abilities.ToList(),
        Notes = []
    };

    private static AttachedUnit Combine(
        UnitProfile bodyguard,
        IReadOnlyList<UnitProfile> leaders,
        IReadOnlyList<string> extraNotes)
    {
        // Keywords: union of all participants
        var keywords = new HashSet<string>(bodyguard.Keywords, StringComparer.OrdinalIgnoreCase);
        foreach (var leader in leaders)
            foreach (var kw in leader.Keywords)
                keywords.Add(kw);

        // Re-rolls and crit threshold: parsed from each leader's leadingAbilities text
        bool hitRerollOnes = false, hitRerollAll = false;
        bool woundRerollOnes = false, woundRerollAll = false;
        int critHitsOn = bodyguard.CriticalHitsOn;
        var grantedAbilities = new List<AbilityProfile>();
        var abilityNotes = new List<string>();

        foreach (var leader in leaders)
        {
            foreach (var ability in leader.LeadingAbilities)
            {
                var t = ability.Text;
                var source = $"{leader.Name} / {ability.Name}";

                if (HitRerollOnesRegex.IsMatch(t) && !hitRerollOnes)
                {
                    hitRerollOnes = true;
                    abilityNotes.Add($"hitRerollOnes set by {source}");
                }
                if (HitRerollAllRegex.IsMatch(t) && !hitRerollAll)
                {
                    hitRerollAll = true;
                    abilityNotes.Add($"hitRerollAll set by {source}");
                }
                if (WoundRerollOnesRegex.IsMatch(t) && !woundRerollOnes)
                {
                    woundRerollOnes = true;
                    abilityNotes.Add($"woundRerollOnes set by {source}");
                }
                if (WoundRerollAllRegex.IsMatch(t) && !woundRerollAll)
                {
                    woundRerollAll = true;
                    abilityNotes.Add($"woundRerollAll set by {source}");
                }
                var cm = CritHitsOnRegex.Match(t);
                if (cm.Success)
                {
                    var threshold = int.Parse(cm.Groups[1].Success ? cm.Groups[1].Value : cm.Groups[2].Value);
                    if (threshold < critHitsOn)
                    {
                        abilityNotes.Add($"criticalHitsOn reduced from {critHitsOn} to {threshold} by {source}");
                        critHitsOn = threshold;
                    }
                }
                grantedAbilities.Add(ability);
            }
        }

        // "re-roll all" subsumes "re-roll ones"
        if (hitRerollAll) hitRerollOnes = false;
        if (woundRerollAll) woundRerollOnes = false;

        var rerolls = new RerollOptions
        {
            HitRerollOnes  = bodyguard.Rerolls.HitRerollOnes  || hitRerollOnes,
            HitRerollAll   = bodyguard.Rerolls.HitRerollAll   || hitRerollAll,
            WoundRerollOnes = bodyguard.Rerolls.WoundRerollOnes || woundRerollOnes,
            WoundRerollAll  = bodyguard.Rerolls.WoundRerollAll  || woundRerollAll
        };

        // Weapon stat modifiers from leading abilities (e.g. +1 Attacks and Strength to melee weapons).
        // Build a modified copy of the bodyguard's models only if at least one modifier applies.
        var modifiedModels = bodyguard.Models.ToList();
        bool weaponsModified = false;
        foreach (var leader in leaders)
        {
            foreach (var ability in leader.LeadingAbilities)
            {
                var mods = ParseWeaponModifiers(ability.Text);
                if (mods.Count == 0) continue;
                var (newModels, modNotes) = ApplyWeaponModifiers(modifiedModels, mods,
                    $"{leader.Name} / {ability.Name}");
                if (modNotes.Count > 0)
                {
                    modifiedModels = newModels;
                    weaponsModified = true;
                    abilityNotes.AddRange(modNotes);
                }
            }
        }
        var effectiveBodyguard = weaponsModified ? bodyguard with { Models = modifiedModels } : bodyguard;

        // Abilities: bodyguard base, minus individual abilities that any leader lacks,
        // plus all leading abilities granted by the attached leaders.
        var effectiveAbilities = bodyguard.Abilities
            .Where(a => !IsIndividualAbility(a.Name) || leaders.All(l => HasAbility(l, a.Name)))
            .ToList();
        effectiveAbilities.AddRange(grantedAbilities);

        return new AttachedUnit
        {
            Bodyguard = effectiveBodyguard,
            Leaders = leaders.ToList(),
            EffectiveKeywords = [.. keywords],
            EffectiveRerolls = rerolls,
            EffectiveCritHitsOn = critHitsOn,
            EffectiveAbilities = effectiveAbilities,
            Notes = [.. extraNotes, .. abilityNotes]
        };
    }

    // ---------------------------------------------------------------------------
    // Leader parsing
    // ---------------------------------------------------------------------------

    // Matches ■-bulleted unit names in multi-line Leader ability text (U+25A0, the format
    // produced by BSData when multiple eligible bodyguards are listed one per line).
    private static readonly Regex BulletedUnitRegex =
        new(@"■\s*(.+)", RegexOptions.Compiled);

    private static LeaderInfo? ParseLeaderInfo(UnitProfile unit)
    {
        var leaderAbility = unit.Abilities
            .FirstOrDefault(a => a.Name.Equals("Leader", StringComparison.OrdinalIgnoreCase));
        if (leaderAbility == null) return null;

        var text = leaderAbility.Text;
        bool isSupport = text.Contains(SupportMarker, StringComparison.OrdinalIgnoreCase);

        // BSData formats eligible bodyguards either as:
        //   (a) a ■-bulleted multi-line list: "■ Unit A\n■ Unit B\n..."
        //   (b) a comma-separated single line: "following units: A, B, C."
        var bulletedMatches = BulletedUnitRegex.Matches(text);
        List<string> eligibleBodyguards;
        if (bulletedMatches.Count > 0)
        {
            eligibleBodyguards = bulletedMatches
                .Select(m => m.Groups[1].Value.Trim())
                .ToList();
        }
        else
        {
            var m = EligibleUnitsRegex.Match(text);
            eligibleBodyguards = m.Success
                ? m.Groups[1].Value
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .ToList()
                : [];
        }

        return new LeaderInfo(unit, isSupport, eligibleBodyguards);
    }

    /// <summary>
    /// Returns true if <paramref name="leader"/> is eligible to lead <paramref name="bodyguardName"/>.
    /// Uses case-insensitive contains matching to handle minor naming variations.
    /// </summary>
    private static bool CanLead(LeaderInfo leader, string bodyguardName) =>
        leader.EligibleBodyguards.Any(eligible =>
            eligible.Equals(bodyguardName, StringComparison.OrdinalIgnoreCase)
            || bodyguardName.Contains(eligible, StringComparison.OrdinalIgnoreCase)
            || eligible.Contains(bodyguardName, StringComparison.OrdinalIgnoreCase));

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    // ---------------------------------------------------------------------------
    // Weapon modifier parsing and application
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Extracts weapon stat modifiers from a leading ability text.
    /// Handles "add N to the Attacks and Strength characteristic of melee weapons" and
    /// similar patterns. Splits compound stat lists like "Attacks and Strength" into
    /// individual <see cref="WeaponModifier"/> entries.
    /// </summary>
    private static List<WeaponModifier> ParseWeaponModifiers(string text)
    {
        var result = new List<WeaponModifier>();
        foreach (Match m in WeaponModifierRegex.Matches(text))
        {
            int delta = int.Parse(m.Groups[1].Value);
            string filter = m.Groups[3].Value.ToLowerInvariant();
            var stats = m.Groups[2].Value
                .Split([" and ", ","], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var stat in stats)
                result.Add(new WeaponModifier(delta, stat.Trim(), filter));
        }
        return result;
    }

    /// <summary>
    /// Applies a list of <see cref="WeaponModifier"/> entries to each matching weapon profile
    /// across all model entries, returning a new model list and a note per change.
    /// </summary>
    private static (List<ModelProfile> Models, List<string> Notes) ApplyWeaponModifiers(
        IReadOnlyList<ModelProfile> models,
        IReadOnlyList<WeaponModifier> modifiers,
        string source)
    {
        var notes = new List<string>();
        var newModels = new List<ModelProfile>();

        foreach (var model in models)
        {
            var newWeapons = new List<WeaponProfile>();
            foreach (var weapon in model.Weapons)
            {
                bool isMelee = weapon.Type.Equals("Melee", StringComparison.OrdinalIgnoreCase);
                var applicable = modifiers
                    .Where(m => m.WeaponFilter == "all"
                        || (m.WeaponFilter == "melee" && isMelee)
                        || (m.WeaponFilter == "ranged" && !isMelee))
                    .ToList();

                if (applicable.Count == 0) { newWeapons.Add(weapon); continue; }

                var newProfiles = weapon.Profiles
                    .Select(p => applicable.Aggregate(p, (acc, mod) =>
                        ApplyStat(acc, mod, weapon.WeaponName, source, notes)))
                    .ToList();
                newWeapons.Add(weapon with { Profiles = newProfiles });
            }
            newModels.Add(model with { Weapons = newWeapons });
        }

        return (newModels, notes);
    }

    /// <summary>
    /// Applies a single <see cref="WeaponModifier"/> to one <see cref="WeaponVariantProfile"/>.
    /// Appends a note to <paramref name="notes"/> if a change was made or could not be made.
    /// Supported stats: Attacks, Strength, Damage. AP and Skill are not modified (game semantics
    /// for those stats differ from a simple numeric delta).
    /// </summary>
    private static WeaponVariantProfile ApplyStat(
        WeaponVariantProfile profile, WeaponModifier mod,
        string weaponName, string source, List<string> notes)
    {
        var label = profile.Variant == "default"
            ? $"'{weaponName}'"
            : $"'{weaponName}' ({profile.Variant})";

        switch (mod.Stat.ToLowerInvariant())
        {
            case "attacks":
                if (profile.Attacks.IsInt)
                {
                    var newVal = profile.Attacks.IntValue + mod.Delta;
                    notes.Add($"Attacks of {label} increased by {mod.Delta} to {newVal} [{source}]");
                    return profile with { Attacks = new ScalarValue(newVal) };
                }
                notes.Add($"Attacks of {label} could not be modified (variable '{profile.Attacks}') [{source}]");
                return profile;

            case "strength":
                var newStr = profile.Strength + mod.Delta;
                notes.Add($"Strength of {label} increased by {mod.Delta} to {newStr} [{source}]");
                return profile with { Strength = newStr };

            case "damage":
                if (profile.Damage.IsInt)
                {
                    var newDmg = profile.Damage.IntValue + mod.Delta;
                    notes.Add($"Damage of {label} increased by {mod.Delta} to {newDmg} [{source}]");
                    return profile with { Damage = new ScalarValue(newDmg) };
                }
                notes.Add($"Damage of {label} could not be modified (variable '{profile.Damage}') [{source}]");
                return profile;

            default:
                return profile;
        }
    }

    private static bool IsCharacter(UnitProfile u) =>
        u.Keywords.Any(k => k.Equals("CHARACTER", StringComparison.OrdinalIgnoreCase));

    private static bool IsIndividualAbility(string name) =>
        IndividualAbilities.Contains(name)
        || name.StartsWith("Scouts", StringComparison.OrdinalIgnoreCase);

    private static bool HasAbility(UnitProfile unit, string name) =>
        unit.Abilities.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        || unit.LeadingAbilities.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
