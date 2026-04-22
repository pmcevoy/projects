using Wh40kArmyEnricher.Core.Contracts;

namespace Wh40kArmyEnricher.Core.Simulation;

public sealed class SimulationAdapter
{
    private readonly CombatSimulator _simulator;

    public SimulationAdapter() : this(new CombatSimulator()) { }
    public SimulationAdapter(CombatSimulator simulator) => _simulator = simulator;

    public SimulationResponse Adapt(SimulationRequest request, UnitProfile attacker, UnitProfile defender)
    {
        // Build defender profile
        int defenderModels = request.DefenderModelCount > 0 ? request.DefenderModelCount : defender.ModelCount;
        int defenderSave = defender.Save;

        // Cover adds +1 to saving throw roll, equivalent to lowering the required target by 1.
        if (request.Cover && !request.IgnoresCover)
            defenderSave -= 1;

        int? defenderFnp = defender.FeelNoPain;
        if (defenderFnp == null && request.FnpOverride > 0)
            defenderFnp = request.FnpOverride;

        var simDefender = new SimDefenderProfile
        {
            Name = defender.Name,
            Models = defenderModels,
            Toughness = defender.Toughness + request.ToughnessModifier,
            Save = defenderSave,
            InvulnerableSave = defender.InvulnerableSave,
            Wounds = defender.Wounds,
            FeelNoPain = defenderFnp,
            Keywords = defender.Keywords,
        };

        // Group weapon selections by profile equality key (attacks excluded)
        var groups = new Dictionary<WeaponGroupKey, WeaponGroup>();

        foreach (var sel in request.WeaponSelections)
        {
            var (model, weaponProfile, variant) = FindWeapon(attacker, sel);
            if (model == null || weaponProfile == null || variant == null) continue;

            int modelCount = sel.ModelCount > 0 ? sel.ModelCount : model.Count;

            int simAp = Math.Max(0, -variant.Ap + request.ApModifier);
            int effectiveSkill = variant.Skill + request.BsWsModifier;
            int effectiveStrength = variant.Strength + request.StrengthModifier;
            var anti = MergeAnti(variant.Abilities.Anti, request);
            var simAbilities = BuildSimAbilities(variant.Abilities, request, anti);

            var key = new WeaponGroupKey(
                weaponProfile.Type, effectiveSkill, effectiveStrength, simAp,
                variant.Damage.ToString(),
                simAbilities.Torrent, simAbilities.Blast, simAbilities.Melta,
                simAbilities.RapidFire, simAbilities.SustainedHits,
                simAbilities.LethalHits, simAbilities.DevastatingWounds,
                simAbilities.TwinLinked, simAbilities.IndirectFire,
                NormaliseAnti(anti));

            string displayName = weaponProfile.WeaponName;
            if (!string.IsNullOrEmpty(variant.Variant))
                displayName += $" ({variant.Variant})";

            var attackExpr = DiceExpression.Parse(variant.Attacks.ToString());
            var scaled = attackExpr.Scale(modelCount);

            if (groups.TryGetValue(key, out var existing))
                groups[key] = existing with { Attacks = existing.Attacks.Add(scaled) };
            else
                groups[key] = new WeaponGroup(displayName, scaled, simAbilities);
        }

        // Build SimWeaponProfile per group
        int hitMod = Math.Clamp(request.HitRollModifier, -1, 1);
        int woundMod = Math.Clamp(request.WoundRollModifier, -1, 1);

        var simWeapons = groups.Select(kvp => new SimWeaponProfile
        {
            WeaponName = kvp.Value.DisplayName,
            Type = kvp.Key.Type,
            Attacks = kvp.Value.Attacks,
            Skill = kvp.Key.Skill,
            Strength = kvp.Key.Strength,
            Ap = kvp.Key.Ap,
            Damage = DiceExpression.Parse(kvp.Key.Damage),
            Abilities = kvp.Value.Abilities,
            AttackModifier = request.AttackModifier,
            HitRollModifier = hitMod,
            WoundRollModifier = woundMod,
            DamageModifier = request.DamageModifier,
            RerollAttackDice = request.RerollAttackDice,
            RerollDamageDice = request.RerollDamageDice,
            WithinHalfRange = request.WithinHalfRange,
        }).ToList();

        var simAttacker = new SimAttackerProfile
        {
            Name = attacker.Name,
            Weapons = simWeapons,
            HitRerollOnes = request.RerollHitOnes,
            HitRerollAll = request.RerollHitAll,
            FishForCriticalHits = request.FishForCritHits,
            WoundRerollOnes = request.RerollWoundOnes,
            WoundRerollAll = request.RerollWoundAll,
            FishForCriticalWounds = request.FishForCritWounds,
            CriticalHitsOn = request.CritHitOn5Plus ? 5 : attacker.CriticalHitsOn,
            CriticalWoundsOn = request.CritWoundOn5Plus ? 5 : 6,
        };

        var (damages, kills, aggregate, perWeapon) = _simulator.Run(simAttacker, simDefender);

        double meanDamage = damages.Average();
        double meanKills = kills.Average();
        double pkill = kills.Count(k => k >= 1) / (double)kills.Count;
        double variance = damages.Sum(d => (d - meanDamage) * (d - meanDamage)) / damages.Count;

        return new SimulationResponse
        {
            MeanDamage = meanDamage,
            ExpectedKills = meanKills,
            PKillAtLeastOne = pkill,
            StdDev = Math.Sqrt(variance),
            StageStats = aggregate,
            WeaponBreakdown = simWeapons.Count > 1 ? perWeapon.ToList() : [],
        };
    }

