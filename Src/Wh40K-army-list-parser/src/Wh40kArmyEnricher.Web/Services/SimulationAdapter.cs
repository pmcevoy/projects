using Wh40kArmyEnricher.Contracts;
using Wh40kArmyEnricher.Core.Simulation;
using Wh40kArmyEnricher.Web.Models;

namespace Wh40kArmyEnricher.Web.Services;

public class SimulationAdapter
{
    private readonly CombatSimulator _simulator;

    public SimulationAdapter()
    {
        _simulator = new CombatSimulator(new DiceRoller());
    }

    // ---------------------------------------------------------------------------
    // Weapon equality key — groups weapon selections that share the same attack pipeline.
    // Attacks are intentionally excluded: Marshal (7A) and Sword Brother (3A) carrying the
    // same MCPW profile are grouped together and their attacks aggregated.
    // ---------------------------------------------------------------------------

    private record WeaponGroupKey(
        string Type, int Skill, int Strength, int Ap, string Damage,
        bool Torrent, bool Blast, int Melta, int RapidFire, int SustainedHits,
        bool LethalHits, bool DevastatingWounds, bool TwinLinked, string Anti);

    private record WeaponGroupData(
        string WeaponName,
        string WeaponType,
        WeaponVariantProfile Variant,
        List<(DiceExpression Attacks, int ModelCount)> Contributions);

    // ---------------------------------------------------------------------------
    // Public entry point
    // ---------------------------------------------------------------------------

