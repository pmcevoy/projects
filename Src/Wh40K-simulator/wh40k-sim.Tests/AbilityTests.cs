using Wh40kSim.Models;
using Wh40kSim.Simulation;

namespace Wh40kSim.Tests;

public class AbilityTests
{
    private static SimulationConfig MakeConfig(
        WeaponProfile weapon,
        int defenderSave = 7,
        int defenderModels = 5,
        int? invuln = null,
        int? fnp = null,
        int attackerModels = 1,
        RerollOptions? rerolls = null)
    {
        return new SimulationConfig
        {
            SimulationRuns = 1,
            Attacker = new AttackerProfile
            {
                Models = attackerModels,
                Weapon = weapon,
                Rerolls = rerolls ?? new RerollOptions(),
            },
            Defender = new DefenderProfile
            {
                Models = defenderModels,
                Toughness = 4,
                Save = defenderSave,
                InvulnerableSave = invuln,
                Wounds = 10,
                FeelNoPain = fnp,
            },
        };
    }

    [Fact]
    public void Torrent_AutoHits_NeverMisses()
    {
        // Roll sequence: wound roll must hit first (no hit roll). Wound=4 (fail vs T4/S4 needing 4+)
        // Torrent + wound miss → 0 damage
        var dice = new FakeDiceRoller(2); // wound roll of 2 (fail)
        var weapon = new WeaponProfile
        {
            Attacks = DiceExpression.Fixed(1),
            Skill = 6, // would never hit normally
            Strength = 4,
            Ap = 0,
            Damage = DiceExpression.Fixed(1),
            Abilities = new WeaponAbilities { Torrent = true },
        };
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(weapon, defenderSave: 7);
        var results = sim.Run(config);
        // wound failed, so 0 damage; but crucially no hit roll was made (only 1 die consumed = wound)
        Assert.Equal(0, results[0]);
    }

    [Fact]
    public void Torrent_WithSuccessfulWoundAndFailedSave_DealsDamage()
    {
        // Wound=4 (hit vs T4/S4 needs 4+), Save=1 (fail), no more rolls
        var dice = new FakeDiceRoller(4, 1);
        var weapon = new WeaponProfile
        {
            Attacks = DiceExpression.Fixed(1),
            Skill = 6,
            Strength = 4,
            Ap = 0,
            Damage = DiceExpression.Fixed(2),
            Abilities = new WeaponAbilities { Torrent = true },
        };
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(weapon, defenderSave: 7);
        var results = sim.Run(config);
        Assert.Equal(2, results[0]);
    }

    [Fact]
    public void SustainedHits_Natural6_GeneratesExtraHits()
    {
        // Hit roll = 6 (nat 6 → 1 sustained hit extra), extra attack: wound=4, save=1
        // Original attack: wound=4, save=1
        // Total = 2 damage (sustained hits: 1, original: 1 damage each)
        var dice = new FakeDiceRoller(6, 4, 1, 4, 1);
        var weapon = new WeaponProfile
        {
            Attacks = DiceExpression.Fixed(1),
            Skill = 3,
            Strength = 4,
            Ap = 0,
            Damage = DiceExpression.Fixed(1),
            Abilities = new WeaponAbilities { SustainedHits = 1 },
        };
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(weapon, defenderSave: 7);
        var results = sim.Run(config);
        Assert.Equal(2, results[0]);
    }

    [Fact]
    public void SustainedHits_DoesNotChain()
    {
        // Sustained hit bonus attacks: even if they "roll" a 6 conceptually, they don't chain.
        // The bonus attack from sustained hits skips to wound roll directly.
        // Hit=6 (nat6, sustained 1 extra). Extra (from sustained): wound=5(hit), save=1.
        // Original: wound=5, save=1. Total=2
        var dice = new FakeDiceRoller(6, 5, 1, 5, 1);
        var weapon = new WeaponProfile
        {
            Attacks = DiceExpression.Fixed(1),
            Skill = 3,
            Strength = 8,
            Ap = 0,
            Damage = DiceExpression.Fixed(1),
            Abilities = new WeaponAbilities { SustainedHits = 1 },
        };
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(weapon, defenderSave: 7);
        var results = sim.Run(config);
        Assert.Equal(2, results[0]);
    }