    private static (ModelProfile? model, WeaponProfile? weapon, WeaponVariantProfile? variant)
        FindWeapon(UnitProfile attacker, WeaponSelection sel)
    {
        foreach (var model in attacker.Models)
        {
            if (!string.IsNullOrEmpty(sel.ModelName) &&
                !string.Equals(model.ModelName, sel.ModelName, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var weapon in model.Weapons)
            {
                if (!string.Equals(weapon.WeaponName, sel.WeaponName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var variant = string.IsNullOrEmpty(sel.VariantName)
                    ? weapon.Profiles.FirstOrDefault()
                    : weapon.Profiles.FirstOrDefault(v =>
                        string.Equals(v.Variant, sel.VariantName, StringComparison.OrdinalIgnoreCase));

                if (variant != null)
                    return (model, weapon, variant);
            }
        }
        return (null, null, null);
    }

    private static SimWeaponAbilities BuildSimAbilities(
        WeaponAbilities src, SimulationRequest req, IReadOnlyDictionary<string, int> anti)
    {
        return new SimWeaponAbilities
        {
            Torrent = src.Torrent,
            Blast = src.Blast || req.BlastOverride,
            Melta = src.Melta,
            RapidFire = src.RapidFire,
            SustainedHits = src.SustainedHits > 0 ? src.SustainedHits : (req.SustainedHitsOverride ? 1 : 0),
            LethalHits = src.LethalHits || req.LethalHitsOverride,
            DevastatingWounds = src.DevastatingWounds || req.DevastatingWoundsOverride,
            TwinLinked = src.TwinLinked,
            IndirectFire = req.IndirectFire,
            Anti = anti,
        };
    }

    private static IReadOnlyDictionary<string, int> MergeAnti(
        Dictionary<string, int> baseAnti, SimulationRequest req)
    {
        var result = new Dictionary<string, int>(baseAnti, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(req.AntiKeyword) && req.AntiThreshold > 0)
        {
            if (!result.TryGetValue(req.AntiKeyword, out int existing) || req.AntiThreshold < existing)
                result[req.AntiKeyword] = req.AntiThreshold;
        }
        return result;
    }

    private static string NormaliseAnti(IReadOnlyDictionary<string, int> anti)
    {
        if (anti.Count == 0) return "";
        return string.Join(",", anti.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                                    .Select(kv => $"{kv.Key}:{kv.Value}"));
    }

    private record WeaponGroup(string DisplayName, DiceExpression Attacks, SimWeaponAbilities Abilities);

    private record WeaponGroupKey(
        WeaponType Type, int Skill, int Strength, int Ap, string Damage,
        bool Torrent, bool Blast, int Melta, int RapidFire, int SustainedHits,
        bool LethalHits, bool DevastatingWounds, bool TwinLinked, bool IndirectFire,
        string Anti);
}