    public SimulationResponse Run(
        IReadOnlyList<UnitProfile> attackerUnits,
        UnitProfile defender,
        SimulationRequest request)
    {
        if (request.WeaponSelections.Count == 0)
            return Error("No weapons selected.");

        var weaponGroups = BuildWeaponGroups(attackerUnits, request.WeaponSelections);
        if (weaponGroups.Count == 0)
            return Error("No matching weapons found for the selected weapon names.");

        var rerolls = new SimRerollOptions
        {
            HitRerollOnes   = request.HitRerolls == "ones",
            HitRerollAll    = request.HitRerolls == "all",
            WoundRerollOnes = request.WoundRerolls == "ones",
            WoundRerollAll  = request.WoundRerolls == "all",
        };

        // Cover bonus cancelled by Ignores Cover.
        int coverBonus = (request.InCover && !request.IgnoresCover) ? 1 : 0;

        var simWeapons = weaponGroups.Select(g =>
        {
            var aggregatedAttacks = AggregateAttacks(g.Contributions);

            // Apply abilities overrides: OR flags / merge Anti.
            var abilities = ApplyAbilityOverrides(g.Variant.Abilities, request);

            // BS/WS characteristic modifier: +1 means target number -1 (e.g. 4+ → 3+).
            int effectiveSkill = g.Variant.Skill - request.BsWsModifier;

            // Strength modifier.
            int effectiveStrength = g.Variant.Strength + request.StrengthModifier;

            // AP modifier: contracts AP is negative; sim AP is positive; +1 modifier = more AP (higher positive).
            int effectiveSimAp = -g.Variant.Ap + request.ApModifier;

            return new SimWeaponProfile
            {
                Name            = g.WeaponName,
                Attacks         = aggregatedAttacks,
                Skill           = Math.Clamp(effectiveSkill, 2, 6),
                Strength        = Math.Max(1, effectiveStrength),
                Ap              = Math.Max(0, effectiveSimAp),
                Damage          = ParseDice(g.Variant.Damage),
                WithinHalfRange = request.WithinHalfRange,
                Abilities       = abilities,
                AttackModifier  = request.AttackModifier,
                DamageModifier  = request.DamageModifier,
                RerollDamageDice = request.RerollDamageDice,
                RerollAttackDice = request.RerollAttackDice,
            };
        }).ToList();

        var attackerName = string.Join(" + ", attackerUnits.Select(u => u.Name));
        var attacker = new SimAttackerProfile
        {
            Name               = attackerName,
            Weapons            = simWeapons,
            Rerolls            = rerolls,
            CriticalHitsOn     = request.CriticalHitsOn5 ? 5 : attackerUnits.Min(u => u.CriticalHitsOn),
            CriticalWoundsOn   = request.CritWoundOn5 ? 5 : 6,
            WoundRollModifier  = request.WoundRollModifier,
            HitRollModifier    = request.HitRollModifier,
            FishForCriticalHits   = request.FishForCriticalHits,
            FishForCriticalWounds = request.FishForCriticalWounds,
        };

        var defenderProfile = new SimDefenderProfile
        {
            Name             = defender.Name,
            Models           = defender.ModelCount,
            Toughness        = Math.Max(1, defender.Toughness + request.ToughnessModifier),
            Save             = defender.Save + coverBonus,
            InvulnerableSave = defender.InvulnerableSave,
            Wounds           = defender.Wounds,
            FeelNoPain       = defender.FeelNoPain,
            Keywords         = defender.Keywords,
        };

        var config = new SimulationConfig
        {
            SimulationRuns = request.Runs > 0 ? request.Runs : 10000,
            Attacker       = attacker,
            Defender       = defenderProfile,
        };

        var (raw, aggregate, perWeapon) = _simulator.Run(config);
        var result = SimulationResult.Compute(raw);

        int woundsPerModel = defender.Wounds > 0 ? defender.Wounds : 1;
        double expectedKills = result.Mean / woundsPerModel;
        double probKillAtLeastOne = raw.Count(d => d >= woundsPerModel) / (double)raw.Count;

        // Weapon description: single weapon uses variant label; multiple uses comma-separated names.
        string weaponDescription = weaponGroups.Count == 1
            ? BuildSingleWeaponLabel(weaponGroups[0], request.WeaponSelections[0].VariantName)
            : string.Join(", ", weaponGroups.Select(g => g.WeaponName).Distinct());

        // Per-weapon breakdown only populated when there are multiple groups (single group → use StageStats).
        var breakdown = perWeapon.Count > 1
            ? perWeapon.Select(w => new WeaponGroupResult { WeaponName = w.WeaponName, Stats = w.Stats }).ToList()
            : new List<WeaponGroupResult>();

        return new SimulationResponse
        {
            Success              = true,
            AttackerName         = attackerName,
            WeaponDescription    = weaponDescription,
            DefenderName         = defender.Name,
            Runs                 = result.Runs,
            MeanDamage           = Math.Round(result.Mean, 2),
            StdDeviation         = Math.Round(result.StdDeviation, 2),
            ExpectedKills        = Math.Round(expectedKills, 2),
            ProbKillAtLeastOne   = Math.Round(probKillAtLeastOne, 3),
            MinDamage            = result.Min,
            MaxDamage            = result.Max,
            StageStats           = aggregate,
            WeaponBreakdown      = breakdown,
        };
    }

    // ---------------------------------------------------------------------------
    // Abilities override — merges user toggles into the base weapon abilities.
    // ---------------------------------------------------------------------------

    private static SimWeaponAbilities ApplyAbilityOverrides(WeaponAbilities source, SimulationRequest request)
    {
        // Merge Anti: combine existing Anti with any override (lower threshold wins).
        var mergedAnti = new Dictionary<string, int>(
            source.Anti, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(request.AntiOverrideKeyword))
        {
            mergedAnti.TryGetValue(request.AntiOverrideKeyword, out int existing);
            mergedAnti[request.AntiOverrideKeyword] = existing == 0
                ? request.AntiOverrideThreshold
                : Math.Min(existing, request.AntiOverrideThreshold);
        }

        return new SimWeaponAbilities
        {
            Torrent           = source.Torrent,
            Blast             = source.Blast || request.BlastOverride,
            Melta             = source.Melta,
            RapidFire         = source.RapidFire,
            SustainedHits     = source.SustainedHits > 0 ? source.SustainedHits : (request.SustainedHitsOverride ? 1 : 0),
            LethalHits        = source.LethalHits || request.LethalHitsOverride,
            DevastatingWounds = source.DevastatingWounds || request.DevastatingWoundsOverride,
            TwinLinked        = source.TwinLinked,
            IndirectFire      = request.IndirectFireOverride,
            Anti              = mergedAnti,
        };
    }