    [Fact]
    public void LethalHits_Natural6OnHit_SkipsWoundRoll()
    {
        // Hit=6 (nat6, lethal hit → auto wound, no wound roll), Save=1 (fail)
        var dice = new FakeDiceRoller(6, 1);
        var weapon = new WeaponProfile
        {
            Attacks = DiceExpression.Fixed(1),
            Skill = 3,
            Strength = 1, // would never wound normally (S1 vs T4 needs 6+), but lethal hits skips this
            Ap = 0,
            Damage = DiceExpression.Fixed(1),
            Abilities = new WeaponAbilities { LethalHits = true },
        };
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(weapon, defenderSave: 7);
        var results = sim.Run(config);
        Assert.Equal(1, results[0]);
    }

    [Fact]
    public void DevastatingWounds_Natural6OnWound_SkipsSaveAppliesFnp()
    {
        // Hit=4(hit), Wound=6(nat6, devastating wound → mortal wounds, skip save)
        // Damage roll = 3 (devastating wounds use weapon damage)
        // No FNP → 3 damage through
        var dice = new FakeDiceRoller(4, 6, 3);
        var weapon = new WeaponProfile
        {
            Attacks = DiceExpression.Fixed(1),
            Skill = 3,
            Strength = 4,
            Ap = 0,
            Damage = DiceExpression.Fixed(1), // damage=1, so mortal wounds = 1
            Abilities = new WeaponAbilities { DevastatingWounds = true },
        };
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(weapon, defenderSave: 2); // save=2+ but devastating wounds skips it
        var results = sim.Run(config);
        Assert.Equal(1, results[0]); // mortal wounds = damage = 1
    }

    [Fact]
    public void Blast_FourModels_AddsZero()
    {
        // 4 models → 4/5 = 0 bonus. D6 roll = 2 → 2 attacks total.
        // Both miss (roll 2 vs BS3). Total = 0 damage.
        var dice = new FakeDiceRoller(2, 2, 2);
        var weapon = new WeaponProfile
        {
            Attacks = DiceExpression.Parse("D6"),
            Skill = 3,
            Strength = 4,
            Ap = 0,
            Damage = DiceExpression.Fixed(1),
            Abilities = new WeaponAbilities { Blast = true },
        };
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(weapon, defenderModels: 4, defenderSave: 7);
        // D6=2, blast bonus=0 → 2 attacks; both miss (roll 2 each)
        var results = sim.Run(config);
        Assert.Equal(0, results[0]);
    }

    [Fact]
    public void Blast_FiveModels_AddsOne()
    {
        // 5 models → 5/5 = 1 bonus. D6 roll = 2 → 3 attacks total.
        // All 3 hit(4), wound(4), save fails(1) → 3 damage
        var dice = new FakeDiceRoller(2, 4, 4, 1, 4, 4, 1, 4, 4, 1);
        var weapon = new WeaponProfile
        {
            Attacks = DiceExpression.Parse("D6"),
            Skill = 3,
            Strength = 4,
            Ap = 0,
            Damage = DiceExpression.Fixed(1),
            Abilities = new WeaponAbilities { Blast = true },
        };
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(weapon, defenderModels: 5, defenderSave: 7);
        var results = sim.Run(config);
        Assert.Equal(3, results[0]);
    }

    [Fact]
    public void Blast_TenModels_AddsTwo()
    {
        // 10 models → 10/5 = 2 bonus. D6 roll = 1 → 3 attacks total.
        // All 3 hit(4), wound(4), save fails(1) → 3 damage
        var dice = new FakeDiceRoller(1, 4, 4, 1, 4, 4, 1, 4, 4, 1);
        var weapon = new WeaponProfile
        {
            Attacks = DiceExpression.Parse("D6"),
            Skill = 3,
            Strength = 4,
            Ap = 0,
            Damage = DiceExpression.Fixed(1),
            Abilities = new WeaponAbilities { Blast = true },
        };
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(weapon, defenderModels: 10, defenderSave: 7);
        var results = sim.Run(config);
        Assert.Equal(3, results[0]);
    }

