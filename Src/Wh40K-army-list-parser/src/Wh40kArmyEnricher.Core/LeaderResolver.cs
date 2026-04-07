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

        // Abilities: bodyguard base, minus individual abilities that any leader lacks,
        // plus all leading abilities granted by the attached leaders.
        var effectiveAbilities = bodyguard.Abilities
            .Where(a => !IsIndividualAbility(a.Name) || leaders.All(l => HasAbility(l, a.Name)))
            .ToList();
        effectiveAbilities.AddRange(grantedAbilities);

        return new AttachedUnit
        {
            Bodyguard = bodyguard,
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

    private static bool IsCharacter(UnitProfile u) =>
        u.Keywords.Any(k => k.Equals("CHARACTER", StringComparison.OrdinalIgnoreCase));

    private static bool IsIndividualAbility(string name) =>
        IndividualAbilities.Contains(name)
        || name.StartsWith("Scouts", StringComparison.OrdinalIgnoreCase);

    private static bool HasAbility(UnitProfile unit, string name) =>
        unit.Abilities.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        || unit.LeadingAbilities.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