    // ---------------------------------------------------------------------------
    // Weapon grouping
    // ---------------------------------------------------------------------------

    private static List<WeaponGroupData> BuildWeaponGroups(
        IReadOnlyList<UnitProfile> attackerUnits,
        IReadOnlyList<WeaponSelection> selections)
    {
        var groups = new Dictionary<WeaponGroupKey, WeaponGroupData>();

        foreach (var sel in selections)
        {
            WeaponVariantProfile? variant = null;
            string weaponType = "Melee";

            // Find the matching variant in the attacker units.
            foreach (var unit in attackerUnits)
            {
                foreach (var model in unit.Models)
                {
                    if (!string.IsNullOrEmpty(sel.ModelName) &&
                        !string.Equals(model.ModelName, sel.ModelName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (var wp in model.Weapons)
                    {
                        if (!string.Equals(wp.WeaponName, sel.WeaponName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        variant = wp.Profiles.FirstOrDefault(p =>
                            string.Equals(p.Variant, sel.VariantName, StringComparison.OrdinalIgnoreCase))
                            ?? wp.Profiles.FirstOrDefault();

                        if (variant != null)
                        {
                            weaponType = wp.Type;
                            break;
                        }
                    }
                    if (variant != null) break;
                }
                if (variant != null) break;
            }

            if (variant == null) continue;

            var key = MakeGroupKey(weaponType, variant);
            var attackExpr = ParseDice(variant.Attacks);
            int modelCount = sel.ModelCount > 0 ? sel.ModelCount : 1;

            if (groups.TryGetValue(key, out var existing))
                existing.Contributions.Add((attackExpr, modelCount));
            else
                groups[key] = new WeaponGroupData(
                    sel.WeaponName, weaponType, variant,
                    new List<(DiceExpression, int)> { (attackExpr, modelCount) });
        }

        return groups.Values.ToList();
    }

    private static WeaponGroupKey MakeGroupKey(string type, WeaponVariantProfile v)
    {
        var anti = string.Join(",", v.Abilities.Anti
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.Key}:{x.Value}"));

        return new WeaponGroupKey(
            type, v.Skill, v.Strength, v.Ap, v.Damage.ToString(),
            v.Abilities.Torrent, v.Abilities.Blast, v.Abilities.Melta,
            v.Abilities.RapidFire, v.Abilities.SustainedHits, v.Abilities.LethalHits,
            v.Abilities.DevastatingWounds, v.Abilities.TwinLinked, anti);
    }

    /// <summary>
    /// Aggregates attack contributions using DiceExpression.Scale and Add.
    /// 3 models × D6 attacks → 3D6; 1×7 + 1×6 + 4×3 → Fixed(25).
    /// </summary>
    private static DiceExpression AggregateAttacks(List<(DiceExpression Attacks, int ModelCount)> contributions)
    {
        var total = DiceExpression.Fixed(0);
        foreach (var (attacks, count) in contributions)
            total = total.Add(attacks.Scale(count));
        return total;
    }

    private static string BuildSingleWeaponLabel(WeaponGroupData group, string variantName)
    {
        var variantLabel = string.Equals(variantName, "default", StringComparison.OrdinalIgnoreCase)
            ? "" : $" [{variantName}]";
        return $"{group.WeaponName}{variantLabel}";
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static SimulationResponse Error(string message) =>
        new() { Success = false, Error = message };

    private static DiceExpression ParseDice(ScalarValue scalar) =>
        scalar.IsInt ? DiceExpression.Fixed(scalar.IntValue) : DiceExpression.Parse(scalar.StringValue);

    private static SimWeaponAbilities MapAbilities(WeaponAbilities a) => new()
    {
        Torrent           = a.Torrent,
        Blast             = a.Blast,
        Melta             = a.Melta,
        RapidFire         = a.RapidFire,
        SustainedHits     = a.SustainedHits,
        LethalHits        = a.LethalHits,
        DevastatingWounds = a.DevastatingWounds,
        TwinLinked        = a.TwinLinked,
        Anti              = a.Anti,
    };
}
