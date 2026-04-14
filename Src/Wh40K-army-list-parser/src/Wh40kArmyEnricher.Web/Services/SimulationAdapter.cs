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

    /// <summary>
    /// Builds a SimulationConfig from the selected attacker units, weapon choice, and defender,
    /// runs the simulation, and returns a <see cref="SimulationResponse"/>.
    /// </summary>
    public SimulationResponse Run(
        IReadOnlyList<UnitProfile> attackerUnits,
        UnitProfile defender,
        SimulationRequest request)
    {
        // Find the weapon variant across all models in all selected attacker units
        WeaponVariantProfile? variant = null;
        string resolvedWeaponName = request.WeaponName;
        int modelCount = 0;

        foreach (var unit in attackerUnits)
        {
            foreach (var model in unit.Models)
            {
                // Match by model name if supplied, otherwise search all models
                if (!string.IsNullOrEmpty(request.ModelName) &&
                    !string.Equals(model.ModelName, request.ModelName, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var wp in model.Weapons)
                {
                    if (!string.Equals(wp.WeaponName, request.WeaponName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var v = wp.Profiles.FirstOrDefault(p =>
                        string.Equals(p.Variant, request.VariantName, StringComparison.OrdinalIgnoreCase))
                        ?? wp.Profiles.FirstOrDefault();

                    if (v != null)
                    {
                        variant = v;
                        resolvedWeaponName = wp.WeaponName;
                        modelCount += model.Count;
                    }
                }
            }
        }

        if (variant == null)
            return Error($"Weapon '{request.WeaponName}' not found on selected attacker units.");

        // User can override the attacking model count
        if (request.AttackingModels > 0)
            modelCount = request.AttackingModels;

        if (modelCount <= 0)
            return Error("No attacking models found for the selected weapon.");

        // Map reroll options
        var rerolls = new SimRerollOptions
        {
            HitRerollOnes = request.HitRerolls == "ones",
            HitRerollAll  = request.HitRerolls == "all",
            WoundRerollOnes = request.WoundRerolls == "ones",
            WoundRerollAll  = request.WoundRerolls == "all",
        };

        // Parse attacks and damage from ScalarValue (may be fixed int or dice string e.g. "D6")
        var attacks = ParseDice(variant.Attacks);
        var damage  = ParseDice(variant.Damage);

        // AP: enricher stores as negative (e.g. -2), simulator expects positive (save + ap)
        int simAp = -variant.Ap;

        var weapon = new SimWeaponProfile
        {
            Name           = resolvedWeaponName,
            Attacks        = attacks,
            Skill          = variant.Skill,
            Strength       = variant.Strength,
            Ap             = simAp,
            Damage         = damage,
            WithinHalfRange = request.WithinHalfRange,
            Abilities      = MapAbilities(variant.Abilities),
        };

        var attackerName = string.Join(" + ", attackerUnits.Select(u => u.Name));
        var attacker = new SimAttackerProfile
        {
            Name          = attackerName,
            Models        = modelCount,
            Weapon        = weapon,
            Rerolls       = rerolls,
            CriticalHitsOn = request.CriticalHitsOn5 ? 5 : attackerUnits.Min(u => u.CriticalHitsOn),
        };

        // Cover: +1 to armour save (makes it harder to fail = numerically higher threshold)
        int coverBonus = request.InCover ? 1 : 0;

        var defenderProfile = new SimDefenderProfile
        {
            Name             = defender.Name,
            Models           = defender.ModelCount,
            Toughness        = defender.Toughness,
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

        var (raw, stageStats) = _simulator.Run(config);
        var result = SimulationResult.Compute(raw);

        // Expected kills: total damage / wounds per model (wounds > 0 guard)
        int woundsPerModel = defender.Wounds > 0 ? defender.Wounds : 1;
        double expectedKills = result.Mean / woundsPerModel;

        // Probability of killing at least one model: P(damage >= woundsPerModel)
        double probKillAtLeastOne = raw.Count(d => d >= woundsPerModel) / (double)raw.Count;

        var variantLabel = string.Equals(request.VariantName, "default", StringComparison.OrdinalIgnoreCase)
            ? "" : $" [{request.VariantName}]";

        return new SimulationResponse
        {
            Success              = true,
            AttackerName         = attackerName,
            WeaponDescription    = $"{resolvedWeaponName}{variantLabel}",
            DefenderName         = defender.Name,
            Runs                 = result.Runs,
            MeanDamage           = Math.Round(result.Mean, 2),
            StdDeviation         = Math.Round(result.StdDeviation, 2),
            ExpectedKills        = Math.Round(expectedKills, 2),
            ProbKillAtLeastOne   = Math.Round(probKillAtLeastOne, 3),
            MinDamage            = result.Min,
            MaxDamage            = result.Max,
            StageStats           = stageStats,
        };
    }

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
