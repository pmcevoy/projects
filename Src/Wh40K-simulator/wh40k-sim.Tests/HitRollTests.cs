using Wh40kSim.Models;
using Wh40kSim.Simulation;

namespace Wh40kSim.Tests;

public class HitRollTests
{
    private static SimulationConfig MakeConfig(
        int skill = 3, int strength = 4, int ap = 1, int damage = 1,
        bool hitRerollOnes = false, bool hitRerollAll = false,
        int toughness = 4, int save = 3, int criticalHitsOn = 6)
    {
        return new SimulationConfig
        {
            SimulationRuns = 1,
            Attacker = new AttackerProfile
            {
                Models = 1,
                Weapon = new WeaponProfile
                {
                    Attacks = DiceExpression.Fixed(1),
                    Skill = skill,
                    Strength = strength,
                    Ap = ap,
                    Damage = DiceExpression.Fixed(damage),
                },
                Rerolls = new RerollOptions
                {
                    HitRerollOnes = hitRerollOnes,
                    HitRerollAll = hitRerollAll,
                },
                CriticalHitsOn = criticalHitsOn,
            },
            Defender = new DefenderProfile
            {
                Models = 1, Toughness = toughness, Save = save, Wounds = 1,
            },
        };
    }

    [Fact]
    public void Natural1_AlwaysFails_EvenVsSkill2()
    {
        // Roll 1 (miss), then wound/save won't be reached
        var dice = new FakeDiceRoller(1);
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(skill: 2);
        var results = sim.Run(config);
        Assert.Equal(0, results[0]);
    }

    [Fact]
    public void Natural6_AlwaysHits()
    {
        // Hit=6 (nat 6 auto-hit), Wound=4 (hits 4+ vs T4/S4), Save=1 (nat 1 always fails save)
        var dice = new FakeDiceRoller(6, 4, 1);
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(skill: 6, strength: 4, toughness: 4, ap: 0, save: 7, damage: 1);
        var results = sim.Run(config);
        Assert.Equal(1, results[0]);
    }

    [Fact]
    public void RerollOnes_RerollsA1()
    {
        // First roll = 1 (rerolled), second = 5 (hit), wound = 4, save fails = 1
        var dice = new FakeDiceRoller(1, 5, 4, 1);
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(skill: 3, strength: 4, toughness: 4, ap: 0, save: 7, damage: 1, hitRerollOnes: true);
        var results = sim.Run(config);
        Assert.Equal(1, results[0]);
    }

    [Fact]
    public void RerollOnes_DoesNotRerollNonOnes()
    {
        // Roll 2 on BS3 — fails without reroll (no reroll triggered since it's not a 1)
        var dice = new FakeDiceRoller(2);
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(skill: 3, hitRerollOnes: true);
        var results = sim.Run(config);
        Assert.Equal(0, results[0]);
    }

    [Fact]
    public void RerollAll_RerollsFailedHit()
    {
        // First roll 2 (fail vs BS3), reroll gets 4 (hit), wound 4, save 1 (fail)
        var dice = new FakeDiceRoller(2, 4, 4, 1);
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(skill: 3, strength: 4, toughness: 4, ap: 0, save: 7, damage: 1, hitRerollAll: true);
        var results = sim.Run(config);
        Assert.Equal(1, results[0]);
    }

    [Fact]
    public void RerollAll_DoesNotRerollSuccessfulHit()
    {
        // Roll 4 vs BS3: hits, no reroll. Wound roll=2 (fail vs T4/S4 needing 4+)
        var dice = new FakeDiceRoller(4, 2);
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(skill: 3, strength: 4, toughness: 4, hitRerollAll: true);
        var results = sim.Run(config);
        Assert.Equal(0, results[0]);
    }

    [Fact]
    public void Natural1_NotRerolledAgain_WhenRerollAll()
    {
        // Even with HitRerollAll, a natural 1 result after reroll is not rerolled a second time.
        // First die: 2 (fail → rerolled), second die: 1 (natural fail, no more rerolls)
        var dice = new FakeDiceRoller(2, 1);
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(skill: 3, hitRerollAll: true);
        var results = sim.Run(config);
        Assert.Equal(0, results[0]);
    }

    [Fact]
    public void CriticalHitsOn5_Roll5_IsCriticalHit_AlwaysHits()
    {
        // criticalHitsOn=5, BS=6 (would never hit normally). Roll 5 → Critical Hit → auto-hit.
        // Wound=4, save fails=1 → 1 damage
        var dice = new FakeDiceRoller(5, 4, 1);
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(skill: 6, strength: 4, toughness: 4, ap: 0, save: 7, damage: 1, criticalHitsOn: 5);
        var results = sim.Run(config);
        Assert.Equal(1, results[0]);
    }

    [Fact]
    public void CriticalHitsOn5_Roll4_IsNotCritical_FollowsSkill()
    {
        // criticalHitsOn=5, BS=6. Roll 4 → not a Critical Hit, and 4 < 6 → miss.
        var dice = new FakeDiceRoller(4);
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(skill: 6, criticalHitsOn: 5);
        var results = sim.Run(config);
        Assert.Equal(0, results[0]);
    }

    [Fact]
    public void RerollAll_DoesNotReroll_CriticalHitThreshold()
    {
        // criticalHitsOn=5, BS=6, HitRerollAll. Roll 5 → Critical Hit (not rerolled despite being < skill).
        // Wound=4, save fails=1 → 1 damage
        var dice = new FakeDiceRoller(5, 4, 1);
        var sim = new CombatSimulator(dice);
        var config = MakeConfig(skill: 6, strength: 4, toughness: 4, ap: 0, save: 7, damage: 1,
            hitRerollAll: true, criticalHitsOn: 5);
        var results = sim.Run(config);
        Assert.Equal(1, results[0]);
    }
}