    [Fact]
    public void RapidFire_WithinHalfRange_AddsAttacks()
    {
        // 1 model, Rapid Fire 1, within half range → 2 total attacks
        // Both attacks hit(4), wound(4), save fails(1) → 2 damage
        var dice = new FakeDiceRoller(4, 4, 1, 4, 4, 1);
        var weapon = new WeaponProfile
        {
            Attacks = DiceExpression.Fixed(1),
            Skill = 3,
            Strength = 4,
            Ap = 0,
            Damage = DiceExpression.Fixed(1),
            Abilities = new WeaponAbilities { RapidFire = 1 },
            WithinHalfRange = true,
        };
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(weapon, defenderSave: 7);
        var results = sim.Run(config);
        Assert.Equal(2, results[0]);
    }

    [Fact]
    public void RapidFire_NotWithinHalfRange_NoExtraAttacks()
    {
        // 1 model, Rapid Fire 1, NOT within half range → 1 attack only
        var dice = new FakeDiceRoller(4, 4, 1);
        var weapon = new WeaponProfile
        {
            Attacks = DiceExpression.Fixed(1),
            Skill = 3,
            Strength = 4,
            Ap = 0,
            Damage = DiceExpression.Fixed(1),
            Abilities = new WeaponAbilities { RapidFire = 1 },
            WithinHalfRange = false,
        };
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(weapon, defenderSave: 7);
        var results = sim.Run(config);
        Assert.Equal(1, results[0]);
    }

    [Fact]
    public void Melta_WithinHalfRange_AddsBonusToDamage()
    {
        // Hit(4), Wound(4), Save fails(1), Damage roll=1, Melta 2 → total damage = 3
        var dice = new FakeDiceRoller(4, 4, 1, 1);
        var weapon = new WeaponProfile
        {
            Attacks = DiceExpression.Fixed(1),
            Skill = 3,
            Strength = 4,
            Ap = 0,
            Damage = DiceExpression.Fixed(1),
            Abilities = new WeaponAbilities { Melta = 2 },
            WithinHalfRange = true,
        };
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(weapon, defenderSave: 7);
        var results = sim.Run(config);
        Assert.Equal(3, results[0]);
    }

    [Fact]
    public void Melta_NotWithinHalfRange_NoBonusDamage()
    {
        // Same setup but not within half range → damage = 1
        var dice = new FakeDiceRoller(4, 4, 1, 1);
        var weapon = new WeaponProfile
        {
            Attacks = DiceExpression.Fixed(1),
            Skill = 3,
            Strength = 4,
            Ap = 0,
            Damage = DiceExpression.Fixed(1),
            Abilities = new WeaponAbilities { Melta = 2 },
            WithinHalfRange = false,
        };
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(weapon, defenderSave: 7);
        var results = sim.Run(config);
        Assert.Equal(1, results[0]);
    }

    [Fact]
    public void InvulnerableSave_BetterThanArmour_IsUsed()
    {
        // Armour save 5+, AP 3 → armour becomes 8+ (impossible). Invuln 3+ → uses 3+.
        // Roll 4 → passes invuln save → 0 damage
        var dice = new FakeDiceRoller(4, 4, 4); // hit, wound, save (roll 4 >= invuln 3+)
        var weapon = new WeaponProfile
        {
            Attacks = DiceExpression.Fixed(1),
            Skill = 3,
            Strength = 4,
            Ap = 3,
            Damage = DiceExpression.Fixed(1),
        };
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(weapon, defenderSave: 5, invuln: 3);
        var results = sim.Run(config);
        Assert.Equal(0, results[0]);
    }
}
